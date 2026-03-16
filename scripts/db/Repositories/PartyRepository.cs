using MySqlConnector;
using DiscordBot.scripts._src;
using Dapper;
using DiscordBot.scripts._src.party;
using DiscordBot.scripts.db.Models;
using Serilog;

namespace DiscordBot.scripts.db.Repositories;

/// <summary>
/// 실제 DB 구조에 맞춘 파티 Repository (Data Access Layer)
/// 순수 DB CRUD 작업만 담당
/// </summary>
public class PartyRepository: ISingleton
{
    /// <summary>
    /// 파티를 생성합니다. (순수 INSERT만)
    /// </summary>
    /// <returns>생성 성공 시 true, 실패 시 false</returns>
    public async Task<bool> CreatePartyAsync(PartyEntity party, MySqlConnection connection, MySqlTransaction transaction)
    {
        // PARTY_KEY가 비어있거나 기본값이면 새로 생성
        if (string.IsNullOrEmpty(party.PARTY_KEY) || party.PARTY_KEY == Guid.AllBitsSet.ToString())
        {
            party.PARTY_KEY = Guid.NewGuid().ToString();
        }
        
        var partySql = @"
INSERT INTO PARTY (DISPLAY_NAME, PARTY_KEY, MAX_COUNT_MEMBER, MESSAGE_KEY, GUILD_KEY, CHANNEL_KEY, OWNER_KEY, OWNER_NICKNAME, EXPIRE_DATE, IS_CLOSED, VOICE_CHANNEL_KEY)
VALUES (@DISPLAY_NAME, @PARTY_KEY, @MAX_COUNT_MEMBER, @MESSAGE_KEY, @GUILD_KEY, @CHANNEL_KEY, @OWNER_KEY, @OWNER_NICKNAME, @EXPIRE_DATE, 0, @VOICE_CHANNEL_KEY)
";
        
        // 명시적으로 파라미터 전달하여 Dapper 매핑 문제 방지
        var parameters = new
        {
            party.DISPLAY_NAME,
            party.PARTY_KEY,
            party.MAX_COUNT_MEMBER,
            party.MESSAGE_KEY,
            party.GUILD_KEY,
            party.CHANNEL_KEY,
            party.OWNER_KEY,
            party.OWNER_NICKNAME,
            party.EXPIRE_DATE,
            party.VOICE_CHANNEL_KEY
        };
        
        var affectedRows = await connection.ExecuteAsync(partySql, parameters, transaction: transaction);
        
        return affectedRows > 0;
    }

    public async Task<PartyEntity?> GetPartyEntity(string partyKey, MySqlConnection connection, MySqlTransaction? transaction = null)
    {
        await using var reader = await connection.QueryMultipleAsync(
            @"
SELECT 
    DISPLAY_NAME,
    CAST(PARTY_KEY AS CHAR(36)) AS PARTY_KEY,
    MAX_COUNT_MEMBER,
    MESSAGE_KEY,
    GUILD_KEY,
    CHANNEL_KEY,
    OWNER_KEY,
    OWNER_NICKNAME,
    EXPIRE_DATE,
    START_DATE,
    VOICE_CHANNEL_KEY,
    IS_CLOSED,
    IS_EXPIRED
FROM PARTY 
WHERE PARTY_KEY = @partyKey
AND IS_EXPIRED = FALSE
FOR UPDATE
;

SELECT * 
FROM PARTY_MEMBER 
WHERE PARTY_KEY = @partyKey
 AND EXIT_FLAG = FALSE
ORDER BY SEQ
;

SELECT * 
FROM PARTY_WAIT_MEMBER 
WHERE PARTY_KEY = @partyKey
 AND EXIT_FLAG = FALSE
ORDER BY SEQ
;
",
            new { partyKey },
            transaction: transaction
        );

        var party = await reader.ReadSingleOrDefaultAsync<PartyEntity>();

        if (party == null) return null;

        party.Members = (await reader.ReadAsync<PartyMemberEntity>()).ToList();
        party.Members.AddRange((await reader.ReadAsync<PartyMemberEntity>()).ToList());

        return party;
    }

    public async Task<JoinType> AddUser(string id, ulong userId, string userNickname, MySqlConnection connection, MySqlTransaction transaction)
    {
        var sql = @"
INSERT INTO PARTY_MEMBER (PARTY_KEY, USER_ID, USER_NICKNAME)
VALUES (@id, @userId, @userNickname)
;
";
        var affectedRows = await connection.ExecuteAsync(sql,
            new { id, userId, userNickname },
            transaction: transaction);

        return affectedRows == 0 ? JoinType.Error : JoinType.Join;
    }

    public async Task<bool> RemoveUser(string id, ulong userId, MySqlConnection connection, MySqlTransaction transaction)
    {
        var sql1 = @"
UPDATE PARTY_MEMBER 
SET EXIT_FLAG = 1
WHERE USER_ID = @USER_ID AND PARTY_KEY = @id";

        var affected1 = await connection.ExecuteAsync(sql1,
            new { id, USER_ID = userId },
            transaction: transaction);

        return affected1 > 0;
    }

