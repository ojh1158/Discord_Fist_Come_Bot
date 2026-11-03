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

    // 4. 파티 만료 기본값: 생성일 +2일 23:59
    public static DateTime DefaultExpireDate() =>
        DateTime.Now.Date.AddDays(2).AddHours(23).AddMinutes(59);

    // 5. 자연어 입력용 한글 날짜 표시 (모달 기본값용)
    public static string ToNaturalInput(this DateTime dateTime)
    {
        var today = DateTime.Now.Date;
        var day = dateTime.Date == today ? "오늘"
            : dateTime.Date == today.AddDays(1) ? "내일"
            : dateTime.Date == today.AddDays(2) ? "모레"
            : dateTime.ToString("yyyy-MM-dd");
        return dateTime.Minute == 0 ? $"{day} {dateTime.Hour}시" : $"{day} {dateTime.Hour}시 {dateTime.Minute}분";
    }

    // 6. 파티 임베드용 한글 날짜 표시 (구 DatePickerState.ToDisplayString과 동일)
    public static string ToKoreanDisplayString(this DateTime dateTime)
    {
        var now = DateTime.Now;
        var result = "";

        if (now.Year != dateTime.Year)
        {
            result += $"{dateTime.Year}년 {dateTime.Month}월 ";
        }
        else if (now.Month != dateTime.Month)
        {
            result += $"{dateTime.Month}월 ";
        }

        result += $"{dateTime.Day}일 {dateTime.Hour}시 {dateTime.Minute}분";

        return result;
    }
}