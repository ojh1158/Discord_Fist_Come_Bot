using Discord.Rest;
using Discord.WebSocket;
using DiscordBot.scripts.db.Models;
using DiscordBot.scripts.src.util;
using Microsoft.Extensions.Caching.Memory;

namespace DiscordBot.scripts.src.Tools;

public class GetUserDisplayNameTool(DiscordSocketClient client) : ISingleton
{
    
    public async Task<string> GetDisplayName(ulong guildId, ulong userId)
    {
        string? result = null;
        var socketGuild = client.GetGuild(guildId);
        
        if (socketGuild is not null)
        {
            var user = socketGuild.GetUser(userId);

            if (user is not null)
            {
                result = user.DisplayName;
            }
        }

        if (result is not null)
        {
            return result;
        }
        
        if (!CacheManager.Cache.TryGetValue(GetKey(guildId), out RestGuild? cacheEntry))
        {
            var guild = await client.Rest.GetGuildAsync(guildId);

            CacheManager.Cache.Set(GetKey(guildId), guild, new MemoryCacheEntryOptions
            {
                AbsoluteExpiration = DateTimeOffset.UtcNow.AddHours(1)
            });
        }

        var guildUser = await cacheEntry!.GetUserAsync(userId);


        if (guildUser is not null)
        {
            result = guildUser.DisplayName;
        }
        else
        {
            var user = await client.Rest.GetUserAsync(userId);
            result = user.GlobalName ?? user.Username;
        }

        return result;
    }

    private string GetKey(ulong guildId)
    {
        return $"{guildId}_RestGuild";
    }
}