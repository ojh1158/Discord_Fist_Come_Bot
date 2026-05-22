using System.IO;
using System.Text.Json;

namespace DiscordBot;

public class ConfigLoader
{
    private static readonly Lazy<Config> _config = new(() => LoadConfig());

    public static Config GetConfig() => _config.Value;

    private static Config LoadConfig()
    {
        var configFilePath = "config.json";
        var defaultConfigFilePath = ".config.json";

        // Check if config.json exists, if not, copy from .config.json
        if (!File.Exists(configFilePath))
        {
            if (File.Exists(defaultConfigFilePath))
            {
                File.Copy(defaultConfigFilePath, configFilePath);
                Console.WriteLine($"Config file created: {configFilePath}");
            }
            else
            {
                throw new FileNotFoundException("Both config.json and .config.json not found. Please create config.json.");
            }
        }

        try
        {
            var jsonContent = File.ReadAllText(configFilePath);
            var config = JsonSerializer.Deserialize<Config>(jsonContent, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            
            if (config == null)
            {
                throw new InvalidOperationException("Failed to deserialize configuration file.");
            }
            
            return config;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Error loading configuration", ex);
        }
    }

    public class Config
    {
        public required DiscordConfig Discord { get; init; }
        public required DatabaseConfig Database { get; init; }
        public required LoggingConfig Logging { get; init; }
    }

    public class DiscordConfig
    {
        public required string Token { get; init; }
        public required string BotName { get; init; }
    }

    public class DatabaseConfig
    {
        public required string ConnectionString { get; init; }
    }

    public class LoggingConfig
    {
        public required string LogLevel { get; init; }
        public required string LogPath { get; init; }
    }
}