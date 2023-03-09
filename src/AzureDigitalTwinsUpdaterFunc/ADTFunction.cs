using Azure;
using Azure.DigitalTwins.Core;
using Azure.Identity;
using Microsoft.Azure.DigitalTwins.Parser;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace AzureDigitalTwinsUpdaterFunc;

public class ADTFunction
{
    private readonly ILogger _logger;
    private readonly ADTOptions _options;
    private readonly DigitalTwinsClient _client;

    public ADTFunction(ILoggerFactory loggerFactory, ADTOptions options)
    {
        _logger = loggerFactory.CreateLogger<ADTFunction>();
        _options = options;

        _client = new DigitalTwinsClient(new Uri(options.ADTInstanceUrl), new DefaultAzureCredential());
    }

    [Function("ADTFunc")]
    public async Task Run([EventHubTrigger("adt", Connection = "EventHubConnectionString")] string[] inputs)
    {
        var exceptions = new List<Exception>();
        foreach (var input in inputs)
        {
            try
            {
                _logger.LogTrace("Function processing message: {Input}", input);
                var digitalTwinUpdateRequest = JsonSerializer.Deserialize<Dictionary<string, object>>(input);

                var modelID = digitalTwinUpdateRequest[_options.ModelFieldName].ToString();
                var digitalTwinID = digitalTwinUpdateRequest[_options.IDFieldName].ToString();

                _logger.LogTrace("Fetching digital twin with ID: {ID}", digitalTwinID);

                var digitalTwin = await _client.GetDigitalTwinAsync<BasicDigitalTwin>(digitalTwinID).ConfigureAwait(false);
                var digitalTwinUpdate = new JsonPatchDocument();

                var model = await _client.GetModelAsync(modelID);
                var modelParser = new ModelParser
                {
                    DtmiResolver = async dtmis =>
                    {
                        var list = new List<string>();
                        foreach (var dtmi in dtmis)
                        {
                            var extendsModel = await _client.GetModelAsync(dtmi.AbsoluteUri);
                            list.Add(extendsModel.Value.DtdlModel);
                        }
                        return list;
                    }
                };
                var parsedModel = await modelParser.ParseAsync(new List<string>() { model.Value.DtdlModel });

                foreach (var item in parsedModel.Values)
                {
                    if (item.EntityKind == DTEntityKind.Property &&
                        item is DTPropertyInfo propertyInfo)
                    {
                        var fieldName = propertyInfo.Name;
                        if (digitalTwinUpdateRequest.ContainsKey(fieldName))
                        {
                            var fieldValue = digitalTwinUpdateRequest[fieldName];

                            if (digitalTwin.Value.Contents.ContainsKey(fieldName))
                            {
                                digitalTwinUpdate.AppendReplace($"/{fieldName}", fieldValue);
                            }
                            else
                            {
                                digitalTwinUpdate.AppendAdd($"/{fieldName}", fieldValue);
                            }
                        }
                    }
                }

                _logger.LogTrace("Updating digital twin with ID: {ID}", digitalTwinID);
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
