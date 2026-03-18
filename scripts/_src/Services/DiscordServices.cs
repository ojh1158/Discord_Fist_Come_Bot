using System.Net;
using Discord;
using Discord.Rest;
using Discord.WebSocket;
using DiscordBot.scripts._src.party;
using DiscordBot.scripts._src.util;
using DiscordBot.scripts.db.Models;
using DiscordBot.scripts.db.Services;
using Serilog;

namespace DiscordBot.scripts._src.Services;


public class DiscordServices : ISingleton
{
    public readonly DiscordSocketClient client;
    public readonly PartyService partyService;
    public readonly UserService userService;

    public DiscordServices(DiscordSocketClient discord, PartyService partyService, UserService userService)
    {
        client = discord;
        this.partyService = partyService;
        this.userService = userService;
        

        _ = Task.Run(async () =>
        {
            client.Log += LogAsync;
            client.Ready += () => ReadyAsync(client);

            // 봇 로그인 및 시작 - 테스트 모드면 TEST_DISCORD_TOKEN 사용
            var token = App.IsTest
                ? Environment.GetEnvironmentVariable("TEST_DISCORDTOKEN")
                : Environment.GetEnvironmentVariable("DISCORDTOKEN");

            if (string.IsNullOrEmpty(token))
            {
                Log.Error($"에러: {(App.IsTest ? "TEST_DISCORD_TOKEN" : "DISCORD_TOKEN")} 환경변수가 설정되지 않았습니다.");
                return;
            }

            Log.Information($"[{(App.IsTest ? "테스트" : "프로덕션")} 모드] 봇 시작 중...");
            await client.LoginAsync(TokenType.Bot, token);
            await client.StartAsync();

            // client.SlashCommandExecuted += HandleSlashCommandAsync;
            // client.ButtonExecuted += HandleButtonAsync;
            // client.ModalSubmitted += HandleModalAsync;
            // client.SelectMenuExecuted += HandleSelectMenuAsync;
            // client.Ready += InitCommands;
        });
    }

    private Task LogAsync(LogMessage log)
    {
        Log.Debug(log.ToString());
        return Task.CompletedTask;
    }

    private async Task ReadyAsync(DiscordSocketClient client)
    {
        var commands = await client.GetGlobalApplicationCommandsAsync();

        var array = new[]
        {
            new SlashCommandBuilder()
                .WithName("파티")
                .WithDescription($"파티를 생성합니다. 허용 인원은 {Constant.MIN_COUNT}-{Constant.MAX_COUNT} 입니다.")
                .AddOption("이름", ApplicationCommandOptionType.String, "파티 이름", isRequired: true, minLength: 1,
                    maxLength: Constant.MAX_NAME_COUNT)
                .AddOption("인원", ApplicationCommandOptionType.Integer, "파티 인원", isRequired: true)
                .AddOption(
                    name: "시작시간설정",
                    type: ApplicationCommandOptionType.Boolean,
                    description: "시작 시간을 설정할지 결정합니다. (True = 설정)",
                    isRequired: true
                )
                .AddOption(
                    name: "채널선택",
                    type: ApplicationCommandOptionType.Channel,
                    description: "파티가 모일 채널을 선택하세요.",
                    channelTypes: [ChannelType.Voice],
                    isRequired: false
                )
        };

        // 내용이 다르거나 없는 명령어 생성/업데이트
        foreach (var commandBuilder in array)
        {
            var built = commandBuilder.Build();
            var existing = commands.FirstOrDefault(c => c.Name == built.Name.Value);

            if (existing == null || !CommandEquals(existing, built))
            {
                if (existing != null)
                {
                    await existing.DeleteAsync();
                }

                await client.CreateGlobalApplicationCommandAsync(built);
            }
        }

        // array에 없는 명령어 삭제
        foreach (var socketApplicationCommand in commands.Where(c => !array.Any(f => f.Name == c.Name)))
        {
            await socketApplicationCommand.DeleteAsync();
        }

        Log.Information($"{client.CurrentUser.Username} 봇이 준비되었습니다!");
    }

