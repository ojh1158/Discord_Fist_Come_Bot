namespace DiscordBot.scripts.db.DB_SETUP;

public interface IDbSetup
{
    string ReturnTableName();
    void ReturnColumns(Dictionary<string, string> columns);
}