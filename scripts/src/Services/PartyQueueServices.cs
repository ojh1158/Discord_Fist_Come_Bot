using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using Discord.WebSocket;
using DiscordBot.scripts.config;
using DiscordBot.scripts.db.Models;
using DiscordBot.scripts.db.Services;
using DiscordBot.scripts.src.party;
using DiscordBot.scripts.src.util;
using Serilog;
using Timer = System.Timers.Timer;

namespace DiscordBot.scripts.src.Services;

public class PartyQueueServices(
    PartyService partyService,
    DiscordServices discordServices,
    Config config,
    TaskDelayQueue delayQueue
    ) : ISingleton
{
    
    public const string EmojiProcessing = "⏳"; // 처리 중 또는 대기 중
    public const string EmojiComplete = "✅";   // 처리 완료
    public const string EmojiFail = "❌";       // 처리 실패 (실패 케이스가 있을 경우)
    
    private static readonly ConcurrentDictionary<string, PartyQueue> QueueDic = new();

    public async Task<PartyQueueResult?> Queue(string partyId, ulong userId, string userNickName, ActionType type, SocketInteraction socketInteraction)
    {
        var queue = PartyDic(partyId);
        var tcs = new TaskCompletionSource<PartyQueueResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        
        queue.AddAsync(new PartyQueueData
        {
            PartyId = partyId,
            UserId = userId,
            Type = type,
            SocketInteraction = socketInteraction,
            UserNickName = userNickName,
            TaskCompletionSource = tcs
        });
        
        if (queue.Count - 1 >= 1)
        {
            await socketInteraction.ModifyOriginalResponseAsync(m =>
            {
                m.Content = "순차 처리 대기중입니다...";
                
                m.Components = null;
                m.Embeds = null;
            });
        }

        PartyQueueResult result;

        if (!tcs.Task.IsCompleted)
            result = await tcs.Task;
        else
            result = tcs.Task.Result;


        await socketInteraction.ModifyOriginalResponseAsync(m =>
        {
            m.Content = result.ResultType.Comment();
            m.Components = null;
            m.Embeds = null;
        });
        _ = discordServices.RespondMessageWithExpire(socketInteraction);
        return result;
    }

    private PartyQueue PartyDic(string partyId)
    {
        var partyQueue = new PartyQueue(discordServices, partyService);
        if (QueueDic.TryAdd(partyId, partyQueue))
        {
            partyQueue.EndEvent += () =>
            {
                if (partyQueue.Count == 0)
                {
                    QueueDic.Remove(partyId, out _);
                }
                else
                {
                    Log.Information("작업이 남아 스킵합니다....");   
                }
            };
        }
        var queue = QueueDic[partyId];
        return queue;
    }

    public async Task<string?> QueueMany(string partyId, ulong[] userIds, string[] userNickNames, ActionType type, SocketInteraction socketInteraction)
    {
        if (userIds is not { Length: >= 1 } || userNickNames is not { Length: >= 1 } ||
            userIds.Length != userNickNames.Length)
        {
            Log.Error("길이가 맞지 않습니다");
            return null;
        }

        var queue = PartyDic(partyId);
        
        var tasks = new List<TaskCompletionSource<PartyQueueResult>>(userIds.Length);
        var dicName = new Dictionary<ulong, string>();
        var dicType = new Dictionary<ulong, ActionType>();
        
        for (var i = 0; i < userIds.Length; i++)
        {
            dicName.Add(userIds[i], userNickNames[i]);
            dicType.Add(userIds[i], ActionType._);
        }

        for (var i = 0; i < userIds.Length; i++)
        {
            var taskCompletionSource = new TaskCompletionSource<PartyQueueResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            tasks.Add(taskCompletionSource);
            queue.AddAsync(new PartyQueueData
            {
                PartyId = partyId,
                UserId = userIds[i],
                Type = type,
                SocketInteraction = socketInteraction,
                UserNickName = userNickNames[i],
                TaskCompletionSource = taskCompletionSource
            });
        }
        
        if (queue.Count - 1 > userIds.Length)
        {
            _ = socketInteraction.ModifyOriginalResponseAsync(m =>
            {
                m.Content = "순차 처리 대기중입니다...";
                m.Components = null;
                m.Embeds = null;
            });
        }
        
        _ = Task.Run(() =>
        {
            foreach (var tcs in tasks)
            {
                var tcs1 = tcs;
                _ = Task.Run(async () =>
                {
                    var resultData = await tcs1.Task;
                    dicType[resultData.Id] = resultData.ResultType;
                });
            }
        });


        var targetCount = 0;
        
        while (true)
        {
            var count = dicType.Count(d => d.Value is not ActionType._);
            if (count <= targetCount)
            {
                await Task.Delay(50);
                continue;
            }
            targetCount = count;
            var exit = targetCount >= userIds.Length;
            
            await socketInteraction.ModifyOriginalResponseAsync(m => {
                m.Content = GetUserQueueStatus(dicName, dicType);
                m.Components = null;
                m.Embeds = null;
            });

            if (exit) break;
        }

        return GetUserQueueStatus(dicName, dicType);
    }

    private string GetUserQueueStatus(Dictionary<ulong, string> dicName, Dictionary<ulong, ActionType> dicType)
    {
        var messageBuilder = new StringBuilder();
        foreach (var kvp in dicName)
        {
            var name = kvp.Value;
            var resultType = dicType[kvp.Key];

            string status;
            string emoji;
            switch (resultType)
            {
                case ActionType._:
                    emoji = EmojiProcessing;
                    status = "아직 작업이 처리되지 않았습니다.";
                    break;
                case ActionType.Join:
                    emoji = EmojiComplete;
                    status = resultType.Comment();
                    break;
                default:
                    emoji = EmojiFail;
                    status = resultType.Comment();
                    break;
            }
            messageBuilder.Append($"{emoji} {name}님이 {status}\n");
        }

        var message = messageBuilder.ToString();
        if (message.Length > 2) message = message[..^1]; // 끝부분 줄바꿈 제거
        
        return message;
    }
    
    private bool _isTask = false;
    
    private class PartyQueue(DiscordServices discordServices, PartyService partyService)
    {
        Queue<PartyQueueData> _queue = new();
        public event Action? EndEvent;
     
        public int Count => _queue.Count;
        public bool IsDone { get; private set; } = false;
        
        public void AddAsync(PartyQueueData data)
        {
            _queue.Enqueue(data);
            _ = Run();
        }
        
        private readonly PartyMessageUpdater _updater = new(discordServices, partyService); 
        private bool _run;
    
        private async Task Run()
        {
            if (_run) return;
            _run = true;
            
            while (true)
            {
                await Task.Delay(500);
                try
                {
                    if (_queue.Count == 0)
                    {
                        break; // 또는 적절한 대기(Task.Delay) 후 재시도 루프 구성
                    }

                    var partyQueue = _queue.Dequeue();
                    partyQueue.NowCount = _queue.Count;

                    PartyEntity? partyEntity = null;
                    
                    var id = partyQueue.UserId;
                    var userName = partyQueue.UserNickName;

                    // 핵심 로직 처리
                    var resultType = partyQueue.Type switch
                    {
                        ActionType.Join => await partyService.JoinPartyAsync(partyQueue.PartyId, id, userName),
                        ActionType.Leave => await partyService.LeavePartyAsync(partyQueue.PartyId, id),
                        _ => throw new ArgumentOutOfRangeException()
                    };


                    if (resultType is ActionType.Error)
                    {
                        if (partyQueue.IsErrorFullCount())
                        {
                            partyEntity = await partyService.GetPartyEntityAsync(partyQueue.PartyId);
                            partyQueue.TaskCompletionSource.SetResult(new PartyQueueResult {
                                AfterEntity = partyEntity, ResultType = ActionType.Error, Id = id 
                            });
                        }
                        else
                        {
                            _queue.Enqueue(partyQueue); // 다시 넣기 (이때 로직 주의)
                            Log.Error($"{partyQueue.UserNickName} 해당 유저가 액션 오류를 유발했습니다.");                        
                        }
                    }
                    else 
                    {
                        partyEntity = await partyService.GetPartyEntityAsync(partyQueue.PartyId);
                        
                        partyQueue.TaskCompletionSource.SetResult(new PartyQueueResult {
                            AfterEntity = partyEntity, ResultType = resultType, Id = id
                        });
                    }
                    _updater.Queue(partyQueue);
                }
                catch (Exception e)
                {
                    Log.Error($"[Run Error] {e.Message}");
                    await Task.Delay(3000);
                }
            }
            EndEvent?.Invoke();
        }
    }
    
    private class PartyQueueData
    {
        public required string PartyId { get; init; }
        public required ulong UserId { get; init; }
        public required string UserNickName { get; init; }
        public required ActionType Type { get; init; }
        public required TaskCompletionSource<PartyQueueResult> TaskCompletionSource { get; init; }
        public SocketInteraction SocketInteraction { get; init; }
        public int NowCount { get; set; }

        public int ErrorCount { get; private set; } = 0;

        public bool IsErrorFullCount()
        {
            ErrorCount++;
            if (ErrorCount >= 3)
            {
                return true;
            }
            return false;
        }
    }

    public class PartyQueueResult
    {
        public required PartyEntity? AfterEntity { get; init; }
        public required ActionType ResultType { get; init; }
        public required ulong Id { get; init; }
        public string? Message { get; set; }
    }
    
    private class PartyMessageUpdater(DiscordServices discordServices, PartyService partyService)
    {
        private readonly Dictionary<string, PartyMessageData> _dic = new();

        public void Queue(PartyQueueData partyQueueData)
        {
            var partyMessageData = new PartyMessageData(discordServices, partyService, partyQueueData);
            if (_dic.TryAdd(partyQueueData.PartyId, partyMessageData))
            {
                Task.Run(async () =>
                {
                    await partyMessageData.CompletionSource.Task;
                    _dic.Remove(partyQueueData.PartyId);
                });
            }

            _dic[partyQueueData.PartyId].Run(partyQueueData);
        }
    }

    private class PartyMessageData(DiscordServices discordServices, PartyService partyService, PartyQueueData partyQueueData)
    {
        public TaskCompletionSource CompletionSource { get; init; } = new();
        public string PartyId { get; } = partyQueueData.PartyId;

        private ushort doneCount = 0;
        private ushort _updateCount = 0;
        private ushort UpdateCount
        {
            get => _updateCount;
            set
            {
                if (value < _updateCount)
                {
                    doneCount = 0;
                    _updateCount = 1;
                }
                else
                {
                    _updateCount = value;
                }
            }
        }

        private bool _run = false;
        private int queueCount;

        public void Run(PartyQueueData targetQueueData)
        {
            if (PartyId != targetQueueData.PartyId)
            {
                Log.Error("Party Id is not match.");
                return;
            }

            queueCount = targetQueueData.NowCount;

            UpdateCount++;
            UpdateMessage();
        }
        
        private void UpdateMessage()
        {
            if (_run) return;
            _run = true;
            var lastUpdateCount = UpdateCount;
            _ = Task.Run(async () =>
            {
                try
                {
                    while (true)
                    {
                        if (UpdateCount <= doneCount)
                        {
                            break;
                        }
                        doneCount = UpdateCount;
                        var thisUpdateCount = UpdateCount;
                        
                        
                        var partyEntity = await partyService.GetPartyEntityAsync(partyQueueData.PartyId);

                        if (lastUpdateCount > thisUpdateCount) continue;
                        var stopwatch = Stopwatch.StartNew();
                        stopwatch.Start();
                        await discordServices.UpdateMessage(partyQueueData.SocketInteraction, partyEntity);
                        stopwatch.Stop();
                        Log.Information($"소요 시간: {stopwatch.Elapsed:mm\\:ss\\.fff}"); 
                        stopwatch.Reset();
                        lastUpdateCount = thisUpdateCount;
                    }
                }
                catch (Exception e)
                {
                    Log.Error(e, $"[Update Error] {e.Message}");
                }
                finally
                {
                    CompletionSource.SetResult();
                    // Log.Information($"작업 {UpdateCount}개 GUI 업데이트 완료");
                }
            });        
        }
    }
}
