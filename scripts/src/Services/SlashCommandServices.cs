using System.Collections.Concurrent;
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
using DiscordBot.scripts.src.util;
using Serilog;
using ActionType = DiscordBot.scripts.src.party.ActionType;

namespace DiscordBot.scripts.src.Services;

public class SlashCommandServices : BaseServices
{
    public readonly DiscordSocketClient Client;
    private readonly GuildService _guildService;
    private readonly PartyService _partyService;
    private readonly PartyQueueServices _partyQueueServices;
    private readonly ConcurrentDictionary<string, PendingCreate> pendingCreates = new();

    // 모달-퍼스트 생성용 임시 보관 (모달은 첫 응답이어야 해서 파티보다 먼저 띄운다)
    public record PendingCreate(
        string Name,
        int Count,
        ulong? VoiceChannelId,
        string AppointeeInput,
        ulong GuildId,
        ulong ChannelId,
        ulong OwnerId,
        string OwnerNickname,
        DateTime CreatedAt,
        Task<PartyEntity?> PartyTask);

    public string AddPending(PendingCreate pending)
    {
        foreach (var kv in pendingCreates)
        {
            if (DateTime.Now - kv.Value.CreatedAt > TimeSpan.FromMinutes(30))
                pendingCreates.TryRemove(kv.Key, out _);
        }
        var id = Guid.NewGuid().ToString();
        pendingCreates[id] = pending;
        return id;
    }

    public bool TakePending(string id, out PendingCreate? pending) =>
        pendingCreates.TryRemove(id, out pending);

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
                    name: "내정자목록",
                    type: ApplicationCommandOptionType.String,
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
        // /파티는 항상 시간 입력 모달을 첫 응답으로 바로 띄운다 (모달은 첫 응답이어야 함).
        // 파티 생성은 백그라운드로 돌리고, 제출 시점에 시간을 합친다.
        if (command.Data.Name != "파티")
        {
            await command.RespondAsync("알 수 없는 명령입니다.", ephemeral: true);
            return;
        }

        if (command.Channel is not SocketGuildChannel guildChannel)
        {
            await command.RespondAsync("서버에서만 사용 가능합니다.", ephemeral: true);
            return;
        }

