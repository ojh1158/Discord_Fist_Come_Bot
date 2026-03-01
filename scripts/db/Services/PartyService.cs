using Discord;
using DiscordBot.scripts._src;
using DiscordBot.scripts._src.party;
using DiscordBot.scripts._src.Services;
using DiscordBot.scripts.db.Models;
using DiscordBot.scripts.db.Repositories;
using MySqlConnector;

namespace DiscordBot.scripts.db.Services;

/// <summary>
/// 파티 비즈니스 로직 처리 (Service Layer)
/// Spring Boot의 @Service와 동일한 역할
/// Repository 메서드에 Lock이 내장되어 있어 간단하게 호출 가능
/// </summary>
public class PartyService : ISingleton 
{
    
    private static DiscordServices discordServices;
    
    public PartyService(DiscordServices discordServices)
    {
        PartyService.discordServices = discordServices;
    }
    
    /// <summary>
    /// 파티 생성
    /// 비즈니스 로직: 기존 파티 만료 처리 + 새 파티 생성
    /// </summary>
    public static Task<bool> CreatePartyAsync(PartyEntity party)
    {
        return DatabaseController.ExecuteInTransactionAsync(async (conn, trans) =>
        {
            return await PartyRepository.CreatePartyAsync(party, conn, trans);
        });
    }
    
    /// <summary>
    /// 파티 참가
    /// 비즈니스 로직: 중복 체크 + 참가 + 대기열 승격
    /// </summary>
    public static Task<JoinType> JoinPartyAsync(PartyEntity entity, ulong userId, string userNickname)
    {
        return DatabaseController.ExecuteInTransactionAsync(async (conn, trans) =>
        {
            // 1. 이미 참가했는지 체크
            var exists = await PartyRepository.ExistsUser(entity.PARTY_KEY, userId, conn, trans);
            if (exists)
            {
                return JoinType.Exists;
            }

            // 2. 유저 추가
            var type = await PartyRepository.AddUser(entity.PARTY_KEY, userId, userNickname, conn, trans);
            if (type == JoinType.Error)
            {
                return JoinType.Error;
            }
            
            return JoinType.Join;
        });
    }
    
    /// <summary>
    /// 파티 나가기
    /// 비즈니스 로직: 나가기 + 대기열 승격
    /// </summary>
    public static Task<bool> LeavePartyAsync(PartyEntity party, ulong userId)
    {
        return DatabaseController.ExecuteInTransactionAsync(async (conn, trans) =>
        {
            if (!await PartyRepository.ExistsUser(party.PARTY_KEY, userId, conn, trans))
            {
                return false;
            }
            
            // 1. 유저 제거
            var removed = await PartyRepository.RemoveUser(party.PARTY_KEY, userId, conn, trans);
            if (!removed)
            {
                return false;
            }

            // 2. 대기열 승격 처리
            await PromoteWaitingMembersAsync(party, conn, trans);

            return true;
        });
    }
    
    /// <summary>
    /// 파티 인원 변경
    /// 비즈니스 로직: 인원 수 변경 + 증가 시 대기열 승격
    /// </summary>
    public static Task<(List<PartyMemberEntity> members, List<PartyMemberEntity> waitMember)> ResizePartyAsync(PartyEntity entity, int newCount)
    {
        return DatabaseController.ExecuteInTransactionAsync(async (conn, trans) =>
        {
            // 1. 인원 수 업데이트
            var updated = await PartyRepository.UpdatePartySize(entity.MESSAGE_KEY, newCount, conn, trans);
            if (!updated)
            {
                throw new Exception("인원 수 업데이트 실패");
            }

            entity.MAX_COUNT_MEMBER = newCount;

            // 2. 대기열 승격 처리 (인원이 증가한 경우)
            await PromoteWaitingMembersAsync(entity, conn, trans);

            // 같은 connection/transaction 사용하여 데드락 방지
            var members = await PartyRepository.GetPartyMemberList(entity.PARTY_KEY, conn, trans);
            var waitMembers = await PartyRepository.GetPartyWaitMemberList(entity.PARTY_KEY, conn, trans);

            return (members, waitMembers);
        });
    }
    
    /// <summary>
    /// 파티 강퇴
    /// 비즈니스 로직: 강퇴 + 대기열 승격
    /// </summary>
    public static Task<bool> KickMemberAsync(PartyEntity entity, ulong targetUserId)
    {
        return DatabaseController.ExecuteInTransactionAsync(async (conn, trans) =>
        {
            // 1. 유저 제거
            var removed = await PartyRepository.RemoveUser(entity.PARTY_KEY, targetUserId, conn, trans);
            if (!removed)
            {
                return false;
            }

            // 2. 대기열 승격 처리
            await PromoteWaitingMembersAsync(entity, conn, trans);

            return true;
        });
    }
    
    /// <summary>
    /// 파티 만료
    /// </summary>
    public static Task<bool> ExpirePartyAsync(ulong messageId)
    {
        return DatabaseController.ExecuteInTransactionAsync(async (conn, trans) =>
        {
            return await PartyRepository.ExpiredParty(messageId, conn, trans);
        });
    }
    
    /// <summary>
    /// 파티 일시정지/재개
    /// </summary>
    public static Task<bool> SetPartyCloseAsync(string partyKey, bool isClose)
    {
        return DatabaseController.ExecuteInTransactionAsync(async (conn, trans) =>
        {
            return await PartyRepository.SetPartyClose(partyKey, isClose, conn, trans);
        });
    }

