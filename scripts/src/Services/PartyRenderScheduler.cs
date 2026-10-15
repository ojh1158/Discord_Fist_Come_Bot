using System.Collections.Concurrent;
using Discord;
using Discord.WebSocket;
using DiscordBot.scripts.db.Services;
using DiscordBot.scripts.src.util;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Emoji = DiscordBot.scripts.src.util.Emoji;

namespace DiscordBot.scripts.src.Services;

public class PartyRenderScheduler(IServiceProvider services) : ISingleton
{
    private const int DebounceMs = 600;
    private const int VerifyMs = 2500;

    private readonly ConcurrentDictionary<string, long> _seq = new();
    private readonly ConcurrentDictionary<string, (bool IsAllMessage, string Message)> _latestArgs = new();

    public async Task<bool> RequestAsync(string partyKey, bool isAllMessage = false, string message = "", bool immediate = false)
    {
        if (string.IsNullOrEmpty(partyKey)) return false;

        var mySeq = _seq.AddOrUpdate(partyKey, 1, (_, v) => v + 1);
        _latestArgs[partyKey] = (isAllMessage, message);

        if (!immediate)
        {
            try
            {
                await Task.Delay(DebounceMs);
            }
            catch (TaskCanceledException)
            {
                return false;
            }
        }

        if (_seq.TryGetValue(partyKey, out var latest) && latest != mySeq)
        {
            Log.Debug("[Render] coalesced party={partyKey} seq={seq} (latest={latest})", partyKey, mySeq, latest);
            return false;
        }

        using (await PartyLocks.AcquireAsync(RenderLockKey(partyKey)))
        {
            if (_seq.TryGetValue(partyKey, out var latestUnderLock) && latestUnderLock != mySeq)
            {
                Log.Debug("[Render] coalesced-under-lock party={partyKey} seq={seq}", partyKey, mySeq);
                return false;
            }

            (bool IsAllMessage, string Message) args = (isAllMessage, message);
            if (_latestArgs.TryGetValue(partyKey, out var latestArgs)) args = latestArgs;

            var writePending = HasPendingWrites(partyKey);
            await RenderFreshAsync(partyKey, args.IsAllMessage, args.Message, hasPending: writePending);

            if (writePending)
                ScheduleVerifyClear(partyKey, mySeq);

            return true;
        }
    }

    private static string RenderLockKey(string partyKey) => $"render_{partyKey}";

    private bool HasPendingWrites(string partyKey)
    {
        try
        {
            var delayQueue = services.GetService<TaskDelayQueue>();
            var state = delayQueue?.GetQueueState(partyKey);
            return state != null && state.WaitingCount > 0;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[Render] 대기 확인 실패 party={partyKey}", partyKey);
            return false;
        }
    }

    private void ScheduleVerifyClear(string partyKey, long mySeq)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(VerifyMs);

                if (_seq.TryGetValue(partyKey, out var latest) && latest != mySeq)
                    return;

                if (HasPendingWrites(partyKey))
                    return;

                using (await PartyLocks.AcquireAsync(RenderLockKey(partyKey)))
                {
                    if (_seq.TryGetValue(partyKey, out var latestUnderLock) && latestUnderLock != mySeq)
                        return;

                    if (HasPendingWrites(partyKey))
                        return;

                    await RenderFreshAsync(partyKey, isAllMessage: false, message: "", hasPending: false);
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[Render] 검증 렌더 실패 party={partyKey}", partyKey);
            }
        });
    }

    private async Task RenderFreshAsync(string partyKey, bool isAllMessage, string message, bool hasPending = false)
    {
        var partyService = services.GetRequiredService<PartyService>();
        var discord = services.GetRequiredService<DiscordServices>();
        var client = services.GetRequiredService<DiscordSocketClient>();

        var party = await partyService.GetPartyEntityAsync(partyKey);
        if (party == null)
        {
            Log.Debug("[Render] party not found party={partyKey}", partyKey);
            return;
        }

        var embed = await discord.UpdatedEmbed(party);
        var component = discord.UpdatedComponent(party);

        Embed[] embeds;
        if (hasPending)
        {
            var waitingBuilder = new EmbedBuilder();
            waitingBuilder.WithColor(Color.Red);
            waitingBuilder.WithDescription($"작업이 남아있습니다... 처리 중입니다 {Emoji.Processing}");
            embeds = [embed, waitingBuilder.Build()];
        }
        else
        {
            embeds = [embed];
        }

        var options = new RequestOptions
        {
            Timeout = 60000,
            RetryMode = RetryMode.AlwaysRetry
        };

        var channel = await client.GetChannelAsync(party.CHANNEL_KEY) as ISocketMessageChannel;
        if (channel == null)
        {
            Log.Warning("[Render] channel not found party={partyKey} channel={channel}", partyKey, party.CHANNEL_KEY);
            return;
        }

        var cacheKey = party.MESSAGE_KEY + "_MessageKey";
        if (!CacheManager.Cache.TryGetValue(cacheKey, out IUserMessage? originalMessage) || originalMessage == null)
        {
            try
            {
                originalMessage = await channel.GetMessageAsync(party.MESSAGE_KEY, options: options) as IUserMessage;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[Render] 원본 메시지 조회 실패 party={partyKey}", partyKey);
                return;
            }

            if (originalMessage != null)
                CacheManager.Cache.Set(cacheKey, originalMessage, CacheManager.GetOptions());
        }

        if (originalMessage != null)
        {
            try
            {
                await originalMessage.ModifyAsync(msg =>
                {
                    msg.Content = null;
                    msg.Embeds = embeds;
                    msg.Components = component;
                }, options);

                if (isAllMessage)
                {
                    await originalMessage.ReplyAsync(message, options: options);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[Render] 메시지 수정 실패 party={partyKey}", partyKey);
            }
        }
        else
        {
            try
            {
                await channel.SendMessageAsync($"{party.DISPLAY_NAME} 파티에 대한 원본 메세지를 찾을 수 없습니다. 파티를 해산합니다.", options: options);
                await partyService.ExpirePartyAsync(party.MESSAGE_KEY);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[Render] 해산 메시지 실패 party={partyKey}", partyKey);
            }
        }
    }
}
