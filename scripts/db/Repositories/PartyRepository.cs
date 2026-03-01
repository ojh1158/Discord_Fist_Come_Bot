using MySqlConnector;
using DiscordBot.scripts._src;
using Dapper;
using DiscordBot.scripts._src.party;
using DiscordBot.scripts.db.Models;

namespace DiscordBot.scripts.db.Repositories;

/// <summary>
/// 실제 DB 구조에 맞춘 파티 Repository (Data Access Layer)
/// 순수 DB CRUD 작업만 담당
/// </summary>
public class PartyRepository
{
    /// <summary>
    /// 파티를 생성합니다. (순수 INSERT만)
    /// </summary>
    /// <returns>생성 성공 시 true, 실패 시 false</returns>
    public static async Task<bool> CreatePartyAsync(PartyEntity party, MySqlConnection connection, MySqlTransaction transaction)
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

    public static async Task<bool> IsPartyExistsAsync(string displayName, ulong guildId, MySqlConnection connection, MySqlTransaction transaction)
    {
        try
        {
            var sql = @"
SELECT EXISTS(
    SELECT 1
    FROM PARTY
    WHERE DISPLAY_NAME = @DisplayName
    AND   GUILD_KEY = @GuildKey
    AND IS_EXPIRED = FALSE
)
";
            return await connection.ExecuteScalarAsync<bool>(sql,
                new { DisplayName = displayName, GuildKey = guildId },
                transaction: transaction);
        }
        catch (Exception e)
        {
            Console.WriteLine($"파티 존재 확인 실패: {e.Message}");
            return false;
        }
    }

    public static async Task<PartyEntity?> GetPartyEntityNotMember(string partyKey, MySqlConnection connection, MySqlTransaction? transaction = null)
    {
        try
        {
            // 1. 파티 기본 정보
            // PARTY_KEY를 명시적으로 문자열로 변환하여 Dapper 매핑 문제 방지
            var party = await connection.QuerySingleOrDefaultAsync<PartyEntity>(
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
",
                new { partyKey },
                transaction: transaction
            );

            return party;
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            return null;
        }
    }

