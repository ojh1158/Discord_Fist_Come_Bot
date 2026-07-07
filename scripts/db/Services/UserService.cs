using System.Collections.Concurrent;
using DiscordBot.scripts.db.Models;
using DiscordBot.scripts.db.Repositories;
using DiscordBot.scripts.src;
using MySqlConnector;

namespace DiscordBot.scripts.db.Services;

/// <summary>
/// 사용자 설정 비즈니스 로직 처리 (Service Layer)
/// Repository 메서드에 Lock이 내장되어 있어 간단하게 호출 가능
/// </summary>
public class UserService(DatabaseController databaseController, UserRepository userRepository, PartyRepository partyRepository) : ISingleton
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
            List<StartAlertEntity> startAlertEntities = [];
            HashSet<ulong> userIDHash = [];
            HashSet<ulong> alertingUserIDHash = [];
            
            var alertEntities = await userRepository.GetAlertUsers(conn, trans);
            
            foreach (var startAlertEntity in alertEntities)
            {
                userIDHash.Add(startAlertEntity.USER_ID);
            }

            // 1. Null 체크 추가 (에러 방지)
            if (alertEntities.Count == 0)
            {
                return [];
            }

            var groupBy = alertEntities.GroupBy(d => d.PARTY_KEY!, d => d).ToArray();

            foreach (var grouping in groupBy)
            {
                var partyEntity = await partyRepository.GetPartyEntity(grouping.Key, conn, trans);
                
                await userRepository.InsertAlertParty(grouping.Key, conn, trans);
                
                if (partyEntity is null) continue;
                
                foreach (var partyMemberEntity in partyEntity.MemberOnly)
                {
                    if (userIDHash.Contains(partyMemberEntity.USER_ID))
                    {
                        alertingUserIDHash.Add(partyMemberEntity.USER_ID);
                    }
                }
            }

            foreach (var startAlertEntity in alertEntities)
            {
                if (alertingUserIDHash.Contains(startAlertEntity.USER_ID))
                {
                    startAlertEntities.Add(startAlertEntity);
                }
            }
    
            return startAlertEntities; // 이미 가져온 리스트를 반환
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

    public Task<ConcurrentDictionary<string, string>?> GetUsersName(params string[] userIds)
    {
        return databaseController.ExecuteInTransactionAsync(async (conn, trans) =>
        {
            return await userRepository.GetUserNames(userIds, conn, trans);
        });
    }
}
