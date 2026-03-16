using DiscordBot.scripts.db.DB_SETUP;

namespace DiscordBot.scripts.db.Models;

/// <summary>
/// 사용자 알림 설정 테이블 엔티티
/// </summary>
public class UserSettingEntity : IDbSetup
{
    /// <summary>Discord 사용자 ID (PK)</summary>
    public ulong USER_ID { get; set; }

    /// <summary>전체 알림 사용 여부 (0: off, 1: on)</summary>
    public bool ALL_ALERT_FLAG { get; set; } = true;

    /// <summary>내 파티 인원 찼을 때 알림 (0: off, 1: on)</summary>
    public bool MY_PARTY_FULL_ALERT_FLAG { get; set; } = false;

    /// <summary>내 파티에 유저 참가 시 알림 (0: off, 1: on)</summary>
    public bool MY_PARTY_JOIN_USER_ALERT_FLAG { get; set; } = false;

    /// <summary>내 파티에서 유저 나감 시 알림 (0: off, 1: on)</summary>
    public bool MY_PARTY_LEFT_USER_ALERT_FLAG { get; set; } = false;
    
    public bool PARTY_START_TIME_ALERT_FLAG { get; set; } = true;

    /// <summary>전체 알림 꺼져 있으면 대기 참가 여부</summary>
    public bool JOIN_PARTY_TO_WAIT_FLAG { get; set; } = true;

    public string ReturnTableName()
    {
        return "USER_CONFIG";
    }

    public void ReturnColumns(Dictionary<string, string> columns)
    {
        columns.Add("USER_ID", "bigint unsigned not null primary key");
        columns.Add("ALL_ALERT_FLAG", "tinyint(1) default 0 not null");
        columns.Add("MY_PARTY_FULL_ALERT_FLAG", "tinyint(1) default 1 not null");
        columns.Add("MY_PARTY_JOIN_USER_ALERT_FLAG", "tinyint(1) default 1 not null");
        columns.Add("MY_PARTY_LEFT_USER_ALERT_FLAG", "tinyint(1) default 1 not null");
        columns.Add("PARTY_START_TIME_ALERT_FLAG", "tinyint(1) default 1 not null");
        columns.Add("PARTY_START_TIME_ALERT_MINUTE", "int default 5 not null");
        columns.Add("JOIN_PARTY_TO_WAIT_FLAG", "tinyint(1) default 1 not null");
        columns.Add("UPDATE_DATE", "datetime default CURRENT_TIMESTAMP null on update CURRENT_TIMESTAMP");
        columns.Add("CREATE_DATE", "datetime default CURRENT_TIMESTAMP null");
    }
}
