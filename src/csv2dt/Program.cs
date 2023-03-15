using Azure.DigitalTwins.Core;
using Azure.Identity;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.Extensions.Configuration;
using System.Diagnostics;
using System.Globalization;

var builder = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
#if DEBUG
    .AddUserSecrets<Program>()
#endif
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables();

var configuration = builder.Build();
var stopwatch = new Stopwatch();

var csvFilePath = configuration.GetValue<string>("csvFilePath") ?? throw new ArgumentNullException("csvFilePath");
var csvDelimiter = configuration.GetValue<string>("csvDelimiter") ?? throw new ArgumentNullException("csvDelimiter");
var adtInstanceUrl = configuration.GetValue<string>("adtInstanceUrl") ?? throw new ArgumentNullException("adtInstanceUrl");

var client = new DigitalTwinsClient(new Uri(adtInstanceUrl), new DefaultAzureCredential());

var config = new CsvConfiguration(CultureInfo.InvariantCulture)
{
    PrepareHeaderForMatch = args => args.Header.ToLower(),
    Delimiter = csvDelimiter
};

var map = new Dictionary<string, string>();
var idCounter = 10_000;

async Task<(string, bool)> CreateDigitalTwinAsync(string uniquePath, string name, string model)
{
    if (map.ContainsKey(uniquePath))
    {
        return (map[uniquePath], false);
    }

    var id = idCounter.ToString();
    idCounter++;

    var dt = new BasicDigitalTwin()
    {
        Metadata = { ModelId = model },
        Contents =
        {
            { "ID", name },
            { "tags", new Dictionary<string, object> {{ "$metadata", new {} }} }
        }
    };
    await client.CreateOrReplaceDigitalTwinAsync(id, dt);
    map.Add(uniquePath, id);
    return (id, true);
}

using (var reader = new StreamReader(csvFilePath))
using (var csv = new CsvReader(reader, config))
{
    csv.Read();
    csv.ReadHeader();

    var columnsCount = csv.HeaderRecord.Length;
    while (csv.Read())
    {
        var path = string.Empty;
        var parentID = string.Empty;
        for (int i = 0; i < columnsCount; i++)
        {
            var model = csv.HeaderRecord[i];
            var name = csv.GetField<string>(i);

            path += $"{model}-{name}|";

            (var id, var created) = await CreateDigitalTwinAsync(path, name, model);

            if (created && !string.IsNullOrEmpty(parentID))
            {
                var relationshipID = idCounter.ToString();
                idCounter++;

                var relationship = new BasicRelationship()
                {
                    SourceId = parentID,
                    TargetId = id,
                    Name = "contains"
                };

                client.CreateOrReplaceRelationship(parentID, relationshipID, relationship);
            }

            parentID = id;
        }
    }

}