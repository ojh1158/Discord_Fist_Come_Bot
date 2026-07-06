namespace DiscordBot.scripts.config;

public class Config
{
    public TestConfig Test { get; set; } = new();
    public DiscordConfig Discord { get; set; } = new();
    public DatabaseConfig Database { get; set; } = new();
    // public RiotConfig Riot { get; set; } = new();
    public DebugConfig Debug { get; set; } = new();
}

public class TestConfig
{
    public bool Enable { get; set; } = false;
    public bool UseScheduler { get; set; } = false;
}

public class DiscordConfig
{
    public string Token { get; set; } = "your token";
    public string TestToken { get; set; } = "test token";
}

public class DatabaseConfig
{
    public string ConnectionString { get; set; } = string.Empty;
    public string TestConnectionString { get; set; } = string.Empty;
}

public class RiotConfig
{
    public bool Enable { get; set; } = false;
    public string ApiKey { get; set; } = string.Empty;
}

public class DebugConfig
{
    public bool ViewStackTrace { get; set; } = false;
}