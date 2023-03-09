using AzureDigitalTwinsUpdaterFunc;
using AzureDigitalTwinsUpdaterFunc.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices(s =>
    {
        s.AddApplicationInsightsTelemetryWorkerService();
        s.AddLogging(builder =>
        {
            builder.AddApplicationInsights();
        });

        s.AddOptions<ADTOptions>()
            .Configure<IConfiguration>((settings, configuration) =>
            {
                configuration.GetSection("ADTOptions").Bind(settings);
            });

        s.AddSingleton<IModelsRepository, ModelsRepository>();
    })
    .Build();

host.Run();
