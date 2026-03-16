using DiscordBot.scripts.db.DB_SETUP;

namespace DiscordBot.scripts.db.Models;

/// <summary>
/// PARTY_MEMBER AND PARTY_WAIT_MEMBER 테이블 엔티티
/// </summary>
public class PartyMemberEntity : IDbSetup
{
    public ulong MESSAGE_KEY { get; set; }
    public ulong USER_ID { get; set; }
    public string USER_NICKNAME { get; set; }


    public string ReturnTableName()
    {
        return "PARTY_MEMBER";
    }

    public void ReturnColumns(Dictionary<string, string> columns)
    {
        columns.Add("SEQ", "bigint unsigned auto_increment primary key");
        columns.Add("PARTY_KEY", "char(36) null");
        columns.Add("MESSAGE_KEY", "bigint unsigned null");
        columns.Add("USER_ID", "bigint unsigned not null");
        columns.Add("USER_NICKNAME", "varchar(255) null");
        columns.Add("EXIT_FLAG", "tinyint(1) default 0 null");
        columns.Add("CREATE_DATE", "datetime default CURRENT_TIMESTAMP null");
    }
}

