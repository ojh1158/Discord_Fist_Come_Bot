using Discord.Rest;
using Discord.WebSocket;
using DiscordBot.scripts._src.party;
using DiscordBot.scripts.db.Models;
using DiscordBot.scripts.db.Services;
using Serilog;

namespace DiscordBot.scripts._src.Services;

public class SlashCommandServices : BaseServices
{
    private readonly GuildService guildService;
    private readonly PartyService partyService;
    public SlashCommandServices(DiscordServices services, GuildService guildService, PartyService partyService) : base(services)
    {
        Services.client.SlashCommandExecuted += HandleSlashCommandAsync;
        this.guildService = guildService;
        this.partyService = partyService;
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
            
            if (!await guildService.GuildCheckAsync(guildChannel.Id, guildChannel.Guild.Name))
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
        
        if (commandName != "파티")
        {
            await message.ModifyAsync(mp => mp.Content = "알 수 없는 명령입니다.");
            return;
        }
        
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
        
        // 중복 파티 딱히 상관없음....
        // if (await PartyService.IsPartyExistsAsync(partyName, (ulong)command.GuildId!))
        // {
        //     await message.ModifyAsync(mp => mp.Content = "해당 파티 이름이 이미 있습니다.");
        //     return;
        // }

        RestUserMessage msg;
        
        if (startTimeSetFlag)
        {
            msg = await command.Channel.SendMessageAsync($"{Services.ToDiscordUserMention(command.User.Id)} 님이 {partyName} 파티 설정을 하고 있습니다! 잠시만 기다려 주세요...");
            Services.MessageWithExpire(msg, 300, () =>
            {
                partyService.ExpirePartyAsync(msg.Id);
            });
        }
        else
        {
            msg = await command.Channel.SendMessageAsync($"초기화 중입니다...");
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
        
        if (!await partyService.CreatePartyAsync(party))
        {
            await message.DeleteAsync();
            await command.ModifyOriginalResponseAsync(mp => mp.Content = "파티 생성에 실패하였습니다.");
            await Services.RespondMessageWithExpire(command);
            return;
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
}