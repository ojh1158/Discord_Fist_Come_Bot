using DiscordBot.scripts.db.DB_SETUP;

namespace DiscordBot.scripts.db.Models;

public class StartAlertEntity : IDbSetup
{
    public string? DISPLAY_NAME { get; set; }
    public string? PARTY_KEY { get; set; }
    public ulong CHANNEL_KEY { get; set; }
    public ulong USER_ID { get; set; }
    
    public string ReturnTableName()
    {
        return "PARTY_START_ALERT_HISTORY";
    }

    public void ReturnColumns(Dictionary<string, string> columns)
    {
        columns.Add("SEQ", "bigint unsigned auto_increment primary key");
        columns.Add("PARTY_KEY", "char(36) not null");
        columns.Add("SEND_TIME", "datetime default CURRENT_TIMESTAMP not null");
        columns.Add("CHANGE_TIME_FLAG", "tinyint(1) default 0 not null");
    }
}