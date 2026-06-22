using Microsoft.Extensions.Caching.Memory;

namespace DiscordBot.scripts.src.util;

public class CacheManager
{
    public static readonly IMemoryCache Cache = new MemoryCache(new MemoryCacheOptions());


    public static MemoryCacheEntryOptions GetOptions()
    {
        return new MemoryCacheEntryOptions()
            .SetAbsoluteExpiration(TimeSpan.FromDays(1));
    }
}