using Serilog;

namespace DiscordBot.scripts.config;

public class LogConfig
{
    public static void Init()
    {
        var logPath = Path.Combine(AppContext.BaseDirectory, "logs", "log-.txt");
        
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information() 
            .WriteTo.Console()
            .WriteTo.File(logPath, 
                rollingInterval: RollingInterval.Day,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}",
                retainedFileCountLimit: 31)
            .CreateLogger();
    }
}