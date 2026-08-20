namespace DatabaseBackupRestore.Models;

public class AppSettings
{
    public string SqlServerName { get; set; } = "RGL_SOFTWARE";
    public string DefaultBackupFolder { get; set; } = @"C:\SQLBackup";
    public string ConnectionString { get; set; } =
        "Server=RGL_SOFTWARE;Integrated Security=True;TrustServerCertificate=True;Connection Timeout=30;";
}
