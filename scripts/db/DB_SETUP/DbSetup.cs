using Dapper;
using DiscordBot.scripts._src;
using MySqlConnector;

namespace DiscordBot.scripts.db.DB_SETUP;

public class DbSetup : ISingleton
{
    public static DbSetup Instant { get; private set; }
    
    private readonly DatabaseController databaseController;

    public DbSetup(DatabaseController databaseController)
    {
        this.databaseController = databaseController;
        Instant = this;
    }

    public async Task SetupAsync(IDbSetup dbSetups)
    {
        await databaseController.ExecuteAsync(async conn =>
        {
            var tableName = dbSetups.ReturnTableName();
            var sql = "CREATE TABLE IF NOT EXISTS " + tableName;
        
            Dictionary<string, string> columns = new();
            List<string> addColumns = new();
            
            dbSetups.ReturnColumns(columns);
        
            for (var i = 0; i < columns.Count; i++)
            {
                var pair = columns.ElementAt(i);
            
                string checkColSql = @"
            SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS 
            WHERE TABLE_SCHEMA = DATABASE() 
            AND TABLE_NAME = @tableName 
            AND COLUMN_NAME = @name;
";

                var affectedRows = await conn.ExecuteAsync(checkColSql, new {tableName, name = pair.Key});

                if (affectedRows == 0)
                {
                    var item = $"ALTER TABLE {tableName} ADD COLUMN {pair.Key} {pair.Value}";
                    if (i >= 1)
                    {
                        var afterKey = columns.ElementAtOrDefault(i - 1);
                        item += $" after {afterKey.Key}";
                    }
                    item += ";";
                    addColumns.Add(item);                
                }
            }
        
            // 테이블 생성
            await conn.ExecuteAsync(sql);
        
            foreach (var addColumn in addColumns)
            {
                await conn.ExecuteAsync(addColumn);
            }

            return string.Empty;
        });
    }
}