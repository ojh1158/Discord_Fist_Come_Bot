using Dapper;
using DiscordBot.scripts.db.Models;
using MySqlConnector;

namespace DiscordBot.scripts.db.Repositories;

/// <summary>
/// 사용자 설정 Repository (Data Access Layer)
/// </summary>
public class UserRepository
{
    /// <summary>
    /// 사용자 ID로 알림 설정을 조회합니다.
    /// </summary>
    /// <returns>없으면 null</returns>
    public static async Task<UserSettingEntity?> GetUserSettingAsync(ulong userId, MySqlConnection? connection = null, MySqlTransaction? transaction = null)
    {
        connection ??= await DatabaseController.GetConnectionAsync();
        
        try
        {
            var entity = await connection.QuerySingleOrDefaultAsync<UserSettingEntity>(
                @"
SELECT
    USER_ID,
    ALL_ALERT_FLAG,
    MY_PARTY_FULL_ALERT_FLAG,
    MY_PARTY_JOIN_USER_ALERT_FLAG,
    MY_PARTY_LEFT_USER_ALERT_FLAG,
    PARTY_START_TIME_ALERT_FLAG,
    JOIN_PARTY_TO_WAIT_FLAG
FROM USER_CONFIG
WHERE USER_ID = @userId
",
                new { userId },
                transaction: transaction);

            return entity;
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            return null;
        }
    }
    
    public static async Task<List<StartAlertEntity>> GetAlertUsers()
    {
        var connection = await DatabaseController.GetConnectionAsync();
        
        try
        {
            var entity = await connection.QueryAsync<StartAlertEntity>(
                @"
WITH HISTORY AS (
    SELECT *
    FROM PARTY_START_ALERT_HISTORY
)
SELECT
    P.CHANNEL_KEY,
    CAST(P.PARTY_KEY AS CHAR(36)) AS PARTY_KEY,
    P.DISPLAY_NAME,
    PM.USER_ID
FROM PARTY AS P
         LEFT JOIN PARTY_MEMBER AS PM
                   ON PM.PARTY_KEY = P.PARTY_KEY
         LEFT JOIN USER_CONFIG AS UC
                   ON UC.USER_ID = PM.USER_ID
WHERE NOT EXISTS (
    SELECT 1
    FROM HISTORY H
    WHERE H.PARTY_KEY = P.PARTY_KEY
    AND H.CHANGE_TIME_FLAG = FALSE
)
  AND P.IS_EXPIRED = 0
  AND P.START_DATE IS NOT NULL
  AND P.START_DATE > NOW()
  AND P.START_DATE <= DATE_ADD(NOW(), INTERVAL 5 MINUTE)
  AND PM.EXIT_FLAG = 0
  AND UC.ALL_ALERT_FLAG = 1 AND UC.PARTY_START_TIME_ALERT_FLAG = 1
GROUP BY PM.USER_ID, P.PARTY_KEY, P.CHANNEL_KEY, P.DISPLAY_NAME
");

            return entity.ToList();
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            return null;
        }
    }
    
    public static async Task<bool> InsertAlertParty(string PARTY_KEY, MySqlConnection? connection = null, MySqlTransaction? transaction = null)
    {
        connection ??= await DatabaseController.GetConnectionAsync();
        
        try
        {
            var sql = @"
INSERT INTO PARTY_START_ALERT_HISTORY (
    PARTY_KEY,
    SEND_TIME
) VALUES (
             @PARTY_KEY, now()
         ) AS new
ON DUPLICATE KEY UPDATE
    SEND_TIME = new.SEND_TIME,
    CHANGE_TIME_FLAG = FALSE
";
            var parameters = new
            {
                PARTY_KEY
            };

            var affectedRows = await connection.ExecuteAsync(sql, parameters, transaction: transaction);
            return affectedRows > 0;
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            return false;
        }
    }
    
    public static async Task<bool> RemoveAlertParty(string PARTY_KEY, MySqlConnection? connection = null, MySqlTransaction? transaction = null)
    {
        connection ??= await DatabaseController.GetConnectionAsync();
        
        try
        {
            var sql = @"
UPDATE PARTY_START_ALERT_HISTORY
SET CHANGE_TIME_FLAG = TRUE
WHERE PARTY_KEY = @PARTY_KEY
";
            var parameters = new
            {
                PARTY_KEY
            };

            var affectedRows = await connection.ExecuteAsync(sql, parameters, transaction: transaction);
            return affectedRows > 0;
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            return false;
        }
    }

    /// <summary>
    /// 사용자 알림 설정을 저장합니다. (없으면 INSERT, 있으면 UPDATE)
    /// </summary>
    public static async Task<bool> SetUserSettingAsync(UserSettingEntity entity, MySqlConnection? connection = null, MySqlTransaction? transaction = null)
    {
        connection ??= await DatabaseController.GetConnectionAsync();
        
        try
        {
            var sql = @"
INSERT INTO USER_CONFIG (
    USER_ID,
    ALL_ALERT_FLAG,
    MY_PARTY_FULL_ALERT_FLAG,
    MY_PARTY_JOIN_USER_ALERT_FLAG,
    MY_PARTY_LEFT_USER_ALERT_FLAG,
    PARTY_START_TIME_ALERT_FLAG,
    JOIN_PARTY_TO_WAIT_FLAG                     
) VALUES (
    @USER_ID,
    @ALL_ALERT_FLAG,
    @MY_PARTY_FULL_ALERT_FLAG,
    @MY_PARTY_JOIN_USER_ALERT_FLAG,
    @MY_PARTY_LEFT_USER_ALERT_FLAG,
    @PARTY_START_TIME_ALERT_FLAG,
    @JOIN_PARTY_TO_WAIT_FLAG
)
ON DUPLICATE KEY UPDATE
    ALL_ALERT_FLAG = VALUES(ALL_ALERT_FLAG),
    MY_PARTY_FULL_ALERT_FLAG = VALUES(MY_PARTY_FULL_ALERT_FLAG),
    MY_PARTY_JOIN_USER_ALERT_FLAG = VALUES(MY_PARTY_JOIN_USER_ALERT_FLAG),
    MY_PARTY_LEFT_USER_ALERT_FLAG = VALUES(MY_PARTY_LEFT_USER_ALERT_FLAG),
    PARTY_START_TIME_ALERT_FLAG = VALUES(PARTY_START_TIME_ALERT_FLAG),
    JOIN_PARTY_TO_WAIT_FLAG = VALUES(JOIN_PARTY_TO_WAIT_FLAG)
";
            var parameters = new
            {
                entity.USER_ID,
                entity.ALL_ALERT_FLAG,
                entity.MY_PARTY_FULL_ALERT_FLAG,
                entity.MY_PARTY_JOIN_USER_ALERT_FLAG,
                entity.MY_PARTY_LEFT_USER_ALERT_FLAG,
                entity.PARTY_START_TIME_ALERT_FLAG,
                entity.JOIN_PARTY_TO_WAIT_FLAG
            };

            var affectedRows = await connection.ExecuteAsync(sql, parameters, transaction: transaction);
            return affectedRows > 0;
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            return false;
        }
    }
}
