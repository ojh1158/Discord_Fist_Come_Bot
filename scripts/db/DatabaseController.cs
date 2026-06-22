using System.Data;
using System.Reflection;
using Dapper;
using DiscordBot.scripts.config;
using DiscordBot.scripts.src;
using MySqlConnector;
using Serilog;

namespace DiscordBot.scripts.db;

public class DatabaseController : IDisposable, ISingleton
{
    private static string _connectionString = string.Empty;
    private MySqlConnection? _connection;

    public static async Task Init(Config config)
    {
        if (config.Test.Enable && !string.IsNullOrEmpty(config.Database?.TestConnectionString))
            _connectionString = config.Database.TestConnectionString;
        else if (!string.IsNullOrEmpty(config.Database?.ConnectionString))
            _connectionString = config.Database.ConnectionString;

        // BINARY(16) <-> Guid 변환 핸들러 등록
        SqlMapper.AddTypeHandler(new GuidBinaryHandler());

       
        await EnsureDatabaseExistsAsync(_connectionString);

        try
        {
            // DB 연결 테스트
            var testConnection = new MySqlConnection(_connectionString);
            await testConnection.OpenAsync();
            await testConnection.CloseAsync();
        }
        catch (MySqlException ex) when (ex.Number == -2)
        {
            throw new Exception("DB 연결 시간이 초과되었습니다. 서버 상태와 네트워크를 확인하세요.");
        }
        catch (MySqlException ex)
        {
            throw new Exception($"MySQL 오류({ex.Number}): DB 연결에 실패하였습니다. 서버 주소, 포트, 인증 정보를 확인하세요.");
        }
        catch (TimeoutException)
        {
            throw new Exception("DB 연결 시간이 초과되었습니다. 서버 상태와 네트워크를 확인하세요.");
        }
        catch (Exception)
        {
            throw new Exception("알 수 없는 이유로 DB 연결에 실패하였습니다.");
        }
    }

    private static async Task EnsureDatabaseExistsAsync(string connectionString)
    {
        var builder = new MySqlConnectionStringBuilder(connectionString);
        var databaseName = builder.Database;

        if (string.IsNullOrEmpty(databaseName))
        {
            throw new Exception("데이터베이스 이름을 설정하지 않았습니다.");
        }

        var masterBuilder = new MySqlConnectionStringBuilder(connectionString)
        {
            Database = string.Empty
        };
        
        await using var masterConnection = new MySqlConnection(masterBuilder.ConnectionString);
        await masterConnection.OpenAsync();

        var checkSql = "SELECT COUNT(*) FROM INFORMATION_SCHEMA.SCHEMATA WHERE SCHEMA_NAME = @db";
        var exists = (long)(await masterConnection.ExecuteScalarAsync(checkSql, new { db = databaseName }) ?? throw new InvalidOperationException());

        if (exists == 0)
        {
            await masterConnection.ExecuteAsync($"CREATE DATABASE `{databaseName}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci");
            Log.Information($"DB '{databaseName}' 생성 완료");
        }
    }

    /// <summary>
    /// BINARY(16)과 Guid 간 변환을 처리하는 Dapper 타입 핸들러
    /// </summary>
    private class GuidBinaryHandler : SqlMapper.TypeHandler<Guid>
    {
        public override void SetValue(IDbDataParameter parameter, Guid value)
        {
            parameter.Value = value.ToByteArray();
            parameter.DbType = DbType.Binary;
        }

        public override Guid Parse(object? value)
        {
            if (value == null || value == DBNull.Value)
            {
                return Guid.Empty;
            }

            if (value is byte[] bytes)
            {
                if (bytes.Length == 16)
                {
                    return new Guid(bytes);
                }

                throw new InvalidCastException(
                    $"Cannot convert byte array of length {bytes.Length} to Guid (expected 16 bytes)");
            }

            throw new InvalidCastException($"Cannot convert {value.GetType()} to Guid");
        }
    }

    public async Task<MySqlConnection> GetConnectionAsync()
    {
        // 매번 새로운 연결 객체를 생성하여 반환합니다.
        var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();
        return connection;
    }

    /// <summary>
    /// DB 작업을 트랜잭션 내에서 실행 (반환값 있음)
    /// Connection과 Transaction을 자동으로 제공하고 Lock 적용
    /// 성공 시 자동 Commit, 실패 시 자동 Rollback
    /// </summary>
    public async Task<T?> ExecuteInTransactionAsync<T>(Func<MySqlConnection, MySqlTransaction, Task<T?>> action)
    {
        // 각 트랜잭션을 구분하기 위한 고유 ID 생성
        string txId = Guid.NewGuid().ToString()[..8];
        var startTime = DateTime.Now;

        try
        {
            // Log.Debug($"[TX-{txId}] 연결 시도 중...");
            await using var connection = await GetConnectionAsync();

            // Log.Debug($"[TX-{txId}] 트랜잭션 시작 중...");
            await using var transaction = await connection.BeginTransactionAsync();

            try
            {
                // Log.Information($"[TX-{txId}] 로직 실행 시작 (T: {typeof(T).Name})");

                var result = await action.Invoke(connection, transaction);

                // Log.Debug($"[TX-{txId}] 커밋 시도 중...");
                await transaction.CommitAsync();

                var duration = DateTime.Now - startTime;
                // Log.Information($"[TX-{txId}] 성공 및 커밋 완료 ({duration.TotalMilliseconds}ms)");
                return result;
            }
            catch (Exception ex)
            {
                Log.Warning($"[TX-{txId}] 로직 오류 발생! 롤백 진행. 메시지: {ex.Message}");
                await transaction.RollbackAsync();

                // 데드락 관련 예외인지 체크
                if (ex is MySqlException { Number: 1213 })
                {
                    Log.Fatal($"🚨 [TX-{txId}] 데드락(Deadlock) 감지됨!");
                }

                throw; // 상위 catch에서 처리하도록 던짐
            }
        }
        catch (Exception ex)
        {
            Log.Fatal($"[TX-{txId}] 트랜잭션 작업 중 최종 예외 발생: {ex.Message}\n{ex.StackTrace}");
            throw;
        }
    }

    public async Task<T?> ExecuteAsync<T>(Func<MySqlConnection, Task<T?>> action)
    {
        try
        {
            // 1. 연결을 생성하고 사용 후 자동으로 닫히도록 await using 사용
            await using var connection = await GetConnectionAsync();

            // 2. 전달받은 로직 실행
            return await action.Invoke(connection);
        }
        catch (Exception ex)
        {
            Log.Fatal($"[ExecuteAsync Fatal] {ex.Message}");
            throw;
        }
    }

    public void Dispose()
    {
        DisposeDatabase();
    }

    public void DisposeDatabase()
    {
        _connection?.Dispose();
    }
}