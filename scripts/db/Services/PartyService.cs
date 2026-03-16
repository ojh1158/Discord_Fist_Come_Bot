using System.Diagnostics.CodeAnalysis;
using Discord;
using DiscordBot.scripts._src;
using DiscordBot.scripts._src.party;
using DiscordBot.scripts._src.Services;
using DiscordBot.scripts.db.Models;
using DiscordBot.scripts.db.Repositories;
using MySqlConnector;
using Serilog;

namespace DiscordBot.scripts.db.Services;

/// <summary>
/// 파티 비즈니스 로직 처리 (Service Layer)
/// Spring Boot의 @Service와 동일한 역할
/// Repository 메서드에 Lock이 내장되어 있어 간단하게 호출 가능
/// </summary>
public class PartyService(DatabaseController databaseController, UserRepository userRepository, PartyRepository partyRepository) : ISingleton 
{
    /// <summary>
    /// 파티 생성
    /// 비즈니스 로직: 기존 파티 만료 처리 + 새 파티 생성
    /// </summary>
    public Task<bool> CreatePartyAsync(PartyEntity party)
    {
        return databaseController.ExecuteInTransactionAsync(async (conn, trans) =>
        {
            return await partyRepository.CreatePartyAsync(party, conn, trans);
        });
    }
    
    /// <summary>
    /// 파티 참가
    /// 비즈니스 로직: 중복 체크 + 참가
    /// </summary>
    public Task<(PartyEntity? entity, JoinType type)> JoinPartyAsync(string partyKey, ulong userId, string userNickname)
    {
        return databaseController.ExecuteInTransactionAsync(async (conn, trans) =>
        {
            var nowParty = await partyRepository.GetPartyEntity(partyKey, conn, trans);

            if (nowParty == null) return (nowParty, JoinType.Error);
            
            if (nowParty.Members.Exists(m => m.USER_ID == userId)
                || nowParty.WaitMembers.Exists(m => m.USER_ID == userId))
            {
                return (nowParty, JoinType.Exists);
            }

            var type = await partyRepository.AddUser(partyKey, userId, userNickname, conn, trans);
            return (await GetPartyEntityAsync(partyKey, conn, trans), type);
        });
    }
    
    /// <summary>
    /// 파티 나가기
    /// 비즈니스 로직: 나가기
    /// </summary>
    public Task<PartyEntity?> LeavePartyAsync(PartyEntity party, ulong userId)
    {
        return databaseController.ExecuteInTransactionAsync(async (conn, trans) =>
        {
            var nowParty = await partyRepository.GetPartyEntity(party.PARTY_KEY, conn, trans);
            
            if (nowParty == null) return null;
            
            if (nowParty.Members.Exists(m => m.USER_ID == userId)
                || nowParty.WaitMembers.Exists(m => m.USER_ID == userId))
            {
                var removed = await partyRepository.RemoveUser(party.PARTY_KEY, userId, conn, trans);
                if (!removed)
                {
                    return null;
                }   
            }
            else
            {
                return null;
            }

            return await GetPartyEntityAsync(party.PARTY_KEY, conn, trans);
        });
    }
    
    /// <summary>
    /// 파티 인원 변경
    /// 비즈니스 로직: 인원 수 변경 + 증가 시 대기열 승격
    /// </summary>
    public Task<bool> ResizePartyAsync(PartyEntity entity, int newCount)
    {
        return databaseController.ExecuteInTransactionAsync(async (conn, trans) =>
        {
            // 1. 인원 수 업데이트
            var updated = await partyRepository.UpdatePartySize(entity.PARTY_KEY, newCount, conn, trans);
            if (!updated)
            {
                return false;
            }

            entity.MAX_COUNT_MEMBER = newCount;

            return true;
        });
    }
    
    /// <summary>
    /// 파티 강퇴
    /// 비즈니스 로직: 강퇴 + 대기열 승격
    /// </summary>
    public Task<bool> KickMemberAsync(PartyEntity entity, ulong targetUserId)
    {
        return databaseController.ExecuteInTransactionAsync(async (conn, trans) =>
        {
            await partyRepository.GetPartyLock(entity.PARTY_KEY, conn, trans);
            
            var removed = await partyRepository.RemoveUser(entity.PARTY_KEY, targetUserId, conn, trans);
            return removed;
        });
    }
    
    /// <summary>
    /// 파티 만료
    /// </summary>
    public Task<bool> ExpirePartyAsync(ulong messageId)
    {
        return databaseController.ExecuteInTransactionAsync(async (conn, trans) =>
        {
            return await partyRepository.ExpiredParty(messageId, conn, trans);
        });
    }
    
    /// <summary>
    /// 파티 일시정지/재개
    /// </summary>
    public Task<bool> SetPartyCloseAsync(string partyKey, bool isClose)
    {
        return databaseController.ExecuteInTransactionAsync(async (conn, trans) =>
        {
            return await partyRepository.SetPartyClose(partyKey, isClose, conn, trans);
        });
    }

    public Task<bool> PartyRename(string partyKey, string newName)
    {
        return databaseController.ExecuteInTransactionAsync(async (conn, trans) =>
        {
            return await partyRepository.PartyRename(partyKey, newName, conn, trans);
        });
    }
    
    public Task<bool> ChangeMessageId(ulong messageId, ulong newid)
    {
        return databaseController.ExecuteInTransactionAsync(async (conn, trans) =>
        {
            return await partyRepository.ChangeMessageId(messageId, newid, conn, trans);
        });
    }
    
    // ==================== 조회 메서드 ====================
    
    /// <summary>
    /// 파티 정보 조회
    /// </summary>
    public async Task<PartyEntity?> GetPartyEntityAsync(string partyKey, MySqlConnection? conn = null, MySqlTransaction? trans = null)
    {
        if (conn == null)
        {
            return await databaseController.ExecuteAsync(async conn =>
            {
                return await partyRepository.GetPartyEntity(partyKey, conn);
            });
        }
        else
        {
            return await partyRepository.GetPartyEntity(partyKey, conn, trans);
        }
    }
    
    /// <summary>
    /// 만료된 파티 목록 조회
    /// </summary>
    public async Task<List<PartyEntity>?> CycleExpiredPartyListAsync()
    {
        return await databaseController.ExecuteInTransactionAsync(async (conn, trans) =>
        {
            var partyEntities = await partyRepository.CycleExpiredPartyList(conn, trans);
            
            for (var i = 0; i < partyEntities.Count; i++)
            {
                var partyEntity = partyEntities[i];
                var entity = await GetPartyEntityAsync(partyEntity.PARTY_KEY, conn, trans);
                partyEntities[i] = entity!;
            }
        
            return partyEntities;
        });
    }
    
    public Task<bool> SetOwner(string partyKey, ulong ownerKey, string name)
    {
        return databaseController.ExecuteInTransactionAsync(async (conn, trans) =>
        {
            return await partyRepository.SetOwner(partyKey, ownerKey, name, conn, trans);
        });
    }

    public Task<bool> SetStartDate(string partyKey, DateTime dateTime)
    {
        return databaseController.ExecuteInTransactionAsync(async (conn, trans) =>
        {
            await userRepository.RemoveAlertParty(partyKey, conn, trans);
            
            return await partyRepository.SetStartDate(partyKey, dateTime, conn, trans);
        });
    }
    
    public Task<bool> SetExpireDate(string partyKey, DateTime dateTime)
    {
        return databaseController.ExecuteInTransactionAsync(async (conn, trans) =>
        {
            return await partyRepository.SetExpireDate(partyKey, dateTime, conn, trans);
        });
    }
}

