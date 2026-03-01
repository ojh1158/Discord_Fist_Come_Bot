namespace DiscordBot.scripts.db.Models;

/// <summary>
/// PARTY 테이블 엔티티
/// </summary>
public class PartyEntity
{
    public string DISPLAY_NAME { get; set; } = string.Empty;
    public int MAX_COUNT_MEMBER { get; set; } = 10;
    public string PARTY_KEY { get; set; } = Guid.AllBitsSet.ToString();
    public ulong MESSAGE_KEY { get; set; }
    public ulong GUILD_KEY { get; set; }
    public ulong CHANNEL_KEY { get; set; }
    public ulong OWNER_KEY { get; set; }
    public string? OWNER_NICKNAME { get; set; }
    
    // 파티 멤버 리스트 (Join 시 자동 매핑)
    public List<PartyMemberEntity> Members { get; set; } = new();
    public List<PartyMemberEntity> WaitMembers { get; set; } = new();
    
    public DateTime? EXPIRE_DATE { get; set; }
    public DateTime? START_DATE { get; set; } = null;
    
    public ulong? VOICE_CHANNEL_KEY { get; set; } = null;
    
    
    public bool IS_CLOSED { get; set; } = false;
    public bool IS_EXPIRED { get; set; } = false;
}

