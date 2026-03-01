using DiscordBot.scripts.db.Models;
using DiscordBot.scripts.db.Repositories;

namespace DiscordBot.scripts.db.Services;

/// <summary>
/// 사용자 설정 비즈니스 로직 처리 (Service Layer)
/// Repository 메서드에 Lock이 내장되어 있어 간단하게 호출 가능
/// </summary>
public class UserService
{
    /// <summary>
    /// 사용자 알림 설정 조회
    /// </summary>
    /// <returns>없으면 null (기본값 사용 시 엔티티 생성은 호출부에서)</returns>
    public static async Task<UserSettingEntity> GetUserSettingAsync(ulong userId)
    {
        var setting = await UserRepository.GetUserSettingAsync(userId);
        if (setting == null)
        {
            var entity = new UserSettingEntity()
            {
                USER_ID = userId
            };
            await SetUserSettingAsync(entity);
                
            setting = entity;
        }
            
        return setting;
    }
    
    public static async Task<List<StartAlertEntity>> GetAlertUsers()
    {
        var alertEntities = await UserRepository.GetAlertUsers();

        var groupBy = alertEntities.GroupBy(d => d.PARTY_KEY!, d => d);


        _ = Task.Run(async () =>
        {
            foreach (var partyKey in groupBy.Select(g => g.Key))
            {
                await UserRepository.InsertAlertParty(partyKey);
            }
        });
        
        return groupBy.SelectMany(g => g).ToList();
    }

    /// <summary>
    /// 사용자 알림 설정 저장 (없으면 INSERT, 있으면 UPDATE)
    /// </summary>
    public static Task<bool> SetUserSettingAsync(UserSettingEntity entity)
    {
        return UserRepository.SetUserSettingAsync(entity);
    }
}
