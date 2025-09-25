using APIMonitorWorkerService;
using APIMonitorWorkerService.Data;
using APIMonitorWorkerService.Services;


var builder = Host.CreateDefaultBuilder(args);

if (OperatingSystem.IsWindows())
{
    builder.UseWindowsService(); 
}
else if (OperatingSystem.IsLinux())
{
    builder.UseSystemd();
}

builder.ConfigureLogging(logging =>
{
    logging.ClearProviders();
    logging.AddConsole();
});

builder.ConfigureServices((hostContext, services) =>
{
    DataLayerExtension.RegisterDataLayer(services, hostContext.Configuration);
    ServiceLayerExtension.RegisterServiceLayer(services, hostContext.Configuration);

    services.AddHostedService<Worker>();
});

var host = builder.Build();

using (var scope = host.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var context = services.GetRequiredService<AppDbContext>();
    var logger = services.GetRequiredService<ILogger<DatabaseInitializer>>();
    await DatabaseInitializer.InitializeAsync(context, logger);
}

host.Run();
