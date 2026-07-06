using System.Reflection;
using Camille.RiotGames;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using DiscordBot.scripts.config;
using DiscordBot.scripts.db;
using DiscordBot.scripts.db.DB_SETUP;
using DiscordBot.scripts.src;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Quartz;
using Serilog;
using Serilog.Events;

namespace DiscordBot.scripts;

public class App
{
    private static Config? _config = null; 
    static async Task<int> Main(string[] args)
    {
        try
        {
            await Start(args);
        }
        catch (Exception e)
        {
            Log.Fatal(_config is { Debug.ViewStackTrace : true } ? $"{e.Message}\n{e.StackTrace}" : $"{e.Message}");
            return 0;
        }

        return 1;
    }
    
    static async Task Start(string[] args)
    {
         LogConfig.Init();
        
         var config = ConfigLoader.GetConfig();
         _config = config;
         
         ConfigLoader.Update();

         await DatabaseController.Init(config);
         
         // .NET 9.0 최신 방식: HostApplicationBuilder 사용
         var builder = Host.CreateApplicationBuilder(args);

         var discordConfig = new DiscordSocketConfig
          {
             GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.MessageContent,
             LogLevel = LogSeverity.Info,
             MessageCacheSize = 1000,
          };

        var assembly = Assembly.GetExecutingAssembly();

        var serviceTypes = assembly.GetTypes()
            .Where(type =>
                type.IsClass &&
                !type.IsAbstract &&
                !type.IsInterface &&
                typeof(ISingleton).IsAssignableFrom(type) &&
                type.Namespace != null)
            .ToList();

         var serviceCollection = builder.Services;
         serviceCollection.AddSingleton(config);
         
         serviceCollection.AddSingleton(new DiscordSocketClient(discordConfig));

         // 3. 💡 [중요] 라이엇 API를 명확하게 싱글톤으로 먼저 등록!
         // if (config.Riot.Enable)
         // {
         //     serviceCollection.AddSingleton<RiotGamesApi>(provider => RiotGamesApi.NewInstance(config.Riot.ApiKey));
         // }

         serviceCollection
            .AddQuartz()
            .AddLogging(configure =>
            {
                configure.AddFilter("Quartz", LogLevel.Error);
                configure.AddFilter("Microsoft", LogLevel.Error);
            })
            .AddQuartzHostedService(q => q.WaitForJobsToComplete = true);
            
         foreach (var serviceType in serviceTypes)
         {
             serviceCollection.AddSingleton(serviceType);
         }
         
         var host = builder.Build();
         
         foreach (var serviceType in serviceTypes)
         {
             host.Services.GetRequiredService(serviceType);
         }

         // 호스트 빌드
        
          Log.Information("DB 업데이트 진행 시작");
          var setupTypes = Assembly.GetExecutingAssembly().GetTypes()
              .Where(t => typeof(IDbSetup).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

          foreach (var type in setupTypes)
          {
              // 2. 인스턴스 생성 (이제 생성자 인자가 없으므로 Activator로 쉽게 생성 가능)
              if (Activator.CreateInstance(type) is IDbSetup setupTarget)
              {
                  Log.Information($"{setupTarget.ReturnTableName()} 테이블 업데이트 중");
                  try
                  {
                      await DbSetup.Instant.SetupAsync(setupTarget);
                      Log.Information($"{setupTarget.ReturnTableName()} 테이블 업데이트 완료!");
                  }
                  catch (Exception ex)
                  {
                      Log.Fatal($"❌ MySQL 연결에 실패했습니다. 데이터베이스가 실행 중인지, 연결 문자열이 올바른지 확인하세요.\n{ex.Message}");
                      return;
                  }
              }
          }
          Log.Information("DB 업데이트 종료");

         // Quartz 스케줄러 시작
         if (!config.Test.Enable || config.Test is { Enable: true, UseScheduler: true })
         {
             _ = Task.Run(async () =>
             {
                 var schedulerFactory = host.Services.GetRequiredService<ISchedulerFactory>();
                 var scheduler = await schedulerFactory.GetScheduler();
                 
                 // Job 정의
                 var job = JobBuilder.Create<CycleJob>()
                     .WithIdentity("cycleJob", "party")
                     .Build();

                 // Trigger 정의 (매 분마다 실행)
                 var trigger = TriggerBuilder.Create()
                     .WithIdentity("cycleTrigger", "party")
                     .WithCronSchedule("0 * * * * ?") // 매 분 0초에 실행
                     .StartNow()
                     .Build();

                 // 스케줄 등록
                 await scheduler.ScheduleJob(job, trigger);
                 await scheduler.Start();

                 Log.Information("[Cycle] Quartz 스케줄러가 시작되었습니다. (매 분마다 실행)");
             });
         }
         
         // 프로그램이 종료되지 않도록 대기
         await host.RunAsync();
    }
}


