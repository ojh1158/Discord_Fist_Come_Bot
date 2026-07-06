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
    
    
    private static readonly SemaphoreSlim slim1 = new(1, 1);
    private static readonly SemaphoreSlim slim2 = new(1, 1);
    
    public async Task<PartyQueueResult> Queue(string partyId, ulong userId, string userNickName, ActionType type, SocketInteraction socketInteraction, bool isMessageUpdate = true)
    {
        var resultType = ActionType.Error;
        PartyEntity? afterParty = null;
        
        var IsLast = await delayQueue.EnqueueAndWaitAsync(partyId, async state =>
        {
            if (isMessageUpdate)
            {
                await socketInteraction.ModifyOriginalResponseAsync(m =>
                {
                    m.Content = $"{Emoji.Processing} 순차 처리 대기중입니다... 앞에 {state.WaitingCount - 1}명 있음";
                    m.Components = null;
                    m.Embeds = null;
                });
            }
        });
        
        await slim1.WaitAsync();

        try
        {
            resultType = type switch
            {
                ActionType.Join => await partyService.JoinPartyAsync(partyId, userId, userNickName),
                ActionType.Leave => await partyService.LeavePartyAsync(partyId, userId),
                _ => throw new ArgumentOutOfRangeException()
            };

            if (resultType is ActionType.Error or ActionType.PartyNull or ActionType.Exists or ActionType.NoExists)
            {
                if (resultType is ActionType.Error or ActionType.PartyNull)
                    Log.Error($"[{userId}]({userNickName}) {resultType.Comment()}");
            }
            
            afterParty = await partyService.GetPartyEntityAsync(partyId);
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"[{userId}]({userNickName}) {resultType.Comment()}");
        }
        finally
        {
            slim1.Release();
        }
        
        await slim2.WaitAsync();

        try
        {
            if (!IsLast)
            {
                await ResponseUpdateAsync();

                return GetResult();
            }
            
            await discordServices.UpdateMessage(socketInteraction, afterParty, useDelay: false);
            
            await ResponseUpdateAsync();  
            
            return GetResult();
        }
        catch (Exception e)
        {
            Log.Error(e, $"[{userId}]({userNickName}) {resultType.Comment()}");
            return GetResult();
        }
        finally
        {
            slim2.Release();
        }

#region 지역 함수
        PartyQueueResult GetResult()
        {
            return new PartyQueueResult
            {
                AfterEntity = afterParty,
                ResultType = resultType,
                Id = userId
            };
        }

        async Task ResponseUpdateAsync()
        {
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
        }
#endregion
    }

    public async Task<string?> QueueMany(string partyId, ulong[] userIds, string[] userNickNames, ActionType type, SocketInteraction socketInteraction)
    {
        if (userIds is not { Length: >= 1 } || userNickNames is not { Length: >= 1 } ||
            userIds.Length != userNickNames.Length)
        {
            Log.Error("길이가 맞지 않습니다");
            return null;
        }
        
        var tasks = new List<Task<PartyQueueResult>>(userIds.Length);
        var dicName = new Dictionary<ulong, string>();
        var dicType = new Dictionary<ulong, ActionType>();
        
        for (var i = 0; i < userIds.Length; i++)
        {
            dicName.Add(userIds[i], userNickNames[i]);
            dicType.Add(userIds[i], ActionType._);
        }


        var state = delayQueue.GetQueueState(partyId);
        
        if (state is not null && state.WaitingCount > 0)
        {
            await socketInteraction.ModifyOriginalResponseAsync(m =>
            {
                m.Content = $"{Emoji.Processing} 순차 처리 대기중입니다... 앞에 {state.WaitingCount}명 있음";
                m.Components = null;
                m.Embeds = null;
            });
        }
        
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
                await delayQueue.EnqueueAndWaitAsync(message.Id.ToString());
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
        
        // var afterParty = await partyService.GetPartyEntityAsync(partyId);
        // await discordServices.UpdateMessage(socketInteraction, afterParty, useDelay: false);

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
                    emoji = Emoji.Processing;
                    status = "아직 작업이 처리되지 않았습니다.";
                    break;
                case ActionType.Join:
                    emoji = Emoji.Join;
                    status = resultType.Comment();
                    break;
                case ActionType.Leave:
                    emoji = Emoji.Leave;
                    status = resultType.Comment();
                    break;
                default:
                    emoji = Emoji.Fail;
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
    }
}
