using Azure.DigitalTwins.Core;
using Azure.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace AzureDigitalTwinsUpdaterFunc.Data;

public class TwinsCacheRepository : ITwinsCacheRepository
{
    private readonly ILogger _logger;
    private readonly ADTOptions _options;
    private readonly ConcurrentDictionary<string, string> _cache = new();
    private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);

    public TwinsCacheRepository(ILoggerFactory loggerFactory, IOptions<ADTOptions> options)
    {
        _logger = loggerFactory.CreateLogger<TwinsCacheRepository>();
        _options = options.Value;
    }

    public async Task<string> GetChildFromCacheAsync(string key, string field)
    {
        var mapKey = $"{key}/{field}".ToLower();
        if (_cache.TryGetValue(mapKey, out var childID))
        {
            return childID;
        }

        _logger.LogWarning("Twin with identifier {Key} and {Field} was not found in cache.", key, field);
        _semaphore.Wait();

        try
        {
            using var scope = _logger.BeginScope("Fetch twins relationships");
            var stopWatch = Stopwatch.StartNew();
            var client = new DigitalTwinsClient(new Uri(_options.ADTInstanceUrl), new DefaultAzureCredential());

            var queryParent = $"SELECT * FROM digitaltwins WHERE equipmentID = '{key}'";
            var parentTwin = new List<BasicDigitalTwin>();
            await foreach (var item in client.QueryAsync<BasicDigitalTwin>(queryParent))
            {
                parentTwin.Add(item);
            }

            if (parentTwin.Count != 1)
            {
                _logger.LogError("Found {Count} digital twins found with unique search condition: equipmentID = '{Key}'.", parentTwin.Count, key);
                throw new Exception($"Found {parentTwin.Count} digital twins found with unique search condition: equipmentID = '{key}'.");
            }

            var parentId = parentTwin.First().Id;
            var queryChild = $"SELECT CT.$dtId, CT.ID FROM DIGITALTWINS T JOIN CT RELATED T.contains WHERE T.$dtId = '{parentId}' AND CT.ID != ''";
            var childTwins = new List<BasicDigitalTwin>();
            await foreach (var item in client.QueryAsync<BasicDigitalTwin>(queryChild))
            {
                childTwins.Add(item);

                var child = item.Contents["ID"].ToString();
                var itemKey = $"{key}/{child}".ToLower();
                _cache.AddOrUpdate(itemKey, item.Id, (key, oldValue) => item.Id);
            }

            if (!childTwins.Any())
            {
                _logger.LogWarning("Could not find child digital twins for parent: $dtId = '{Key}'.", parentId);
            }

            _logger.LogInformation("Update digital twin cache finished in {Elapsed}", stopWatch.Elapsed);
        }
        finally
        {
            _semaphore.Release();
        }

        if (_cache.TryGetValue(mapKey, out var childIDAfterUpdate))
        {
            // This was added by update
            return childIDAfterUpdate;
        }
        else
        {
            _logger.LogError("Twin with identifier {Key} and {Field} was not found in cache even after update.", key, field);
            throw new Exception($"Twin with identifier {key} and {field} was not found in cache even after update.");
        }
    }
}
