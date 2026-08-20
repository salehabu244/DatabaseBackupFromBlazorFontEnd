using System.Diagnostics;
using DatabaseBackupRestore.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace DatabaseBackupRestore.Services;

public class RestoreService
{
    private readonly AppSettings _settings;
    private readonly BackupHistoryStore _history;
    private readonly ILogger<RestoreService> _logger;

    public RestoreService(
        IOptions<AppSettings> settings,
        BackupHistoryStore history,
        ILogger<RestoreService> logger)
    {
        _settings = settings.Value;
        _history = history;
        _logger = logger;
    }

    public async Task<RestoreResult> RestoreDatabaseAsync(
        RestoreRequest request, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var result = new RestoreResult
        {
            DestinationDatabaseName = request.DestinationDatabaseName,
            BackupFile = request.BackupFile
        };

        try
        {
            if (string.IsNullOrWhiteSpace(request.BackupFile))
                throw new InvalidOperationException("Backup file is required.");
            if (!File.Exists(request.BackupFile))
                throw new FileNotFoundException("Backup file not found.", request.BackupFile);
            if (string.IsNullOrWhiteSpace(request.DestinationDatabaseName))
                throw new InvalidOperationException("Destination database name is required.");

            var header = await ReadBackupHeaderAsync(request.BackupFile, cancellationToken).ConfigureAwait(false);

            await using var connection = new SqlConnection(_settings.ConnectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            // Build MOVE clauses so the data/log files are placed on the server with the new DB name.
            var moveClauses = string.Join(", ",
                header.LogicalNames.Select(name =>
                    $"MOVE N'{name.LogicalName}' TO N'{BuildTargetPath(name.PhysicalName, request.DestinationDatabaseName)}'"));

            // If overwriting and the DB exists, drop active connections by setting it single-user first.
            if (request.OverwriteExisting && await DatabaseExistsAsync(connection, request.DestinationDatabaseName, cancellationToken).ConfigureAwait(false))
            {
                await ExecuteNonQueryAsync(connection,
                    $"ALTER DATABASE [{request.DestinationDatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;",
                    cancellationToken).ConfigureAwait(false);
            }

            var replaceClause = request.OverwriteExisting ? ", REPLACE" : string.Empty;
            var sql = $"RESTORE DATABASE [{request.DestinationDatabaseName}] " +
                      $"FROM DISK = N'{request.BackupFile.Replace("'", "''")}' " +
                      $"WITH {moveClauses}{replaceClause};";

            _logger.LogInformation("Starting restore: {Sql}", sql);

            await using (var cmd = new SqlCommand(sql, connection) { CommandTimeout = 0 })
            {
                await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            sw.Stop();
            result.Success = true;
            result.Duration = sw.Elapsed;
            result.RestoreSql = sql;

            _history.Add(new BackupHistory
            {
                DatabaseName = request.DestinationDatabaseName,
                BackupFile = request.BackupFile,
                Status = BackupStatus.Success,
                Detail = $"Restore completed in {result.Duration.TotalSeconds:0.##}s",
                IsRestore = true
            });

            _logger.LogInformation("Restore of {Database} completed in {Duration}s",
                request.DestinationDatabaseName, result.Duration.TotalSeconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "Restore of {Database} failed", request.DestinationDatabaseName);
            result.Success = false;
            result.ErrorMessage = ex.Message;
            result.Duration = sw.Elapsed;

            _history.Add(new BackupHistory
            {
                DatabaseName = request.DestinationDatabaseName,
                BackupFile = request.BackupFile,
                Status = BackupStatus.Failed,
                Detail = ex.Message,
                IsRestore = true
            });
        }

        return result;
    }

    private async Task<BackupFileHeader> ReadBackupHeaderAsync(
        string backupFile, CancellationToken cancellationToken)
    {
        var header = new BackupFileHeader();
        await using var connection = new SqlConnection(_settings.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        // filelistonly returns LogicalName / PhysicalName rows for each data/log file in the backup.
        await using var cmd = new SqlCommand(
            $"RESTORE FILELISTONLY FROM DISK = N'{backupFile.Replace("'", "''")}';", connection)
        { CommandTimeout = 0 };
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            header.LogicalNames.Add(new BackupFileEntry
            {
                LogicalName = reader.GetString(reader.GetOrdinal("LogicalName")),
                PhysicalName = reader.GetString(reader.GetOrdinal("PhysicalName"))
            });
        }

        if (header.LogicalNames.Count == 0)
        {
            throw new InvalidOperationException(
                "Could not read file list from the backup. The file may be invalid or empty.");
        }

        return header;
    }

    private static string BuildTargetPath(string physicalName, string destinationDatabaseName)
    {
        var dir = Path.GetDirectoryName(physicalName) ?? string.Empty;
        var file = Path.GetFileName(physicalName);
        var ext = Path.GetExtension(physicalName);
        var stem = Path.GetFileNameWithoutExtension(physicalName);

        // Replace the original DB name token when present (e.g. MyDb -> MyDb_restored).
        var newFile = $"{stem}_{destinationDatabaseName}{ext}";
        return string.IsNullOrEmpty(dir) ? newFile : Path.Combine(dir, newFile);
    }

    private static async Task<bool> DatabaseExistsAsync(
        SqlConnection connection, string dbName, CancellationToken cancellationToken)
    {
        await using var cmd = new SqlCommand(
            "SELECT COUNT(*) FROM sys.databases WHERE name = @n", connection);
        cmd.Parameters.AddWithValue("@n", dbName);
        var count = (int)(await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) ?? 0);
        return count > 0;
    }

    private static async Task ExecuteNonQueryAsync(
        SqlConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var cmd = new SqlCommand(sql, connection);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}

public class RestoreResult
{
    public string DestinationDatabaseName { get; set; } = string.Empty;
    public string BackupFile { get; set; } = string.Empty;
    public bool Success { get; set; }
    public TimeSpan Duration { get; set; }
    public string? ErrorMessage { get; set; }
    public string? RestoreSql { get; set; }
}

internal class BackupFileHeader
{
    public List<BackupFileEntry> LogicalNames { get; } = new();
}

internal class BackupFileEntry
{
    public string LogicalName { get; set; } = string.Empty;
    public string PhysicalName { get; set; } = string.Empty;
}