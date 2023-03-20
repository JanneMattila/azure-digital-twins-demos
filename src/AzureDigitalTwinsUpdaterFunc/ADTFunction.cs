using Azure;
using Azure.DigitalTwins.Core;
using Azure.Identity;
using AzureDigitalTwinsUpdaterFunc.Data;
using Microsoft.Azure.DigitalTwins.Parser;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace AzureDigitalTwinsUpdaterFunc;

public class ADTFunction
{
    private readonly ILogger _logger;
    private readonly ADTOptions _options;
    private readonly IModelsRepository _modelsRepository;
    private readonly ITwinsCacheRepository _twinsCacheRepository;
    private readonly DigitalTwinsClient _client;

    public ADTFunction(ILoggerFactory loggerFactory, IOptions<ADTOptions> options, IModelsRepository modelsRepository, ITwinsCacheRepository twinsCacheRepository)
    {
        _logger = loggerFactory.CreateLogger<ADTFunction>();
        _options = options.Value;
        _modelsRepository = modelsRepository;
        _twinsCacheRepository = twinsCacheRepository;

        _client = new DigitalTwinsClient(new Uri(_options.ADTInstanceUrl), new DefaultAzureCredential());
    }

    [Function("ADTFunc")]
    public async Task Run([EventHubTrigger("%EventHubName%", Connection = "EventHubConnectionString")] string[] inputs)
    {
        var exceptions = new List<Exception>();
        foreach (var input in inputs)
        {
            try
            {
                _logger.LogTrace("Function processing message: {Input}", input);
                var digitalTwinUpdateRequest = JsonSerializer.Deserialize<Dictionary<string, object>>(input);

                if (digitalTwinUpdateRequest == null)
                {
                    _logger.LogError("Could not deserialize message: {Input}", input);
                    continue;
                }

                if (string.IsNullOrEmpty(_options.ProcessingLogic) == true ||
                    string.Compare(_options.ProcessingLogic, "ByID", true) == 0)
                {
                    await ProcessByIDField(digitalTwinUpdateRequest);
                }
                else if (string.Compare(_options.ProcessingLogic, "ByChild", true) == 0)
                {
                    await ProcessByChildField(digitalTwinUpdateRequest);
                }
                else
                {
                    _logger.LogError("Unsupported processing logic type: {ProcessingLogic}", _options.ProcessingLogic);
                    continue;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception while processing message");
                exceptions.Add(ex);
            }
        }

        if (exceptions.Count > 1)
        {
            throw new AggregateException(exceptions);
        }

        if (exceptions.Count == 1)
        {
            throw exceptions.Single();
        }
    }

    private async Task ProcessByIDField(Dictionary<string, object> digitalTwinUpdateRequest)
    {
        if (!digitalTwinUpdateRequest.ContainsKey(_options.IDFieldName))
        {
            _logger.LogWarning("Message does not contain {IDFieldName}. Skipping.", _options.IDFieldName);
            return;
        }

        var digitalTwinID = digitalTwinUpdateRequest[_options.IDFieldName].ToString();

        _logger.LogTrace("Fetching digital twin with ID: {ID}", digitalTwinID);

        var digitalTwin = await _client.GetDigitalTwinAsync<BasicDigitalTwin>(digitalTwinID).ConfigureAwait(false);
        var modelID = digitalTwin.Value.Metadata.ModelId;

        var digitalTwinUpdate = new JsonPatchDocument();

        var fieldsAdded = 0;
        var fieldsUpdated = 0;

        var model = await _modelsRepository.GetModelAsync(modelID);
        foreach (var modelField in model)
        {
            if (modelField.EntityKind == DTEntityKind.Property &&
                modelField is DTPropertyInfo propertyInfo)
            {
                var fieldName = propertyInfo.Name;
                if (digitalTwinUpdateRequest.ContainsKey(fieldName))
                {
                    var fieldValue = digitalTwinUpdateRequest[fieldName];

                    if (digitalTwin.Value.Contents.ContainsKey(fieldName))
                    {
                        digitalTwinUpdate.AppendReplace($"/{fieldName}", fieldValue);
                        fieldsUpdated++;
                    }
                    else
                    {
                        digitalTwinUpdate.AppendAdd($"/{fieldName}", fieldValue);
                        fieldsAdded++;
                    }
                }
            }
        }

        _logger.LogInformation("Updating digital twin with ID: {ID} and added {FieldsAdded} and updated {FieldsUpdated} fields.", digitalTwinID, fieldsAdded, fieldsUpdated);
        await _client.UpdateDigitalTwinAsync(digitalTwinID, digitalTwinUpdate, ETag.All).ConfigureAwait(false);
    }

    private async Task ProcessByChildField(Dictionary<string, object> digitalTwinUpdateRequest)
    {
        if (!digitalTwinUpdateRequest.ContainsKey(_options.IDFieldName))
        {
            _logger.LogWarning("Message does not contain {IDFieldName}. Skipping.", _options.IDFieldName);
            return;
        }

        var key = digitalTwinUpdateRequest[_options.IDFieldName].ToString();
        digitalTwinUpdateRequest.Remove(_options.IDFieldName);

        foreach (var item in digitalTwinUpdateRequest)
        {
            var childPropertyName = item.Key;
            _logger.LogTrace("Fetching digital twin with key: {Key} and child property name: {ChildPropertyName}", key, childPropertyName);

            var childDigitalTwinID = await _twinsCacheRepository.GetChildFromCacheAsync(key, childPropertyName);

            var digitalTwin = await _client.GetDigitalTwinAsync<BasicDigitalTwin>(childDigitalTwinID).ConfigureAwait(false);
            var digitalTwinUpdate = new JsonPatchDocument();

            var fieldsAdded = 0;
            var fieldsUpdated = 0;
            var fieldName = "OPCUANodeValue";

            var fieldValue = digitalTwinUpdateRequest[item.Key];

            if (digitalTwin.Value.Contents.ContainsKey(fieldName))
            {
                digitalTwinUpdate.AppendReplace($"/{fieldName}", item.Value);
                fieldsUpdated++;
            }
            else
            {
                digitalTwinUpdate.AppendAdd($"/{fieldName}", item.Value);
                fieldsAdded++;
            }

            _logger.LogInformation("Updating child digital twin with ID: {ID} and added {FieldsAdded} and updated {FieldsUpdated} fields.", childDigitalTwinID, fieldsAdded, fieldsUpdated);
            await _client.UpdateDigitalTwinAsync(childDigitalTwinID, digitalTwinUpdate, ETag.All).ConfigureAwait(false);
        }
    }
}
