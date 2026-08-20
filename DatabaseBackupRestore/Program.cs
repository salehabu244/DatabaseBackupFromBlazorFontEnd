using DatabaseBackupRestore.Components;
using DatabaseBackupRestore.Models;
using DatabaseBackupRestore.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Configure strongly-typed settings from appsettings.json.
builder.Services.Configure<AppSettings>(builder.Configuration.GetSection("AppSettings"));

// Register application services using dependency injection.
builder.Services.AddSingleton<DatabaseService>();
builder.Services.AddSingleton<BackupService>();
builder.Services.AddSingleton<RestoreService>();

// Register an in-memory history store (singleton keeps state across requests).
builder.Services.AddSingleton<BackupHistoryStore>();

// Add logging.
builder.Services.AddLogging();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();