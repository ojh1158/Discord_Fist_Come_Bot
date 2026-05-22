namespace DiscordBot.scripts.src.party;

public enum JoinType
{
    Error,
    Exists,
    Wait,
    Join
}

public static class JoinTypeExtensions
{
    private static Dictionary<JoinType, string> dic = new()
    {
        { JoinType.Error , "알 수 없는 오류가 나타났습니다."},
        { JoinType.Exists , "이미 참가하고있습니다."},
        { JoinType.Wait , "대기열에 추가되었습니다"},
        { JoinType.Join , "파티에 참가하였습니다."},
    };

    public static string GetComment(this JoinType joinType)
    {
        return dic[joinType];
    }
}