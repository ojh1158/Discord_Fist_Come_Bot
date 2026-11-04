using Discord;
using Discord.WebSocket;
using DiscordBot.scripts.db.Models;
using DiscordBot.scripts.db.Services;
using DiscordBot.scripts.src.party;
using DiscordBot.scripts.src.Tools;
using DiscordBot.scripts.src.util;
using Serilog;

namespace DiscordBot.scripts.src.Services;

public class ModalServices : BaseServices
{
    private readonly Color[] colors = new []
    {
        Color.Red,
        Color.Blue,
        Color.Green,
        Color.Gold,
        Color.Purple,
        Color.Magenta,
        Color.Orange,
        Color.Teal,
        Color.DarkGreen,
        Color.DarkBlue,
        Color.DarkRed,
        Color.DarkOrange,
        Color.DarkPurple,
        Color.DarkTeal,
        Color.DarkMagenta,
    };
    
    private readonly PartyService partyService;
    private readonly TeamTool teamTool;
    private readonly SlashCommandServices slashCommandServices;

    public ModalServices(DiscordServices services, PartyService partyService, TeamTool teamTool, SlashCommandServices slashCommandServices) : base(services)
    {
        Services.client.ModalSubmitted += HandleModalAsync;
        this.partyService = partyService;
        this.teamTool = teamTool;
        this.slashCommandServices = slashCommandServices;
    }

