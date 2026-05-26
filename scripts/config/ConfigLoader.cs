using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using Serilog;

namespace DiscordBot.scripts.config;

public class ConfigLoader
{
    private static readonly Lazy<(Config config, string filePath)> _loaded = new(LoadConfig);

    public static Config GetConfig() => _loaded.Value.config;

    public static void Update()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        var jsonString = JsonSerializer.Serialize(_loaded.Value.config, options);
        File.WriteAllText(_loaded.Value.filePath, jsonString);
    }

    private static (Config config, string filePath) LoadConfig()
    {
        var configPath = "config.json";

        if (!File.Exists(configPath))
        {
            CreateDefaultConfig(configPath);
            Log.Information("⚠ config.json 파일이 새로 생성되었습니다.");
            Log.Information("아래 항목들을 수정한 후 프로그램을 다시 실행하세요:");
            Log.Information("  - Discord.Token : 디스코드 봇 토큰");
            Log.Information("  - Database.ConnectionString : MySQL, MariaDB 연결 문자열");
            Environment.Exit(0);
        }

        var jsonContent = File.ReadAllText(configPath);
        
        CheckNewFields(jsonContent);
        
        var config = JsonSerializer.Deserialize<Config>(jsonContent, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (config == null)
        {
            throw new InvalidOperationException("설정 파일 역직렬화에 실패하였습니다.");
        }

        if (string.IsNullOrWhiteSpace(config.Discord.Token))
        {
            throw new InvalidOperationException("설정 오류: config.json에 Discord Token이 설정되지 않았습니다.");
        }

        if (string.IsNullOrWhiteSpace(config.Database.ConnectionString) && string.IsNullOrWhiteSpace(config.Database.TestConnectionString))
        {
            throw new InvalidOperationException("설정 오류: config.json에 데이터베이스 연결 문자열이 설정되지 않았습니다.");
        }

        return (config, Path.GetFullPath(configPath));
    }

    private static void CheckNewFields(string jsonContent)
    {
        using var doc = JsonDocument.Parse(jsonContent);
        var existingKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        
        void ExtractKeys(JsonElement element)
        {
            foreach (var prop in element.EnumerateObject())
            {
                existingKeys.Add(prop.Name);
                if (prop.Value.ValueKind == JsonValueKind.Object || prop.Value.ValueKind == JsonValueKind.Array)
                {
                    ExtractKeys(prop.Value);
                }
            }
        }
        
        ExtractKeys(doc.RootElement);

        var type = typeof(Config);
        var missingFields = new List<string>();
        
        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!existingKeys.Contains(prop.Name) && prop.PropertyType.IsClass)
            {
                foreach (var subProp in prop.PropertyType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    missingFields.Add($"{prop.Name}.{subProp.Name}");
                }
            }
        }

        if (missingFields.Count > 0)
        {
            var defaultConfig = new Config();
            var mergedConfig = MergeConfig(JsonSerializer.Deserialize<Config>(jsonContent, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!, defaultConfig);
            
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            File.WriteAllText("config.json", JsonSerializer.Serialize(mergedConfig, options));

            Log.Information("⚠ 새로운 설정 필드가 추가되어 config.json에 기본값을 반영했습니다:");
            foreach (var field in missingFields)
            {
                Log.Information($"  - {field}");
            }
            Log.Information("필요시 값을 수정한 후 프로그램을 다시 실행하세요.");
            Environment.Exit(0);
        }
    }

    private static Config MergeConfig(Config existing, Config defaults)
    {
        var type = typeof(Config);
        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!prop.PropertyType.IsClass) continue;
            
            var existingValue = prop.GetValue(existing);
            var defaultValue = prop.GetValue(defaults);
            
            if (existingValue == null && defaultValue != null)
            {
                prop.SetValue(existing, defaultValue);
            }
            else if (existingValue != null && defaultValue != null)
            {
                foreach (var subProp in prop.PropertyType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    var existingSub = subProp.GetValue(existingValue);
                    var defaultSub = subProp.GetValue(defaultValue);
                    
                    if (!HasValue(existingSub, subProp.PropertyType))
                    {
                        subProp.SetValue(existingValue, defaultSub);
                    }
                }
            }
        }
        return existing;
    }

    private static bool HasValue(object? value, Type type)
    {
        if (value == null) return false;
        if (type == typeof(string)) return !string.IsNullOrEmpty((string)value);
        if (type.IsValueType)
        {
            var defaultValue = Activator.CreateInstance(type);
            return !value.Equals(defaultValue);
        }
        return true;
    }

    private static void CreateDefaultConfig(string path)
    {
        var defaultConfig = new Config();
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
        File.WriteAllText(path, JsonSerializer.Serialize(defaultConfig, options));
    }
}