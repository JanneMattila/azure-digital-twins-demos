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
    private readonly DigitalTwinsClient _client;

    public ADTFunction(ILoggerFactory loggerFactory, IOptions<ADTOptions> options, IModelsRepository modelsRepository)
    {
        _logger = loggerFactory.CreateLogger<ADTFunction>();
        _options = options.Value;
        _modelsRepository = modelsRepository;

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

                if (!digitalTwinUpdateRequest.ContainsKey(_options.IDFieldName))
                {
                    _logger.LogWarning("Message does not contain {IDFieldName}. Skipping.", _options.IDFieldName);
                    continue;
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
}
