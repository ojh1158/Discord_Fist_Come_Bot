using System.Text;
using Camille.Enums;
using Camille.RiotGames;
using Discord;
using Discord.Interactions;
using Discord.Rest;
using Discord.WebSocket;
using DiscordBot.scripts.config;
using DiscordBot.scripts.db.Models;
using DiscordBot.scripts.db.Services;
using DiscordBot.scripts.src.party;
using Serilog;
using ActionType = DiscordBot.scripts.src.party.ActionType;

namespace DiscordBot.scripts.src.Services;

public class SlashCommandServices : BaseServices
{
    public readonly DiscordSocketClient Client;
    private readonly GuildService _guildService;
    private readonly PartyService _partyService;
    private readonly PartyQueueServices _partyQueueServices;
    public SlashCommandServices(DiscordSocketClient discord, DiscordServices discordServices, GuildService guildService, PartyService partyService, PartyQueueServices partyQueueServices) : base(discordServices)
    {
        Client = discord;
        _guildService = guildService;
        _partyService = partyService;
        _partyQueueServices = partyQueueServices;
        
        Services.client.SlashCommandExecuted += HandleSlashCommandAsync;
        Client.Ready += () => ReadyAsync(Client);
    }
    
    private async Task ReadyAsync(DiscordSocketClient client)
    {
        
        var commands = await client.GetGlobalApplicationCommandsAsync();

        var array = new List<SlashCommandBuilder>
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
                ).AddOption(
                    name: "내정자목록",
                    type: ApplicationCommandOptionType.String, // 💡 String으로 변경!
                    description: "@멘션으로 내정자들을 연달아 태그해주세요. (예: @홍길동 @임꺽정)",
                    isRequired: false
                )
                .AddOption(
                    name: "채널선택",
                    type: ApplicationCommandOptionType.Channel,
                    description: "파티가 모일 채널을 선택하세요.",
                    channelTypes: [ChannelType.Voice],
                    isRequired: false
                )
        };

        // if (Config.Riot.Enable)
        // {
        //     new SlashCommandBuilder()
        //         .WithName("")
        // }

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

    private async Task HandleSlashCommandAsync(SocketSlashCommand command)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await SlashCommandAsync(command);
            }
            catch (Exception e)
            {
                Log.Error($"{e.Message}\n{e.StackTrace}");
            }
        });

        await Task.CompletedTask;
    }
    

    private async Task SlashCommandAsync(SocketSlashCommand command)
    {
        await command.RespondAsync("초기화 중입니다...", ephemeral: true);
        var message = await command.GetOriginalResponseAsync();
        
        
        var commandName = command.Data.Name;
        
        if (command.Channel is SocketGuildChannel guildChannel)
        {
            // 봇의 현재 권한 가져오기
            var permissions = guildChannel.Guild.CurrentUser.GetPermissions(guildChannel);

            // 필요한 권한 체크 (채널 보기 & 메시지 보내기)
            if (!permissions.ViewChannel)
            {
                await message.ModifyAsync(mp => mp.Content = "🚫 이 채널에 대한 접근 권한이 없습니다. 권한을 확인해주세요.");
                return;
            }

            if (!permissions.SendMessages)
            {
                await message.ModifyAsync(mp => mp.Content = "🚫 이 채널에 대한 메시지 전송 권한이 없습니다. 권한을 확인해주세요.");
                return;
            }

            // 메시지 기록 보기 권한 체크
            if (!permissions.ReadMessageHistory)
            {
                await message.ModifyAsync(mp => mp.Content = "🚫 이 채널의 '메시지 기록 보기' 권한이 없습니다.");
                return;
            }
            
            if (!await _guildService.GuildCheckAsync(guildChannel.Id, guildChannel.Guild.Name))
            {
                await message.ModifyAsync(mp => mp.Content = "🚫 이 채널을 검증할 수 없거나 제한되었습니다.");
                return;
            }
        }
        else
        {
            await message.ModifyAsync(mp => mp.Content = "서버에서만 사용 가능합니다.");
            return;
        }
        
        switch (commandName)
        {
            case "파티":
                await Party(command, message, guildChannel);
                break;
            // case "등록":
            //     await LeagueAccount(command, message, guildChannel);
            //     break;
            default:
                await message.ModifyAsync(mp => mp.Content = "알 수 없는 명령입니다.");
                return;
        }
    }

    private async Task Party(SocketSlashCommand command, RestInteractionMessage message, SocketGuildChannel guildChannel)
    {
        var commandOptions = command.Data.Options;
        var nameOption = commandOptions.FirstOrDefault(x => x.Name == "이름");
        var countOption = commandOptions.FirstOrDefault(x => x.Name == "인원");
        var choseChannel = commandOptions.FirstOrDefault(x => x.Name == "채널선택")?.Value as SocketVoiceChannel;
        var startTimeSetFlag = commandOptions.FirstOrDefault(opt => opt.Name == "시작시간설정")?.Value as bool? ?? false;
        
        if (nameOption?.Value == null || countOption?.Value == null || !int.TryParse(countOption.Value.ToString(), out var count))
        {
            await message.ModifyAsync(mp => mp.Content = "명령어에 오류가 있습니다.");
            return;
        }

        if (count is < Constant.MIN_COUNT or > Constant.MAX_COUNT)
        {
            await message.ModifyAsync(mp => mp.Content = $"파티 인원은 최소 {Constant.MIN_COUNT} 최대 {Constant.MAX_COUNT}까지만 지정할 수 있습니다.");
            return;
        }
                
        var partyName = nameOption.Value.ToString()!;

        RestUserMessage msg;
        
        if (startTimeSetFlag)
        {
            msg = await command.Channel.SendMessageAsync($"{Services.ToDiscordUserMention(command.User.Id)} 님이 {partyName} 파티 설정을 하고 있습니다! 잠시만 기다려 주세요...");
            Services.MessageWithExpire(msg, 300, () =>
            {
                _partyService.ExpirePartyAsync(msg.Id);
            });
        }
        else
        {
            msg = await command.Channel.SendMessageAsync($"초기화 중입니다...");
        }
        
        var appointeeInput = commandOptions.FirstOrDefault(x => x.Name == "내정자목록")?.Value?.ToString();
        var appointeeUserIds = new Dictionary<ulong, string>();

        var restGuild = await Services.client.Rest.GetGuildAsync(command.GuildId ?? 0);

        if (!string.IsNullOrWhiteSpace(appointeeInput))
        {
            var matches = System.Text.RegularExpressions.Regex.Matches(appointeeInput, @"<@(?!&)(!?\d+)>");
    
            foreach (System.Text.RegularExpressions.Match match in matches)
            {
                var idString = match.Groups[1].Value.Replace("!", "");
        
                if (ulong.TryParse(idString, out ulong userId))
                {
                    IGuildUser? guildUser = guildChannel.GetUser(userId);
                    if (guildUser == null)
                    {
                        try
                        {
                            guildUser = await restGuild.GetUserAsync(userId);
                        }
                        catch
                        {
                            guildUser = null; 
                        }
                    }

                    if (guildUser != null)
                    {
                        if (guildUser.IsBot) continue;

                        appointeeUserIds.TryAdd(guildUser.Id, guildUser.DisplayName);
                    }
                }
            }
        }

        var party = new PartyEntity
        {
            DISPLAY_NAME = partyName,
            PARTY_KEY = Guid.NewGuid().ToString(),
            MAX_COUNT_MEMBER = count,
            MESSAGE_KEY = msg.Id,
            GUILD_KEY = (ulong)command.GuildId!,
            CHANNEL_KEY = (ulong)command.ChannelId!,
            OWNER_KEY = command.User.Id,
            OWNER_NICKNAME = command.User is SocketGuildUser user
                ? user.DisplayName
                : command.User.Username,
            EXPIRE_DATE = null,
            VOICE_CHANNEL_KEY = choseChannel?.Id
        };
        
        if (!await _partyService.CreatePartyAsync(party))
        {
            await message.DeleteAsync();
            await command.ModifyOriginalResponseAsync(mp => mp.Content = "파티 생성에 실패하였습니다.");
            await Services.RespondMessageWithExpire(command);
            return;
        }
        
        if (appointeeUserIds.Count != 0)
        {
            ulong[] userIds = new ulong[appointeeUserIds.Count];
            string[] names = new string[appointeeUserIds.Count];

            var dicCount = 0;
            foreach (var (key, value) in appointeeUserIds)
            {
                userIds[dicCount] = key;
                names[dicCount++] = value;
            }
            
            var isFullAlart = party.Members.Count < party.MAX_COUNT_MEMBER &&
                          party.Members.Count + appointeeUserIds.Count >= party.MAX_COUNT_MEMBER;
            
            await _partyQueueServices.QueueMany(party.PARTY_KEY, userIds, names, ActionType.Join, command);
            
            party = await _partyService.GetPartyEntityAsync(party.PARTY_KEY);
            
            if (party is not null)
            {
                await Services.UpdateMessage(command, party);
                
                if (isFullAlart)
                {
                    Services.SendUserAlert(party, command.User, Constant.JOIN_AUTO_KEY);
                }

                var stringBuilder = new StringBuilder();

                stringBuilder.AppendLine($"[**{party.DISPLAY_NAME}**]파티에서 아래의 파티원이 내정자로 선정되었습니다!");
                
                foreach (var (id, name) in appointeeUserIds)
                {
                    stringBuilder.AppendLine($"<@{id}> {name}님이 {ActionType.Join.Comment()}");
                }
                
                await command.Channel.SendMessageAsync(stringBuilder.ToString());
            }
        }

        if (startTimeSetFlag)
        {
            var datePickup = Services.CreateDatePickup(Constant.START_TIME_OPEN_KEY, party);
            
            await message.ModifyAsync(mp =>
            {
                mp.Content = datePickup.Content;
                mp.Embed = datePickup.Embed;
                mp.Components = datePickup.Components;
            });
        }
        else
        {
            var updatedEmbed = await Services.UpdatedEmbed(party);
            var component = Services.UpdatedComponent(party);

            await msg.ModifyAsync(m =>
            {
                m.Content = "";
                m.Embed = updatedEmbed;
                m.Components = component;
            });
            
            await message.ModifyAsync(mp => mp.Content = "파티를 생성하였습니다!");
            Services.MessageWithExpire(message);
        }
    }

    // private async Task LeagueAccount(SocketSlashCommand command, RestInteractionMessage message,
    //     SocketGuildChannel guildChannel)
    // {
    //     await command.DeferAsync(ephemeral: true); // 봇이 생각 중... 표시 (나만 보기)
    //
    //     try
    //     {
    //         // 1. 라이엇 API로 실제 존재하는 유저인지 확인 및 PUUID 추출
    //         var account = await RiotApi.AccountV1().GetByRiotIdAsync(RegionalRoute.ASIA, nickName, tags);
    //         
    //         if (account == null)
    //         {
    //             await FollowupAsync("라이엇 계정을 찾을 수 없습니다. 닉네임과 태그를 확인해주세요.");
    //             return;
    //         }
    //
    //         // 2. 명령어 입력한 유저 정보 가져오기
    //         ulong discordId = Context.User.Id;
    //         string discordName = Context.User.Username;
    //
    //         await FollowupAsync($"🟢 연동 성공! [{discordName}]님은 이제 [{account.GameName}#{account.TagLine}]으로 활동합니다.");
    //     }
    //     catch (Exception ex)
    //     {
    //         await FollowupAsync($"오류가 발생했습니다: {ex.Message}");
    //     }
    // }
}