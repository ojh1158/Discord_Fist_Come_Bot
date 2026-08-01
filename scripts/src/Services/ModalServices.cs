using Discord;
using Discord.WebSocket;
using DiscordBot.scripts.db.Models;
using DiscordBot.scripts.db.Services;
using DiscordBot.scripts.src.party;
using DiscordBot.scripts.src.Tools;
using Serilog;

namespace DiscordBot.scripts.src.Services;

public class ModalServices : BaseServices
{
    private readonly Color[] colors = new []
    {
        Color.Red,
        Color.Blue,
        Color.Green,
        Color.Gold,
        Color.Purple,
        Color.Magenta,
        Color.Orange,
        Color.Teal,
        Color.DarkGreen,
        Color.DarkBlue,
        Color.DarkRed,
        Color.DarkOrange,
        Color.DarkPurple,
        Color.DarkTeal,
        Color.DarkMagenta,
    };
    
    private readonly PartyService partyService;
    private readonly TeamTool teamTool;
    
    public ModalServices(DiscordServices services, PartyService partyService, TeamTool teamTool) : base(services)
    {
        Services.client.ModalSubmitted += HandleModalAsync;
        this.partyService = partyService;
        this.teamTool = teamTool;
    }

    private async Task HandleModalAsync(SocketModal modal)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await ModalAsync(modal);
            }
            catch (Exception e)
            {
                Log.Error($"{e.Message}\n{e.StackTrace}");
            }
        });

        await Task.CompletedTask;
    }
    
    
    
    private async Task ModalAsync(SocketModal modal)
    {
        var customId = modal.Data.CustomId;
        
        // CustomId 파싱: "{action}_{messageId}" 또는 "{action}_{messageId}_{extra}"
        // 하위 호환성: "party_{action}_{messageId}" 형식도 지원
        var parts = customId.Split('_');
        if (parts.Length < 2)
            return;
        
        string action;
        int messageIdIndex;
        
        // 이전 형식 지원: "party_{action}_{messageId}"
        if (parts[0] == "party" && parts.Length >= 3)
        {
            action = parts[1];
            messageIdIndex = 2;
        }
        // 새로운 형식: "{action}_{messageId}"
        else
        {
            action = parts[0];
            messageIdIndex = 1;
        }
        
        if (messageIdIndex >= parts.Length)
            return;
        
        var partyKey = parts[messageIdIndex];

        await InitCommands(modal, action);

        var partyEntity = await partyService.GetPartyEntityAsync(partyKey);

        var partyClass = new PartyClass();
        await partyClass.Init(partyEntity, modal, Services.client);
        var party = partyClass.Entity;

        var message = "";


        switch (action)
        {
            case Constant.SETTING_MODEL_KEY:
                var renameOk = true;
                var resizeOk = true;
                
                // 입력값 가져오기
                var countInput = modal.Data.Components.FirstOrDefault(c => c.CustomId == "count");
                int teamCount = party.MAX_COUNT_MEMBER;
                if (countInput == null || !int.TryParse(countInput.Value, out teamCount))
                {
                    message += $"인원 오류: 유호한 숫자를 입력해주세요.\n";
                    resizeOk = false;
                }

                if (party.MAX_COUNT_MEMBER != teamCount)
                {
                    // 범위 체크
                    if (teamCount < 1 || teamCount > Constant.MAX_COUNT)
                    {
                        message += $"인원 오류: 파티 인원은 {1}~{Constant.MAX_COUNT} 사이여야 합니다.\n";
                        resizeOk = false;
                    }

                    if (partyClass is { IsOwner: false, IsAdmin: false })
                    {
                        message += $"인원 오류: 파티장 또는 관리자만 인원을 변경할 수 있습니다.\n";
                        resizeOk = false;
                    }

                    if (resizeOk)
                    {
                        await partyService.ResizePartyAsync(party, teamCount);
                        
                        party.MAX_COUNT_MEMBER = teamCount;
                        message += $"인원: 인원을 변경하였습니다.\n";
                    }
                }
                        
                var nameInput = modal.Data.Components.FirstOrDefault(c => c.CustomId == "name");
                var name = nameInput?.Value ?? "";
                if (string.IsNullOrEmpty(name))
                {
                    renameOk = false;
                }

                if (renameOk && name != party.DISPLAY_NAME)
                {
                    if (await partyService.PartyRename(partyKey, name))
                    {
                        message += "제목: 제목을 변경하였습니다.\n";
                        party.DISPLAY_NAME = name;
                    }
                    else
                    {
                        message += "제목 오류: 제목을 변경할 수 없었습니다.\n";
                    }
                }

                if (message == "")
                {
                    message = "설정이 취소되었습니다.";
                }
                await modal.ModifyOriginalResponseAsync(m => m.Content = message);
                _ = Services.RespondMessageWithExpire(modal); 
                break;
            case Constant.TEAM_KEY:
                countInput = modal.Data.Components.FirstOrDefault(c => c.CustomId == "count");

                if (countInput == null || !int.TryParse(countInput.Value, out teamCount))
                {
                    await modal.ModifyOriginalResponseAsync(m => m.Content = "숫자가 유효하지 않습니다.");
                    _ = Services.RespondMessageWithExpire(modal);
                    return;
                }

                var range = (int)MathF.Min(party.MAX_COUNT_MEMBER, party.Members.Count);
                var partyMemberEntities = party.Members[..range];

                if (Math.Min(partyMemberEntities.Count, 10) < teamCount)
                {
                    await modal.ModifyOriginalResponseAsync(m => m.Content = "멤버 인원 또는 10개보다 팀이 많을 수 없습니다!");
                    _ = Services.RespondMessageWithExpire(modal);
                    return;
                }
                
                var mg = await modal.Channel.SendMessageAsync("초기화 중...");
                await teamTool.Random(partyClass, party, mg, countInput.Value);
                if (modal.HasResponded)
                {
                    await modal.DeleteOriginalResponseAsync();
                }
                else
                {
                    await modal.RespondAsync("생성하였습니다", ephemeral: true);
                }
                
                return;
        }
        
        
        await Services.UpdateMessage(modal, party, false, "");
    }
}