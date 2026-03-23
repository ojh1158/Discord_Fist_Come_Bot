using Discord;
using Discord.WebSocket;
using DiscordBot.scripts._src.party;
using DiscordBot.scripts.db.Models;
using DiscordBot.scripts.db.Services;
using Serilog;

namespace DiscordBot.scripts._src.Services;

public class ButtonServices : BaseServices
{
    private readonly UserService userService;
    private readonly PartyService partyService;
    private readonly GuildService guildService;
    
    public ButtonServices(DiscordServices services, UserService userService, PartyService partyService, GuildService guildService) : base(services)
    {
        Services.client.ButtonExecuted += HandleButtonAsync;
        this.userService = userService;
        this.partyService = partyService;
        this.guildService = guildService;
    }
    
    private async Task HandleButtonAsync(SocketMessageComponent component)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await ProcessButtonAction(component);
            }
            catch (Exception e)
            {
                Log.Error($"{e.Message}\n{e.StackTrace}");
            }
        });

        await Task.CompletedTask;
    }

    private async Task ProcessButtonAction(SocketMessageComponent component)
    {
        var customId = component.Data.CustomId;
        
        // CustomId 파싱: "{action}_{messageId}" 또는 "{action}_{messageId}_{extra}"
        // 하위 호환성: "party_{action}_{messageId}" 형식도 지원
        var parts = customId.Split('_');
        if (parts.Length < 2)
            return;
        
        if (parts[0] == Constant.USER_ALERT_SETTING_KEY)
        {
            await UserSetting(component, parts);
            return;
        }
        
        string action;
        int messageIdIndex;
        
        // 이전 형식 지원: "party_{action}_{messageId}"
        if (parts[0] == "party" && parts.Length >= 3)
        {
            action = parts[1];
            messageIdIndex = 2;
        }
        // 새로운 형식: "{action}_{messageId}"
        else
        {
            action = parts[0];
            messageIdIndex = 1;
        }
        
        if (action is Constant.TEAM_REMOVE_KEY)
        {
            if (messageIdIndex >= parts.Length)
                return;
                
            if (!ulong.TryParse(parts[messageIdIndex], out var key))
                return;

            var mes = await component.Channel.GetMessageAsync(key);
            await mes.DeleteAsync();
            return;
        }

        if (messageIdIndex >= parts.Length)
            return;
        
        var partyKey = parts[messageIdIndex];
        
        await InitCommands(component, action);

        var isAllMessage = false;
        var message = "알 수 없는 오류가 나타났습니다.";
        var isAddSettingButton = false;

        var partyEntity = await partyService.GetPartyEntityAsync(partyKey);
        var partyClass = new PartyClass();
        var error = await partyClass.Init(partyEntity, component, Services.client);
        var party = partyClass.Entity;
        
        if (error is not "")
        {
            await component.ModifyOriginalResponseAsync(m => m.Content = error);
            return;
        }

        var type = JoinType.Error;
        
        switch (action)
        {
            case Constant.JOIN_KEY:
                (party, type) = await partyService.JoinPartyAsync(party.PARTY_KEY, partyClass.guildUser.Id, partyClass.userNickname);
                    
                // Service에서 중복 체크 포함하여 처리
                if (type is JoinType.Join or JoinType.Wait && party != null)
                {
                    if (type is JoinType.Wait)
                    {
                        message = "파티 인원이 가득 찼습니다. 대기 인원으로 등록되었습니다.";
                    }
                    else
                    {
                        message = $"✅ {party.DISPLAY_NAME} 파티에 참가했습니다!";
                    }

                    isAddSettingButton = true;
                    
                    Services.SendUserAlert(party, component.User, action);
                    
                    party = await partyService.GetPartyEntityAsync(party.PARTY_KEY);
                }
                else if(type is JoinType.Exists or JoinType.Error || party == null)
                {
                    await component.ModifyOriginalResponseAsync(m => m.Content = type is JoinType.Exists ? "파티에 이미 참가하였습니다." : "알 수 없는 오류가 나타났습니다.");
                    _ = Services.RespondMessageWithExpire(component);
                    return;
                }
                
                break;
                
            case Constant.LEAVE_KEY:
                var afterParty = await partyService.LeavePartyAsync(party, partyClass.userId);
                if (afterParty != null)
                {
                    message = $"❌ {party.DISPLAY_NAME} 파티에서 나갔습니다.";
                    
                    Services.SendUserAlert(partyEntity!, component.User, action);
                    if (party.Members.Count > party.MAX_COUNT_MEMBER)
                    {
                        var userEntity = party.Members[party.MAX_COUNT_MEMBER];

                        var user = await Services.client.Rest.GetUserAsync(userEntity.USER_ID);
                        
                        Services.SendUserAlert(afterParty, user, Constant.USER_ALERT_JOIN_PARTY_TO_WAIT_FLAG);
                    }
                    
                    party = afterParty;
                }
                else
                {
                    await component.ModifyOriginalResponseAsync(m => m.Content = "파티에 참가하지 않았거나 나가기에 실패했습니다.");
                    return;
                }
                break;
            case Constant.OPTION_KEY:
                // if (partyClass.isNone)
                // {
                //     await component.ModifyOriginalResponseAsync(m => m.Content ="권한이 없어 표시할 기능이 없습니다.");
                //     await Services.RespondMessageWithExpire(component, time: 5);
                //     return;
                // }
                
                // 옵션 버튼들 만들기
                var componentBuilder = new ComponentBuilder();
                
                componentBuilder.WithButton("개인 알림 설정",$"{Constant.USER_ALERT_SETTING_KEY}_{Constant.USER_ALERT_SETTING_OPEN_KEY}", ButtonStyle.Success, row:0);

                if (partyClass is {isAdmin: true} or {isPartyMember: true} or {isOwner: true} or {isWater:true})
                {
                    componentBuilder.WithButton(Constant.PULLING_UP_KEY,$"{Constant.PULLING_UP_KEY}_{partyKey}", ButtonStyle.Success, row:1);
                    componentBuilder.WithButton(Constant.TEAM_KEY,$"{Constant.TEAM_KEY}_{partyKey}", ButtonStyle.Success, row:1);
                }

                if (partyClass is {isAdmin: true} or {isPartyMember: true} or {isOwner: true} && party.Members.Count >= 1)
                {
                    componentBuilder.WithButton(Constant.PING_KEY, $"{Constant.PING_KEY}_{partyKey}", ButtonStyle.Success, row:1);
                    if (partyClass.isAdmin || partyClass.isOwner)
                    {
                        componentBuilder.WithButton(Constant.KICK_KEY,$"{Constant.KICK_KEY}_{partyKey}", ButtonStyle.Primary, row:2);
                    }
                }

                if (partyClass.isAdmin || partyClass.isOwner)
                {
                    componentBuilder.WithButton(Constant.JOIN_AUTO_KEY, $"{Constant.JOIN_AUTO_KEY}_{partyKey}", ButtonStyle.Primary, row:2);
                    componentBuilder.WithButton(Constant.PARTY_KEY,$"{Constant.PARTY_KEY}_{partyKey}", ButtonStyle.Primary, row:2);
                    componentBuilder.WithButton(Constant.START_TIME_OPEN_KEY,$"{Constant.START_TIME_OPEN_KEY}_{partyKey}", ButtonStyle.Primary, row:2);
                    componentBuilder.WithButton(Constant.EXPIRE_TIME_OPEN_KEY,$"{Constant.EXPIRE_TIME_OPEN_KEY}_{partyKey}", ButtonStyle.Primary, row:2);
                    
                    componentBuilder.WithButton(Constant.MOVE_OWNER_KEY, $"{Constant.MOVE_OWNER_KEY}_{partyKey}", ButtonStyle.Primary, row:3);
                    componentBuilder.WithButton(party.IS_CLOSED ? "재개" : Constant.CLOSE_KEY, $"{Constant.CLOSE_KEY}_{partyKey}", party.IS_CLOSED ? ButtonStyle.Success : ButtonStyle.Danger, row:3);
                    componentBuilder.WithButton(Constant.EXPIRE_KEY, $"{Constant.EXPIRE_KEY}_{partyKey}", ButtonStyle.Secondary, row:3);
                }
                
                await component.ModifyOriginalResponseAsync( m =>
                {
                    m.Content = "버튼을 선택해주세요.";
                    m.Components = componentBuilder.Build();
                });

                await Services.RespondMessageWithExpire(component, time: 30);
                return;
            case Constant.CLOSE_KEY:
                
                var closed = party.IS_CLOSED;
                var e = party.IS_CLOSED ? "오픈" : "마감";
                
                if (partyClass is { isOwner: false, isAdmin: false })
                {
                    await component.ModifyOriginalResponseAsync(msg =>
                    {
                        msg.Content = $"❌ 파티를 생성한 사람만 {e}할 수 있습니다.";
                    });
                    await Services.RespondMessageWithExpire(component);
                    return;
                }
                
                if (!await partyService.SetPartyCloseAsync(partyKey, !closed))
                {
                    await component.ModifyOriginalResponseAsync(msg =>
                    {
                        msg.Content = "❌ 파티 조작에 실패하였습니다.";
                    });
                    await Services.RespondMessageWithExpire(component);
                    return;   
                }

                await component.ModifyOriginalResponseAsync(msg =>
                {
                    msg.Content = $"✅ 파티를 {e}했습니다.";
                });
                await Services.RespondMessageWithExpire(component);
                
                party.IS_CLOSED = !closed;
                message = $"{partyClass.userRoleString}님이 {party.DISPLAY_NAME} 파티를 {e}하였습니다.";
                isAllMessage = true;
                break;
            case Constant.PING_KEY:
                
                if (partyClass is { isOwner: false, isAdmin: false, isPartyMember: false })
                {
                    await component.ModifyOriginalResponseAsync(msg =>
                    {
                        msg.Content = "❌ 관리자, 파티원, 파티장만 호출할 수 있습니다!";
                    });
                    await Services.RespondMessageWithExpire(component);
                    return;
                }
                
                await component.ModifyOriginalResponseAsync(msg =>
                {
                    msg.Content = "✅ 파티원을 호출했습니다.";
                });
                await Services.RespondMessageWithExpire(component);
                
                var mentions = string.Join(" ", party.Members[..party.MAX_COUNT_MEMBER].Select(m => $"<@{m.USER_ID}>"));
                isAllMessage = true;
                message = $"🔔 {partyClass.userRoleString}님이 파티원을 호출하였습니다!\n{mentions}";
                break;
            case Constant.EXPIRE_KEY:
                
                if (!partyClass.isOwner && !partyClass.isAdmin)
                {
                    await component.ModifyOriginalResponseAsync(msg =>
                    {
                        msg.Content = "❌ 파티장 또는 관리자만 만료시킬 수 있습니다.";
                    });
                    await Services.RespondMessageWithExpire(component);
                    return;
                }
                
                var confirmComponent = new ComponentBuilder()
                    .WithButton("예", $"{Constant.YES_BUTTON_KEY}_{partyKey}", ButtonStyle.Danger)
                    .WithButton("아니오", $"{Constant.NO_BUTTON_KEY}_{partyKey}", ButtonStyle.Secondary)
                    .Build();
                
                await component.ModifyOriginalResponseAsync(msg =>
                {
                    msg.Content = $"⚠️ **{party.DISPLAY_NAME}** 파티를 영구적으로 만료시키시겠습니까?\n만료된 파티는 복구할 수 없습니다.";
                    msg.Components = confirmComponent;
                });
                _ = Services.RespondMessageWithExpire(component, time: 30);
                return;
            case Constant.PARTY_KEY:
                var renameModal = new ModalBuilder()
                    .WithTitle("파티 설정 변경")
                    .WithCustomId($"{Constant.SETTING_MODEL_KEY}_{partyKey}")
                    .AddTextInput("이름", "name", TextInputStyle.Short, 
                        placeholder: $"여기에 이름 입력", 
                        required: true,
                        value: party.DISPLAY_NAME,
                        minLength: 1,
                        maxLength: Constant.MAX_NAME_COUNT)
                    .AddTextInput("새로운 인원 수", "count", TextInputStyle.Short, 
                        placeholder: $"{1}-{Constant.MAX_COUNT}", 
                        required: true,
                        value: party.MAX_COUNT_MEMBER.ToString(),
                        minLength: 1,
                        maxLength: 3)
                    .Build();

                await component.RespondWithModalAsync(renameModal);
                return;
            case Constant.JOIN_AUTO_KEY:
                
                var selectMenuBuilder = new SelectMenuBuilder()
                    .WithCustomId($"{Constant.JOIN_AUTO_KEY}_{partyKey}")
                    .WithPlaceholder("추가할 유저를 선택하세요")
                    .WithMinValues(1)
                    .WithMaxValues(25)
                    .WithType(ComponentType.UserSelect);
                
                var ag = new ComponentBuilder()
                    .WithSelectMenu(selectMenuBuilder)
                    .Build();

                await component.ModifyOriginalResponseAsync(msg =>
                {
                    msg.Content = $"⚠️ 추가할 유저를 선택하세요";
                    msg.Components = ag;
                });
                return;
            case Constant.KICK_KEY:
                
                var menuBuilder = new SelectMenuBuilder()
                    .WithCustomId($"{Constant.KICK_KEY}_{partyKey}")
                    .WithPlaceholder("강퇴할 유저를 선택하세요")
                    .WithMinValues(1)
                    .WithMaxValues(25)
                    .WithType(ComponentType.UserSelect);
                
                var build = new ComponentBuilder()
                    .WithSelectMenu(menuBuilder)
                    .Build();

                await component.ModifyOriginalResponseAsync(msg =>
                {
                    msg.Content = $"⚠️ 강퇴할 유저를 선택하세요";
                    msg.Components = build;
                });
                return;
            case Constant.TEAM_KEY:
                if (party.Members.Count <= 0)
                {
                    await component.UpdateAsync(m =>
                    {
                        m.Content = "현재 인원이 1보다 작습니다.";
                        m.Components = null;
                    });
                    _ = Services.RespondMessageWithExpire(component);
                    return;
                }
                
                var maxCount = Math.Min(party.MemberOnly.Count, 10);
                
                var teamModal = new ModalBuilder()
                    .WithTitle("팀 만들기")
                    .WithCustomId($"{Constant.TEAM_KEY}_{partyKey}")
                    .AddTextInput("팀 갯수", "count", TextInputStyle.Short, 
                        placeholder: $"{1}-{maxCount}", 
                        required: true,
                        value: "",
                        minLength: 0,
                        maxLength: 10)
                    .Build();
                
                await component.RespondWithModalAsync(teamModal);
                await component.DeleteOriginalResponseAsync();
                return;
            case Constant.PULLING_UP_KEY:
                
                var channel = component.Channel;
                var sendMessageAsync = await channel.SendMessageAsync("초기화 중입니다...");
                
                if (!await partyService.ChangeMessageId(party.MESSAGE_KEY, sendMessageAsync.Id))
                {
                    await sendMessageAsync.DeleteAsync();
                    _ = Services.RespondMessageWithExpire(component);
                    return;
                }

                var lastMessage = await channel.GetMessageAsync(party.MESSAGE_KEY);
                await lastMessage.DeleteAsync();

                party.MESSAGE_KEY = sendMessageAsync.Id;

                var updatedEmbed = await Services.UpdatedEmbed(party);
                var updatedComponent = Services.UpdatedComponent(party);
                
                await sendMessageAsync.ModifyAsync(m =>
                {
                    m.Embed = updatedEmbed;
                    m.Components = updatedComponent;
                    m.Content = "";
                });

                await component.DeleteOriginalResponseAsync();
                return;
            case Constant.YES_BUTTON_KEY:
                if (await Services.ExpirePartyAsync(party, component.Channel))
                {
                    await component.ModifyOriginalResponseAsync(msg =>
                    {
                        msg.Content = $"✅ **{party.DISPLAY_NAME}** 파티를 만료시켰습니다.";
                        msg.Components = null;
                    });
                    _ = Services.RespondMessageWithExpire(component);
                    message = $"❌ {partyClass.userRoleString}님이 파티를 만료시켰습니다.";
                    isAllMessage = true;
                }
                else
                {
                    await component.ModifyOriginalResponseAsync(msg =>
                    {
                        msg.Content = $"오류로 인하여 파티를 만료시키지 못하였습니다.";
                        msg.Components = null;
                    });
                    _ = Services.RespondMessageWithExpire(component);
                }
                break;
            case Constant.NO_BUTTON_KEY:
                await component.ModifyOriginalResponseAsync(msg =>
                {
                    msg.Content = "❌ 만료가 취소되었습니다.";
                    msg.Components = null;
                });
                _ = Services.RespondMessageWithExpire(component);
                return;
            case Constant.START_TIME_OPEN_KEY or Constant.EXPIRE_TIME_OPEN_KEY:
                
                var time = Constant.START_TIME_OPEN_KEY == action ? party.START_DATE : party.EXPIRE_DATE;
                
                var messageProperties = Services.CreateDatePickup(action, party, time);

                await component.ModifyOriginalResponseAsync(mp =>
                {
                    mp.Content = messageProperties.Content;
                    mp.Embed = messageProperties.Embed;
                    mp.Components = messageProperties.Components;
                });
                
                return;
            case Constant.DATE_PICKUP_KEY or Constant.DATE_PICKUP_FIRST_KEY:
                var guid = parts[2];
                var title = parts[3];
                var dateAction = parts[4];
                
                if (!Services.dateDic.TryGetValue(guid, out var date))
                {
                    await component.ModifyOriginalResponseAsync(mp =>
                    {
                        mp.Content = "데이터 처리 중 오류가 발생하였습니다, 다시 시도해주세요";
                        mp.Components = null;
                        mp.Embed = null;
                    });
                    return;
                }

                switch (dateAction)
                {
                    case Constant.DATE_YEAR_SELECT_KEY or Constant.DATE_HOUR_SELECT_KEY:
                        date.IsDateDisplay = dateAction == Constant.DATE_YEAR_SELECT_KEY;
                        
                        var datePickup = Services.CreateDatePickup(title, partyEntity!, guid: guid);
            
                        await component.ModifyOriginalResponseAsync(mp =>
                        {
                            mp.Content = datePickup.Content;
                            mp.Embed = datePickup.Embed;
                            mp.Components = datePickup.Components;
                        });       
                        return;
                    
                    case Constant.DATE_YES:
                        var dateTime = date.ToDateTime();
                        
                        switch (title)
                        {
                            case Constant.START_TIME_OPEN_KEY:
                                if (await partyService.SetStartDate(party.PARTY_KEY, dateTime))
                                {
                                    message = "시작 날짜를 설정하였습니다.";
                                    party.START_DATE = dateTime;
                                }
                                else
                                {
                                    message = "날짜 설정에 실패하였습니다.";
                                }
                                break;
                            case Constant.EXPIRE_TIME_OPEN_KEY:
                                if (await partyService.SetExpireDate(party.PARTY_KEY, dateTime))
                                {
                                    message = "만료 날짜를 설정하였습니다.";
                                    party.EXPIRE_DATE = dateTime;
                                }
                                else
                                {
                                    message = "날짜 설정에 실패하였습니다.";
                                }
                                break;
                        }
                        
                        await component.ModifyOriginalResponseAsync(mp =>
                        {
                            mp.Content = message;
                            mp.Embed = null;
                            mp.Components = null;
                        });       
                        
                        break;
                    case Constant.DATE_NO:
                        // if (action == PartyConstant.DATE_PICKUP_FIRST_KEY)
                        // {
                        //     await component.ModifyOriginalResponseAsync(mp =>
                        //     {
                        //         mp.Content = "설정을 취소하였습니다.";
                        //         mp.Embed = null;
                        //         mp.Components = null;
                        //     });
                        // }
                        
                        await component.ModifyOriginalResponseAsync(mp =>
                        {
                            mp.Content = "설정을 취소하였습니다.";
                            mp.Embed = null;
                            mp.Components = null;
                        });
                        
                        message = "설정을 취소하였습니다.";
                        break;
                }
                break;
                
                case Constant.MOVE_OWNER_KEY:
                selectMenuBuilder = new SelectMenuBuilder()
                    .WithCustomId($"{Constant.MOVE_OWNER_KEY}_{partyKey}")
                    .WithPlaceholder("파티를 위임할 유저를 선택하세요")
                    .WithMinValues(1)
                    .WithMaxValues(1)
                    .WithType(ComponentType.UserSelect);
                
                ag = new ComponentBuilder()
                    .WithSelectMenu(selectMenuBuilder)
                    .Build();

                await component.ModifyOriginalResponseAsync(msg =>
                {
                    msg.Content = $"⚠️ 파티를 위임할 유저를 선택하세요";
                    msg.Components = ag;
                });
                
                return;
                break;
                
        }
        
        await Services.UpdateMessage(component, party, isAllMessage, message);
        await Services.RespondMessageWithExpire(component, isAddSettingButton ? 20 : 10, message: message, addComponent: isAddSettingButton ? Services.GetSettingComponent() : null);
    }


    private async Task UserSetting(SocketMessageComponent component, string[] parts)
    {
        var action = parts[1];
        var user = component.User;

        IUserMessage? message = null;
            await component.UpdateAsync(mp => mp.Content = "작업 중...");
        


        var setting = await userService.GetUserSettingAsync(user.Id);

        switch (action)
        {
            case Constant.USER_ALERT_SETTING_OPEN_KEY:
                break;
            case Constant.USER_ALERT_ALL_FLAG:
                setting.ALL_ALERT_FLAG = !setting.ALL_ALERT_FLAG;
                break;
            case Constant.USER_ALERT_MY_PARTY_FULL_FLAG:
                setting.MY_PARTY_FULL_ALERT_FLAG = !setting.MY_PARTY_FULL_ALERT_FLAG;
                break;
            case Constant.USER_ALERT_MY_PARTY_JOIN_USER_FLAG:
                setting.MY_PARTY_JOIN_USER_ALERT_FLAG = !setting.MY_PARTY_JOIN_USER_ALERT_FLAG;
                break;
            case Constant.USER_ALERT_MY_PARTY_LEFT_USER_FLAG:
                setting.MY_PARTY_LEFT_USER_ALERT_FLAG = !setting.MY_PARTY_LEFT_USER_ALERT_FLAG;
                break;
            case Constant.USER_ALERT_JOIN_PARTY_TO_WAIT_FLAG:
                setting.JOIN_PARTY_TO_WAIT_FLAG = !setting.JOIN_PARTY_TO_WAIT_FLAG;
                break;
            case Constant.PARTY_START_TIME_ALERT_FLAG:
                setting.PARTY_START_TIME_ALERT_FLAG = !setting.PARTY_START_TIME_ALERT_FLAG;
                break;
            case Constant.CLOSE_KEY:
                try
                {
                    if (message != null)
                    {
                        await message.DeleteAsync();
                    }
                    else
                    {
                        await component.DeleteOriginalResponseAsync();
                    }
                }
                catch (Exception)
                {
                    // ignored
                }
                return;
        }

        if (action != Constant.USER_ALERT_SETTING_OPEN_KEY)
        {
            await userService.SetUserSettingAsync(setting);
        }

        var embed = BuildAlertSettingEmbed(setting);
        var com = GetOptionComponent(setting);

        if (message != null)
        {
            await message.ModifyAsync(mp =>
            {
                mp.Content = null;
                mp.Embed = embed;
                mp.Components = com;
            });
        }
        else
        {
            await component.ModifyOriginalResponseAsync(mp =>
            {
                mp.Content = null;
                mp.Embed = embed;
                mp.Components = com;
            });
        }
    }

    private static Embed BuildAlertSettingEmbed(UserSettingEntity entity)
    {
        var eb = new EmbedBuilder()
            .WithTitle("🔔 알림 설정")
            .WithDescription("아래 버튼을 누르면 해당 알림이 **켜지거나 꺼집니다.**\n초록색 = 켜짐, 빨간색 = 꺼짐")
            .WithColor(entity.ALL_ALERT_FLAG ? Color.Green : Color.Blue)
            .WithFooter("버튼을 누르면 즉시 반영됩니다.")
            .AddField("모든 알림", entity.ALL_ALERT_FLAG ? "✅ 켜짐" : "❌ 꺼짐", true)
            .AddField(Constant.PARTY_START_TIME_ALERT_FLAG, entity.PARTY_START_TIME_ALERT_FLAG ? "✅ 켜짐" : "❌ 꺼짐", true)
            .AddField(Constant.USER_ALERT_MY_PARTY_FULL_FLAG, entity.MY_PARTY_FULL_ALERT_FLAG ? "✅ 켜짐" : "❌ 꺼짐", true)
            .AddField(Constant.USER_ALERT_JOIN_PARTY_TO_WAIT_FLAG, entity.JOIN_PARTY_TO_WAIT_FLAG ? "✅ 켜짐" : "❌ 꺼짐", true)
            .AddField(Constant.USER_ALERT_MY_PARTY_JOIN_USER_FLAG, entity.MY_PARTY_JOIN_USER_ALERT_FLAG ? "✅ 켜짐" : "❌ 꺼짐", true)
            .AddField(Constant.USER_ALERT_MY_PARTY_LEFT_USER_FLAG, entity.MY_PARTY_LEFT_USER_ALERT_FLAG ? "✅ 켜짐" : "❌ 꺼짐", true);
        return eb.Build();
    }

    private MessageComponent GetOptionComponent(UserSettingEntity entity)
    {
        var b = new ComponentBuilder();
        var key = Constant.USER_ALERT_SETTING_KEY;

        b.WithButton(Constant.USER_ALERT_ALL_FLAG, $"{key}_{Constant.USER_ALERT_ALL_FLAG}", GetButtonStyle(entity.ALL_ALERT_FLAG), row: 0);

        if (entity.ALL_ALERT_FLAG)
        {
            b.WithButton(Constant.PARTY_START_TIME_ALERT_FLAG, $"{key}_{Constant.PARTY_START_TIME_ALERT_FLAG}", GetButtonStyle(entity.PARTY_START_TIME_ALERT_FLAG), row: 1);
            b.WithButton(Constant.USER_ALERT_MY_PARTY_FULL_FLAG, $"{key}_{Constant.USER_ALERT_MY_PARTY_FULL_FLAG}", GetButtonStyle(entity.MY_PARTY_FULL_ALERT_FLAG), row: 1);
            b.WithButton(Constant.USER_ALERT_JOIN_PARTY_TO_WAIT_FLAG, $"{key}_{Constant.USER_ALERT_JOIN_PARTY_TO_WAIT_FLAG}", GetButtonStyle(entity.JOIN_PARTY_TO_WAIT_FLAG), row: 1);
            b.WithButton(Constant.USER_ALERT_MY_PARTY_JOIN_USER_FLAG, $"{key}_{Constant.USER_ALERT_MY_PARTY_JOIN_USER_FLAG}", GetButtonStyle(entity.MY_PARTY_JOIN_USER_ALERT_FLAG), row: 2);
            b.WithButton(Constant.USER_ALERT_MY_PARTY_LEFT_USER_FLAG, $"{key}_{Constant.USER_ALERT_MY_PARTY_LEFT_USER_FLAG}", GetButtonStyle(entity.MY_PARTY_LEFT_USER_ALERT_FLAG), row: 2);
        }

        b.WithButton("설정 닫기", $"{key}_{Constant.CLOSE_KEY}", ButtonStyle.Secondary, row: 3);

        return b.Build();
    }

    private static ButtonStyle GetButtonStyle(bool b)
    {
        return b ? ButtonStyle.Success : ButtonStyle.Danger;
    }
}