    public async Task UpdateMessage(SocketInteraction component, PartyEntity party, bool isAllMessage, string message)
    {
        // 임베드 메시지 업데이트
        var updatedEmbed = await UpdatedEmbed(party);
        var updatedComponent = UpdatedComponent(party);

        var originalMessage = await component.Channel.GetMessageAsync(party.MESSAGE_KEY) as IUserMessage;
        if (originalMessage == null)
        {
            if (await client.GetChannelAsync(party.CHANNEL_KEY) is IMessageChannel cl)
            {
                originalMessage = await cl.GetMessageAsync(party.MESSAGE_KEY) as IUserMessage;
            }
        }

        // 원본 메시지 수정
        if (originalMessage != null)
        {

            await originalMessage.ModifyAsync(msg =>
            {
                msg.Content = null;
                msg.Embed = updatedEmbed;
                msg.Components = updatedComponent;
            });

            if (isAllMessage)
            {
                if (!component.HasResponded)
                {
                    await component.DeferAsync();
                }

                await originalMessage.ReplyAsync(message);
            }
        }
        else
        {
            await component.Channel.SendMessageAsync($"{party.DISPLAY_NAME} 파티에 대한 원본 메세지를 찾을 수 없습니다. 파티를 해산합니다.");
            await partyService.ExpirePartyAsync(party.MESSAGE_KEY);
        }
    }