    public static Task<bool> PartyRename(string partyKey, string newName)
    {
        return DatabaseController.ExecuteInTransactionAsync(async (conn, trans) =>
        {
            // await PartyRepository.GetPartyEntity(messageKey, conn, trans);
            return await PartyRepository.PartyRename(partyKey, newName, conn, trans);
        });
    }
    
    public static Task<bool> ChangeMessageId(ulong messageId, ulong newid)
    {
        return DatabaseController.ExecuteInTransactionAsync(async (conn, trans) =>
        {
            return await PartyRepository.ChangeMessageId(messageId, newid, conn, trans);
        });
    }
    
    // ==================== 조회 메서드 ====================
    
    /// <summary>
    /// 파티 정보 조회
    /// </summary>
    public static async Task<PartyEntity?> GetPartyEntityAsync(string partyKey)
    {
        var con = await DatabaseController.GetConnectionAsync();
        
        try{
            var entity = await PartyRepository.GetPartyEntityNotMember(partyKey, con);

            if (entity == null) return null;

            // 2. 파티 멤버
            entity.Members = await PartyRepository.GetPartyMemberList(partyKey, con);

            // 3. 대기 멤버
            entity.WaitMembers = await PartyRepository.GetPartyWaitMemberList(partyKey, con);
            
            return entity;
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            return null;
        }
    }
    
    /// <summary>
    /// 파티 멤버 리스트 조회
    /// </summary>
    public static Task<List<PartyMemberEntity>?> GetPartyMemberListAsync(string id)
    {
        return DatabaseController.ExecuteInTransactionAsync(async (conn, trans) =>
        {
            return await PartyRepository.GetPartyMemberList(id, conn, trans);
        });
    }
    
    /// <summary>
    /// 대기 멤버 리스트 조회
    /// </summary>
    public static Task<List<PartyMemberEntity>?> GetPartyWaitMemberListAsync(string id)
    {
        return DatabaseController.ExecuteInTransactionAsync(async (conn, trans) =>
        {
            return await PartyRepository.GetPartyWaitMemberList(id, conn, trans);
        });
    }
    
    /// <summary>
    /// 파티 존재 여부 확인
    /// </summary>
    public static Task<bool> IsPartyExistsAsync(string displayName, ulong guildId)
    {
        return DatabaseController.ExecuteInTransactionAsync(async (conn, trans) =>
        {
            return await PartyRepository.IsPartyExistsAsync(displayName, guildId, conn, trans);
        });
    }
    
    /// <summary>
    /// 만료된 파티 목록 조회
    /// </summary>
    public static async Task<List<PartyEntity>> CycleExpiredPartyListAsync()
    {
        var partyEntities = await PartyRepository.CycleExpiredPartyList();

        for (var i = 0; i < partyEntities.Count; i++)
        {
            var partyEntity = partyEntities[i];
            var entity = await GetPartyEntityAsync(partyEntity.PARTY_KEY);
            partyEntities[i] = entity!;
        }
        
        return partyEntities;
    }

    public static Task<bool> SetStartDate(string partyKey, DateTime dateTime)
    {
        return DatabaseController.ExecuteInTransactionAsync(async (conn, trans) =>
        {
            await UserRepository.RemoveAlertParty(partyKey, conn, trans);
            
            return await PartyRepository.SetStartDate(partyKey, dateTime, conn, trans);
        });
    }
    
    public static Task<bool> SetExpireDate(string partyKey, DateTime dateTime)
    {
        return DatabaseController.ExecuteInTransactionAsync(async (conn, trans) =>
        {
            return await PartyRepository.SetExpireDate(partyKey, dateTime, conn, trans);
        });
    }

    /// <summary>
    /// 대기열 승격 처리 (비즈니스 로직)
    /// 파티에 빈 자리가 있고 대기 멤버가 있으면 자동 승격
    /// </summary>
    private static async Task PromoteWaitingMembersAsync(PartyEntity entity, MySqlConnection connection,
        MySqlTransaction transaction)
    {
        var partyAllMemberList = await PartyRepository.GetPartyAllMemberList(entity.PARTY_KEY, connection, transaction);

        _ = Task.Run(async () =>
        {
            // 1. 모의 계산만: DB(partyAllMemberList)에 있는 멤버만 남긴 “가상” 리스트 (entity는 수정 안 함)
            var simulatedMembers = entity.Members
                .Where(m => partyAllMemberList.Any(p => p.USER_ID == m.USER_ID))
                .ToList();
        
            var simulatedCount = simulatedMembers.Count;
            var waitCount = entity.WaitMembers.Count;
        
            if (simulatedCount < entity.MAX_COUNT_MEMBER && waitCount > 0)
            {
                var count = Math.Min(entity.MAX_COUNT_MEMBER - simulatedCount, waitCount);
                // 2. 알림 대상만 앞에서 count명 (스냅샷으로 열거)
                var waitSnapshot = entity.WaitMembers.Take(count).ToList();
        
                for (var i = 0; i < waitSnapshot.Count; i++)
                {
                    var targetMember = waitSnapshot[i];
                    var user = await discordServices.client.Rest.GetUserAsync(targetMember.USER_ID);
                    discordServices.SendUserAlert(entity, user, PartyConstant.USER_ALERT_JOIN_PARTY_TO_WAIT_FLAG);
                }
            }
        });

        await PartyRepository.RemoveAllUser(entity.PARTY_KEY, connection, transaction);

        for (var i = 0; i < partyAllMemberList.Count; i++)
        {
            await PartyRepository.AddUser(entity.PARTY_KEY, partyAllMemberList[i].USER_ID,
                partyAllMemberList[i].USER_NICKNAME, connection, transaction);
        }
    }
}

