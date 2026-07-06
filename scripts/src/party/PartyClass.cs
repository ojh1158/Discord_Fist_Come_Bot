using Discord.WebSocket;
using DiscordBot.scripts.db.Models;

namespace DiscordBot.scripts.src.party;

public class PartyClass
{
    public PartyEntity Entity { get; private set; } = null!;

    public ulong UserId;
    public bool IsOwner;
    public bool IsAdmin;
    public bool IsWater;
    public bool IsPartyMember;
    public bool IsNone;
    public string UserNickname;
    public string UserRoleString;

    public async Task<string> Init(PartyEntity? partyEntity, SocketInteraction  interaction, DiscordSocketClient discord)
    {
        if (partyEntity == null)
        {
            return "파티를 찾을 수 없습니다.";
        }
        
        Entity = partyEntity;
        

        UserId = interaction.User.Id;
        IsOwner = partyEntity.OWNER_KEY == UserId;
        if (interaction.User is not SocketGuildUser user)
        {
            return "길드 채팅에서만 사용할 수 있습니다.";
        }

        var findIndex = Entity.MemberOnly.FindIndex(m => m.USER_ID == UserId);
        
        IsAdmin = user.GuildPermissions is { Administrator: true };
        IsWater = findIndex > partyEntity.MAX_COUNT_MEMBER;
        IsPartyMember = findIndex != -1 && findIndex <= partyEntity.MAX_COUNT_MEMBER;
        IsNone = !IsAdmin && !IsWater && findIndex == -1;
        
        // 길드에서 최신 유저 정보를 가져와서 닉네임 확인 (Rest API 사용)
        try
        {
            var restGuild = await discord.Rest.GetGuildAsync(user.Guild.Id);
            if (restGuild != null)
            {
                var guildUserInfo = await restGuild.GetUserAsync(UserId);
                if (guildUserInfo != null && !string.IsNullOrEmpty(guildUserInfo.Nickname))
                {
                    // Rest API에서 가져온 닉네임 사용
                    UserNickname = guildUserInfo.Nickname;
                }
                else
                {
                    // 닉네임이 없으면 Username 사용
                    UserNickname = guildUserInfo?.GlobalName ?? user.Username;
                }
            }
            else
            {
                UserNickname = string.IsNullOrEmpty(user.Nickname) ? user.Username : user.Nickname;
            }
        }
        catch
        {
            // Rest API 호출 실패 시 기존 user 사용
            UserNickname = string.IsNullOrEmpty(user.Nickname) ? user.Username : user.Nickname;
        }
        
        
        UserRoleString = "일반";

        var isMaker = user.Id is Constant.MAKE_USER_ID;
        if (IsWater)
            UserRoleString = "대기자";
        if (IsPartyMember)
            UserRoleString = "파티원";
        if (isMaker)
            UserRoleString = "슈퍼방장";
        if (IsAdmin)
            UserRoleString = "관리자";
        if (IsOwner)
            UserRoleString = "파티장";
        
        if (isMaker)
            IsOwner = true;

        UserRoleString += $"({UserNickname})";
        
        return "";
    }
}