    public void MessageWithExpire(IUserMessage message, int time = 10, Action? action = null)
    {
        var content = message.Content ?? "";

        var separator = "\u200B"; // Zero-Width Space
        var exMessage = $"{separator} (해당 메세지는 {DateTime.Now.AddSeconds(time).ToDiscordRelativeTimestamp()} 삭제됩니다.)";

        message.ModifyAsync(mp => mp.Content = $"{content}{exMessage}");

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

                await latestMessage.DeleteAsync();
                action?.Invoke();
            }
            catch (Discord.Net.HttpException ex) when (ex.HttpCode == HttpStatusCode.NotFound)
            {

            }
            catch (Exception ex)
            {
                _ = LogAsync(new LogMessage(LogSeverity.Error, "MessageWithExpire", ex.Message));
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

    private bool CommandEquals(SocketApplicationCommand existing, SlashCommandProperties built)
    {
        // Description 비교
        if (existing.Description != built.Description.Value) return false;

        // Options 개수 비교
        var builtOptionsCount = built.Options.IsSpecified ? built.Options.Value.Count : 0;
        if (existing.Options.Count != builtOptionsCount) return false;

        // Options가 없으면 true
        if (!built.Options.IsSpecified) return existing.Options.Count == 0;

        var existingOptions = existing.Options.ToList();
        var builtOptions = built.Options.Value.ToList();

        for (int i = 0; i < existingOptions.Count; i++)
        {
            var e = existingOptions[i];
            var b = builtOptions[i];

            if (e.Name != b.Name || e.Type != b.Type ||
                e.Description != b.Description)
                return false;
        }

        return true;
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

        var stateDate = party.START_DATE.HasValue ? new DatePickerState().FromDateTime(party.START_DATE.Value) : null;
        var expireDate = party.EXPIRE_DATE.HasValue
            ? new DatePickerState().FromDateTime(party.EXPIRE_DATE.Value)
            : null;

        var description = "";
        
        if (party.START_DATE.HasValue && stateDate != null)
        {
            description += $"**시작시간: {party.START_DATE?.ToDiscordRelativeTimestamp()} ({stateDate.ToDisplayString()}) **\n";
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

        if (expireDate != null)
        {
            description += $"\n\n\n**만료시간: {party.EXPIRE_DATE?.ToDiscordRelativeTimestamp()} ({expireDate.ToDisplayString()})**";
        }
        else
        {
            description += $"\n\u200B";
        }

        var color = Color.Blue;
        if (party.MAX_COUNT_MEMBER == members.Count) color = Color.Green;
        if (party.IS_CLOSED) color = Color.Orange;
        if (party.IS_EXPIRED) color = Color.Red;

        var ownerUser = await client.Rest.GetGuildAsync(party.GUILD_KEY);
        var user = await ownerUser.GetUserAsync(party.OWNER_KEY);
        // ownerUser ??= (await client.GetUserAsync(party.OWNER_KEY)) as SocketGuildUser;
        string? ownerAvatarUrl = user?.GetDisplayAvatarUrl();

        var updatedEmbed = new EmbedBuilder();
        updatedEmbed
            .WithTitle(title)
            .WithAuthor(party.OWNER_NICKNAME ?? "알 수 없음", ownerAvatarUrl) // 여기에 추가!
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
            await partyService.ExpirePartyAsync(party.MESSAGE_KEY);
            return true;
        }

        if (channel == null) return false;

        var result = await partyService.ExpirePartyAsync(party.MESSAGE_KEY);

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
                            var ownerSetting = await userService.GetUserSettingAsync(partyEntity.OWNER_KEY);

                            IUser? owner = null;
                            
                            if (ownerSetting is { ALL_ALERT_FLAG: true, MY_PARTY_JOIN_USER_ALERT_FLAG: true })
                            {
                                owner ??= await client.GetUserAsync(partyEntity.OWNER_KEY);
                                _ = owner.SendMessageAsync($"<@{user.Id}>({dic[user.Id].USER_NICKNAME}) 님이 {partyEntity.DISPLAY_NAME} 에 참가하였습니다! {ToLinkChanner(partyEntity)}");
                            }
                            
                            if (memberOnly.Count == partyEntity.MAX_COUNT_MEMBER && ownerSetting is { ALL_ALERT_FLAG: true, MY_PARTY_FULL_ALERT_FLAG: true })
                            {
                                owner ??= await client.GetUserAsync(partyEntity.OWNER_KEY);
                                _ = owner.SendMessageAsync($"{partyEntity.DISPLAY_NAME} 파티가 모였습니다! {ToLinkChanner(partyEntity)}");
                            }
                        });
                    }

                    if (memberOnly.Count == partyEntity.MAX_COUNT_MEMBER)
                    {
                        foreach (var partyEntityMember in memberOnly)
                        {
                            if (partyEntityMember.USER_ID != user.Id && partyEntityMember.USER_ID != partyEntity.OWNER_KEY)
                            {
                                _ = Task.Run(async () =>
                                {
                                    var ownerSetting = await userService.GetUserSettingAsync(partyEntityMember.USER_ID);
                            
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
                            var ownerSetting = await userService.GetUserSettingAsync(partyEntity.OWNER_KEY);
                            
                            if (ownerSetting is { ALL_ALERT_FLAG: true, MY_PARTY_JOIN_USER_ALERT_FLAG: true })
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
                    var target = await partyService.GetPartyEntityAsync(partyEntity.PARTY_KEY);
                    if (target != null)
                    {
                        foreach (var partyMemberEntity in target.MemberOnly)
                        {
                            if (partyMemberEntity.USER_ID == user.Id) continue;
                            
                            _ = Task.Run(async () =>
                            {
                                var userSetting = await userService.GetUserSettingAsync(partyMemberEntity.USER_ID);
                            
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
                        var userSetting = await userService.GetUserSettingAsync(user.Id);
                            
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

    public Dictionary<string, DatePickerState> dateDic = new();
    
    public MessageProperties CreateDatePickup(string title, PartyEntity party, DateTime? time = null, string? guid = null, bool isFirst = false)
    {
        dateDic.TryGetValue(guid ?? "", out var datePickerState);
        
        var componentBuilder = new ComponentBuilder();
        var embedBuilder = new EmbedBuilder();
        
        var state = datePickerState ?? new DatePickerState();
        if (time != null) state.FromDateTime(time.Value);
        var stateId = state.Id;
        var key = stateId.ToString();

        embedBuilder.WithTitle($"{title}");
        embedBuilder.WithDescription($"{state.ToDisplayString()} {state.ToDateTime().ToDiscordRelativeTimestamp()}");
        embedBuilder.WithColor(Color.Blue);

        var dataPickup = $"{(isFirst ? Constant.DATE_PICKUP_FIRST_KEY : Constant.DATE_PICKUP_KEY)}_{party.PARTY_KEY}_{key}_{title}";

        if (state.IsDateDisplay)
        {
            // Row 0: 년
            componentBuilder.WithSelectMenu($"{dataPickup}_{Constant.YEAR_KEY}", GetYearOptions(state.Year), "년도");
            // Row 1: 월
            componentBuilder.WithSelectMenu($"{dataPickup}_{Constant.MONTH_KEY}", GetMonthOptions(state.Month), "월");
            // Row 2: 일
            componentBuilder.WithSelectMenu($"{dataPickup}_{Constant.DAY_TENS_KEY}", GetDayTensOptions(state.DayTens), "일 (10단위)");
            componentBuilder.WithSelectMenu($"{dataPickup}_{Constant.DAY_ONES_KEY}", GetDayOnesOptions(state.DayOnes), "일 (1단위)");
        }
        else
        {
            // Row 3: 시
            componentBuilder.WithSelectMenu($"{dataPickup}_{Constant.HOUR_KEY}", GetHourOptions(state.Hour), "시");
            
            // Row 4: 분
            componentBuilder.WithSelectMenu($"{dataPickup}_{Constant.MIN_TENS_KEY}", GetMinTensOptions(state.MinTens), "분 (10단위)");
            componentBuilder.WithSelectMenu($"{dataPickup}_{Constant.MIN_ONES_KEY}", GetMinOnesOptions(state.MinOnes), "분 (1단위)");
        }

        if (state.IsDateDisplay)
            componentBuilder.WithButton(Constant.DATE_HOUR_SELECT_KEY,
                $"{dataPickup}_{Constant.DATE_HOUR_SELECT_KEY}");
        else
            componentBuilder.WithButton(Constant.DATE_YEAR_SELECT_KEY,
                $"{dataPickup}_{Constant.DATE_YEAR_SELECT_KEY}");

        // Row 5: 확인/취소 버튼 (SelectMenu가 4개를 차지했으므로 마지막은 버튼)
        componentBuilder.WithButton(Constant.DATE_YES, $"{dataPickup}_{Constant.DATE_YES}", ButtonStyle.Success, row: 4);
        componentBuilder.WithButton(Constant.DATE_NO, $"{dataPickup}_{Constant.DATE_NO}", ButtonStyle.Danger, row: 4);

        dateDic.TryAdd(key, state);
        
        return new MessageProperties
        {
            Content = "",
            Components = componentBuilder.Build(),
            Embed = embedBuilder.Build()
        };
    }
    
    // 년도: 현재 기준 -1년부터 +5년 정도가 적당합니다.
    private List<SelectMenuOptionBuilder> GetYearOptions(int current)
    {
        var options = new List<SelectMenuOptionBuilder>();
        int startYear = DateTime.Now.Year;
        for (int i = startYear; i < startYear + 10; i++) // 향후 10년
        {
            options.Add(new SelectMenuOptionBuilder()
                .WithLabel($"{i}년")
                .WithValue(i.ToString())
                .WithDefault(i == current)
            );
        }
        return options;
    }

    // 월: 1~12월
    private List<SelectMenuOptionBuilder> GetMonthOptions(int current)
    {
        var options = new List<SelectMenuOptionBuilder>();
        for (int i = 1; i <= 12; i++)
        {
            options.Add(new SelectMenuOptionBuilder()
                .WithLabel($"{i}월")
                .WithValue(i.ToString())
                .WithDefault(i == current));
        }
        return options;
    }

    // 시간: 0~23시
    private List<SelectMenuOptionBuilder> GetHourOptions(int current)
    {
        var options = new List<SelectMenuOptionBuilder>();
        for (int i = 0; i <= 23; i++)
        {
            options.Add(new SelectMenuOptionBuilder()
                .WithLabel($"{i:D2}시") // 01시, 02시 형태
                .WithValue(i.ToString())
                .WithDefault(i == current));
        }
        return options;
    }

    // --- '일(Day)' 조립용 ---
    private List<SelectMenuOptionBuilder> GetDayTensOptions(int current)
    {
        var options = new List<SelectMenuOptionBuilder>();
        int currentTens = (current / 10) * 10;
        for (int i = 0; i <= 30; i += 10)
        {
            options.Add(new SelectMenuOptionBuilder()
                .WithLabel($"({i.ToString()[0]}N)일")
                .WithValue(i.ToString())
                .WithDefault(i == currentTens));
        }
        return options;
    }

    private List<SelectMenuOptionBuilder> GetDayOnesOptions(int current)
    {
        var options = new List<SelectMenuOptionBuilder>();
        int currentOnes = current % 10;
        for (int i = 0; i <= 9; i++)
        {
            options.Add(new SelectMenuOptionBuilder()
                .WithLabel($"(N{i})일")
                .WithValue(i.ToString())
                .WithDefault(i == currentOnes));
        }
        return options;
    }

    // --- '분(Minute)' 조립용 (동일 로직) ---
    private List<SelectMenuOptionBuilder> GetMinTensOptions(int current)
    {
        var options = new List<SelectMenuOptionBuilder>();
        int currentTens = (current / 10) * 10;
        for (int i = 0; i <= 50; i += 10)
        {
            options.Add(new SelectMenuOptionBuilder().WithLabel($"({i.ToString()[0]}N)분").WithValue(i.ToString()).WithDefault(i == currentTens));
        }
        return options;
    }

    private List<SelectMenuOptionBuilder> GetMinOnesOptions(int current)
    {
        var options = new List<SelectMenuOptionBuilder>();
        for (int i = 0; i <= 9; i++)
        {
            options.Add(new SelectMenuOptionBuilder().WithLabel($"(N{i})분").WithValue(i.ToString()).WithDefault(i == (current % 10)));
        }
        return options;
    }
}

public class DatePickerState
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int Year { get; set; } = DateTime.Now.Year;
    public int Month { get; set; } = DateTime.Now.Month;
    
    // 조립형 '일' (Day) - 현재 날짜 기준 자동 분리
    public int DayTens { get; set; } = (DateTime.Now.Day / 10) * 10;
    public int DayOnes { get; set; } = DateTime.Now.Day % 10;
    public int FullDay => DayTens + DayOnes;

    public int Hour { get; set; } = DateTime.Now.Hour;

    // 조립형 '분' (Minute) - 현재 분 기준 자동 분리
    public int MinTens { get; set; } = (DateTime.Now.Minute / 10) * 10;
    public int MinOnes { get; set; } = 0;
    public int FullMinute => MinTens + MinOnes;
    
    public bool IsDateDisplay { get; set; } = false;
    

    // 상단 표시용 문자열
    public string ToDisplayString()
    {
        var now = DateTime.Now;
        var result = "";

        if (now.Year != Year)
        {
            result += $"{Year}년 {Month}월 ";
        }
        else if (now.Month != Month)
        {
            result += $"{Month}월 ";
        }

        result += $"{FullDay}일 {Hour}시 {FullMinute}분";

        return result;
    }

    public DateTime ToDateTime()
    {
        try
        {
            // 1. 기본적으로 사용자가 선택한 값으로 생성 시도
            return new DateTime(Year, Month, FullDay, Hour, FullMinute, 0);
        }
        catch (ArgumentOutOfRangeException)
        {
            // 2. 만약 2월 31일 처럼 말도 안 되는 날짜면, 해당 월의 마지막 날로 보정
            int lastDay = DateTime.DaysInMonth(Year, Month);
            return new DateTime(Year, Month, lastDay, Hour, FullMinute, 0);
        }
    }

    /// <summary>DateTime을 받아 이 인스턴스의 년/월/일/시/분 필드를 채웁니다 (ToDateTime의 역연산).</summary>
    public DatePickerState FromDateTime(DateTime dateTime)
    {
        Year = dateTime.Year;
        Month = dateTime.Month;
        int day = dateTime.Day;
        DayTens = (day / 10) * 10;
        DayOnes = day % 10;
        Hour = dateTime.Hour;
        int minute = dateTime.Minute;
        MinTens = (minute / 10) * 10;
        MinOnes = minute % 10;

        return this;
    }
}