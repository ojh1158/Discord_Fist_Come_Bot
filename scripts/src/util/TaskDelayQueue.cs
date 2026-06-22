using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Serilog;

namespace DiscordBot.scripts.src.util;

public class TaskDelayQueue : ISingleton
{
    // 🔥 기본 상수를 최소/최대 범위로 정의합니다.
    private const float MinDelaySeconds = 1f;
    private const float MaxDelaySeconds = 5f;
    private const float DelayStepSeconds = 1f; // 연타 시 늘어날 가중치 (원하는 대로 조절 가능)
    private const double SpamWindowSeconds = 7.0f; // 연타 기준 시간 (30초)
    
    private readonly MemoryCacheEntryOptions _options = new MemoryCacheEntryOptions().SetAbsoluteExpiration(TimeSpan.FromHours(1));

    private string GetKey(string partyId) => $"{partyId}_DelayQueue";

    public async Task<DelayInfo?> EnqueueAndWaitAsync(string partyId)
    {
        var key = GetKey(partyId);
        
        if (!CacheManager.Cache.TryGetValue(key, out PartyQueueState? state))
        {
            var partyQueueState = new PartyQueueState();
            CacheManager.Cache.Set(key, partyQueueState, _options);
            state = partyQueueState;
        }
        
        if (state == null)
        {
            Log.Error("><><");
            return null;
        }
        
        // 1. 해당 파티방의 대기 카운트 증가
        Interlocked.Increment(ref state.WaitingCount);

        // 2. 해당 파티방 전용 세마포어 진입
        await state.Semaphore.WaitAsync();

        try
        {
            var now = DateTime.UtcNow;

            if (!state.IsFirstRequest)
            {
                // 💡 [핵심] 마지막 요청으로부터 30초 이내에 또 요청이 왔는지 확인
                if ((now - state.LastProcessedTime).TotalSeconds <= SpamWindowSeconds)
                {
                    // 30초 이내 연타라면 딜레이 단계를 증가시킴 (최대 10초 제한)
                    state.CurrentDelaySeconds = Math.Min(MaxDelaySeconds, state.CurrentDelaySeconds + DelayStepSeconds);
                    Log.Information("[{PartyId}] 연타 감지! 딜레이가 {Delay}초로 증가합니다.", partyId, state.CurrentDelaySeconds);
                }
                else
                {
                    // 30초가 지났다면 딜레이를 다시 최소치(1.5초)로 초기화
                    state.CurrentDelaySeconds = MinDelaySeconds;
                }

                var currentDelay = state.CurrentDelaySeconds;

                // 실제 대기해야 하는 시간 계산
                var timeSinceLastRequest = now - state.LastProcessedTime;
                if (timeSinceLastRequest < TimeSpan.FromSeconds(currentDelay))
                {
                    var delayTime = TimeSpan.FromSeconds(currentDelay) - timeSinceLastRequest;
                    await Task.Delay(delayTime);
                }
            }
            else
            {
                state.IsFirstRequest = false;
                state.CurrentDelaySeconds = MinDelaySeconds; // 첫 요청은 기본 1.5초 세팅
            }

            // 3. 딜레이가 끝난 '지금 통과 시점'을 마지막 처리 시간으로 기록
            state.LastProcessedTime = DateTime.UtcNow;

            // 4. 현재 내 뒤에 줄 서 있는 사람 수 계산
            var currentQueueCount = Math.Max(0, state.WaitingCount - 1);
            
            return new DelayInfo
            {
                WaitingCount = currentQueueCount,
                DelaySeconds = state.CurrentDelaySeconds,
            };
        }
        finally
        {
            // 5. 다음 사람을 위해 세마포어 해제
            Interlocked.Decrement(ref state.WaitingCount);
            state.Semaphore.Release();
        }
    }

    public DelayInfo? GetDelayInfo(string partyId)
    {
        if (CacheManager.Cache.TryGetValue(GetKey(partyId), out PartyQueueState? state))
        {
            return new DelayInfo
            {
                DelaySeconds = state!.CurrentDelaySeconds,
                WaitingCount = state.WaitingCount,
            };
        }
        return null;
    }
    
    public class DelayInfo
    {
        public required int WaitingCount { get; init; }
        public required float DelaySeconds { get; init; }
    }

    // 파티별 상태를 담는 내부 클래스
    public class PartyQueueState
    {
        public readonly SemaphoreSlim Semaphore = new(1, 1);
        public int WaitingCount = 0;                        
        public DateTime LastProcessedTime = DateTime.MinValue;
        public bool IsFirstRequest = true;
        
        // 💡 파티별로 누적되는 현재 딜레이 시간을 저장하는 변수 추가
        public float CurrentDelaySeconds { get; set; } = MinDelaySeconds;
    }
}