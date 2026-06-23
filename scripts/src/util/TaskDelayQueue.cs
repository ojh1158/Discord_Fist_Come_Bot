using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Serilog;

namespace DiscordBot.scripts.src.util;

public class TaskDelayQueue : ISingleton
{
    public const float MinDelaySeconds = 1f;
    public const float MaxDelaySeconds = 5f;
    public const float DelayStepSeconds = 1f; 
    public const double SpamWindowSeconds = 7.0f; 
    
    private readonly MemoryCacheEntryOptions _options = new MemoryCacheEntryOptions().SetAbsoluteExpiration(TimeSpan.FromHours(1));

    private string GetKey(string partyId, bool keepOnlyLast) => $"{partyId}_DelayQueue_{(keepOnlyLast? "keep" : "noKeep")}";
    private static readonly SemaphoreSlim _lock = new(1, 1);

    public async Task<DelayInfo?> EnqueueAndWaitAsync(string partyId, bool keepOnlyLast)
    {
        var key = GetKey(partyId, keepOnlyLast);

        PartyQueueState? state;
        
        await _lock.WaitAsync();
        try
        {
            if (!CacheManager.Cache.TryGetValue(key, out state))
            {
                var partyQueueState = new PartyQueueState();
                CacheManager.Cache.Set(key, partyQueueState, _options);
                state = partyQueueState;
            }
        }
        finally
        {
            _lock.Release();
        }
        
        if (state == null)
        {
            Log.Error("><><");
            return null;
        }
        
        Interlocked.Increment(ref state.WaitingCount);

        var myTcs = new TaskCompletionSource<DelayInfo?>();
        TaskCompletionSource<DelayInfo?>? previousTcs = null;
        CancellationTokenSource? previousCts = null;

        var myCts = new CancellationTokenSource();

        lock (state.LockObject)
        {
            if (keepOnlyLast)
            {
                previousTcs = state.LastActiveTcs;
                previousCts = state.LastActiveCts;
                
                // 내가 가장 최신 요청(대기자)이 됨
                state.LastActiveTcs = myTcs;
                state.LastActiveCts = myCts;
            }
        }

        _ = Task.Run(async () =>
        {
            // 💡 [수정] 새 요청이 들어왔으므로 "직전 요청"의 타이머를 터뜨리고 탈락(false)시킵니다.
            if (keepOnlyLast && previousTcs != null && !previousTcs.Task.IsCompleted)
            {
                previousCts?.Cancel();       // 직전 녀석의 Task.Delay 취소
                previousTcs.TrySetResult(null); // 직전 녀석 탈락 처리
            }

            // 내 순서가 올 때까지 대기
            await state.Semaphore.WaitAsync();
            
            Interlocked.Decrement(ref state.WaitingCount);

            try
            {
                // 💡 만약 세마포어 기다리는 도중에 내 뒤에 또 연타가 와서 내가 취소되었다면 바로 퇴장
                if (keepOnlyLast && myCts.Token.IsCancellationRequested)
                {
                    myTcs.TrySetResult(null);
                    return;
                }

                var now = DateTime.UtcNow;

                if (!state.IsFirstRequest)
                {
                    if ((now - state.LastProcessedTime).TotalSeconds <= SpamWindowSeconds)
                    {
                        state.CurrentDelaySeconds = Math.Min(MaxDelaySeconds, state.CurrentDelaySeconds + DelayStepSeconds);
                        // Log.Information("[{PartyId}] 연타 감지! 딜레이가 {Delay}초로 증가합니다.", partyId, state.CurrentDelaySeconds);
                    }
                    else
                    {
                        state.CurrentDelaySeconds = MinDelaySeconds;
                    }

                    var currentDelay = state.CurrentDelaySeconds;
                    var timeSinceLastRequest = now - state.LastProcessedTime;
                    
                    if (timeSinceLastRequest < TimeSpan.FromSeconds(currentDelay))
                    {
                        var delayTime = TimeSpan.FromSeconds(currentDelay) - timeSinceLastRequest;
                        
                        // 내 뒤에 연타가 오면 myCts.Token에 의해 대기가 취소됩니다.
                        await Task.Delay(delayTime, myCts.Token);
                    }
                }
                else
                {
                    state.IsFirstRequest = false;
                    state.CurrentDelaySeconds = MinDelaySeconds;
                }

                state.LastProcessedTime = DateTime.UtcNow;
                
                // 💡 대기를 무사히 버텼다면, 내가 "진짜 최종 생존자"인지 검사합니다.
                lock (state.LockObject)
                {
                    var currentQueueCount = Math.Max(0, state.WaitingCount - 1);
                    // 아직도 내가 마지막 보루로 등록되어 있다면 찐 최종 성공(true)
                    var result = new DelayInfo
                    {
                        WaitingCount = currentQueueCount,
                        DelaySeconds = state.CurrentDelaySeconds,
                        WaitTcs = myTcs,
                        IsLastRequest = true
                    };
                    
                    switch (keepOnlyLast)
                    {
                        case true when state.LastActiveTcs == myTcs:
                        case false:
                            myTcs.TrySetResult(result);
                            break;
                        default:
                            myTcs.TrySetResult(null); // 그새 뒤에 누가 왔다면 탈락
                            break;
                    }
                }
            }
            catch (TaskCanceledException)
            {
                myTcs.TrySetResult(null);
            }
            finally
            {
                state.Semaphore.Release();
            }
        });
        
        var currentQueueCount = Math.Max(0, state.WaitingCount - 1);

        return new DelayInfo
        {
            WaitingCount = currentQueueCount,
            DelaySeconds = state.CurrentDelaySeconds,
            WaitTcs = myTcs,
            IsLastRequest = false
        };
    }

    public DelayInfo? GetDelayInfo(string partyId, bool keepOnlyLast)
    {
        if (CacheManager.Cache.TryGetValue(GetKey(partyId, keepOnlyLast), out PartyQueueState? state))
        {
            var actualWaitingCount = Math.Max(0, state!.WaitingCount - 1);
            
            return new DelayInfo
            {
                DelaySeconds = state.CurrentDelaySeconds,
                WaitingCount = actualWaitingCount,
                WaitTcs = state.LastActiveTcs,
                IsLastRequest = false,
            };
        }
        return null;
    } 

    public class DelayInfo
    {
        public required int WaitingCount { get; init; }
        public required float DelaySeconds { get; init; }
        public required TaskCompletionSource<DelayInfo?>? WaitTcs { get; init; }
        
        public required bool IsLastRequest { get; init; }
    }

    public class PartyQueueState
    {
        public readonly SemaphoreSlim Semaphore = new(1, 1);
        public readonly object LockObject = new(); 
        public int WaitingCount = 0;                        
        public DateTime LastProcessedTime = DateTime.MinValue;
        public bool IsFirstRequest = true;
        public float CurrentDelaySeconds = MinDelaySeconds;
        
        public TaskCompletionSource<DelayInfo?>? LastActiveTcs { get; set; }
        // 💡 각 요청의 딜레이(Task.Delay)를 원격 조율하기 위해 CTS도 상태창에 함께 저장합니다.
        public CancellationTokenSource? LastActiveCts { get; set; } 
    }
}