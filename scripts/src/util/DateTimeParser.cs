using System.Globalization;
using System.Text.RegularExpressions;

namespace DiscordBot.scripts.src.util;

/// <summary>모달 자유입력용 한국어 시간 파서 (KST, DateTime.Now 기준).</summary>
public static class DateTimeParser
{
    public const string HelpText = "예: 2026-01-01 23:00 / 11/5 20:00 / 내일 20시 / 모레 19시반 / 금 20시 / 3시간 후 / 22.5=22시30분 (삭제: 없음)";

    private static readonly string[] ClearWords =
        ["없음", "미정", "삭제", "지움", "지우기", "제거", "취소", "해제", "-", "x", "X"];

    private static readonly string[] DateTimeFormats =
    [
        "yyyy-M-d H:m", "yyyy-M-d H:mm", "yyyy-M-d HH:mm",
        "yyyy/M/d H:mm", "yyyy/M/d HH:mm", "yyyy.M.d H:mm", "yyyy.M.d HH:mm",
        "M-d H:mm", "M-d HH:mm", "M/d H:mm", "M/d HH:mm", "M.d H:mm", "M.d HH:mm",
    ];

    private static readonly string[] DateOnlyFormats =
        ["yyyy-M-d", "yyyy/M/d", "yyyy.M.d", "M-d", "M/d", "M.d"];

    private static readonly string[] TimeOnlyFormats = ["H:mm", "HH:mm"];

    private static readonly Dictionary<string, DayOfWeek> Weekdays = new()
    {
        ["일"] = DayOfWeek.Sunday,
        ["월"] = DayOfWeek.Monday,
        ["화"] = DayOfWeek.Tuesday,
        ["수"] = DayOfWeek.Wednesday,
        ["목"] = DayOfWeek.Thursday,
        ["금"] = DayOfWeek.Friday,
        ["토"] = DayOfWeek.Saturday,
    };

    /// <summary>입력 파싱. 삭제어면 isClear=true. 실패 시 error에 사유.</summary>
    public static bool TryParse(string? input, DateTime now, out DateTime result, out bool isClear, out string error)
    {
        result = default;
        isClear = false;
        error = "";

        var text = (input ?? "").Trim();
        if (text.Length == 0)
        {
            // 빈칸 제출도 시간 삭제로 취급
            isClear = true;
            return true;
        }

        if (ClearWords.Contains(text, StringComparer.OrdinalIgnoreCase))
        {
            isClear = true;
            return true;
        }

        var compact = text.Replace(" ", "");

        // 0) 소수점 시간: 22.5시 → 22:30, 접미사 없이 13시 이상이면 22.5 → 22:30
        var decSi = Regex.Match(compact, @"^(\d{1,2})\.(\d+)시$");
        if (decSi.Success)
        {
            if (!TryDecimalTime(decSi, out var decHour, out var decMin, out error))
                return false;
            result = new DateTime(now.Year, now.Month, now.Day, decHour, decMin, 0);
            if (result <= now)
                result = result.AddDays(1);
            return true;
        }
        var decBare = Regex.Match(compact, @"^(\d{1,2})\.(\d{1,2})$");
        if (decBare.Success && int.Parse(decBare.Groups[1].Value) > 12)
        {
            if (!TryDecimalTime(decBare, out var decHour2, out var decMin2, out error))
                return false;
            result = new DateTime(now.Year, now.Month, now.Day, decHour2, decMin2, 0);
            if (result <= now)
                result = result.AddDays(1);
            return true;
        }

        // 1) 절대 형식: 2026-01-01 23:00
        if (DateTime.TryParseExact(text, DateTimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out result)
            || DateTime.TryParseExact(compact, DateTimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out result))
        {
            if (!HasYear(text) && !HasYear(compact) && result < now)
                result = result.AddYears(1);
            return true;
        }

        // 2) 날짜만: 2026-01-01, 11/5 → 기본 20시
        if (DateTime.TryParseExact(text, DateOnlyFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out result)
            || DateTime.TryParseExact(compact, DateOnlyFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out result))
        {
            result = new DateTime(result.Year, result.Month, result.Day, 20, 0, 0);
            if (!HasYear(text) && !HasYear(compact) && result < now)
                result = result.AddYears(1);
            return true;
        }

        // 3) 시간만: 20:00 → 오늘, 지났으면 내일
        if (DateTime.TryParseExact(text, TimeOnlyFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out result)
            || DateTime.TryParseExact(compact, TimeOnlyFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out result))
        {
            result = new DateTime(now.Year, now.Month, now.Day, result.Hour, result.Minute, 0);
            if (result <= now)
                result = result.AddDays(1);
            return true;
        }

        // 4) 한국어 자연어
        return TryParseKorean(compact, now, out result, out error);
    }

