namespace DiscordBot.scripts._src.party;

// ReSharper disable once ClassNeverInstantiated.Global
public class PartyConstant
{
    public const int MIN_COUNT = 1;
    public const int MAX_COUNT = 200;
    public const int MAX_HOUR = 168;
    
    public const int MAX_NAME_COUNT = 50;
    
    public const string VERSION = "2.0.2";

    public const string JOIN_KEY = "참가";
    public const string LEAVE_KEY = "나가기";
    public const string CLOSE_KEY = "일시정지";
    public const string OPTION_KEY = "기능";

    public const string EXPIRE_BUTTON_KEY = "expire";
    public const string OPTION_BUTTON_KEY = "button";
    public const string KICK_BUTTON_KEY = "kick";
    
    public const string SETTING_MODEL_KEY = "setting";
    
    public const string PULLING_UP_KEY = "끌어올리기";
    public const string EXPIRE_KEY = "만료(영구)";
    public const string PING_KEY = "호출(파티원)";
    public const string PARTY_KEY = "파티설정";
    public const string JOIN_AUTO_KEY = "인원추가";
    public const string KICK_KEY = "강퇴";
    public const string START_TIME_OPEN_KEY = "시작 시간 선택";
    public const string EXPIRE_TIME_OPEN_KEY = "만료 시간 선택";
    public const string TEAM_KEY = "팀 만들기";
    public const string TEAM_REMOVE_KEY = "팀 삭제";

    public const string DATE_PICKUP_KEY = "날짜 선택기";
    public const string DATE_PICKUP_FIRST_KEY = "처음 날짜 선택 시";

    public const string YEAR_KEY = "년";
    public const string MONTH_KEY = "월";
    public const string DAY_TENS_KEY = "일10"; // 십의 자리
    public const string DAY_ONES_KEY = "일1";  // 일의 자리
    public const string HOUR_KEY = "시";
    public const string MIN_TENS_KEY = "분10"; // 십의 자리
    public const string MIN_ONES_KEY = "분1";  // 일의 자리
    public const string DATE_YEAR_SELECT_KEY = "년.월.일 선택";
    public const string DATE_HOUR_SELECT_KEY = "시.분 선택";

    public const string USER_ALERT_SETTING_KEY = "개인 알람 설정";

    public const string USER_ALERT_SETTING_OPEN_KEY = "알람 설정 열기";
    
    public const string USER_ALERT_ALL_FLAG = "모든 알림";
    public const string USER_ALERT_MY_PARTY_FULL_FLAG = "참여한 파티가 인원수를 충족하는 경우 알림";
    public const string USER_ALERT_MY_PARTY_JOIN_USER_FLAG = "내 파티에 유저가 들어올 경우 알림";
    public const string USER_ALERT_MY_PARTY_LEFT_USER_FLAG = "내 파티에 유저가 나갈 경우 알림";
    public const string USER_ALERT_JOIN_PARTY_TO_WAIT_FLAG = "대기열에서 파티로 참가될 경우 알림";
    public const string PARTY_START_TIME_ALERT_FLAG = "파티 시간 5분전 알림";
    
    
    public const string DATE_YES = "확인";
    public const string DATE_NO = "취소";
        
    
    public const string YES_BUTTON_KEY = "yes";
    public const string NO_BUTTON_KEY = "no";
}