using Discord;
using Quartz;
using DiscordBot.scripts.db.Services;

namespace DiscordBot.scripts._src.Services;

[DisallowConcurrentExecution]
public class CycleJob : IJob
{
    private readonly DiscordServices _discordServices;

    public CycleJob(DiscordServices discordServices)
    {
        _discordServices = discordServices;
    }

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
                Console.WriteLine($"[Cycle] 만료 파티 체크 시작 (시간: {executeTime:HH:mm:ss} UTC)");
            
                var partyList = await PartyService.CycleExpiredPartyListAsync();
            
                if (partyList is { Count: > 0 })
                {
                    Console.WriteLine($"[Cycle] {partyList.Count}개의 만료 파티 발견");
                    foreach (var partyEntity in partyList)
                    {
                        await _discordServices.ExpirePartyAsync(partyEntity);
                    }
                }
                else
                {
                    Console.WriteLine("[Cycle] 만료된 파티가 없습니다.");
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"[Cycle] 오류 발생: {e.Message}");
                Console.WriteLine(e);
                throw; // Quartz가 재시도할 수 있도록 예외 전파
            }
        });
    }
    
    private void SendAlertMessage()
    {
        Task.Run(async () =>
        {
            var alertUsers = await UserService.GetAlertUsers();
            
            foreach (var entity in alertUsers)
            {
                try
                {
                    var user = await _discordServices.client.Rest.GetUserAsync(entity.USER_ID);
                    
                    _ = user.SendMessageAsync($"**{entity.DISPLAY_NAME}** 파티 시작 5분 전입니다! {_discordServices.ToLinkChanner(entity.CHANNEL_KEY)}");
                }
                catch (Exception e)
                {
                    Console.WriteLine($"[SendAlert] 오류 발생: {e.Message}");
                    Console.WriteLine(e);
                }
                
            }
        });
    }
}