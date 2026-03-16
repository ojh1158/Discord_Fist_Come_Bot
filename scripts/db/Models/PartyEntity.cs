using DiscordBot.scripts.db.DB_SETUP;

namespace DiscordBot.scripts.db.Models;

/// <summary>
/// PARTY 테이블 엔티티
/// </summary>
public class PartyEntity : IDbSetup
{
    public string DISPLAY_NAME { get; set; } = string.Empty;
    public int MAX_COUNT_MEMBER { get; set; } = 10;
    public string PARTY_KEY { get; set; } = Guid.AllBitsSet.ToString();
    public ulong MESSAGE_KEY { get; set; }
    public ulong GUILD_KEY { get; set; }
    public ulong CHANNEL_KEY { get; set; }
    public ulong OWNER_KEY { get; set; }
    public string? OWNER_NICKNAME { get; set; }
    
    // 파티 멤버 리스트 (Join 시 자동 매핑)
    public List<PartyMemberEntity> Members { get; set; } = new();
    public List<PartyMemberEntity> WaitMembers { get; set; } = new();
    
    public DateTime? EXPIRE_DATE { get; set; }
    public DateTime? START_DATE { get; set; } = null;
    
    public ulong? VOICE_CHANNEL_KEY { get; set; } = null;
    
    
    public bool IS_CLOSED { get; set; } = false;
    public bool IS_EXPIRED { get; set; } = false;
    public string ReturnTableName()
    {
        return "PARTY";
    }

    public void ReturnColumns(Dictionary<string, string> columns)
    {
        columns.Add("SEQ", "bigint unsigned auto_increment primary key");
        columns.Add("PARTY_KEY", "char(36) null");
        columns.Add("DISPLAY_NAME", "varchar(255) not null");
        columns.Add("MAX_COUNT_MEMBER", "int unsigned null");
        columns.Add("MESSAGE_KEY", "bigint unsigned not null");
        columns.Add("GUILD_KEY", "bigint unsigned not null");
        columns.Add("CHANNEL_KEY", "bigint unsigned not null");
        columns.Add("OWNER_KEY", "bigint unsigned not null");
        columns.Add("OWNER_NICKNAME", "varchar(255) null");
        columns.Add("EXPIRE_DATE", "datetime null");
        columns.Add("START_DATE", "datetime null");
        columns.Add("VOICE_CHANNEL_KEY", "bigint unsigned null");
        columns.Add("IS_CLOSED", "tinyint(1) default 0 not null");
        columns.Add("IS_EXPIRED", "tinyint(1) default 0 null");
        columns.Add("CREATE_DATE", "datetime default CURRENT_TIMESTAMP null");
    }
}

