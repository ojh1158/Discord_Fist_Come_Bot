namespace DiscordBot.scripts.src.party;

public enum ActionType
{
    _,
    Error,
    PartyNull,
    Exists,
    Wait,
    Join,
    Leave,
    NoExists,
}

public static class ActionTypeExtensions
{
    private static Dictionary<ActionType, string> dic = new()
    {
        { ActionType.Error , "알 수 없는 오류가 나타났습니다."},
        { ActionType.PartyNull , "파티를 찾을 수 없었습니다."},
        { ActionType.Exists , "파티에 이미 참가하고있습니다."},
        { ActionType.Wait , "파티 대기열에 추가되었습니다."},
        { ActionType.Join , "파티에 참가하였습니다."},
        { ActionType.Leave , "파티에서 탈퇴하였습니다."},
        { ActionType.NoExists , "파티에서 이미 나갔습니다."}
    };

    public static string Comment(this ActionType actionType)
    {
        return dic[actionType];
    }
}