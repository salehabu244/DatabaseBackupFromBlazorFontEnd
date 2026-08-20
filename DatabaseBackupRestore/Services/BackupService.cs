using System.Diagnostics;
using DatabaseBackupRestore.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace DatabaseBackupRestore.Services;
public class BackupService
{
    private readonly AppSettings _settings;
    private readonly BackupHistoryStore _history;
    private readonly ILogger<BackupService> _logger;

    public BackupService(
        IOptions<AppSettings> settings,
        BackupHistoryStore history,
        ILogger<BackupService> logger)
    {
        _settings = settings.Value;
        _history = history;
        _logger = logger;
    }
    public static string GenerateFileName(string databaseName)
    {
        var safe = string.Concat(databaseName
            .Where(c => !char.IsWhiteSpace(c) && char.IsLetterOrDigit(c)));
        return $"{safe}_{DateTime.Now:yyyyMMdd_HHmmss}.bak";
    }

    public async Task<BackupResult> BackupDatabaseAsync(
        BackupRequest request, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var result = new BackupResult { DatabaseName = request.DatabaseName };

        try
        {
            if (string.IsNullOrWhiteSpace(request.DatabaseName))
                throw new InvalidOperationException("Database name is required.");
            if (string.IsNullOrWhiteSpace(request.BackupFolder))
                throw new InvalidOperationException("Backup folder is required.");

            Directory.CreateDirectory(request.BackupFolder);

            var fileName = string.IsNullOrWhiteSpace(request.BackupFileName)
                ? GenerateFileName(request.DatabaseName)
                : request.BackupFileName.Trim();

            if (!fileName.EndsWith(".bak", StringComparison.OrdinalIgnoreCase))
            {
                fileName += ".bak";
            }

            var fullPath = Path.Combine(request.BackupFolder, fileName);
            result.BackupFile = fullPath;

            var compression = request.Compression ? ", COMPRESSION" : string.Empty;
            var sql = $"BACKUP DATABASE [{request.DatabaseName}] " +
                      $"TO DISK = N'{fullPath.Replace("'", "''")}' " +
                      $"WITH INIT, FORMAT{compression};";

            _logger.LogInformation("Starting backup: {Sql}", sql);

            await using var connection = new SqlConnection(_settings.ConnectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await using var cmd = new SqlCommand(sql, connection)
            {
                CommandTimeout = 0
            };
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            sw.Stop();
            result.Success = true;
            result.Duration = sw.Elapsed;

            _history.Add(new BackupHistory
            {
                DatabaseName = request.DatabaseName,
                BackupFile = fullPath,
                Status = BackupStatus.Success,
                Detail = $"Backup completed in {result.Duration.TotalSeconds:0.##}s ({FormatBytes(result.BackupFile)})",
                IsRestore = false
            });

            _logger.LogInformation("Backup of {Database} completed in {Duration}s",
                request.DatabaseName, result.Duration.TotalSeconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "Backup of {Database} failed", request.DatabaseName);
            result.Success = false;
            result.ErrorMessage = ex.Message;
            result.Duration = sw.Elapsed;

            _history.Add(new BackupHistory
            {
                DatabaseName = request.DatabaseName,
                BackupFile = result.BackupFile,
                Status = BackupStatus.Failed,
                Detail = ex.Message,
                IsRestore = false
            });
        }

        return result;
    }

    private static string FormatBytes(string path)
    {
        try
        {
            var fi = new FileInfo(path);
            return fi.Exists ? $"{fi.Length / 1024d / 1024d:0.##} MB" : "unknown size";
        }
        catch
        {
            return "unknown size";
        }
    }
}

public class BackupResult
{
    public string DatabaseName { get; set; } = string.Empty;
    public string BackupFile { get; set; } = string.Empty;
    public bool Success { get; set; }
    public TimeSpan Duration { get; set; }
    public string? ErrorMessage { get; set; }
}