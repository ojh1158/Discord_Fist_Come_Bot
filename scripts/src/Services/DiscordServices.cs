using System.Collections.Concurrent;
using System.Net;
using Discord;
using Discord.Rest;
using Discord.WebSocket;
using DiscordBot.scripts.config;
using DiscordBot.scripts.db.Models;
using DiscordBot.scripts.db.Services;
using DiscordBot.scripts.src.party;
using DiscordBot.scripts.src.util;
using Serilog;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Emoji = DiscordBot.scripts.src.util.Emoji;

namespace DiscordBot.scripts.src.Services;


public class DiscordServices : ISingleton
{
    public readonly DiscordSocketClient client;
    private readonly PartyService _partyService;
    private readonly UserService _userService;
    private readonly TaskDelayQueue _taskDelayQueue;
    private readonly IServiceProvider _services;

    public DiscordServices(DiscordSocketClient discord, PartyService partyService, UserService userService, TaskDelayQueue taskDelayQueue, Config config, IServiceProvider services)
    {
        client = discord;
        _partyService = partyService;
        _userService = userService;
        _taskDelayQueue = taskDelayQueue;
        _services = services;
        
        _ = Task.Run(async () =>
        {
            client.Log += LogAsync;

            var token = config.Test.Enable
                ? config.Discord.TestToken
                : config.Discord.Token;

            if (string.IsNullOrEmpty(token))
            {
                Log.Error($"에러: {(config.Test.Enable ? "TEST_DISCORD_TOKEN" : "DISCORD_TOKEN")} 환경변수가 설정되지 않았습니다.");
                return;
            }

            Log.Information($"[{(config.Test.Enable ? "테스트" : "프로덕션")} 모드] 봇 시작 중...");
            await client.LoginAsync(TokenType.Bot, token);
            await client.StartAsync();
        });
    }

    private Task LogAsync(LogMessage log)
    {
        Log.Debug(log.ToString());
        return Task.CompletedTask;
    }

    public async Task UpdateMessage(SocketInteraction component, PartyEntity party, bool isAllMessage = false, string message = "", bool useDelay = true)
    {
        try
        {
            var scheduler = _services.GetService<PartyRenderScheduler>();
            if (scheduler == null)
            {
                Log.Error("[UpdateMessage] PartyRenderScheduler 미등록");
                return;
            }

            await scheduler.RequestAsync(party.PARTY_KEY, isAllMessage, message, immediate: !useDelay);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[UpdateMessage] 스케줄러 요청 실패 party={partyKey}", party.PARTY_KEY);
        }
    }

    public void MessageWithExpire(IUserMessage message, int time = 10, Action? action = null)
    {
        var content = message.Content ?? "";

        var separator = "\u200B"; // Zero-Width Space
        var exMessage = $"{separator} (해당 메세지는 {DateTime.Now.AddSeconds(time).ToDiscordRelativeTimestamp()} 삭제됩니다.)";

        RestInteractionMessage? restInteractionMessage = null;
        if (message is RestInteractionMessage interactionMessage)
        {
            restInteractionMessage = interactionMessage;
            
            restInteractionMessage.ModifyAsync(mp => mp.Content = $"{content}{exMessage}");
        }
        else
        {
            message.ModifyAsync(mp => mp.Content = $"{content}{exMessage}");
        }
        
        // 백그라운드에서 삭제
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(time));

