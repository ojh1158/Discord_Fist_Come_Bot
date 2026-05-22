namespace DiscordBot.scripts.src.util;

public static class ExtensionMethods
{
    // 1. KST 시간을 UTC Offset이 포함된 객체로 변환
    public static DateTimeOffset ToKstOffset(this DateTime dateTime)
    {
        var kstOffset = TimeSpan.FromHours(9);
        // 입력된 dateTime의 틱(Ticks)을 그대로 쓰되, KST(+9) 시간대임을 명시합니다.
        return new DateTimeOffset(dateTime.Ticks, kstOffset);
    }

    // 2. .To 다음에 올 코드: ToUnixTimeSeconds()
    public static long ToUtcUnixTimeSeconds(this DateTime dateTime)
    {
        // KST로 변환된 Offset 객체에서 바로 Unix 초를 뽑아냅니다.
        // 이 메서드는 내부적으로 자동으로 UTC로 계산하여 변환해줍니다.
        return dateTime.ToKstOffset().ToUnixTimeSeconds();
    }

    // 3. 디스코드 상대적 시간 태그 생성
    public static string ToDiscordRelativeTimestamp(this DateTime dateTime)
    {
        return $"<t:{dateTime.ToUtcUnixTimeSeconds()}:R>";
    }
}