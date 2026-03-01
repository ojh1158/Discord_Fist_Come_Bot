using Discord;
using Discord.WebSocket;
using DiscordBot.scripts._src.party;
using DiscordBot.scripts.db.Models;
using DiscordBot.scripts.db.Repositories;
using DiscordBot.scripts.db.Services;

namespace DiscordBot.scripts._src.Services;

public class MenuServices : BaseServices
{
    public MenuServices(DiscordServices services) : base(services)
    {
        Services.client.SelectMenuExecuted += HandleSelectMenuAsync;
    }
    private async Task HandleSelectMenuAsync(SocketMessageComponent component)
    {
        var customId = component.Data.CustomId;
        
        // CustomId 파싱: "{action}_{messageId}" 또는 "{action}_{messageId}_{extra}"
        // 하위 호환성: "party_{action}_{messageId}" 형식도 지원
        var parts = customId.Split('_');
        if (parts.Length < 2)
            return;
        
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
        
        var allMessageFlag = false;
        var allmessage = "";
        
        if (messageIdIndex >= parts.Length)
            return;
        
        var partyKey = parts[messageIdIndex];
        
        await InitCommands(component, action);
        // 파티 정보 가져오기
        var partyEntity = await PartyService.GetPartyEntityAsync(partyKey);

        if (partyEntity == null)
        {
            await component.ModifyOriginalResponseAsync(m => m.Content = "파티를 찾을 수 없습니다...");
            _ = Services.RespondMessageWithExpire(component, 5);
            return;
        }

        var partyClass = new PartyClass();
        await partyClass.Init(partyEntity, component, Services.client);

        // 선택된 값들 가져오기 (SelectMenu는 여러 값 선택 가능)
        var selectedValues = component.Data.Values; // string[] 배열
        switch (action)
        {
            case PartyConstant.JOIN_AUTO_KEY:
        
                if (action == PartyConstant.JOIN_AUTO_KEY)
                {
                    if (partyEntity == null)
                    {
                        await component.ModifyOriginalResponseAsync(m=> m.Content = "파티를 찾을 수 없습니다.");
                        return;
                    }

                    string? message = null;
                    var IsFullAlart = false;
                    var addCount = 0;
                    
                    foreach (var selectedValue in selectedValues)
                    {
                        // 선택된 유저 ID를 파싱하여 파티에 추가
                        // 예: selectedValue가 "123456789" (ulong)라면
                        
                        
                        if (ulong.TryParse(selectedValue, out var userId))
                        {
                            IUser? user = Services.client.GetGuild(partyEntity.GUILD_KEY).GetUser(userId);
                            user ??= await Services.client.GetUserAsync(userId); // RestUser 반환
                            
                            if (user is { IsBot: false })
                            {
                                string name;
                                
                                if (user is IGuildUser guildUser)
                                {
                                    name = guildUser.DisplayName;
                                }
                                else
                                {
                                    // 길드에서 최신 유저 정보를 가져와서 닉네임 확인 (Rest API 사용)
                                    try
                                    {
                                        var restGuild = await Services.client.Rest.GetGuildAsync(partyEntity.GUILD_KEY);
                                        if (restGuild != null)
                                        {
                                            var guildUserInfo = await restGuild.GetUserAsync(userId);
                                            if (guildUserInfo != null && !string.IsNullOrEmpty(guildUserInfo.Nickname))
                                            {
                                                // Rest API에서 가져온 닉네임 사용
                                                name = guildUserInfo.Nickname;
                                            }
                                            else
                                            {
                                                // 닉네임이 없으면 Username 사용
                                                name = guildUserInfo?.GlobalName ?? user.Username;
                                            }
                                        }
                                        else
                                        {
                                            name = user.GlobalName ?? user.Username;
                                        }
                                    }
                                    catch
                                    {
                                        name = user.GlobalName ?? user.Username;
                                    }
                                }
                                
                                var type = await PartyService.JoinPartyAsync(partyEntity, userId, name);
                                addCount++;
                                
                                if (!IsFullAlart)
                                {
                                    IsFullAlart = partyEntity.Members.Count + addCount == partyEntity.MAX_COUNT_MEMBER;
                                }
                                
                                // 파티에 유저 추가 로직
                                if (message == null)
                                    message = "";
                                message += $"{name} : {type.GetComment()}\n";
                            }
                        }
                    }
                    
                    string finalMessage;
                    if (string.IsNullOrWhiteSpace(message))
                    {
                        finalMessage = "아무도 추가되지 못하였습니다.";
                        await component.ModifyOriginalResponseAsync(m => m.Content = finalMessage);
                        _ = Services.RespondMessageWithExpire(component, 5);
                    }
                    else
                    {
                        allMessageFlag = true;
                        allmessage = $"{partyClass.userRoleString}님이 다음 파티원을 초대하였습니다!\n" + message;
                        if(IsFullAlart) Services.SendUserAlert(partyEntity, component.User, PartyConstant.JOIN_AUTO_KEY);
                        await component.DeleteOriginalResponseAsync();
                    }
                }
                break;
            case PartyConstant.KICK_KEY:

                var ms = "";

                var dic = new Dictionary<ulong, PartyMemberEntity>();
                
                foreach (var entity in partyEntity.Members)
                {
                    dic.TryAdd(entity.USER_ID, entity);
                }
                foreach (var entity in partyEntity.WaitMembers)
                {
                    dic.TryAdd(entity.USER_ID, entity);
                }
                
                List<Task> tasks = [];
                
                foreach (var selectedValue in selectedValues)
                {
                    if (ulong.TryParse(selectedValue, out var userId))
                    {
                        if (dic.TryGetValue(userId, out var value))
                        {
                            tasks.Add(PartyService.KickMemberAsync(partyEntity, userId));
                            ms += $"{value.USER_NICKNAME} 님을 추방하였습니다.\n"; 
                        }
                    }
                }

                await Task.WhenAll(tasks);
                
                await component.DeleteOriginalResponseAsync();
                await component.Channel.SendMessageAsync($"{partyClass.userNickname} 님이 아래의 파티원을 추방하였습니다.\n"+ ms);
                break;
            case PartyConstant.DATE_PICKUP_KEY or PartyConstant.DATE_PICKUP_FIRST_KEY:
                var guid = parts[2];
                var title = parts[3];
                var dateAction = parts[4];
                var s = selectedValues.First();
                
                if (s == null || !Services.dateDic.TryGetValue(guid, out var date))
                {
                    allmessage = "데이터 처리 중 오류가 발생하였습니다, 다시 시도해주세요";
                    break;
                }
                
                switch (dateAction)
                {
                    case PartyConstant.YEAR_KEY:
                        date.Year = int.Parse(s);  // 2025
                        break;
                    case PartyConstant.MONTH_KEY:
                        date.Month = int.Parse(s);  // 1~12
                        break;
                    case PartyConstant.DAY_TENS_KEY:
                        date.DayTens = int.Parse(s);  // 0, 10, 20, 30
                        break;
                    case PartyConstant.DAY_ONES_KEY:
                        date.DayOnes = int.Parse(s);  // 0~9
                        break;
                    case PartyConstant.HOUR_KEY:
                        date.Hour = int.Parse(s);  // 0~23
                        break;
                    case PartyConstant.MIN_TENS_KEY:
                        date.MinTens = int.Parse(s);  // 0, 10, ..., 50
                        break;
                    case PartyConstant.MIN_ONES_KEY:
                        date.MinOnes = int.Parse(s);  // 0~9
                        break;
                }
                
                var datePickup = Services.CreateDatePickup(title, partyEntity, guid: guid);
            
                await component.ModifyOriginalResponseAsync(mp =>
                {
                    mp.Content = datePickup.Content;
                    mp.Embed = datePickup.Embed;
                    mp.Components = datePickup.Components;
                });
                
                return;
        }
        
        var party = await PartyService.GetPartyEntityAsync(partyKey);

        if (party != null)
        {
            await Services.UpdateMessage(component, party, allMessageFlag, allmessage);
        }
    }
}