namespace DiscordBot.scripts.config;

public class ConfigClass
{
    public DiscordConfig Discord { get; set; } = new();
    public DatabaseConfig Database { get; set; } = new();
    public LoggingConfig Logging { get; set; } = new();
         
    public sealed class DiscordConfig
    {
        public string Token { get; set; } = string.Empty;
        public string TestToken { get; set; } = string.Empty;
        public string BotName { get; set; } = string.Empty;
    }
             
    public sealed class DatabaseConfig
    {
        public string ConnectionString { get; set; } = string.Empty;
        public string TestConnectionString { get; set; } = string.Empty;
    }
         
    public sealed class LoggingConfig
    {
        public string LogLevel { get; set; } = "Information";
        public string LogPath { get; set; } = "logs/";
    }
}