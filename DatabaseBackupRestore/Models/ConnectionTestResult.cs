namespace DatabaseBackupRestore.Models;
public class ConnectionTestResult
{
    public bool IsConnected { get; set; }
    public string? ServerName { get; set; }
    public string? ServerVersion { get; set; }
    public int DatabaseCount { get; set; }
    public string? TotalDatabaseSize { get; set; }
    public DateTime TestedAtUtc { get; set; } = DateTime.UtcNow;
    public string? ErrorMessage { get; set; }
}
