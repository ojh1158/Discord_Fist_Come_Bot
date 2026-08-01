using Discord;
using Discord.Rest;
using Discord.WebSocket;
using DiscordBot.scripts.db.Models;
using DiscordBot.scripts.src.party;
using DiscordBot.scripts.src.Services;
using DiscordBot.scripts.src.util;

namespace DiscordBot.scripts.src.Tools;

public class TeamTool(DiscordServices services) : ISingleton
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
    
    public async Task Random(PartyClass partyClass, PartyEntity party, RestUserMessage mg, string? inputCount, int createCount = 0)
    {
        if (inputCount == null || !int.TryParse(inputCount, out var teamCount))
        {
            await mg.ModifyAsync(m => m.Content = "숫자가 유효하지 않습니다.");
            services.MessageWithExpire(mg);
            return;
        }

        var range = (int)MathF.Min(party.MAX_COUNT_MEMBER, party.Members.Count);
        var partyMemberEntities = party.Members[..range];

        string? error = null;

        if (Math.Min(partyMemberEntities.Count, 10) < teamCount)
        {
            if (createCount == 0)
            {
                await mg.ModifyAsync(m => m.Content = "멤버 인원 또는 10개보다 팀이 많을 수 없습니다!");
                services.MessageWithExpire(mg);
                return;
            }
            else
            {
                error = $"{partyMemberEntities.Count}개(팀 인원) 또는 10개보다 팀이 많을 수 없습니다! ({inputCount}개의 팀 생성 시도)";
            }
        }

        createCount++;
        
        var randomList = new List<ulong>();
                
        foreach (var entity in partyMemberEntities) randomList.Add(entity.USER_ID);
                
        // 여기서 셔플
        var rng = System.Random.Shared;
        for (int i = randomList.Count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (randomList[i], randomList[j]) = (randomList[j], randomList[i]);
        }
                
        var result = new List<Embed>();

        if (error != null)
        {
            var info = new EmbedBuilder();
            info.WithTitle($"{error} {DateTime.UtcNow.ToDiscordRelativeTimestamp()}");
            info.WithColor(Color.Red);
            result.Add(info.Build());
        }
        else
        {
            var info = new EmbedBuilder();
            info.WithTitle($"{partyClass.UserRoleString}님의 {createCount}회 파티 {DateTime.Now.ToDiscordRelativeTimestamp()}");
            info.WithColor(Color.Gold);
            result.Add(info.Build());

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
                    teamMembers.Add($"<@{random}> ({partyMemberEntities.Find(f => f.USER_ID == random)?.USER_NICKNAME ?? "알 수 없음"})");
                    memberIndex++;
                }
                        
                var team = new EmbedBuilder();
                team.WithTitle($"{i + 1}팀");
                team.WithColor(colors[i % colors.Length]);
                team.WithDescription(string.Join("\n", teamMembers));
                result.Add(team.Build());
            }
        }

        var cb = new ComponentBuilder();

        cb.WithButton(Constant.TEAM_AGAIN_KEY, $"{Constant.TEAM_AGAIN_KEY}_{party.PARTY_KEY}_{inputCount}_{createCount}", ButtonStyle.Success);
        cb.WithButton(Constant.TEAM_REMOVE_KEY, $"{Constant.TEAM_REMOVE_KEY}_{party.PARTY_KEY}", ButtonStyle.Danger);
                
        await mg.ModifyAsync(m =>
        {
            m.Components = cb.Build();
            m.Content = null;
            m.Embeds = result.ToArray();
        });
    }
}