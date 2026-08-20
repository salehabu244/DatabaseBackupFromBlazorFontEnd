using System.ComponentModel.DataAnnotations;

namespace DatabaseBackupRestore.Models;
public class BackupRequest
{
    [Required(ErrorMessage = "Database is required.")]
    public string DatabaseName { get; set; } = string.Empty;
    [Required(ErrorMessage = "Backup folder is required.")]
    public string BackupFolder { get; set; } = string.Empty;
    public string? BackupFileName { get; set; }
    public bool Compression { get; set; } = true;
}
