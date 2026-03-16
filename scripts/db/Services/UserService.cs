using DiscordBot.scripts._src;
using DiscordBot.scripts.db.Models;
using DiscordBot.scripts.db.Repositories;
using MySqlConnector;

namespace DiscordBot.scripts.db.Services;

/// <summary>
/// 사용자 설정 비즈니스 로직 처리 (Service Layer)
/// Repository 메서드에 Lock이 내장되어 있어 간단하게 호출 가능
/// </summary>
public class UserService(DatabaseController databaseController, UserRepository userRepository) : ISingleton
{
    /// <summary>
    /// 사용자 알림 설정 조회
    /// </summary>
    /// <returns>없으면 null (기본값 사용 시 엔티티 생성은 호출부에서)</returns>
    public Task<UserSettingEntity> GetUserSettingAsync(ulong userId)
    {
        return databaseController.ExecuteInTransactionAsync(async (conn, trans) =>
        {
            var setting = await userRepository.GetUserSettingAsync(userId, conn, trans);
            if (setting == null)
            {
                var entity = new UserSettingEntity()
                {
                    USER_ID = userId
                };
                await SetUserSettingAsync(entity, conn, trans);
                    
                setting = entity;
            }
                
            return setting;
        })!;
    }
    
    public async Task<List<StartAlertEntity>?> GetAlertUsers()
    {
        return await databaseController.ExecuteInTransactionAsync(async (conn, trans) =>
        {
            var alertEntities = await userRepository.GetAlertUsers(conn, trans);

            // 1. Null 체크 추가 (에러 방지)
            if (alertEntities == null || !alertEntities.Any())
            {
                return new List<StartAlertEntity>();
            }

            var groupBy = alertEntities.GroupBy(d => d.PARTY_KEY!, d => d);
        
            // 2. Task.Run 제거. 트랜잭션 내에서는 반드시 await로 순차 실행해야 함
            foreach (var group in groupBy)
            {
                // Insert 작업이 끝날 때까지 기다려야 트랜잭션이 유지됨
                await userRepository.InsertAlertParty(group.Key, conn, trans);
            }
    
            return alertEntities.ToList(); // 이미 가져온 리스트를 반환
        });
    }

    /// <summary>
    /// 사용자 알림 설정 저장 (없으면 INSERT, 있으면 UPDATE)
    /// </summary>
    public Task<bool> SetUserSettingAsync(UserSettingEntity entity, MySqlConnection? connection = null, MySqlTransaction? transaction = null)
    {
        if (connection == null)
        {
            return databaseController.ExecuteInTransactionAsync(async (conn, trans) =>
            {
                return await SetUserSettingAsync(entity, conn, trans);
            });
        }

        return userRepository.SetUserSettingAsync(entity, connection, transaction);
    }
}
