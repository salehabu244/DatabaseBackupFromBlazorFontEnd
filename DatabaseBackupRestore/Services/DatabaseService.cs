using DatabaseBackupRestore.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace DatabaseBackupRestore.Services;

public class DatabaseService
{
    private static readonly HashSet<string> SystemDatabases = new(StringComparer.OrdinalIgnoreCase)
    {
        "master", "model", "msdb", "tempdb"
    };

    private readonly AppSettings _settings;
    private readonly ILogger<DatabaseService> _logger;

    public DatabaseService(IOptions<AppSettings> settings, ILogger<DatabaseService> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    public string ConnectionString => _settings.ConnectionString;
    public string ServerName => _settings.SqlServerName;
    public async Task<ConnectionTestResult> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        var result = new ConnectionTestResult { ServerName = _settings.SqlServerName };
        try
        {
            await using var connection = new SqlConnection(_settings.ConnectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await using var versionCmd = new SqlCommand("SELECT @@VERSION", connection);
            result.ServerVersion = (string?)await versionCmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);

            await using var countCmd = new SqlCommand(
                "SELECT COUNT(*) FROM sys.databases WHERE database_id > 4 AND state = 0", connection);
            result.DatabaseCount = (int)(await countCmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) ?? 0);

            result.TotalDatabaseSize = await GetTotalUserDatabaseSizeAsync(connection, cancellationToken)
                .ConfigureAwait(false);
            result.IsConnected = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to SQL Server {Server}", _settings.SqlServerName);
            result.IsConnected = false;
            result.ErrorMessage = ex.Message;
        }

        return result;
    }
    public async Task<IReadOnlyList<DatabaseInfo>> GetUserDatabasesAsync(CancellationToken cancellationToken = default)
    {
        var list = new List<DatabaseInfo>();
        await using var connection = new SqlConnection(_settings.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        const string sql = @"
SELECT  d.name,
        CAST(SUM(mf.size) * 8.0 / 1024 AS DECIMAL(18, 2)) AS SizeMb,
        (
            SELECT TOP (1) bs.backup_finish_date
            FROM msdb.dbo.backupset bs
            WHERE bs.database_name = d.name
            ORDER BY bs.backup_finish_date DESC
        ) AS LastBackup
FROM    sys.databases d
LEFT JOIN sys.master_files mf ON mf.database_id = d.database_id
WHERE   d.database_id > 4
  AND   d.state = 0
GROUP BY d.name
ORDER BY d.name;";

        await using var cmd = new SqlCommand(sql, connection);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var name = reader.GetString(0);
            if (SystemDatabases.Contains(name))
            {
                continue;
            }

            list.Add(new DatabaseInfo
            {
                Name = name,
                SizeMb = reader.IsDBNull(1) ? 0 : Convert.ToDouble(reader.GetValue(1)),
                LastBackup = reader.IsDBNull(2) ? null : reader.GetDateTime(2)
            });
        }

        return list;
    }
    private static async Task<string> GetTotalUserDatabaseSizeAsync(
        SqlConnection connection, CancellationToken cancellationToken)
    {
        await using var cmd = new SqlCommand(
            "SELECT CAST(SUM(mf.size) * 8.0 / 1024 AS DECIMAL(18, 2)) " +
            "FROM sys.master_files mf JOIN sys.databases d ON d.database_id = mf.database_id " +
            "WHERE d.database_id > 4", connection);

        var raw = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        var mb = raw == null || raw == DBNull.Value ? 0d : Convert.ToDouble(raw);
        return mb >= 1024
            ? $"{mb / 1024:0.##} GB"
            : $"{mb:0.##} MB";
    }
}

public class DatabaseInfo
{
    public string Name { get; set; } = string.Empty;
    public double SizeMb { get; set; }
    public DateTime? LastBackup { get; set; }

    public string DisplaySize => SizeMb >= 1024 ? $"{SizeMb / 1024:0.##} GB" : $"{SizeMb:0.##} MB";

    public string LastBackupDisplay =>
        LastBackup.HasValue
            ? LastBackup.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
            : "Never";
}