using DiscordBot.scripts.db.DB_SETUP;

namespace DiscordBot.scripts.db.Models;

public class UserNameEntity : IDbSetup
{
    public ulong GUILD_ID { get; set; }
    public string? USER_NICKNAME { get; set; }
    public DateTime? CREATE_TIME { get; set; }
    public DateTime? UPDATE_TIME { get; set; }

    public string ReturnTableName()
    {
        return "USER_NAME";
    }

    public void ReturnColumns(Dictionary<string, string> columns)
    {
        columns.Add("SEQ", "bigint unsigned auto_increment primary key");
        columns.Add("GUILD_ID", "bigint(20) not null");
        columns.Add("USER_NICKNAME", "varchar(255) not null");
        columns.Add("CREATE_TIME", "datetime default CURRENT_TIMESTAMP not null");
        columns.Add("UPDATE_TIME", "datetime default CURRENT_TIMESTAMP on update CURRENT_TIMESTAMP not null");
        
    }
}