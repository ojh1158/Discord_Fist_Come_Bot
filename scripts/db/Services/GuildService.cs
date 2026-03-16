using DiscordBot.scripts._src;
using DiscordBot.scripts._src.Services;
using DiscordBot.scripts.db.Repositories;

namespace DiscordBot.scripts.db.Services;

/// <summary>
/// 길드 비즈니스 로직 처리 (Service Layer)
/// </summary>
public class GuildService(DatabaseController databaseController, GuildRepository guildRepository) : ISingleton
{
    public Task<bool> GuildCheckAsync(ulong guildId, string guildName)
    {
        return databaseController.ExecuteInTransactionAsync(async (conn, trans) =>
        {
            return await guildRepository.GuildCheck(guildId, guildName, conn, trans);
        });
    }
}


