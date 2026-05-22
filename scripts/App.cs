using System.Reflection;
using Discord;
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

namespace DiscordBot.scripts;

public class App
{
    public static bool IsTest = false;
    static async Task<int> Main(string[] args)
    {
         IsTest = args is ["test", ..];
         
         var configPath = Path.Combine(AppContext.BaseDirectory, "config.json");
         var config = File.Exists(configPath) 
             ? System.Text.Json.JsonSerializer.Deserialize<ConfigClass>(await File.ReadAllTextAsync(configPath))
             : new ConfigClass();

         if (config is null)
         {
             Log.Error("Failed to load config.json");
             return 0;
         }
         
         LogConfig.Init(config);
         DatabaseController.Init(config);
        
        // .NET 9.0 최신 방식: HostApplicationBuilder 사용
         var builder = Host.CreateApplicationBuilder(args);

         var discordConfig = new DiscordSocketConfig
          {
             GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.MessageContent,
             LogLevel = LogSeverity.Info
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
         serviceCollection
              .AddSingleton(new DiscordSocketClient(discordConfig))
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
                await DbSetup.Instant.SetupAsync(setupTarget);
                Log.Information($"{setupTarget.ReturnTableName()} 테이블 업데이트 완료!");
            }
        }
        Log.Information("DB 업데이트 종료");

        // Quartz 스케줄러 시작 (테스트 모드가 아닐 때만)
        if (!IsTest)
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
        return 0;
    }
}


