using System.ComponentModel.DataAnnotations;

namespace DatabaseBackupRestore.Models;
public class RestoreRequest
{
    [Required(ErrorMessage = "Backup file is required.")]
    public string BackupFile { get; set; } = string.Empty;
    [Required(ErrorMessage = "Destination database name is required.")]
    public string DestinationDatabaseName { get; set; } = string.Empty;
    public bool OverwriteExisting { get; set; } = false;
}