    public async Task<bool> ChangeMessageId(ulong messageId, ulong newMessageId, MySqlConnection connection, MySqlTransaction transaction)
    {
        var sql1 = @"
UPDATE PARTY
SET MESSAGE_KEY = @NEW
WHERE MESSAGE_KEY = @MESSAGE_KEY
";
        var affected1 = await connection.ExecuteAsync(sql1,
            new { MESSAGE_KEY = messageId, NEW = newMessageId },
            transaction: transaction);

        return affected1 > 0;
    }
    
    /// <summary>
    /// 파티 인원 수 업데이트 (순수 UPDATE만)
    /// </summary>
    public async Task<bool> UpdatePartySize(string partyKey, int newSize, MySqlConnection connection, MySqlTransaction transaction)
    {
        var sql = @"
UPDATE PARTY
SET MAX_COUNT_MEMBER = @newSize
WHERE PARTY_KEY = @partyKey
FOR UPDATE
";
        var affectedRows = await connection.ExecuteAsync(sql,
            new { newSize, partyKey },
            transaction: transaction);

        return affectedRows > 0;
    }

    public async Task<bool> SetPartyClose(string partyKey, bool isClose, MySqlConnection connection, MySqlTransaction transaction)
    {
        var sql = @"
UPDATE PARTY
SET IS_CLOSED = @isClose
WHERE PARTY_KEY = @partyKey
    ";
        var affectedRows = await connection.ExecuteAsync(sql,
            new { partyKey, isClose = isClose ? 1 : 0 },
            transaction: transaction);

        return affectedRows > 0;
    }
    
    public async Task<bool> PartyRename(string partyKey, string newName, MySqlConnection connection, MySqlTransaction transaction)
    {
        var sql = @"
UPDATE PARTY
SET DISPLAY_NAME= @newName
WHERE PARTY_KEY = @partyKey
    ";
        var affectedRows = await connection.ExecuteAsync(sql,
            new { partyKey, newName },
            transaction: transaction);

        return affectedRows > 0;
    }


    public async Task<bool> ExpiredParty(ulong messageId, MySqlConnection connection, MySqlTransaction transaction)
    {
        var affectedRows = await connection.ExecuteAsync(
            @"
UPDATE PARTY
SET IS_EXPIRED = TRUE
WHERE MESSAGE_KEY = @MESSAGE_KEY
",
            new { MESSAGE_KEY = messageId },
            transaction: transaction);

        return affectedRows > 0;
    }



    public async Task<List<PartyEntity>> CycleExpiredPartyList(MySqlConnection connection, MySqlTransaction transaction)
    {
        var parties = (await connection.QueryAsync<PartyEntity>(
            @"
SELECT 
    DISPLAY_NAME,
    CAST(PARTY_KEY AS CHAR(36)) AS PARTY_KEY,
    MAX_COUNT_MEMBER,
    MESSAGE_KEY,
    GUILD_KEY,
    CHANNEL_KEY,
    OWNER_KEY,
    OWNER_NICKNAME,
    EXPIRE_DATE,
    IS_CLOSED,
    IS_EXPIRED
FROM PARTY 
WHERE IS_EXPIRED = FALSE
AND EXPIRE_DATE <= NOW()
", transaction: transaction)).ToList();

        return parties;
    }
    
    public async Task<bool> SetStartDate(string partyKey, DateTime dateTime, MySqlConnection connection, MySqlTransaction transaction)
    {
        var sql = @"
UPDATE PARTY
SET START_DATE= @dateTime
WHERE PARTY_KEY = @partyKey
    ";
        var affectedRows = await connection.ExecuteAsync(sql,
            new { partyKey, dateTime },
            transaction: transaction);

        return affectedRows > 0;
    }
    
    public async Task<bool> SetOwner(string partyKey, ulong ownerKey, string name, MySqlConnection connection, MySqlTransaction transaction)
    {
        var sql = @"
UPDATE PARTY
SET OWNER_KEY= @ownerKey, OWNER_NICKNAME= @name
WHERE PARTY_KEY = @partyKey
    ";
        var affectedRows = await connection.ExecuteAsync(sql,
            new { partyKey, ownerKey, name },
            transaction: transaction);

        return affectedRows > 0;
    }
    
    public async Task<bool> SetExpireDate(string partyKey, DateTime dateTime, MySqlConnection connection, MySqlTransaction transaction)
    {
        var sql = @"
UPDATE PARTY
SET EXPIRE_DATE= @dateTime
WHERE PARTY_KEY = @partyKey
    ";
        var affectedRows = await connection.ExecuteAsync(sql,
            new { partyKey, dateTime },
            transaction: transaction);

        return affectedRows > 0;
    }

    public async Task GetPartyLock(string partyKey, MySqlConnection connection, MySqlTransaction transaction)
    {
        var sql = @"
        SELECT * FROM PARTY WHERE PARTY_KEY = @partyKey FOR UPDATE;
    ";

        await connection.ExecuteAsync(sql,
            new { partyKey },
            transaction: transaction);
    }
}

