using System.ComponentModel;
using Discord;
using Discord.WebSocket;
using DiscordBot.scripts._src.party;
using DiscordBot.scripts.db.Models;
using DiscordBot.scripts.db.Services;
using Serilog;

namespace DiscordBot.scripts._src.Services;

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
    
    public ModalServices(DiscordServices services, PartyService partyService) : base(services)
    {
        Services.client.ModalSubmitted += HandleModalAsync;
        this.partyService = partyService;
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

                    if (partyClass is { isOwner: false, isAdmin: false })
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

                if (Math.Min(party.Members.Count, 10) < teamCount)
                {
                    await modal.ModifyOriginalResponseAsync(m => m.Content = "멤버 인원 또는 10개보다 팀이 많을 수 없습니다!");
                    _ = Services.RespondMessageWithExpire(modal);
                    return;
                }
                
                var randomList = new List<ulong>();
                
                foreach (var entity in party.Members) randomList.Add(entity.USER_ID);
                
                // 여기서 셔플
                var rng = Random.Shared;
                for (int i = randomList.Count - 1; i > 0; i--)
                {
                    var j = rng.Next(i + 1);
                    (randomList[i], randomList[j]) = (randomList[j], randomList[i]);
                }
                
                var result = new List<Embed>();

                int membersPerTeam = (int)Math.Ceiling((double)randomList.Count / teamCount);
                int memberIndex = 0;

                for (int i = 0; i < teamCount; i++)
                {
                    // 현재 팀에 할당할 멤버 수 계산
                    int currentTeamSize = membersPerTeam;
                    if (i == teamCount - 1)
                    {
                        // 마지막 팀은 나머지 멤버 모두 할당
                        currentTeamSize = randomList.Count - memberIndex;
                    }
                    
                    // 현재 팀의 멤버 리스트 생성
                    var teamMembers = new List<string>();
                    for (int j = 0; j < currentTeamSize && memberIndex < randomList.Count; j++)
                    {
                        var random = randomList[memberIndex];
                        teamMembers.Add($"<@{random}> ({party.Members.Find(f => f.USER_ID == random)?.USER_NICKNAME ?? "알 수 없음"})");
                        memberIndex++;
                    }
                    
                    var team = new EmbedBuilder();
                    team.WithTitle($"{i + 1}팀");
                    team.WithColor(colors[i % colors.Length]);
                    team.WithDescription(string.Join("\n", teamMembers));
                    result.Add(team.Build());
                }
                
                if (modal.HasResponded)
                {
                    await modal.DeleteOriginalResponseAsync();
                }
                else
                {
                    await modal.RespondAsync("생성하였습니다", ephemeral: true);
                }

                var cb = new ComponentBuilder();

                var mg = await modal.Channel.SendMessageAsync("초기화 중...");
                cb.WithButton(Constant.TEAM_REMOVE_KEY, $"{Constant.TEAM_REMOVE_KEY}_{mg.Id}", ButtonStyle.Danger);

                
                await mg.ModifyAsync(m =>
                {
                    m.Components = cb.Build();
                    m.Content = $"{partyClass.userRoleString} 님이 {teamCount}개의 팀을 뽑았습니다!";
                    m.Embeds = result.ToArray();
                });
                return;
        }
        
        
        await Services.UpdateMessage(modal, party, false, "");
    }
}