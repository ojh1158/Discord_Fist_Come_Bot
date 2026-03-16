using Discord;
using DiscordBot.scripts._src.Services;
using DiscordBot.scripts.db.Services;
using Quartz;
using Serilog;

namespace DiscordBot.scripts._src;

[DisallowConcurrentExecution]
public class CycleJob(PartyService partyService, DiscordServices discordServices, UserService userService) : IJob
{
    public Task Execute(IJobExecutionContext context)
    {
        CheckExpiredParty();
        SendAlertMessage();
        return Task.CompletedTask;
    }

    private void CheckExpiredParty()
    {
        Task.Run(async () =>
        {
            try
            {
                var executeTime = DateTime.UtcNow;
                Log.Information($"[Cycle] 만료 파티 체크 시작 (시간: {executeTime:HH:mm:ss} UTC)");
            
                var partyList = await partyService.CycleExpiredPartyListAsync();
            
                if (partyList is { Count: > 0 })
                {
                    Log.Information($"[Cycle] {partyList.Count}개의 만료 파티 발견");
                    foreach (var partyEntity in partyList)
                    {
                        await discordServices.ExpirePartyAsync(partyEntity);
                    }
                }
                else
                {
                    Log.Information("[Cycle] 만료된 파티가 없습니다.");
                }
            }
            catch (Exception e)
            {
                Log.Error($"{e.Message}\n{e.StackTrace}");
                throw; // Quartz가 재시도할 수 있도록 예외 전파
            }
        });
    }
    
    private void SendAlertMessage()
    {
        Task.Run(async () =>
        {
            var alertUsers = await userService.GetAlertUsers();
            
            foreach (var entity in alertUsers)
            {
                try
                {
                    var user = await discordServices.client.Rest.GetUserAsync(entity.USER_ID);
                    
                    _ = user.SendMessageAsync($"**{entity.DISPLAY_NAME}** 파티 시작 5분 전입니다! {discordServices.ToLinkChanner(entity.CHANNEL_KEY)}");
                }
                catch (Exception e)
                {
                    Log.Error($"{e.Message}\n{e.StackTrace}");
                }
                
            }
        });
    }
}