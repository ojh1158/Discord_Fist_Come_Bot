using Discord.WebSocket;
using DiscordBot.scripts.src.party;

namespace DiscordBot.scripts.src.Services;

public class BaseServices(DiscordServices services) : ISingleton
{
    protected DiscordServices Services = services;
    
    private string warkText = "작업 중...";

    protected async Task InitCommands(SocketInteraction component, string action)
    {
        switch (action)
        {
            case Constant.PARTY_KEY or Constant.TEAM_KEY or Constant.START_TIME_OPEN_KEY or Constant.EXPIRE_TIME_OPEN_KEY or Constant.TIME_SETTING_KEY:
                break;
            default:
                if (!component.HasResponded && action is
                        not Constant.JOIN_KEY
                        and not Constant.LEAVE_KEY
                        and not Constant.OPTION_KEY
                   )
                {
                    try
                    {
                        switch (component)
                        {
                            case SocketMessageComponent messageComponent:
                                await messageComponent.UpdateAsync(msg =>
                                {
                                    msg.Content = warkText;
                                    msg.Components = null;
                                });
                                break;
                            case SocketModal submitModal:
                                // 모달 제출에는 UpdateAsync(컴포넌트용)가 아닌 DeferAsync를 사용해야
                                // Unknown Webhook(10015) 없이 후속 ModifyOriginalResponseAsync가 동작한다.
                                if (!submitModal.HasResponded)
                                    await submitModal.DeferAsync(ephemeral: true);
                                else
                                    await submitModal.ModifyOriginalResponseAsync(msg => msg.Content = warkText);
                                break;
                        }
                    }
                    catch
                    {
                        await component.RespondAsync(warkText, ephemeral: true);
                    }
                }
                else if (component.HasResponded)
                {
                    await component.ModifyOriginalResponseAsync(m => m.Content = warkText);
                }
                else
                {
                    await component.RespondAsync(warkText, ephemeral: true);
                }
                break;
        }
    }
}