            try
            {
                var channel = message.Channel;
                if (channel == null) return;

                var latestMessage = await channel.GetMessageAsync(message.Id);
                if (latestMessage == null) return;

                var old = latestMessage.Content;
                var s = old.Split(separator)[0];
                if (s != content)
                {
                    return;
                }
                
                if (restInteractionMessage is not null)
                {
                    await restInteractionMessage.DeleteAsync();
                }
                else
                {
                    await latestMessage.DeleteAsync();
                }
                
                action?.Invoke();
            }
            catch (Discord.Net.HttpException ex) when (ex.HttpCode == HttpStatusCode.NotFound)
            {
                
            }
            catch (Exception ex)
            {
                Log.Error(ex, ex.Message);
            }
        });
    }

    public async Task RespondMessageWithExpire(SocketInteraction component, int time = 10, string? message = null,
        MessageComponent? addComponent = null)
    {
        var separator = "\u200B"; // Zero-Width Space
        var exMessage = $"{separator} (해당 메세지는 {DateTime.Now.AddSeconds(time).ToDiscordRelativeTimestamp()} 삭제됩니다.)";

        if (message != null)
        {
            // HasResponded 체크 - 이미 응답했는지 확인
            if (!component.HasResponded)
            {
                if (addComponent != null)
                    await component.RespondAsync(message + exMessage, components: addComponent, ephemeral: true);
                else await component.RespondAsync(message + exMessage, ephemeral: true);
            }
            else
            {
                await component.ModifyOriginalResponseAsync(m =>
                {
                    m.Content = message + exMessage;
                    if (addComponent != null) m.Components = addComponent;
                });
            }
        }
        else
        {
            var originalResponse = await component.GetOriginalResponseAsync();
            if (originalResponse == null)
            {
                // 원본 응답이 없는 경우 (이미 삭제되었거나 존재하지 않음)
                return;
            }

            message = originalResponse.Content;
            await component.ModifyOriginalResponseAsync(m =>
            {
                m.Content = message + exMessage;
                if (addComponent != null) m.Components = addComponent;
            });
        }

        // 백그라운드에서 삭제
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(time));

            var originalResponseAsync = await component.GetOriginalResponseAsync();
            if (originalResponseAsync == null) return;

            var old = originalResponseAsync.Content;
            var s = old.Split(separator)[0];
            if (s != message)
            {
                return;
            }

            try
            {
                await component.DeleteOriginalResponseAsync();
            }
            catch (Exception ex)
            {
                Log.Error($"[RespondMessageWithExpire] 삭제 실패: {ex.Message}");
            }
        });
    }
    
    public async Task<Embed> UpdatedEmbed(PartyEntity party)
    {
        var members = party.Members.Count <= party.MAX_COUNT_MEMBER ? party.Members : party.Members[..party.MAX_COUNT_MEMBER];
        var memberList = party.Members.Count > 0
            ? string.Join("\n", members.Select(member => $"**<@{member.USER_ID}> ({member.USER_NICKNAME})**"))
            : "없음";

        string state;
        if (party.IS_EXPIRED)
            state = " (만료)";
        else if (party.IS_CLOSED)
            state = " (일시정지)";
        else
            state = "";

        var title = $"**{party.DISPLAY_NAME}** {state}";

        var description = "";

        if (party.START_DATE.HasValue)
        {
            description += $"**시작시간: {party.START_DATE?.ToDiscordRelativeTimestamp()} ({party.START_DATE.Value.ToKoreanDisplayString()}) **\n";
        }
        if (party.VOICE_CHANNEL_KEY != null)
        {
            description += $"**파티장소: <#{party.VOICE_CHANNEL_KEY}> **\n";
        }
        else
        {
            description += "\n";
        }


        description += $"** 참가자: {members.Count}/{party.MAX_COUNT_MEMBER} **\n\n{memberList}";

        if (party.Members.Count > party.MAX_COUNT_MEMBER)
        {
            description += $"\n====================\n**대기열: {party.Members.Count - party.MAX_COUNT_MEMBER}\n**";

            var array = party.Members[party.MAX_COUNT_MEMBER..];
            for (var i = 0; i < array.Count; i++)
            {
                var member = array[i];
                description += $"\n순번: {i + 1} | 닉네임: <@{member.USER_ID}> ({member.USER_NICKNAME})";
            }
        }

        // 만료시간 추가 (강조 표시)

        if (party.EXPIRE_DATE.HasValue)
        {
            description += $"\n\n\n**만료시간: {party.EXPIRE_DATE?.ToDiscordRelativeTimestamp()} ({party.EXPIRE_DATE.Value.ToKoreanDisplayString()})**";
        }
        else
        {
            description += $"\n\u200B";
        }

        var color = Color.Blue;
        if (party.MAX_COUNT_MEMBER == members.Count) color = Color.Green;
        if (party.IS_CLOSED) color = Color.Orange;
        if (party.IS_EXPIRED) color = Color.Red;
        
        var cacheKey = party.OWNER_KEY + "_AvatarUrl";
        
        if (!CacheManager.Cache.TryGetValue(cacheKey, out string? result))
        {
            var ownerUser = await client.Rest.GetGuildAsync(party.GUILD_KEY);
            var user = await ownerUser.GetUserAsync(party.OWNER_KEY);
            string? ownerAvatarUrl = user?.GetDisplayAvatarUrl();
            var cacheOptions = new MemoryCacheEntryOptions()
                .SetAbsoluteExpiration(TimeSpan.FromDays(1));
            CacheManager.Cache.Set(cacheKey, ownerAvatarUrl, cacheOptions);
            Log.Information($"[{cacheKey}] 캐쉬 생성");
            result = ownerAvatarUrl;
        }


        var updatedEmbed = new EmbedBuilder();
        updatedEmbed
            .WithTitle(title)
            .WithAuthor(party.OWNER_NICKNAME ?? "알 수 없음", result) // 여기에 추가!
            .WithDescription(description)
            .WithColor(color)
            .WithFooter($"버그제보(Discord): ojh1158 Version: {Constant.VERSION}")
            .WithCurrentTimestamp();


        return updatedEmbed.Build();
    }

    private async Task<List<Embed>> GetDisplayEmbeds(List<PartyMemberEntity> members, ulong guildId)
    {
        var embedList = new List<Embed>();
        foreach (var partyMemberEntity in members)
        {
            var user = client.GetGuild(guildId)?.GetUser(partyMemberEntity.USER_ID);
            user ??= (await client.GetUserAsync(partyMemberEntity.USER_ID)) as SocketGuildUser;
            string? userUrl = user?.GetDisplayAvatarUrl();

            var updatedEmbed = new EmbedBuilder();
            updatedEmbed
                .WithAuthor(partyMemberEntity.USER_NICKNAME, userUrl); // 여기에 추가!
            embedList.Add(updatedEmbed.Build());
        }

        return embedList;
    }

    public MessageComponent UpdatedComponent(PartyEntity party)
    {
        var partyKey = party.PARTY_KEY;

        var component = new ComponentBuilder();
        var maxFlag = party.MAX_COUNT_MEMBER <= party.Members.Count;

        if (party.IS_EXPIRED) return component.Build();

        if (!party.IS_CLOSED)
        {
            // 인원이 가득 찬 경우
            if (maxFlag)
            {
                component.WithButton("대기하기", $"{Constant.JOIN_KEY}_{partyKey}");
            }
            else
            {
                component.WithButton(Constant.JOIN_KEY, $"{Constant.JOIN_KEY}_{partyKey}",
                    ButtonStyle.Success);
            }
        }

        component.WithButton(Constant.LEAVE_KEY, $"{Constant.LEAVE_KEY}_{partyKey}", ButtonStyle.Danger);

        component.WithButton(Constant.OPTION_KEY, $"{Constant.OPTION_KEY}_{partyKey}", ButtonStyle.Secondary);

        component.WithButton(Constant.PULLING_UP_KEY,$"{Constant.PULLING_UP_KEY}_{partyKey}", ButtonStyle.Success);

        
        return component.Build();
    }

    public async Task<bool> ExpirePartyAsync(PartyEntity party, ISocketMessageChannel? channel = null)
    {
        try
        {
            channel = await client.GetChannelAsync(party.CHANNEL_KEY) as ISocketMessageChannel;
        }
        catch (Exception)
        {
            Log.Error("더 이상 접근할 수 없는 메세지입니다.");
            await _partyService.ExpirePartyAsync(party.MESSAGE_KEY);
            return true;
        }

        if (channel == null) return false;

        var result = await _partyService.ExpirePartyAsync(party.MESSAGE_KEY);

        if (!result) return false;

        party.IS_EXPIRED = true;

        try
        {
            // 메시지를 먼저 가져와서 봇이 작성한 메시지인지 확인
            var message = await channel.GetMessageAsync(party.MESSAGE_KEY);
            if (message == null || message.Author.Id != client.CurrentUser.Id)
            {
                // 봇이 작성한 메시지가 아니거나 메시지가 없는 경우
                Log.Warning(
                    $"[ExpirePartyAsync] 메시지를 수정할 수 없습니다. (MESSAGE_KEY: {party.MESSAGE_KEY}, Author: {message?.Author?.Id})");
                return true; // DB 업데이트는 성공했으므로 true 반환
            }

            var embed = await UpdatedEmbed(party);

            await channel.ModifyMessageAsync(party.MESSAGE_KEY, msg =>
            {
                msg.Embed = embed;
                msg.Components = null;
            });
        }
        catch (Exception ex)
        {
            Log.Error($"[ExpirePartyAsync] 메시지 수정 중 오류 발생: {ex.Message}");
            return true; // DB 업데이트는 성공했으므로 true 반환
        }

        return true;
    }

    public MessageComponent GetSettingComponent()
    {
        return new ComponentBuilder()
            .WithButton(Constant.USER_ALERT_SETTING_KEY, $"{Constant.USER_ALERT_SETTING_KEY}_{Constant.USER_ALERT_SETTING_OPEN_KEY}", ButtonStyle.Success)
            .Build();
    }
    
    public string ToDiscordUserMention(ulong userId) => $"<@{userId}>";

    public void SendUserAlert(PartyEntity partyEntity, IUser user, string action)
    {
        Task.Run(async () =>
        {
            var allList = new List<PartyMemberEntity>(partyEntity.Members);
            
            var dic = allList.ToDictionary(d => d.USER_ID, d => d);
            var memberOnly = partyEntity.MemberOnly;
            
            switch (action)
            {
                case Constant.JOIN_KEY:
                    if (user.Id != partyEntity.OWNER_KEY)
                    {
                        _ = Task.Run(async () =>
                        {
                            var ownerSetting = await _userService.GetUserSettingAsync(partyEntity.OWNER_KEY);

                            IUser? owner = null;
                            
                            if (ownerSetting is { ALL_ALERT_FLAG: true, MY_PARTY_JOIN_USER_ALERT_FLAG: true })
                            {
                                owner ??= await client.GetUserAsync(partyEntity.OWNER_KEY);
                                _ = owner.SendMessageAsync($"<@{user.Id}>({dic[user.Id].USER_NICKNAME}) 님이 {partyEntity.DISPLAY_NAME} 에 참가하였습니다! {ToLinkChanner(partyEntity)}");
                            }
                            
                            if (partyEntity.Members.Count == partyEntity.MAX_COUNT_MEMBER && ownerSetting is { ALL_ALERT_FLAG: true, MY_PARTY_FULL_ALERT_FLAG: true })
                            {
                                owner ??= await client.GetUserAsync(partyEntity.OWNER_KEY);
                                _ = owner.SendMessageAsync($"{partyEntity.DISPLAY_NAME} 파티가 모였습니다! {ToLinkChanner(partyEntity)}");
                            }
                        });
                    }

                    if (partyEntity.Members.Count == partyEntity.MAX_COUNT_MEMBER)
                    {
                        foreach (var partyEntityMember in memberOnly)
                        {
                            if (partyEntityMember.USER_ID != user.Id && partyEntityMember.USER_ID != partyEntity.OWNER_KEY)
                            {
                                _ = Task.Run(async () =>
                                {
                                    var ownerSetting = await _userService.GetUserSettingAsync(partyEntityMember.USER_ID);
                            
                                    if (ownerSetting is { ALL_ALERT_FLAG: true, MY_PARTY_FULL_ALERT_FLAG: true })
                                    {
                                        var otherUser = await client.GetUserAsync(partyEntityMember.USER_ID);
                                        _ = otherUser.SendMessageAsync($"**{partyEntity.DISPLAY_NAME}** 파티가 모였습니다! {ToLinkChanner(partyEntity)}");
                                    }
                                });
                            }
                        }
                    }
                    
                    break;
                case Constant.LEAVE_KEY:
                    if (user.Id != partyEntity.OWNER_KEY)
                    {
                        _ = Task.Run(async () =>
                        {
                            var ownerSetting = await _userService.GetUserSettingAsync(partyEntity.OWNER_KEY);
                            
                            if (ownerSetting is { ALL_ALERT_FLAG: true, MY_PARTY_LEFT_USER_ALERT_FLAG: true })
                            {
                                if (await client.GetChannelAsync(partyEntity.CHANNEL_KEY) is SocketGuildChannel guildChannel)
                                {
                                    var owner = await client.Rest.GetGuildUserAsync(guildChannel.Guild.Id, partyEntity.OWNER_KEY);
                                    _ = owner?.SendMessageAsync($"<@{user.Id}>({(user is SocketGuildUser guildUser ? $"{guildUser.DisplayName}" : $"{user.Username}")}) 님이 {partyEntity.DISPLAY_NAME} 파티에서 나갔습니다. {ToLinkChanner(partyEntity)}");
                                }
                            }
                        });
                    }
                    break;
                case Constant.JOIN_AUTO_KEY:
                    var target = await _partyService.GetPartyEntityAsync(partyEntity.PARTY_KEY);
                    if (target != null)
                    {
                        foreach (var partyMemberEntity in target.MemberOnly)
                        {
                            if (partyMemberEntity.USER_ID == user.Id) continue;
                            
                            _ = Task.Run(async () =>
                            {
                                var userSetting = await _userService.GetUserSettingAsync(partyMemberEntity.USER_ID);
                            
                                if (userSetting is { ALL_ALERT_FLAG: true, JOIN_PARTY_TO_WAIT_FLAG: true })
                                {
                                    var otherUser = await client.GetUserAsync(partyMemberEntity.USER_ID);
                                    _ = otherUser.SendMessageAsync($"**{partyEntity.DISPLAY_NAME}** 파티가 모였습니다! {ToLinkChanner(partyEntity)}");
                                }
                            });
                        }
                    }
                    break;
                    
                case Constant.USER_ALERT_JOIN_PARTY_TO_WAIT_FLAG:
                    _ = Task.Run(async () =>
                    {
                        var userSetting = await _userService.GetUserSettingAsync(user.Id);
                            
                        if (userSetting is { ALL_ALERT_FLAG: true, JOIN_PARTY_TO_WAIT_FLAG: true })
                        {
                            _ = user.SendMessageAsync($"**{partyEntity.DISPLAY_NAME}** 파티에 빈자리가 생겨 참가되었습니다! {ToLinkChanner(partyEntity)}");
                        }
                    });
                    break;
            }
        });
    }

    public string ToLinkChanner(PartyEntity partyEntity)
    {
        return ToLinkChanner(partyEntity.CHANNEL_KEY);
    }

    public string ToLinkChanner(ulong channelId)
    {
        return $"\n(<#{channelId}>)[{DateTime.Now.ToDiscordRelativeTimestamp()}]\n\\\u200B";
    }

}