    public static async Task<List<PartyMemberEntity>> GetPartyMemberList(string id, MySqlConnection connection, MySqlTransaction? transaction = null)
    {
        try
        {
            var result = await connection.QueryAsync<PartyMemberEntity>(
                @"
SELECT * 
FROM PARTY_MEMBER 
WHERE PARTY_KEY = @id
 AND EXIT_FLAG = FALSE
ORDER BY CREATE_DATE
",
                new { id },
                transaction: transaction);

            return result.ToList();
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
    }

    public static async Task<List<PartyMemberEntity>> GetPartyWaitMemberList(string id, MySqlConnection connection, MySqlTransaction? transaction = null)
    {
        try
        {
            var result = await connection.QueryAsync<PartyMemberEntity>(
                @"
SELECT * 
FROM PARTY_WAIT_MEMBER 
WHERE PARTY_KEY = @id
AND EXIT_FLAG = FALSE
ORDER BY CREATE_DATE
",
                new { id },
                transaction: transaction);

            return result.ToList();
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
    }
    
    public static async Task<List<PartyMemberEntity>> GetPartyAllMemberList(string id, MySqlConnection connection, MySqlTransaction? transaction = null)
    {
        List<PartyMemberEntity> result = [];
        
        result.AddRange(await GetPartyMemberList(id, connection, transaction));
        result.AddRange(await GetPartyWaitMemberList(id, connection, transaction));
        
        return result;
    }

    public static async Task<bool> ExistsUser(string id, ulong userId, MySqlConnection connection, MySqlTransaction transaction)
    {
        try
        {
            var result = await connection.ExecuteScalarAsync<bool>(
                @"
SELECT EXISTS(
    SELECT 1
    FROM PARTY_MEMBER
    WHERE PARTY_KEY = @id
      AND USER_ID = @USER_ID
      AND EXIT_FLAG = false
    UNION ALL
    SELECT 1
    FROM PARTY_WAIT_MEMBER
    WHERE PARTY_KEY = @id
      AND USER_ID = @USER_ID
      AND EXIT_FLAG = false
    LIMIT 1
    )
",
                new { id , USER_ID = userId },
                transaction: transaction);

            return result;
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            return false;
        }   
    }

    public static async Task<JoinType> AddUser(string id, ulong userId, string userNickname, MySqlConnection connection, MySqlTransaction transaction)
    {
        try
        {
            string sql;
                // 파티 멤버는 MAX_COUNT_MEMBER 체크 후 추가
                sql = @"
INSERT INTO PARTY_MEMBER (PARTY_KEY, USER_ID, USER_NICKNAME)
SELECT @id, @userId, @userNickname
WHERE (
    SELECT COUNT(*) 
    FROM PARTY_MEMBER 
    WHERE PARTY_KEY = @id
    AND EXIT_FLAG = FALSE
) < (
    SELECT MAX_COUNT_MEMBER 
    FROM PARTY 
    WHERE PARTY_KEY = @id
)";
                
            var affectedRows = await connection.ExecuteAsync(sql,
                new { id, userId, userNickname },
                transaction: transaction);
            
            if (affectedRows == 0)
            {
                // 대기 멤버는 제한 없이 추가
                sql = @"
INSERT INTO PARTY_WAIT_MEMBER (PARTY_KEY, USER_ID, USER_NICKNAME) 
VALUES (@id, @userId, @userNickname)";
                
                affectedRows = await connection.ExecuteAsync(sql,
                    new { id, userId, userNickname },
                    transaction: transaction);
                
                return affectedRows == 0 ? JoinType.Error : JoinType.Wait;
            }

            return JoinType.Join;
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            return JoinType.Error;
        }   
    }

    public static async Task<bool> RemoveUser(string id, ulong userId, MySqlConnection connection, MySqlTransaction transaction)
    {

        try
        {
            // 두 개의 별도 UPDATE로 분리
            var sql1 = @"
UPDATE PARTY_MEMBER 
SET EXIT_FLAG = 1
WHERE USER_ID = @USER_ID AND PARTY_KEY = @id";

            var sql2 = @"
UPDATE PARTY_WAIT_MEMBER 
SET EXIT_FLAG = 1
WHERE USER_ID = @USER_ID AND PARTY_KEY = @id";

            var affected1 = await connection.ExecuteAsync(sql1,
                new { id, USER_ID = userId },
                transaction: transaction);

            var affected2 = await connection.ExecuteAsync(sql2,
                new { id, USER_ID = userId },
                transaction: transaction);

            // 둘 중 하나라도 업데이트되면 성공
            return (affected1 + affected2) > 0;
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            return false;
        }
    }

    public static async Task<bool> ChangeMessageId(ulong messageId, ulong newMessageId, MySqlConnection connection, MySqlTransaction transaction)
    {
        try
        {
            // 두 개의 별도 UPDATE로 분리
            var sql1 = @"
UPDATE PARTY
SET MESSAGE_KEY = @NEW
WHERE MESSAGE_KEY = @MESSAGE_KEY
";

            var affected1 = await connection.ExecuteAsync(sql1,
                new { MESSAGE_KEY = messageId , NEW = newMessageId },
                transaction: transaction);

            // 둘 중 하나라도 업데이트되면 성공
            return affected1 > 0;
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            return false;
        }
    }

    public static async Task<bool> ExitAllUser(Guid id, MySqlConnection connection, MySqlTransaction transaction)
    {
        try
        {
            // 두 개의 별도 UPDATE로 분리
            var sql1 = @"
UPDATE PARTY_MEMBER 
SET EXIT_FLAG = 1
WHERE PARTY_KEY = @id";

            var sql2 = @"
UPDATE PARTY_WAIT_MEMBER 
SET EXIT_FLAG = 1
WHERE PARTY_KEY = @id";

            var affected1 = await connection.ExecuteAsync(sql1,
                new {id},
                transaction: transaction);

            var affected2 = await connection.ExecuteAsync(sql2,
                new {id},
                transaction: transaction);

            // 둘 중 하나라도 업데이트되면 성공
            return (affected1 + affected2) > 0;
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            return false;
        }
    }
    
    /// <summary>
    /// 파티 인원 수 업데이트 (순수 UPDATE만)
    /// </summary>
    public static async Task<bool> UpdatePartySize(ulong messageId, int newSize, MySqlConnection connection, MySqlTransaction transaction)
    {

        try
        {
            var sql = @"
UPDATE PARTY
SET MAX_COUNT_MEMBER = @MaxCount
WHERE MESSAGE_KEY = @MessageKey
";
            var affectedRows = await connection.ExecuteAsync(sql,
                new { MaxCount = newSize, MessageKey = messageId },
                transaction: transaction);

            return affectedRows > 0;
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            return false;
        }
    }

    public static async Task<bool> SetPartyClose(string partyKey, bool isClose, MySqlConnection connection, MySqlTransaction transaction)
    {

        try
        {
            var sql = @"
UPDATE PARTY
SET IS_CLOSED = @isClose
WHERE PARTY_KEY = @partyKey
    ";
            
            var affectedRows = await connection.ExecuteAsync(sql,
                new { partyKey , isClose = isClose ? 1 : 0 },
                transaction: transaction);

            return affectedRows > 0;
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            return false;
        }
    }
    
    public static async Task<bool> PartyRename(string partyKey, string newName, MySqlConnection connection, MySqlTransaction transaction)
    {

        try
        {
            var sql = @"
UPDATE PARTY
SET DISPLAY_NAME= @newName
WHERE PARTY_KEY = @partyKey
    ";
            
            var affectedRows = await connection.ExecuteAsync(sql,
                new { partyKey , newName },
                transaction: transaction);

            return affectedRows > 0;
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            return false;
        }
    }


    public static async Task<bool> ExpiredParty(ulong messageId, MySqlConnection connection, MySqlTransaction transaction)
    {
        try
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
        catch (Exception e)
        {
            Console.WriteLine(e);
            return false;
        }   
    }



    public static async Task<List<PartyEntity>> CycleExpiredPartyList()
    {
        MySqlConnection connection = await DatabaseController.GetConnectionAsync();
        try
        {
            // 만료 시간이 지난 파티 목록 조회
            // PARTY_KEY를 명시적으로 문자열로 변환하여 Dapper 매핑 문제 방지
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
")).ToList();

            if (!parties.Any())
            {
                return new List<PartyEntity>();
            }
            
            return parties;
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            return new List<PartyEntity>();
        }
    }

    public static async Task<bool> RemoveAllUser(string id, MySqlConnection connection,
        MySqlTransaction transaction)
    {
        try
        {
            var sql = @"
UPDATE PARTY_MEMBER
SET EXIT_FLAG = 1
WHERE PARTY_KEY = @id
            ";
            
            var sql2 = @"
UPDATE PARTY_WAIT_MEMBER
SET EXIT_FLAG = 1
WHERE PARTY_KEY = @id
            ";
            
            var a1 = await connection.ExecuteAsync(sql,
                new { id },
                transaction: transaction);
            
            var a2 = await connection.ExecuteAsync(sql2,
                new { id },
                transaction: transaction);

            return a2 > 0 || a1 > 0;
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            return false;
        }
    }
    
    public static async Task<bool> SetStartDate(string partyKey, DateTime dateTime, MySqlConnection connection, MySqlTransaction transaction)
    {

        try
        {
            var sql = @"
UPDATE PARTY
SET START_DATE= @dateTime
WHERE PARTY_KEY = @partyKey
    ";
            
            var affectedRows = await connection.ExecuteAsync(sql,
                new { partyKey , dateTime },
                transaction: transaction);

            return affectedRows > 0;
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            return false;
        }
    }
    
    public static async Task<bool> SetExpireDate(string partyKey, DateTime dateTime, MySqlConnection connection, MySqlTransaction transaction)
    {

        try
        {
            var sql = @"
UPDATE PARTY
SET EXPIRE_DATE= @dateTime
WHERE PARTY_KEY = @partyKey
    ";
            
            var affectedRows = await connection.ExecuteAsync(sql,
                new { partyKey , dateTime },
                transaction: transaction);

            return affectedRows > 0;
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            return false;
        }
    }
    
}

