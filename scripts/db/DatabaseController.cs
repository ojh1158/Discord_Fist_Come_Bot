using System.Data;
using System.Reflection;
using Dapper;
using MySqlConnector;

namespace DiscordBot.scripts.db;

public class DatabaseController : IDisposable
{
    private static string _connectionString = string.Empty;
    private static MySqlConnection? _connection;

    // 전역 DB Lock (모든 DB 접근을 순차적으로 제어)
    private static readonly SemaphoreSlim _dbLock = new SemaphoreSlim(1, 1);

    public static void Init()
    {
        _connectionString = Environment.GetEnvironmentVariable("DATABASE__CONNECTIONSTRING") 
            ?? throw new InvalidOperationException("DATABASE__CONNECTIONSTRING 환경변수가 설정되지 않았습니다.");
        
        // BINARY(16) <-> Guid 변환 핸들러 등록
        SqlMapper.AddTypeHandler(new GuidBinaryHandler());
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

        public override Guid Parse(object value)
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
                throw new InvalidCastException($"Cannot convert byte array of length {bytes.Length} to Guid (expected 16 bytes)");
            }
            
            throw new InvalidCastException($"Cannot convert {value.GetType()} to Guid");
        }
    }

    public static async Task<MySqlConnection> GetConnectionAsync()
    {
        // 매번 새로운 연결 객체를 생성하여 반환합니다.
        var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();
        return connection;
    }
    
    /// <summary>
    /// DB 작업을 Lock 내에서 실행 (반환값 있음)
    /// Connection을 자동으로 제공하고 Lock 적용
    /// </summary>
    public static async Task<T> ExecuteAsync<T>(Func<MySqlConnection, Task<T>> action)
    {
        await _dbLock.WaitAsync();
        try
        {
            var connection = await GetConnectionAsync();
            return await action(connection);
        }
        finally
        {
            _dbLock.Release();
        }
    }
    
    /// <summary>
    /// DB 작업을 Lock 내에서 실행 (반환값 없음)
    /// Connection을 자동으로 제공하고 Lock 적용
    /// </summary>
    public static async Task ExecuteAsync(Func<MySqlConnection, Task> action)
    {
        await _dbLock.WaitAsync();
        try
        {
            var connection = await GetConnectionAsync();
            await action(connection);
        }
        finally
        {
            _dbLock.Release();
        }
    }
    
    /// <summary>
    /// DB 작업을 트랜잭션 내에서 실행 (반환값 있음)
    /// Connection과 Transaction을 자동으로 제공하고 Lock 적용
    /// 성공 시 자동 Commit, 실패 시 자동 Rollback
    /// </summary>
    public static async Task<T?> ExecuteInTransactionAsync<T>(Func<MySqlConnection, MySqlTransaction, Task<T?>> action)
    {
        await _dbLock.WaitAsync();
        try
        {
            var connection = await GetConnectionAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            
            try
            {
                var result = await action.Invoke(connection, transaction);
                await transaction.CommitAsync();
                return result;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                Console.WriteLine(ex.Message);
                return default;
            }
        }
        finally
        {
            _dbLock.Release();
        }
    }

    /// <summary>
    /// 파라미터화된 비쿼리 실행 (INSERT, UPDATE, DELETE)
    /// </summary>
    public static async Task<int> ExecuteNonQueryAsync(string sql, Dictionary<string, object>? parameters = null)
    {
        var connection = await GetConnectionAsync();
        using var command = new MySqlCommand(sql, connection);
        
        if (parameters != null)
        {
            foreach (var param in parameters)
            {
                command.Parameters.AddWithValue(param.Key, param.Value);
            }
        }
        
        return await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// 스칼라 값 조회 (COUNT, SUM 등)
    /// </summary>
    public static async Task<object?> ExecuteScalarAsync(string sql, Dictionary<string, object>? parameters = null)
    {
        var connection = await GetConnectionAsync();
        using var command = new MySqlCommand(sql, connection);
        
        if (parameters != null)
        {
            foreach (var param in parameters)
            {
                command.Parameters.AddWithValue(param.Key, param.Value);
            }
        }
        
        return await command.ExecuteScalarAsync();
    }

    /// <summary>
    /// 비파라미터 쿼리 실행 (하위 호환성 유지)
    /// </summary>
    public static async Task NonQuery(string sql)
    {
        await ExecuteNonQueryAsync(sql);
    }

    /// <summary>
    /// SELECT 쿼리 결과를 제네릭 타입 리스트로 반환
    /// </summary>
    public static async Task<List<T>> Query<T>(string sql, Dictionary<string, object>? parameters = null) where T : new()
    {
        var connection = await GetConnectionAsync();
        var result = new List<T>();

        using var command = new MySqlCommand(sql, connection);
        
        if (parameters != null)
        {
            foreach (var param in parameters)
            {
                command.Parameters.AddWithValue(param.Key, param.Value);
            }
        }
        
        using var reader = await command.ExecuteReaderAsync();

        var properties = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance);

        while (await reader.ReadAsync())
        {
            var item = new T();
            foreach (var prop in properties)
            {
                try
                {
                    var ordinal = reader.GetOrdinal(prop.Name);
                    if (!reader.IsDBNull(ordinal))
                    {
                        var value = reader.GetValue(ordinal);
                        
                        // 타입 변환 처리 개선
                        if (prop.PropertyType == typeof(bool) && value is sbyte sbyteValue)
                        {
                            prop.SetValue(item, sbyteValue != 0);
                        }
                        else if (prop.PropertyType == typeof(bool?) && value is sbyte nullableSbyteValue)
                        {
                            prop.SetValue(item, nullableSbyteValue != 0);
                        }
                        else
                        {
                            prop.SetValue(item, Convert.ChangeType(value, Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType));
                        }
                    }
                }
                catch
                {
                    // 컬럼이 없거나 타입 변환 실패 시 무시
                }
            }
            result.Add(item);
        }

        return result;
    }

    /// <summary>
    /// 단일 결과를 제네릭 타입으로 반환
    /// </summary>
    public static async Task<T?> QuerySingle<T>(string sql, Dictionary<string, object>? parameters = null) where T : new()
    {
        var results = await Query<T>(sql, parameters);
        return results.FirstOrDefault();
    }
    
    public void Dispose()
    {
        DisposeDatabase();
    }

    public static void DisposeDatabase()
    {
        _connection?.Dispose();
    }
    
    public class QueryResult<T>
    {
        public T? Value;
        public bool IsError;
        public string? Error;

        public void Result(Action<T> action, Action<string> error)
        {
            if (IsError && Value != null)
            {
                action.Invoke(Value);
            }
            else
            {
                error.Invoke(Error ?? string.Empty);
            }
        }
    }
}
