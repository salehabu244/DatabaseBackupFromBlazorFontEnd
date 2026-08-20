namespace DatabaseBackupRestore.Models;
public enum BackupStatus
{
    Success,
    Failed
}
public class BackupHistory
{
    public int Id { get; set; }
    public string DatabaseName { get; set; } = string.Empty;
    public string BackupFile { get; set; } = string.Empty;
    public DateTime Date { get; set; } = DateTime.UtcNow;
    public BackupStatus Status { get; set; }
    public string? Detail { get; set; }
    public bool IsRestore { get; set; }
}
