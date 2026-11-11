using Elocaro.Backup;

var builder = WebApplication.CreateBuilder(args);
// Every request (health check every 30 s) would otherwise fill the log at Information level.
builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
var settings = BackupSettings.Load(Environment.GetEnvironmentVariable);
builder.Services.AddSingleton(settings);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IProcessRunner, ProcessRunner>();
builder.Services.AddSingleton<BackupEngine>();
builder.Services.AddSingleton<BackupCoordinator>();
builder.Services.AddHostedService<EnvironmentScheduler>();
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));

var app = builder.Build();

// Options (--urls=…, test host) are no command.
var command = args.Length > 0 && !args[0].StartsWith('-') ? args[0] : "serve";
if (command != "serve")
{
    return await Cli.RunAsync(command, args[1..], app.Services, settings);
}

app.MapBackupApi();
await app.RunAsync();
return 0;

public partial class Program;
