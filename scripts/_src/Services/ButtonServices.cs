using Discord;
using Discord.WebSocket;
using DiscordBot.scripts._src.party;
using DiscordBot.scripts.db.Models;
using DiscordBot.scripts.db.Services;

namespace DiscordBot.scripts._src.Services;

public class ButtonServices : BaseServices
{
    public ButtonServices(DiscordServices services) : base(services)
    {
        Services.client.ButtonExecuted += HandleButtonAsync;
    }

    private async Task HandleButtonAsync(SocketMessageComponent component)
    {
        var customId = component.Data.CustomId;
        
        // CustomId 파싱: "{action}_{messageId}" 또는 "{action}_{messageId}_{extra}"
        // 하위 호환성: "party_{action}_{messageId}" 형식도 지원
        var parts = customId.Split('_');
        if (parts.Length < 2)
            return;
        
        if (parts[0] == PartyConstant.USER_ALERT_SETTING_KEY)
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
        
        if (action is PartyConstant.TEAM_REMOVE_KEY)
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

        var partyEntity = await PartyService.GetPartyEntityAsync(partyKey);
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
            case PartyConstant.JOIN_KEY:
                type = await PartyService.JoinPartyAsync(party, partyClass.guildUser.Id, partyClass.userNickname);
                    
                // Service에서 중복 체크 포함하여 처리
                if (type is JoinType.Join or JoinType.Wait)
                {
                    if (type is JoinType.Wait)
                    {
                        message = "파티 인원이 가득 찼습니다. 대기 인원으로 등록되었습니다.";
                    }
                    else
                    {
                        message = $"✅ {party.DISPLAY_NAME} 파티에 참가했습니다!";
                    }

                    var members = await PartyService.GetPartyMemberListAsync(party.PARTY_KEY);
                    var waitMembers = await PartyService.GetPartyWaitMemberListAsync(party.PARTY_KEY);

                    if (members != null && waitMembers != null)
                    {
                        party.Members = members;
                        party.WaitMembers = waitMembers;
                    }
                    else
                    {
                        message = "파티 UI 업데이트에 실패하였습니다. 인원 등록은 완료되었습니다";
                    }

                    isAddSettingButton = true;
                    
                    Services.SendUserAlert(partyEntity!, component.User, action);
                }
                else if(type is JoinType.Exists or JoinType.Error)
                {
                    await component.ModifyOriginalResponseAsync(m => m.Content = type is JoinType.Exists ? "파티에 이미 참가하였습니다." : "알 수 없는 오류가 나타났습니다.");
                    _ = Services.RespondMessageWithExpire(component);
                    return;
                }
                break;
                
            case PartyConstant.LEAVE_KEY:
                if (await PartyService.LeavePartyAsync(party, partyClass.userId))
                {
                    message = $"❌ {party.DISPLAY_NAME} 파티에서 나갔습니다.";
                    
                    var members = await PartyService.GetPartyMemberListAsync(party.PARTY_KEY);
                    var waitMembers = await PartyService.GetPartyWaitMemberListAsync(party.PARTY_KEY);

                    if (members != null && waitMembers != null)
                    {
                        party.Members = members;
                        party.WaitMembers = waitMembers;
                    }
                    else
                    {
                        message = "파티 UI 업데이트에 실패하였습니다. 나가기는 완료 되었습니다.";
                    }
                    
                    Services.SendUserAlert(partyEntity!, component.User, action);
                }
                else
                {
                    await component.ModifyOriginalResponseAsync(m => m.Content = "파티에 참가하지 않았거나 나가기에 실패했습니다.");
                    return;
                }
                break;
            case PartyConstant.OPTION_KEY:
                // if (partyClass.isNone)
                // {
                //     await component.ModifyOriginalResponseAsync(m => m.Content ="권한이 없어 표시할 기능이 없습니다.");
                //     await Services.RespondMessageWithExpire(component, time: 5);
                //     return;
                // }
                
                // 옵션 버튼들 만들기
                var componentBuilder = new ComponentBuilder();
                
                componentBuilder.WithButton("개인 알림 설정",$"{PartyConstant.USER_ALERT_SETTING_KEY}_{PartyConstant.USER_ALERT_SETTING_OPEN_KEY}", ButtonStyle.Success, row:0);

                if (partyClass is {isAdmin: true} or {isPartyMember: true} or {isOwner: true} or {isWater:true})
                {
                    componentBuilder.WithButton(PartyConstant.PULLING_UP_KEY,$"{PartyConstant.PULLING_UP_KEY}_{partyKey}", ButtonStyle.Success, row:1);
                    componentBuilder.WithButton(PartyConstant.TEAM_KEY,$"{PartyConstant.TEAM_KEY}_{partyKey}", ButtonStyle.Success, row:1);
                }

                if (partyClass is {isAdmin: true} or {isPartyMember: true} or {isOwner: true} && party.Members.Count >= 1)
                {
                    componentBuilder.WithButton(PartyConstant.PING_KEY, $"{PartyConstant.PING_KEY}_{partyKey}", ButtonStyle.Success, row:1);
                    if (partyClass.isAdmin || partyClass.isOwner)
                    {
                        componentBuilder.WithButton(PartyConstant.KICK_KEY,$"{PartyConstant.KICK_KEY}_{partyKey}", ButtonStyle.Primary, row:2);
                    }
                }

                if (partyClass.isAdmin || partyClass.isOwner)
                {
                    componentBuilder.WithButton(PartyConstant.JOIN_AUTO_KEY, $"{PartyConstant.JOIN_AUTO_KEY}_{partyKey}", ButtonStyle.Primary, row:2);
                    componentBuilder.WithButton(PartyConstant.PARTY_KEY,$"{PartyConstant.PARTY_KEY}_{partyKey}", ButtonStyle.Primary, row:2);
                    componentBuilder.WithButton(PartyConstant.START_TIME_OPEN_KEY,$"{PartyConstant.START_TIME_OPEN_KEY}_{partyKey}", ButtonStyle.Primary, row:2);
                    componentBuilder.WithButton(PartyConstant.EXPIRE_TIME_OPEN_KEY,$"{PartyConstant.EXPIRE_TIME_OPEN_KEY}_{partyKey}", ButtonStyle.Primary, row:2);
                    
                    
                    componentBuilder.WithButton(party.IS_CLOSED ? "재개" : PartyConstant.CLOSE_KEY, $"{PartyConstant.CLOSE_KEY}_{partyKey}", party.IS_CLOSED ? ButtonStyle.Success : ButtonStyle.Danger, row:3);
                    componentBuilder.WithButton(PartyConstant.EXPIRE_KEY, $"{PartyConstant.EXPIRE_KEY}_{partyKey}", ButtonStyle.Secondary, row:3);
                }
                
                await component.ModifyOriginalResponseAsync( m =>
                {
                    m.Content = "버튼을 선택해주세요.";
                    m.Components = componentBuilder.Build();
                });

                await Services.RespondMessageWithExpire(component, time: 30);
                return;
            case PartyConstant.CLOSE_KEY:
                
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
                
                if (!await PartyService.SetPartyCloseAsync(partyKey, !closed))
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
            case PartyConstant.PING_KEY:
                
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
                
                var mentions = string.Join(" ", party.Members.Select(m => $"<@{m.USER_ID}>"));
                isAllMessage = true;
                message = $"🔔 {partyClass.userRoleString}님이 파티원을 호출하였습니다!\n{mentions}";
                break;
            case PartyConstant.EXPIRE_KEY:
                
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
                    .WithButton("예", $"{PartyConstant.YES_BUTTON_KEY}_{partyKey}", ButtonStyle.Danger)
                    .WithButton("아니오", $"{PartyConstant.NO_BUTTON_KEY}_{partyKey}", ButtonStyle.Secondary)
                    .Build();
                
                await component.ModifyOriginalResponseAsync(msg =>
                {
                    msg.Content = $"⚠️ **{party.DISPLAY_NAME}** 파티를 영구적으로 만료시키시겠습니까?\n만료된 파티는 복구할 수 없습니다.";
                    msg.Components = confirmComponent;
                });
                _ = Services.RespondMessageWithExpire(component, time: 30);
                return;
            case PartyConstant.PARTY_KEY:
                var renameModal = new ModalBuilder()
                    .WithTitle("파티 설정 변경")
                    .WithCustomId($"{PartyConstant.SETTING_MODEL_KEY}_{partyKey}")
                    .AddTextInput("이름", "name", TextInputStyle.Short, 
                        placeholder: $"여기에 이름 입력", 
                        required: true,
                        value: party.DISPLAY_NAME,
                        minLength: 1,
                        maxLength: PartyConstant.MAX_NAME_COUNT)
                    .AddTextInput("새로운 인원 수", "count", TextInputStyle.Short, 
                        placeholder: $"{1}-{PartyConstant.MAX_COUNT}", 
                        required: true,
                        value: party.MAX_COUNT_MEMBER.ToString(),
                        minLength: 1,
                        maxLength: 3)
                    .Build();

                await component.RespondWithModalAsync(renameModal);
                return;
            case PartyConstant.JOIN_AUTO_KEY:
                
                var selectMenuBuilder = new SelectMenuBuilder()
                    .WithCustomId($"{PartyConstant.JOIN_AUTO_KEY}_{partyKey}")
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
            case PartyConstant.KICK_KEY:
                
                var menuBuilder = new SelectMenuBuilder()
                    .WithCustomId($"{PartyConstant.KICK_KEY}_{partyKey}")
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
            case PartyConstant.TEAM_KEY:
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
                
                var maxCount = Math.Min(party.Members.Count, 10);
                
                var teamModal = new ModalBuilder()
                    .WithTitle("팀 만들기")
                    .WithCustomId($"{PartyConstant.TEAM_KEY}_{partyKey}")
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
            case PartyConstant.PULLING_UP_KEY:
                
                var channel = component.Channel;
                var sendMessageAsync = await channel.SendMessageAsync("초기화 중입니다...");
                
                if (!await PartyService.ChangeMessageId(party.MESSAGE_KEY, sendMessageAsync.Id))
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
            case PartyConstant.YES_BUTTON_KEY:
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
            case PartyConstant.NO_BUTTON_KEY:
                await component.ModifyOriginalResponseAsync(msg =>
                {
                    msg.Content = "❌ 만료가 취소되었습니다.";
                    msg.Components = null;
                });
                _ = Services.RespondMessageWithExpire(component);
                return;
            case PartyConstant.KICK_BUTTON_KEY:
                await component.DeferAsync();
                
                var id = parts[2];
                var targetUserId = ulong.Parse(id);
                var result = "";
                
                if (await PartyService.KickMemberAsync(party, targetUserId))
                {
                    var user = Services.client.GetGuild(party.GUILD_KEY).GetUser(targetUserId);

                    if (user is IGuildUser guildUser)
                    {
                        result = $"{guildUser.DisplayName} 님을 추방하였습니다.";
                    }
                    else if (user != null)
                    {
                        result = $"{user.GlobalName ?? user.Username} 님을 추방하였습니다.";
                    }
                    else
                    {
                        result = "해당 유저를 추방하였습니다.";
                    }
                    
                    var members = await PartyService.GetPartyMemberListAsync(party.PARTY_KEY);
                    var waitMembers = await PartyService.GetPartyWaitMemberListAsync(party.PARTY_KEY);

                    if (members != null && waitMembers != null)
                    {
                        party.Members = members;
                        party.WaitMembers = waitMembers;
                    }
                    else
                    {
                        message = "파티 UI 업데이트에 실패하였습니다. 추방은 완료되었습니다";
                    }
                }
                else
                {
                    result = $"오류";
                }
                
                await component.ModifyOriginalResponseAsync(msg =>
                {
                    msg.Content = result;
                    msg.Components = null;
                });
                _ = Services.RespondMessageWithExpire(component, time: 30);
                await Services.UpdateMessage(component, party, isAllMessage, message);
                return;
            
            case PartyConstant.START_TIME_OPEN_KEY or PartyConstant.EXPIRE_TIME_OPEN_KEY:
                
                var time = PartyConstant.START_TIME_OPEN_KEY == action ? party.START_DATE : party.EXPIRE_DATE;
                
                var messageProperties = Services.CreateDatePickup(action, party, time);

                await component.ModifyOriginalResponseAsync(mp =>
                {
                    mp.Content = messageProperties.Content;
                    mp.Embed = messageProperties.Embed;
                    mp.Components = messageProperties.Components;
                });
                
                return;
            case PartyConstant.DATE_PICKUP_KEY or PartyConstant.DATE_PICKUP_FIRST_KEY:
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
                    case PartyConstant.DATE_YEAR_SELECT_KEY or PartyConstant.DATE_HOUR_SELECT_KEY:
                        date.IsDateDisplay = dateAction == PartyConstant.DATE_YEAR_SELECT_KEY;
                        
                        var datePickup = Services.CreateDatePickup(title, partyEntity!, guid: guid);
            
                        await component.ModifyOriginalResponseAsync(mp =>
                        {
                            mp.Content = datePickup.Content;
                            mp.Embed = datePickup.Embed;
                            mp.Components = datePickup.Components;
                        });       
                        return;
                    
                    case PartyConstant.DATE_YES:
                        var dateTime = date.ToDateTime();
                        
                        switch (title)
                        {
                            case PartyConstant.START_TIME_OPEN_KEY:
                                if (await PartyService.SetStartDate(party.PARTY_KEY, dateTime))
                                {
                                    message = "시작 날짜를 설정하였습니다.";
                                    party.START_DATE = dateTime;
                                }
                                else
                                {
                                    message = "날짜 설정에 실패하였습니다.";
                                }
                                break;
                            case PartyConstant.EXPIRE_TIME_OPEN_KEY:
                                if (await PartyService.SetExpireDate(party.PARTY_KEY, dateTime))
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
                    case PartyConstant.DATE_NO:
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
        }
        
        await Services.UpdateMessage(component, party, isAllMessage, message);
        await Services.RespondMessageWithExpire(component, isAddSettingButton ? 20 : 10, message: message, addComponent: isAddSettingButton ? Services.GetSettingComponent() : null);
    }


