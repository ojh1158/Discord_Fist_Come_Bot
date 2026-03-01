namespace DiscordBot.scripts.db.Models;

public class StartAlertEntity
{
    public string? DISPLAY_NAME { get; set; }
    public string? PARTY_KEY { get; set; }
    public ulong CHANNEL_KEY { get; set; }
    public ulong USER_ID { get; set; }
}