    private async Task HandleModalAsync(SocketModal modal)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await ModalAsync(modal);
            }
            catch (Exception e)
            {
                Log.Error($"{e.Message}\n{e.StackTrace}");
            }
        });

        await Task.CompletedTask;
    }

    // /파티(시간설정) 모달 제출: 백그라운드 생성 결과를 기다렸다가 시작·만료 시간을 합친다
    private async Task CreatePartyModalAsync(SocketModal modal, string[] parts)
    {
        if (parts.Length < 2 || !slashCommandServices.TakePending(parts[1], out var pending) || pending is null)
        {
            await modal.ModifyOriginalResponseAsync(m => m.Content = "생성 정보가 만료되었습니다. /파티 명령어로 다시 생성해주세요.");
            _ = Services.RespondMessageWithExpire(modal);
            return;
        }

        var startInput = modal.Data.Components.FirstOrDefault(c => c.CustomId == Constant.DATE_INPUT_FIELD)?.Value ?? "";
        var expireInput = modal.Data.Components.FirstOrDefault(c => c.CustomId == Constant.DATE_INPUT_FIELD_EXPIRE)?.Value ?? "";

        DateTime? startDate = null;

        if (!DateTimeParser.TryParse(startInput, DateTime.Now, out var parsedStart, out var startClear, out var startError))
        {
            await modal.ModifyOriginalResponseAsync(m => m.Content = $"{startError}\n{DateTimeParser.HelpText}");
            _ = Services.RespondMessageWithExpire(modal);
            return;
        }
        if (!startClear)
            startDate = parsedStart;

        if (!DateTimeParser.TryParse(expireInput, DateTime.Now, out var parsedExpire, out var expireClear, out var expireError))
        {
            await modal.ModifyOriginalResponseAsync(m => m.Content = $"{expireError}\n{DateTimeParser.HelpText}");
            _ = Services.RespondMessageWithExpire(modal);
            return;
        }

        PartyEntity? party;
        try
        {
            party = await pending.PartyTask.WaitAsync(TimeSpan.FromSeconds(60));
        }
        catch (TimeoutException)
        {
            await modal.ModifyOriginalResponseAsync(m => m.Content = "파티 생성 중입니다. 잠시 후 기능 메뉴에서 시간을 설정해주세요.");
            _ = Services.RespondMessageWithExpire(modal);
            return;
        }

        if (party is null)
        {
            await modal.ModifyOriginalResponseAsync(m => m.Content = "파티 생성에 실패하였습니다.");
            _ = Services.RespondMessageWithExpire(modal);
            return;
        }

        if (startDate.HasValue)
        {
            await partyService.SetStartDate(party.PARTY_KEY, startDate.Value);
            party.START_DATE = startDate;
        }

        // 만료는 백그라운드에서 자동값(+2일 23:59)으로 이미 설정됨. 바뀐 경우에만 갱신.
        if (!expireClear && party.EXPIRE_DATE != parsedExpire)
        {
            if (!await partyService.SetExpireDate(party.PARTY_KEY, parsedExpire))
            {
                await modal.ModifyOriginalResponseAsync(m => m.Content = "만료 날짜 설정에 실패하였습니다.");
                _ = Services.RespondMessageWithExpire(modal);
                return;
            }
            party.EXPIRE_DATE = parsedExpire;
        }
        else if (expireClear && party.EXPIRE_DATE is not null)
        {
            if (!await partyService.ClearExpireDate(party.PARTY_KEY))
            {
                await modal.ModifyOriginalResponseAsync(m => m.Content = "만료 날짜 설정에 실패하였습니다.");
                _ = Services.RespondMessageWithExpire(modal);
                return;
            }
            party.EXPIRE_DATE = null;
        }

        await Services.UpdateMessage(modal, party, false, "");

        var done = "파티를 생성하였습니다!";
        if (startDate.HasValue)
            done += $"\n시작: {startDate:yyyy-MM-dd HH:mm} ({startDate.Value.ToDiscordRelativeTimestamp()})";
        else
            done += " (시작 시간 미지정)";
        if (party.EXPIRE_DATE.HasValue)
            done += $"\n만료: {party.EXPIRE_DATE:yyyy-MM-dd HH:mm} ({party.EXPIRE_DATE.Value.ToDiscordRelativeTimestamp()})";

        try
        {
            await modal.ModifyOriginalResponseAsync(m => m.Content = done);
        }
        catch
        {
            await modal.FollowupAsync(done, ephemeral: true);
        }
        _ = Services.RespondMessageWithExpire(modal);
    }

    // 시간 입력 오류용 재시도/종료 버튼 (다시 시도: 시간 모달 재오픈, 종료: 메시지 삭제)
    private static MessageComponent BuildRetryButtons(string partyKey) => new ComponentBuilder()
        .WithButton("다시 시도", $"{Constant.TIME_RETRY_KEY}_{partyKey}", ButtonStyle.Primary)
        .WithButton("종료", $"{Constant.TIME_CLOSE_KEY}_{partyKey}", ButtonStyle.Secondary)
        .Build();

    // 시작·만료 통합 모달 제출: 미리채움 그대로 제출하면 그 값으로 설정된다
    private async Task UpdateBothTimesAsync(SocketModal modal, string partyKey, PartyEntity party)
    {
        var startInput = modal.Data.Components.FirstOrDefault(c => c.CustomId == Constant.DATE_INPUT_FIELD)?.Value ?? "";
        var expireInput = modal.Data.Components.FirstOrDefault(c => c.CustomId == Constant.DATE_INPUT_FIELD_EXPIRE)?.Value ?? "";

        if (!DateTimeParser.TryParse(startInput, DateTime.Now, out var parsedStart, out var startClear, out var startError))
        {
            var retryButtons = BuildRetryButtons(partyKey);
            await modal.ModifyOriginalResponseAsync(m =>
            {
                m.Content = $"{startError}\n{DateTimeParser.HelpText}";
                m.Components = retryButtons;
            });
            _ = Services.RespondMessageWithExpire(modal);
            return;
        }
        if (!DateTimeParser.TryParse(expireInput, DateTime.Now, out var parsedExpire, out var expireClear, out var expireError))
        {
            var retryButtons = BuildRetryButtons(partyKey);
            await modal.ModifyOriginalResponseAsync(m =>
            {
                m.Content = $"{expireError}\n{DateTimeParser.HelpText}";
                m.Components = retryButtons;
            });
            _ = Services.RespondMessageWithExpire(modal);
            return;
        }

        var startText = "유지";
        if (startClear)
        {
            if (party.START_DATE is not null && await partyService.ClearStartDate(partyKey))
                party.START_DATE = null;
            startText = "삭제됨";
        }
        else if (party.START_DATE != parsedStart)
        {
            if (!await partyService.SetStartDate(partyKey, parsedStart))
            {
                await modal.ModifyOriginalResponseAsync(m => m.Content = "시작 날짜 설정에 실패하였습니다.");
                _ = Services.RespondMessageWithExpire(modal);
                return;
            }
            party.START_DATE = parsedStart;
            startText = $"{parsedStart:yyyy-MM-dd HH:mm} ({parsedStart.ToDiscordRelativeTimestamp()})";
        }
        else if (party.START_DATE.HasValue)
        {
            startText = $"{party.START_DATE:yyyy-MM-dd HH:mm} ({party.START_DATE.Value.ToDiscordRelativeTimestamp()})";
        }

        var expireText = "유지";
        if (expireClear)
        {
            if (party.EXPIRE_DATE is not null && await partyService.ClearExpireDate(partyKey))
                party.EXPIRE_DATE = null;
            expireText = "삭제됨";
        }
        else if (party.EXPIRE_DATE != parsedExpire)
        {
            if (!await partyService.SetExpireDate(partyKey, parsedExpire))
            {
                await modal.ModifyOriginalResponseAsync(m => m.Content = "만료 날짜 설정에 실패하였습니다.");
                _ = Services.RespondMessageWithExpire(modal);
                return;
            }
            party.EXPIRE_DATE = parsedExpire;
            expireText = $"{parsedExpire:yyyy-MM-dd HH:mm} ({parsedExpire.ToDiscordRelativeTimestamp()})";
        }
        else if (party.EXPIRE_DATE.HasValue)
        {
            expireText = $"{party.EXPIRE_DATE:yyyy-MM-dd HH:mm} ({party.EXPIRE_DATE.Value.ToDiscordRelativeTimestamp()})";
        }

        await Services.UpdateMessage(modal, party, false, "");

        var bothMessage = $"시간을 설정하였습니다.\n시작: {startText}\n만료: {expireText}";
        try
        {
            await modal.ModifyOriginalResponseAsync(m =>
            {
                m.Content = bothMessage;
                m.Components = null;
                m.Embeds = null;
            });
        }
        catch
        {
            await modal.FollowupAsync(bothMessage, ephemeral: true);
        }
        _ = Services.RespondMessageWithExpire(modal);
    }



    private async Task ModalAsync(SocketModal modal)
    {
        var customId = modal.Data.CustomId;
        
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
        
        if (messageIdIndex >= parts.Length)
            return;
        
        var partyKey = parts[messageIdIndex];

        await InitCommands(modal, action);

        // 파티 생성 모달: 파티가 아직 없을 수 있어서 별도 처리 (제출 시점에 백그라운드 생성 결과와 합친다)
        if (action == Constant.DATE_CREATE_KEY)
        {
            await CreatePartyModalAsync(modal, parts);
            return;
        }

        var partyEntity = await partyService.GetPartyEntityAsync(partyKey);

        var partyClass = new PartyClass();
        await partyClass.Init(partyEntity, modal, Services.client);
        var party = partyClass.Entity;

        var message = "";


        switch (action)
        {
            case Constant.SETTING_MODEL_KEY:
                var renameOk = true;
                var resizeOk = true;
                
                // 입력값 가져오기
                var countInput = modal.Data.Components.FirstOrDefault(c => c.CustomId == "count");
                int teamCount = party.MAX_COUNT_MEMBER;
                if (countInput == null || !int.TryParse(countInput.Value, out teamCount))
                {
                    message += $"인원 오류: 유호한 숫자를 입력해주세요.\n";
                    resizeOk = false;
                }

                if (party.MAX_COUNT_MEMBER != teamCount)
                {
                    // 범위 체크
                    if (teamCount < 1 || teamCount > Constant.MAX_COUNT)
                    {
                        message += $"인원 오류: 파티 인원은 {1}~{Constant.MAX_COUNT} 사이여야 합니다.\n";
                        resizeOk = false;
                    }

                    if (partyClass is { IsOwner: false, IsAdmin: false })
                    {
                        message += $"인원 오류: 파티장 또는 관리자만 인원을 변경할 수 있습니다.\n";
                        resizeOk = false;
                    }

                    if (resizeOk)
                    {
                        await partyService.ResizePartyAsync(party, teamCount);
                        
                        party.MAX_COUNT_MEMBER = teamCount;
                        message += $"인원: 인원을 변경하였습니다.\n";
                    }
                }
                        
                var nameInput = modal.Data.Components.FirstOrDefault(c => c.CustomId == "name");
                var name = nameInput?.Value ?? "";
                if (string.IsNullOrEmpty(name))
                {
                    renameOk = false;
                }

                if (renameOk && name != party.DISPLAY_NAME)
                {
                    if (await partyService.PartyRename(partyKey, name))
                    {
                        message += "제목: 제목을 변경하였습니다.\n";
                        party.DISPLAY_NAME = name;
                    }
                    else
                    {
                        message += "제목 오류: 제목을 변경할 수 없었습니다.\n";
                    }
                }

                if (message == "")
                {
                    message = "설정이 취소되었습니다.";
                }
                await modal.ModifyOriginalResponseAsync(m => m.Content = message);
                _ = Services.RespondMessageWithExpire(modal); 
                break;
            case Constant.DATE_MODAL_KEY:
                var kind = parts.Length > 2 ? parts[2] : "";
                if (kind == Constant.DATE_MODAL_BOTH)
                {
                    await UpdateBothTimesAsync(modal, partyKey, party);
                    break;
                }
                var isStartTime = kind == Constant.START_TIME_OPEN_KEY;
                var timeInput = modal.Data.Components.FirstOrDefault(c => c.CustomId == Constant.DATE_INPUT_FIELD)?.Value ?? "";

                if (!DateTimeParser.TryParse(timeInput, DateTime.Now, out var parsedTime, out var isClear, out var parseError))
                {
                    var retryButtons = BuildRetryButtons(partyKey);
                    await modal.ModifyOriginalResponseAsync(m =>
                    {
                        m.Content = $"{parseError}\n{DateTimeParser.HelpText}";
                        m.Components = retryButtons;
                    });
                    _ = Services.RespondMessageWithExpire(modal);
                    return;
                }

                bool timeOk;
                if (isClear)
                    timeOk = isStartTime ? await partyService.ClearStartDate(partyKey) : await partyService.ClearExpireDate(partyKey);
                else
                    timeOk = isStartTime ? await partyService.SetStartDate(partyKey, parsedTime) : await partyService.SetExpireDate(partyKey, parsedTime);

                if (!timeOk)
                {
                    await modal.ModifyOriginalResponseAsync(m => m.Content = "날짜 설정에 실패하였습니다.");
                    _ = Services.RespondMessageWithExpire(modal);
                    return;
                }

                if (isClear)
                {
                    if (isStartTime)
                        party.START_DATE = null;
                    else
                        party.EXPIRE_DATE = null;
                    message = isStartTime ? "시작 시간을 삭제하였습니다." : "만료 시간을 삭제하였습니다.";
                }
                else
                {
                    if (isStartTime)
                        party.START_DATE = parsedTime;
                    else
                        party.EXPIRE_DATE = parsedTime;
                    message = isStartTime
                        ? $"시작 날짜를 설정하였습니다: {parsedTime:yyyy-MM-dd HH:mm} ({parsedTime.ToDiscordRelativeTimestamp()})"
                        : $"만료 날짜를 설정하였습니다: {parsedTime:yyyy-MM-dd HH:mm} ({parsedTime.ToDiscordRelativeTimestamp()})";
                }

                try
                {
                    await modal.ModifyOriginalResponseAsync(m =>
                    {
                        m.Content = message;
                        m.Components = null;
                        m.Embeds = null;
                    });
                }
                catch
                {
                    // 원본 응답 수정이 실패하면(웹훅 만료 등) 후속 메시지로 결과 전달
                    await modal.FollowupAsync(message, ephemeral: true);
                }
                _ = Services.RespondMessageWithExpire(modal);
                break;
            case Constant.TEAM_KEY:
                countInput = modal.Data.Components.FirstOrDefault(c => c.CustomId == "count");

                if (countInput == null || !int.TryParse(countInput.Value, out teamCount))
                {
                    await modal.ModifyOriginalResponseAsync(m => m.Content = "숫자가 유효하지 않습니다.");
                    _ = Services.RespondMessageWithExpire(modal);
                    return;
                }

                var range = (int)MathF.Min(party.MAX_COUNT_MEMBER, party.Members.Count);
                var partyMemberEntities = party.Members[..range];

                if (Math.Min(partyMemberEntities.Count, 10) < teamCount)
                {
                    await modal.ModifyOriginalResponseAsync(m => m.Content = "멤버 인원 또는 10개보다 팀이 많을 수 없습니다!");
                    _ = Services.RespondMessageWithExpire(modal);
                    return;
                }
                
                var mg = await modal.Channel.SendMessageAsync("초기화 중...");
                await teamTool.Random(partyClass, party, mg, countInput.Value);
                if (modal.HasResponded)
                {
                    await modal.DeleteOriginalResponseAsync();
                }
                else
                {
                    await modal.RespondAsync("생성하였습니다", ephemeral: true);
                    _ = Services.RespondMessageWithExpire(modal);
                }
                
                return;
        }
        
        
        await Services.UpdateMessage(modal, party, false, "");
    }
}