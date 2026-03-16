using DiscordBot.scripts.db.DB_SETUP;

namespace DiscordBot.scripts.db.Models;

/// <summary>
/// GUILD_INFO 테이블 엔티티
/// </summary>
public class GuildInfoEntity : IDbSetup
{
    public uint SEQ { get; set; }
    public ulong ID { get; set; }
    public string NAME { get; set; } = string.Empty;
    public bool BAN_FLAG { get; set; } = false;
    
    
    public string ReturnTableName()
    {
        return "GUILD_INFO";
    }

    public void ReturnColumns(Dictionary<string, string> columns)
    {
        columns.Add("SEQ", "int unsigned auto_increment primary key");
        columns.Add("ID", "bigint unsigned null");
        columns.Add("NAME", "varchar(255)                not null");
        columns.Add("BAN_FLAG", "tinyint(1)      default 0   null");
        columns.Add("USE_COUNT", "bigint unsigned default '0' not null");
    }
}