    private async Task UserSetting(SocketMessageComponent component, string[] parts)
    {
        var action = parts[1];
        var user = component.User;

        IUserMessage? message = null;
        
        // if (component.Channel is SocketGuildChannel)
        // {
        //     await component.UpdateAsync(mp =>
        //     {
        //         mp.Content = "설정을 진행하시려면 봇과의 DM으로 이동해주세요!";
        //         mp.Components = null;
        //         mp.Embed = null;
        //     });
        //     
        //     _ = Services.RespondMessageWithExpire(component, 15);
        //     
        //     message = await user.SendMessageAsync("작업 중...");
        // }
        // else
        // {
        //     await component.UpdateAsync(mp => mp.Content = "작업 중...");
        // }
            await component.UpdateAsync(mp => mp.Content = "작업 중...");
        


        var setting = await UserService.GetUserSettingAsync(user.Id);

        switch (action)
        {
            case PartyConstant.USER_ALERT_SETTING_OPEN_KEY:
                break;
            case PartyConstant.USER_ALERT_ALL_FLAG:
                setting.ALL_ALERT_FLAG = !setting.ALL_ALERT_FLAG;
                break;
            case PartyConstant.USER_ALERT_MY_PARTY_FULL_FLAG:
                setting.MY_PARTY_FULL_ALERT_FLAG = !setting.MY_PARTY_FULL_ALERT_FLAG;
                break;
            case PartyConstant.USER_ALERT_MY_PARTY_JOIN_USER_FLAG:
                setting.MY_PARTY_JOIN_USER_ALERT_FLAG = !setting.MY_PARTY_JOIN_USER_ALERT_FLAG;
                break;
            case PartyConstant.USER_ALERT_MY_PARTY_LEFT_USER_FLAG:
                setting.MY_PARTY_LEFT_USER_ALERT_FLAG = !setting.MY_PARTY_LEFT_USER_ALERT_FLAG;
                break;
            case PartyConstant.USER_ALERT_JOIN_PARTY_TO_WAIT_FLAG:
                setting.JOIN_PARTY_TO_WAIT_FLAG = !setting.JOIN_PARTY_TO_WAIT_FLAG;
                break;
            case PartyConstant.PARTY_START_TIME_ALERT_FLAG:
                setting.PARTY_START_TIME_ALERT_FLAG = !setting.PARTY_START_TIME_ALERT_FLAG;
                break;
            case PartyConstant.CLOSE_KEY:
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

        if (action != PartyConstant.USER_ALERT_SETTING_OPEN_KEY)
        {
            await UserService.SetUserSettingAsync(setting);
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
            .AddField(PartyConstant.PARTY_START_TIME_ALERT_FLAG, entity.PARTY_START_TIME_ALERT_FLAG ? "✅ 켜짐" : "❌ 꺼짐", true)
            .AddField(PartyConstant.USER_ALERT_MY_PARTY_FULL_FLAG, entity.MY_PARTY_FULL_ALERT_FLAG ? "✅ 켜짐" : "❌ 꺼짐", true)
            .AddField(PartyConstant.USER_ALERT_JOIN_PARTY_TO_WAIT_FLAG, entity.JOIN_PARTY_TO_WAIT_FLAG ? "✅ 켜짐" : "❌ 꺼짐", true)
            .AddField(PartyConstant.USER_ALERT_MY_PARTY_JOIN_USER_FLAG, entity.MY_PARTY_JOIN_USER_ALERT_FLAG ? "✅ 켜짐" : "❌ 꺼짐", true)
            .AddField(PartyConstant.USER_ALERT_MY_PARTY_LEFT_USER_FLAG, entity.MY_PARTY_LEFT_USER_ALERT_FLAG ? "✅ 켜짐" : "❌ 꺼짐", true);
        return eb.Build();
    }

    private MessageComponent GetOptionComponent(UserSettingEntity entity)
    {
        var b = new ComponentBuilder();
        var key = PartyConstant.USER_ALERT_SETTING_KEY;

        b.WithButton(PartyConstant.USER_ALERT_ALL_FLAG, $"{key}_{PartyConstant.USER_ALERT_ALL_FLAG}", GetButtonStyle(entity.ALL_ALERT_FLAG), row: 0);

        if (entity.ALL_ALERT_FLAG)
        {
            b.WithButton(PartyConstant.PARTY_START_TIME_ALERT_FLAG, $"{key}_{PartyConstant.PARTY_START_TIME_ALERT_FLAG}", GetButtonStyle(entity.PARTY_START_TIME_ALERT_FLAG), row: 1);
            b.WithButton(PartyConstant.USER_ALERT_MY_PARTY_FULL_FLAG, $"{key}_{PartyConstant.USER_ALERT_MY_PARTY_FULL_FLAG}", GetButtonStyle(entity.MY_PARTY_FULL_ALERT_FLAG), row: 1);
            b.WithButton(PartyConstant.USER_ALERT_JOIN_PARTY_TO_WAIT_FLAG, $"{key}_{PartyConstant.USER_ALERT_JOIN_PARTY_TO_WAIT_FLAG}", GetButtonStyle(entity.JOIN_PARTY_TO_WAIT_FLAG), row: 1);
            b.WithButton(PartyConstant.USER_ALERT_MY_PARTY_JOIN_USER_FLAG, $"{key}_{PartyConstant.USER_ALERT_MY_PARTY_JOIN_USER_FLAG}", GetButtonStyle(entity.MY_PARTY_JOIN_USER_ALERT_FLAG), row: 2);
            b.WithButton(PartyConstant.USER_ALERT_MY_PARTY_LEFT_USER_FLAG, $"{key}_{PartyConstant.USER_ALERT_MY_PARTY_LEFT_USER_FLAG}", GetButtonStyle(entity.MY_PARTY_LEFT_USER_ALERT_FLAG), row: 2);
        }

        b.WithButton("설정 닫기", $"{key}_{PartyConstant.CLOSE_KEY}", ButtonStyle.Secondary, row: 3);

        return b.Build();
    }

    private static ButtonStyle GetButtonStyle(bool b)
    {
        return b ? ButtonStyle.Success : ButtonStyle.Danger;
    }
}