using Discord.WebSocket;
using DiscordBot.scripts.db.Models;

namespace DiscordBot.scripts._src.party;

public class PartyClass
{
    public PartyEntity Entity { get; private set; } = null!;

    public SocketGuildUser guildUser = null!;
    
    public ulong userId;
    public bool isOwner;
    public bool isAdmin;
    public bool isWater;
    public bool isPartyMember;
    public bool isNone;
    public string userNickname;
    public string userRoleString;

    public async Task<string> Init(PartyEntity? partyEntity, SocketInteraction  interaction, DiscordSocketClient discord)
    {
        if (partyEntity == null)
        {
            return "파티를 찾을 수 없습니다.";
        }
        
        Entity = partyEntity;
        

        userId = interaction.User.Id;
        isOwner = partyEntity.OWNER_KEY == userId;
        if (interaction.User is not SocketGuildUser user)
        {
            return "길드 채팅에서만 사용할 수 있습니다.";
        }

        var findIndex = Entity.Members.FindIndex(m => m.USER_ID == userId);
        
        guildUser = user;
        isAdmin = user.GuildPermissions is { Administrator: true };
        isWater = findIndex > partyEntity.MAX_COUNT_MEMBER;
        isPartyMember = findIndex <= partyEntity.MAX_COUNT_MEMBER;
        isNone = !isAdmin && !isWater && findIndex == -1;
        
        // 길드에서 최신 유저 정보를 가져와서 닉네임 확인 (Rest API 사용)
        try
        {
            var restGuild = await discord.Rest.GetGuildAsync(user.Guild.Id);
            if (restGuild != null)
            {
                var guildUserInfo = await restGuild.GetUserAsync(userId);
                if (guildUserInfo != null && !string.IsNullOrEmpty(guildUserInfo.Nickname))
                {
                    // Rest API에서 가져온 닉네임 사용
                    userNickname = guildUserInfo.Nickname;
                }
                else
                {
                    // 닉네임이 없으면 Username 사용
                    userNickname = guildUserInfo?.GlobalName ?? user.Username;
                }
            }
            else
            {
                userNickname = string.IsNullOrEmpty(user.Nickname) ? user.Username : user.Nickname;
            }
        }
        catch
        {
            // Rest API 호출 실패 시 기존 user 사용
            userNickname = string.IsNullOrEmpty(user.Nickname) ? user.Username : user.Nickname;
        }
        
        
        userRoleString = "일반";

        var isMaker = user.Id is Constant.MAKE_USER_ID;
        if (isWater)
            userRoleString = "대기자";
        if (isPartyMember)
            userRoleString = "파티원";
        if (isMaker)
            userRoleString = "슈퍼방장..?";
        if (isAdmin)
            userRoleString = "관리자";
        if (isOwner)
            userRoleString = "파티장";
        
        if (isMaker)
            isOwner = true;

        userRoleString += $"({userNickname})";
        
        return "";
    }
}