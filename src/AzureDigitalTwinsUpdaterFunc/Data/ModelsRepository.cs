using Azure.DigitalTwins.Core;
using Azure.Identity;
using Microsoft.Azure.DigitalTwins.Parser;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace AzureDigitalTwinsUpdaterFunc.Data;

public class ModelsRepository : IModelsRepository
{
    private readonly ILogger _logger;
    private readonly ADTOptions _options;
    private readonly ConcurrentDictionary<string, List<DTEntityInfo>> _modelMap = new();
    private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);

    public ModelsRepository(ILoggerFactory loggerFactory, IOptions<ADTOptions> options)
    {
        _logger = loggerFactory.CreateLogger<ModelsRepository>();
        _options = options.Value;
    }

    public async Task<List<DTEntityInfo>> GetModelAsync(string modelId)
    {
        if (_modelMap.TryGetValue(modelId, out var model))
        {
            return model;
        }
        else
        {
            _logger.LogWarning("Model {ModelId} not found in repository. Updating models cache.", modelId);
            _semaphore.Wait();

            try
            {
                if (_modelMap.TryGetValue(modelId, out var modelAdded))
                {
                    // This was already added by another thread
                    return modelAdded;
                }

                await UpdateModelsAsync();
            }
            finally
            {
                _semaphore.Release();
            }

            if (_modelMap.TryGetValue(modelId, out var modelAddedAfterUpdate))
            {
                // This was added by update
                return modelAddedAfterUpdate;
            }
            else
            {
                _logger.LogError("Model {ModelId} not found in repository even after update.", modelId);
                throw new Exception($"Model {modelId} not found in repository even after update.");
            }
        }
    }

    private async Task UpdateModelsAsync()
    {
        using var scope = _logger.BeginScope("Updating models cache");
        var stopWatch = Stopwatch.StartNew();
        var client = new DigitalTwinsClient(new Uri(_options.ADTInstanceUrl), new DefaultAzureCredential());

        var digitalTwinsModels = new Dictionary<string, DigitalTwinsModelData>();
        var getModelOptions = new GetModelsOptions()
        {
            IncludeModelDefinition = true
        };
        await foreach (var item in client.GetModelsAsync(getModelOptions))
        {
            digitalTwinsModels.Add(item.Id, item);
        }

        foreach (var digitalTwinsModel in digitalTwinsModels)
        {
            var modelParser = new ModelParser
            {
                DtmiResolver = async dtmis =>
                {
                    var list = new List<string>();
                    foreach (var dtmi in dtmis)
                    {
                        list.Add(digitalTwinsModels[dtmi.AbsoluteUri].DtdlModel);
                    }
                    return await Task.FromResult(list);
                }
            };

            var parsedModel = await modelParser.ParseAsync(new List<string>() { digitalTwinsModel.Value.DtdlModel });
            var entityInfos = parsedModel.Values.ToList();
            _modelMap.AddOrUpdate(digitalTwinsModel.Key, entityInfos, (key, oldValue) => entityInfos);
        }

        _logger.LogInformation("Update models cache finished in {Elapsed}", stopWatch.Elapsed);
    }
}
