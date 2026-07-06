using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Serilog;

namespace DiscordBot.scripts.src.util;

public class TaskDelayQueue : ISingleton
{
    public const float MinDelaySeconds = 0.5f;
    public const float MaxDelaySeconds = 3f;
    public const float DelayStepSeconds = 0.5f;
    private readonly MemoryCacheEntryOptions _options = new MemoryCacheEntryOptions().SetAbsoluteExpiration(TimeSpan.FromHours(1));

    private string GetKey(string partyId) => $"{partyId}_DelayQueue";
    private static readonly SemaphoreSlim _lock = new(1, 1);
    
    
    public async Task<bool> EnqueueAndWaitAsync(string partyId, Func<PartyQueueState, Task>? waitingAction = null)
    {
        var key = GetKey(partyId);

        PartyQueueState? state;
        TaskCompletionSource<bool> tcs = new();
        
        try
        {
            await _lock.WaitAsync();
            if (!CacheManager.Cache.TryGetValue(key, out state))
            {
                var partyQueueState = new PartyQueueState();
                CacheManager.Cache.Set(key, partyQueueState, _options);
                state = partyQueueState;
            }
            state!.Enqueue(tcs);
            
            waitingAction?.Invoke(state);
        }
        finally
        {
            _lock.Release();
        }
        
        return await tcs.Task;
    }

    public PartyQueueState? GetQueueState(string partyId)
    {
        if (CacheManager.Cache.TryGetValue(GetKey(partyId), out PartyQueueState? state))
        {
            return state;
        }
        return null;
    } 

    public class PartyQueueState
    {
        private ConcurrentQueue<TcsData> Queue { get; } = new();
        private TcsData? LastData => Queue.LastOrDefault();

        public bool IsFull
        {
            get
            {
                var lastData = LastData;
                return lastData is not null && lastData.MaxDelayCount <= lastData.Count;
            }
        }
        public int WaitingCount
        {
            get
            {
                var result = Queue.Sum(d => d.Count);
                
                return result;
            }
        }

        private static readonly SemaphoreSlim Lock = new(1, 1);

        private float _delaySeconds = MinDelaySeconds;
        private float DelaySeconds
        { 
            get
            {
                var result = _delaySeconds;
                _delaySeconds = Math.Min(_delaySeconds + DelayStepSeconds, MaxDelaySeconds);
                return result;
            }
        }
        
        private int _delayCount = 1;
        private int DelayCount => _delayCount++;

        private void Init()
        {
            _delayCount = 1;
            _delaySeconds = MinDelaySeconds;
            _isRun = false;
        }

        public async void Enqueue(TaskCompletionSource<bool> tcs)
        {
            try
            {
                await Lock.WaitAsync();

                if (Queue.IsEmpty || IsFull)
                {
                    Queue.Enqueue(new TcsData { MaxDelayCount = DelayCount });
                }

                LastData?.TcsBag.Add(tcs);
                
                if (LastData is not null)
                {
                    Log.Information($"{LastData.Count}/{LastData.MaxDelayCount} 진행 중");
                }
            }
            catch (Exception e)
            {
                Log.Error(e, e.Message);
            }
            finally
            {
                Lock.Release();
            }

            Run();
        }

        private bool _isRun = false;
        private void Run()
        {
            if (_isRun) return;
            _isRun = true;

            Task.Run(async () =>
            {
                while (_isRun)
                {
                    await Task.Delay(TimeSpan.FromSeconds(DelaySeconds));


                    if (Queue.TryDequeue(out var data) && !data.TcsBag.IsEmpty)
                    {
                        var first = data.TcsBag.FirstOrDefault();
                        if (first == null) continue;

                        foreach (var taskCompletionSource in data.TcsBag.Reverse())
                        {
                            taskCompletionSource.TrySetResult(taskCompletionSource.GetHashCode() == first.GetHashCode());
                        }
                    }

                    if (Queue.IsEmpty)
                    {
                        Init();
                    }
                }
            });
        }
    }
    
    public class TcsData
    {
        public ConcurrentBag<TaskCompletionSource<bool>> TcsBag { get; } = new();
        public required int MaxDelayCount { get; init; } 
        public int Count => TcsBag.Count;
    }
}