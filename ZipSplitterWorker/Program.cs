using Serilog;
using ZipSplitterWorker;
using ILogger = Serilog.ILogger;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

// Configure Serilog to write logs to both console and file
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .WriteTo.File($"logs/log{DateTimeOffset.UtcNow:yyyy-MM-dd}.txt")
    .CreateLogger();

builder.Logging.ClearProviders();

// Retrieve ZipFilesPath from configuration
var zipFilesPath = builder.Configuration.GetSection("ZipFilesPath").Value;

if (string.IsNullOrEmpty(zipFilesPath))
{
    Log.Error("ZipFilesPath is not configured in appsettings.json");
    throw new ArgumentException("ZipFilesPath is not configured in appsettings.json");
}

ILogger logger = Log.ForContext<Worker>();

// Register the Worker service
builder.Services.AddSingleton(logger);

builder.Services.AddHostedService<Worker>(provider => new Worker(provider.GetRequiredService<ILogger>(), zipFilesPath));


var app = builder.Build();

try
{
    Log.Information("Starting host");
    await app.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Host terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}