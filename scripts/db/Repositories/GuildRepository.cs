using Dapper;
using DiscordBot.scripts._src;
using MySqlConnector;
using DiscordBot.scripts.db.Models;
using Serilog;

namespace DiscordBot.scripts.db.Repositories;

/// <summary>
/// 길드 정보
/// </summary>
public class GuildRepository : ISingleton
{ 
    public async Task<bool> GuildCheck(ulong guildId, string guildName, MySqlConnection connection, MySqlTransaction transaction)
    {
        var guildInfoEntity = await connection.QuerySingleOrDefaultAsync<GuildInfoEntity>(
            @"
SELECT *
FROM GUILD_INFO
WHERE ID = @id
FOR UPDATE
",
            new { id = guildId },
            transaction: transaction);

        if (guildInfoEntity == null)
        {
            var sql = @"
INSERT INTO GUILD_INFO (ID, NAME) 
VALUES (@id, @name)
    ";
            var affectedRows = await connection.ExecuteAsync(sql, new { id = guildId, name = guildName }, transaction: transaction);
            if (affectedRows <= 0)
                return false;
        }
        else
        {
            if (guildInfoEntity.BAN_FLAG)
                return false;

            var sql = @"
UPDATE GUILD_INFO
SET USE_COUNT = USE_COUNT + 1,
    NAME = @name
WHERE ID = @id
    ";
            var affectedRows = await connection.ExecuteAsync(sql, new { id = guildId, name = guildName }, transaction: transaction);
            if (affectedRows <= 0)
                return false;
        }

        return true;
    }
}