    private static bool HasYear(string text) => Regex.IsMatch(text, @"^\d{4}[./-]");

    private static bool TryParseKorean(string compact, DateTime now, out DateTime result, out string error)
    {
        result = default;
        error = "";

        if (compact is "지금" or "바로")
        {
            result = now;
            return true;
        }

        // N시간/N분 후 → 지금 기준
        var hourAfter = Regex.Match(compact, @"^(\d+)시간(\d+)?분?(후|뒤)$");
        if (hourAfter.Success)
        {
            result = now.AddHours(int.Parse(hourAfter.Groups[1].Value));
            if (hourAfter.Groups[2].Success)
                result = result.AddMinutes(int.Parse(hourAfter.Groups[2].Value));
            return true;
        }

        var minAfter = Regex.Match(compact, @"^(\d+)분(후|뒤)$");
        if (minAfter.Success)
        {
            result = now.AddMinutes(int.Parse(minAfter.Groups[1].Value));
            return true;
        }

        var baseDate = now.Date;
        var hasDate = false;
        var explicitToday = false;
        var rollWeekIfPast = false;
        var rollYearIfPast = false;
        var rest = compact;

        // N일 후
        var dayAfter = Regex.Match(rest, @"(\d+)일(후|뒤)");
        if (dayAfter.Success)
        {
            baseDate = now.Date.AddDays(int.Parse(dayAfter.Groups[1].Value));
            hasDate = true;
            rest = rest.Replace(dayAfter.Value, "");
        }
        // 오늘/내일/모레/글피
        else if (rest.Contains("오늘"))
        {
            explicitToday = true;
            hasDate = true;
            rest = rest.Replace("오늘", "");
        }
        else if (rest.Contains("내일") || rest.Contains("낼"))
        {
            baseDate = now.Date.AddDays(1);
            hasDate = true;
            rest = rest.Replace("내일", "").Replace("낼", "");
        }
        else if (rest.Contains("모레"))
        {
            baseDate = now.Date.AddDays(2);
            hasDate = true;
            rest = rest.Replace("모레", "");
        }
        else if (rest.Contains("글피"))
        {
            baseDate = now.Date.AddDays(3);
            hasDate = true;
            rest = rest.Replace("글피", "");
        }
        // 2026-01-01
        else
        {
            var ymd = Regex.Match(rest, @"(\d{4})[./-](\d{1,2})[./-](\d{1,2})");
            if (ymd.Success)
            {
                var (ok, date) = BuildDate(int.Parse(ymd.Groups[1].Value), int.Parse(ymd.Groups[2].Value), int.Parse(ymd.Groups[3].Value));
                if (!ok)
                {
                    error = "존재하지 않는 날짜입니다.";
                    return false;
                }
                baseDate = date;
                hasDate = true;
                rest = rest.Replace(ymd.Value, "");
            }
            // 금요일 / 다음주 금 ("금 8.5" 같은 요일+소수점 시간을 날짜로 오독하지 않도록 월/일보다 먼저 판정)
            else
            {
                var week = Regex.Match(rest, @"(이번|다음|담)?(일|월|화|수|목|금|토)(요일)?");
                if (week.Success)
                {
                    var prefix = week.Groups[1].Value;
                    var target = Weekdays[week.Groups[2].Value];
                    var diff = ((int)target - (int)now.DayOfWeek + 7) % 7;
                    if (prefix is "다음" or "담")
                        diff += 7;
                    baseDate = now.Date.AddDays(diff);
                    hasDate = true;
                    rollWeekIfPast = prefix is not ("다음" or "담");
                    rest = rest.Replace(week.Value, "");
                }
                // 11/5 (월이 13 이상이면 날짜가 아니라 소수점 시간으로 보고 시간 파싱에 맡긴다)
                else
                {
                    var md = Regex.Match(rest, @"(\d{1,2})[./-](\d{1,2})");
                    if (md.Success
                        && int.TryParse(md.Groups[1].Value, out var mdMonth)
                        && mdMonth >= 1 && mdMonth <= 12)
                    {
                        var (ok, date) = BuildDate(now.Year, mdMonth, int.Parse(md.Groups[2].Value));
                        if (!ok)
                        {
                            error = "존재하지 않는 날짜입니다.";
                            return false;
                        }
                        baseDate = date;
                        hasDate = true;
                        rollYearIfPast = true;
                        rest = rest.Replace(md.Value, "");
                    }
                }
            }
        }

        // 시간: 20:00, 저녁 8시/8시반/8시 30분, 22.5(=22시 30분)
        // (시간의 '시' 뒤에 '간'이 오면 단위이므로 제외)
        var hour = 20;
        var minute = 0;
        var hasTime = false;

        var dec = Regex.Match(rest, @"^(\d{1,2})\.(\d+)시?$");
        if (dec.Success)
        {
            if (!TryDecimalTime(dec, out hour, out minute, out error))
                return false;
            hasTime = true;
        }

        var colon = Regex.Match(hasTime ? "" : rest, @"(\d{1,2}):(\d{1,2})");
        if (colon.Success)
        {
            hour = int.Parse(colon.Groups[1].Value);
            minute = int.Parse(colon.Groups[2].Value);
            if (hour > 23 || minute > 59)
            {
                error = "시간이 올바르지 않습니다 (0~23시, 0~59분).";
                return false;
            }
            hasTime = true;
        }
        else if (!hasTime)
        {
            var si = Regex.Match(rest, @"(오전|오후|낮|밤|저녁|새벽|아침)?(\d{1,2})시(?!간)(반|(\d{1,2})분?)?");
            if (si.Success)
            {
                var meridiem = si.Groups[1].Value;
                hour = int.Parse(si.Groups[2].Value);
                if (hour > 23 || (meridiem.Length > 0 && hour > 12))
                {
                    error = "시간이 올바르지 않습니다.";
                    return false;
                }
                hour = ApplyMeridiem(meridiem, hour);
                if (si.Groups[3].Value == "반")
                    minute = 30;
                else if (si.Groups[4].Success)
                {
                    minute = int.Parse(si.Groups[4].Value);
                    if (minute > 59)
                    {
                        error = "분이 올바르지 않습니다 (0~59분).";
                        return false;
                    }
                }
                hasTime = true;
            }
        }

        if (!hasDate && !hasTime)
        {
            error = $"이해하지 못했어요. {HelpText}";
            return false;
        }

        // 시간만 입력 → 오늘, 지났으면 내일 (오늘을 명시했으면 에러)
        if (!hasDate)
        {
            var candidate = new DateTime(now.Year, now.Month, now.Day, hour, minute, 0);
            if (candidate <= now)
            {
                if (explicitToday)
                {
                    error = "이미 지난 시간입니다. 내일 이후로 입력해주세요.";
                    return false;
                }
                candidate = candidate.AddDays(1);
            }
            result = candidate;
            return true;
        }

        try
        {
            result = new DateTime(baseDate.Year, baseDate.Month, baseDate.Day, hour, minute, 0);
        }
        catch (ArgumentOutOfRangeException)
        {
            error = "존재하지 않는 날짜입니다.";
            return false;
        }

        if (explicitToday && result <= now)
        {
            error = "이미 지난 시간입니다. 내일 이후로 입력해주세요.";
            return false;
        }

        if (rollYearIfPast && result < now)
            result = result.AddYears(1);
        else if (rollWeekIfPast && result <= now)
            result = result.AddDays(7);

        return true;
    }

    private static bool TryDecimalTime(Match m, out int hour, out int minute, out string error)
    {
        hour = int.Parse(m.Groups[1].Value);
        minute = (int)Math.Round(double.Parse("0." + m.Groups[2].Value, CultureInfo.InvariantCulture) * 60);
        error = "";
        if (minute >= 60)
        {
            hour += 1;
            minute = 0;
        }
        if (hour > 23)
        {
            error = "시간이 올바르지 않습니다.";
            return false;
        }
        return true;
    }

    private static (bool ok, DateTime date) BuildDate(int year, int month, int day)
    {
        try
        {
            return (true, new DateTime(year, month, day));
        }
        catch (ArgumentOutOfRangeException)
        {
            return (false, default);
        }
    }

    private static int ApplyMeridiem(string meridiem, int hour) => meridiem switch
    {
        "오전" or "새벽" or "아침" => hour == 12 ? 0 : hour,
        "오후" or "낮" or "저녁" or "밤" => hour < 12 ? hour + 12 : hour,
        _ => hour,
    };
}