        // 길드 검증(DB USE_COUNT)은 실제 생성 시점에 수행, 여기서는 권한만 선검사
        var preError = CheckChannelPermissions(guildChannel);
        if (preError is not null)
        {
            await command.RespondAsync(preError, ephemeral: true);
            return;
        }
        await OpenCreateTimeModalAsync(command, guildChannel);
    }

    private static string? CheckChannelPermissions(SocketGuildChannel guildChannel)
    {
        // 봇의 현재 권한 가져오기
        var permissions = guildChannel.Guild.CurrentUser.GetPermissions(guildChannel);

        // 필요한 권한 체크 (채널 보기 & 메시지 보내기)
        if (!permissions.ViewChannel)
            return "🚫 이 채널에 대한 접근 권한이 없습니다. 권한을 확인해주세요.";

        if (!permissions.SendMessages)
            return "🚫 이 채널에 대한 메시지 전송 권한이 없습니다. 권한을 확인해주세요.";

        // 메시지 기록 보기 권한 체크
        if (!permissions.ReadMessageHistory)
            return "🚫 이 채널의 '메시지 기록 보기' 권한이 없습니다.";

        return null;
    }

    // /파티: 생성 정보를 임시 보관하고 시간 입력 모달을 첫 응답으로 띄운다.
    // 파티 본체는 백그라운드에서 바로 만들고, 제출 시점에 시간만 합친다. 만료는 +2일 23:59 고정.
    private async Task OpenCreateTimeModalAsync(SocketSlashCommand command, SocketGuildChannel guildChannel)
    {
        var commandOptions = command.Data.Options;
        var name = commandOptions.FirstOrDefault(x => x.Name == "이름")?.Value?.ToString() ?? "";
        var countStr = commandOptions.FirstOrDefault(x => x.Name == "인원")?.Value?.ToString() ?? "";

        if (string.IsNullOrWhiteSpace(name) || !int.TryParse(countStr, out var count)
            || count is < Constant.MIN_COUNT or > Constant.MAX_COUNT)
        {
            await command.RespondAsync(
                $"파티 이름과 인원(최소 {Constant.MIN_COUNT} 최대 {Constant.MAX_COUNT})을 확인해주세요.",
                ephemeral: true);
            return;
        }

        var voiceId = (commandOptions.FirstOrDefault(x => x.Name == "채널선택")?.Value as SocketVoiceChannel)?.Id;
        var rawAppointees = commandOptions.FirstOrDefault(x => x.Name == "내정자목록")?.Value?.ToString() ?? "";
        var ownerId = command.User.Id;
        var ownerName = command.User is SocketGuildUser guildUser ? guildUser.DisplayName : command.User.Username;

        var tcs = new TaskCompletionSource<PartyEntity?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = new PendingCreate(
            name, count, voiceId, rawAppointees,
            guildChannel.Guild.Id, guildChannel.Id,
            ownerId, ownerName, DateTime.Now, tcs.Task);
        var pendingId = AddPending(pending);

        var modal = new ModalBuilder()
            .WithTitle("시작 시간 입력")
            .WithCustomId($"{Constant.DATE_CREATE_KEY}_{pendingId}")
            .AddTextDisplay(Constant.DATE_CREATE_EXAMPLES)
            .AddTextInput("날짜/시간 (비워두면 시간 없이 생성)", Constant.DATE_INPUT_FIELD, TextInputStyle.Short,
                placeholder: Constant.DATE_INPUT_PLACEHOLDER,
                required: false,
                value: null,
                minLength: 0,
                maxLength: 100)
            .AddTextInput("만료 날짜/시간 (2일 후 23:59 자동)", Constant.DATE_INPUT_FIELD_EXPIRE, TextInputStyle.Short,
                placeholder: Constant.DATE_INPUT_PLACEHOLDER,
                required: false,
                value: ExtensionMethods.DefaultExpireDate().ToNaturalInput(),
                minLength: 0,
                maxLength: 100)
            .Build();

        await command.RespondWithModalAsync(modal);

        _ = Task.Run(async () =>
        {
            try
            {
                tcs.TrySetResult(await BuildPartyAsync(pending));
            }
            catch (Exception ex)
            {
                Log.Error($"파티 백그라운드 생성 실패: {ex.Message}\n{ex.StackTrace}");
                tcs.TrySetResult(null);
            }
        });
    }

    // 모달이 떠 있는 동안 백그라운드에서 파티 본체 생성 (채널 메시지는 자유롭게 사용)
    public async Task<PartyEntity?> BuildPartyAsync(PendingCreate pending)
    {
        var guild = Services.client.GetGuild(pending.GuildId);
        if (guild is null)
            return null;

        if (!await _guildService.GuildCheckAsync(pending.GuildId, guild.Name))
        {
            var channel = guild.GetTextChannel(pending.ChannelId);
            if (channel is not null)
                await channel.SendMessageAsync("🚫 이 채널을 검증할 수 없거나 제한되었습니다.");
            return null;
        }

        var targetChannel = guild.GetTextChannel(pending.ChannelId);
        if (targetChannel is null)
            return null;

        var msg = await targetChannel.SendMessageAsync("초기화 중입니다...");

        var appointeeUserIds = await ResolveAppointeesAsync(
            pending.GuildId, pending.AppointeeInput, pending.OwnerId, pending.OwnerNickname);

        var party = new PartyEntity
        {
            DISPLAY_NAME = pending.Name,
            PARTY_KEY = Guid.NewGuid().ToString(),
            MAX_COUNT_MEMBER = pending.Count,
            MESSAGE_KEY = msg.Id,
            GUILD_KEY = pending.GuildId,
            CHANNEL_KEY = pending.ChannelId,
            OWNER_KEY = pending.OwnerId,
            OWNER_NICKNAME = pending.OwnerNickname,
            EXPIRE_DATE = ExtensionMethods.DefaultExpireDate(),
            VOICE_CHANNEL_KEY = pending.VoiceChannelId
        };

        if (!await _partyService.CreatePartyAsync(party))
        {
            try { await msg.DeleteAsync(); } catch { }
            await targetChannel.SendMessageAsync("파티 생성에 실패하였습니다.");
            return null;
        }

        if (appointeeUserIds.Count != 0)
        {
            foreach (var (key, value) in appointeeUserIds)
            {
                await _partyService.JoinPartyAsync(party.PARTY_KEY, key, value);
            }

            var isFullAlert = party.Members.Count < party.MAX_COUNT_MEMBER &&
                          party.Members.Count + appointeeUserIds.Count >= party.MAX_COUNT_MEMBER;

            var fresh = await _partyService.GetPartyEntityAsync(party.PARTY_KEY);
            if (fresh is not null)
                party = fresh;

            if (appointeeUserIds.Count > 1)
            {
                if (isFullAlert)
                {
                    var owner = await Services.client.GetUserAsync(pending.OwnerId);
                    if (owner is not null)
                        Services.SendUserAlert(party, owner, Constant.JOIN_AUTO_KEY);
                }

                var stringBuilder = new StringBuilder();

                stringBuilder.AppendLine($"[**{party.DISPLAY_NAME}**]파티에서 아래의 파티원이 내정자로 선정되었습니다!");

                foreach (var (id, name) in appointeeUserIds)
                {
                    stringBuilder.AppendLine($"<@{id}> {name}님이 {ActionType.Join.Comment()}");
                }

                await targetChannel.SendMessageAsync(stringBuilder.ToString());
            }
        }

        var updatedEmbed = await Services.UpdatedEmbed(party);
        var component = Services.UpdatedComponent(party);

        await msg.ModifyAsync(m =>
        {
            m.Content = "";
            m.Embed = updatedEmbed;
            m.Components = component;
        });

        return party;
    }

    // 멘션 문자열에서 내정자 해결 (명령어 방장 자동 포함, Bot 제외)
    private async Task<Dictionary<ulong, string>> ResolveAppointeesAsync(
        ulong guildId, string rawInput, ulong ownerId, string ownerNickname)
    {
        var result = new Dictionary<ulong, string>();
        var input = $"<@{ownerId}> " + rawInput;

        var guild = Services.client.GetGuild(guildId);
        var restGuild = await Services.client.Rest.GetGuildAsync(guildId);

        if (!string.IsNullOrWhiteSpace(input))
        {
            var matches = System.Text.RegularExpressions.Regex.Matches(input, @"<@(?!&)(!?\d+)>");

            foreach (System.Text.RegularExpressions.Match match in matches)
            {
                var idString = match.Groups[1].Value.Replace("!", "");

                if (ulong.TryParse(idString, out ulong userId))
                {
                    IGuildUser? guildUser = guild?.GetUser(userId);
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

                        result.TryAdd(guildUser.Id, guildUser.DisplayName);
                    }
                }
            }
        }

        result.TryAdd(ownerId, ownerNickname);
        return result;
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