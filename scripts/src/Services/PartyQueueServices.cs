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
    
    private static readonly SemaphoreSlim _semaphore = new(1, 1);
    
    public async Task<PartyQueueResult?> Queue(string partyId, ulong userId, string userNickName, ActionType type, SocketInteraction socketInteraction, bool isMessageUpdate = true)
    {
        if (_semaphore.CurrentCount == 0 && isMessageUpdate)
        {
            await socketInteraction.ModifyOriginalResponseAsync(m =>
            {
                m.Content = "순차 처리 대기중입니다...";
                m.Components = null;
                m.Embeds = null;
            });
        }
        
        await _semaphore.WaitAsync();
        
        var isError = false;
        var resultType = ActionType.Error;

        var result = new PartyQueueResult
        {
            AfterEntity = null,
            ResultType = ActionType.Error,
            Id = userId,
        };
        
        try
        {
            var partyEntity = await partyService.GetPartyEntityAsync(partyId);
            isError = partyEntity is null;
            
            resultType = type switch
            {
                ActionType.Join => await partyService.JoinPartyAsync(partyId, userId, userNickName),
                ActionType.Leave => await partyService.LeavePartyAsync(partyId, userId),
                _ => throw new ArgumentOutOfRangeException()
            };
            
            if (resultType is ActionType.Error or ActionType.Exists or ActionType.NoExists)
            {
                Log.Error($"[{userId}]({userNickName}) {resultType.Comment()}");
                isError = true;
            }
        }
        finally
        {
            _semaphore.Release();
        }
        
        var delayInfo = await delayQueue.EnqueueAndWaitAsync(partyId, true);
        
            
        if (delayInfo is null)
        {
            Log.Error($"[{userId}]({userNickName}) delayInfo null 오류");
        }
        else if (Math.Abs(delayInfo.DelaySeconds - TaskDelayQueue.MaxDelaySeconds) < 0)
        {
            await socketInteraction.ModifyOriginalResponseAsync(m =>
            {
                m.Content = $"{EmojiProcessing} 순차 처리 대기중입니다...";
                m.Components = null;
                m.Embeds = null;
            });
        }

        
        else if (!isError)
        {
            // Log.Information($"[{userId}]({userNickName}) {delayInfo.WaitingCount} 웨이터");
            var afterParty = await partyService.GetPartyEntityAsync(partyId);
                
            if (isMessageUpdate && delayInfo.WaitingCount - 1 >= 1)
            {
                await socketInteraction.ModifyOriginalResponseAsync(m =>
                {
                    m.Content = $"{EmojiProcessing} 순차 처리 대기중입니다...";
                    m.Components = null;
                    m.Embeds = null;
                });
            }

            if (delayInfo.WaitTcs is not null)
            {
                var task = await delayInfo.WaitTcs.Task;
                    
                result = new PartyQueueResult
                {
                    AfterEntity = afterParty,
                    ResultType = resultType,
                    Id = userId,
                };
                    
                if (task is not null)
                {
                    // Log.Information($"[{userId}]({userNickName}) ======= 업데이트 =======");
                    _ = discordServices.UpdateMessage(socketInteraction, afterParty, delayInfo: delayInfo);    
                }
            }
                
        }

        if (isMessageUpdate)
        {
            await socketInteraction.ModifyOriginalResponseAsync(m =>
            {
                m.Content = resultType.Comment();
                m.Components = null;
                m.Embeds = null;
            });
            _ = discordServices.RespondMessageWithExpire(socketInteraction);
        }
        
        return result;
    }

    public async Task<string?> QueueMany(string partyId, ulong[] userIds, string[] userNickNames, ActionType type, SocketInteraction socketInteraction)
    {
        if (userIds is not { Length: >= 1 } || userNickNames is not { Length: >= 1 } ||
            userIds.Length != userNickNames.Length)
        {
            Log.Error("길이가 맞지 않습니다");
            return null;
        }

        
        var tasks = new List<Task<PartyQueueResult?>>(userIds.Length);
        var dicName = new Dictionary<ulong, string>();
        var dicType = new Dictionary<ulong, ActionType>();
        
        for (var i = 0; i < userIds.Length; i++)
        {
            dicName.Add(userIds[i], userNickNames[i]);
            dicType.Add(userIds[i], ActionType._);
        }
        
       
        var delayInfo = delayQueue.GetDelayInfo(partyId, true);
        
        if (delayInfo is not null && delayInfo.WaitingCount - 1 > userIds.Length)
        {
            await socketInteraction.ModifyOriginalResponseAsync(m =>
            {
                m.Content = "순차 처리 대기중입니다...";
                m.Components = null;
                m.Embeds = null;
            });
        }

        // _ = Task.Run(async () =>
        // {
        //     for (var i = 0; i < userIds.Length; i++)
        //     {
        //         var result = await Queue(partyId, userIds[i], userNickNames[i], type, socketInteraction,
        //             isMessageUpdate: false);
        //         if (result is null) continue;
        //         dicType[result.Id] = result.ResultType;
        //     }
        // });
        
        for (var i = 0; i < userIds.Length; i++)
        {
            tasks.Add(Queue(partyId, userIds[i], userNickNames[i], type, socketInteraction, isMessageUpdate: false));
        }
        
        
        _ = Task.Run(() =>
        {
            foreach (var tcs in tasks)
            {
                var tcs1 = tcs;
                _ = Task.Run(async () =>
                {
                    var resultData = await tcs1;
                    
                    if (resultData is not null)
                        dicType[resultData.Id] = resultData.ResultType;
                });
            }
        });

        var targetCount = 0;
        
        var message = await socketInteraction.GetOriginalResponseAsync();
        
        while (true)
        {
            var count = dicType.Count(d => d.Value is not ActionType._);
            if (count < targetCount)
            {
                delayInfo = await delayQueue.EnqueueAndWaitAsync(message.Id.ToString(), false);
                if (delayInfo is {WaitTcs: not null})
                {
                    await delayInfo.WaitTcs.Task;
                }
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
    
    public class PartyQueueResult
    {
        public required PartyEntity? AfterEntity { get; init; }
        public required ActionType ResultType { get; init; }
        public required ulong Id { get; init; }
        public string? Message { get; set; }
    }
}
