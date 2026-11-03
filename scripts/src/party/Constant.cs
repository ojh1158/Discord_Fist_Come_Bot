namespace DiscordBot.scripts.src.party;

// ReSharper disable once ClassNeverInstantiated.Global
public class Constant
{
    public const int MIN_COUNT = 1;
    public const int MAX_COUNT = 100;
    public const int MAX_HOUR = 168;
    
    public const int MAX_NAME_COUNT = 50;
    
    public const string VERSION = "2.3.1";

    public const string JOIN_KEY = "참가";
    public const string LEAVE_KEY = "나가기";
    public const string CLOSE_KEY = "일시정지";
    public const string OPTION_KEY = "기능";

    public const string EXPIRE_BUTTON_KEY = "expire";
    public const string OPTION_BUTTON_KEY = "button";
    public const string KICK_BUTTON_KEY = "kick";
    
    public const string SETTING_MODEL_KEY = "setting";
    
    public const string PULLING_UP_KEY = "끌올";
    public const string EXPIRE_KEY = "만료(영구)";
    public const string PING_KEY = "호출(파티원)";
    public const string PARTY_KEY = "파티설정";
    public const string JOIN_AUTO_KEY = "인원추가";
    public const string MOVE_OWNER_KEY = "방장위임";
    public const string KICK_KEY = "강퇴";
    public const string START_TIME_OPEN_KEY = "시작 시간 선택";
    public const string EXPIRE_TIME_OPEN_KEY = "만료 시간 선택";
    public const string TEAM_KEY = "팀 만들기";
    public const string TEAM_REMOVE_KEY = "팀 삭제";
    public const string TEAM_AGAIN_KEY = "팀 다시 만들기";

    public const string DATE_INPUT_HELP = "예: 오늘 18시 / 내일 20시 / 모레 19시반 / 금 20시 / 11/5 20:00 / 2026-01-01 23:00 / 20:00 / 3시간 후 / 30분 후 (삭제: 없음)";
    public const string DATE_INPUT_PLACEHOLDER = "오늘 20시 / 내일 20시 / 모레 19시반 / 금 20시 / 11/5 20:00 / 3시간 후 / 삭제: 없음";
    public const string DATE_INPUT_EXAMPLES = "입력 예시\n• 오늘 18시 / 내일 20시 / 모레 19시반\n• 금 20시 / 11/5 20:00 / 2026-01-01 23:00\n• 20:00 / 3시간 후 / 30분 후\n• 시간 삭제: 없음 또는 빈칸 제출";
    public const string DATE_MODAL_KEY = "시간입력";
    public const string DATE_CREATE_KEY = "파티생성";
    public const string DATE_INPUT_FIELD_EXPIRE = "datetime_expire";
    public const string TIME_SETTING_KEY = "시간 설정";
    public const string DATE_MODAL_BOTH = "전체";
    public const string DATE_CREATE_EXAMPLES = "입력 예시\n• 오늘 18시 / 내일 20시 / 모레 19시반\n• 금 20시 / 11/5 20:00 / 2026-01-01 23:00\n• 20:00 / 3시간 후 / 30분 후\n• 비워두면 시간 없이 생성 (만료: 2일 후 23:59 자동)";
    public const string DATE_INPUT_FIELD = "datetime";



    public const string USER_ALERT_SETTING_KEY = "개인 알람 설정";

    public const string USER_ALERT_SETTING_OPEN_KEY = "알람 설정 열기";
    
    public const string USER_ALERT_ALL_FLAG = "알림";
    public const string USER_ALERT_MY_PARTY_FULL_FLAG = "참여한 파티가 인원수를 충족하는 경우 알림";
    public const string USER_ALERT_MY_PARTY_JOIN_USER_FLAG = "내 파티에 유저가 들어올 경우 알림";
    public const string USER_ALERT_MY_PARTY_LEFT_USER_FLAG = "내 파티에 유저가 나갈 경우 알림";
    public const string USER_ALERT_JOIN_PARTY_TO_WAIT_FLAG = "대기열에서 파티로 참가될 경우 알림";
    public const string PARTY_START_TIME_ALERT_FLAG = "파티 시간 5분전 알림";
    
    

        
    
    public const string YES_BUTTON_KEY = "yes";
    public const string NO_BUTTON_KEY = "no";

    public const ulong MAKE_USER_ID = 315522271802556416;
}