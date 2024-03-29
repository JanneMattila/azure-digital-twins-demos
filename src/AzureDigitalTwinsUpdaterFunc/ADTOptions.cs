﻿namespace AzureDigitalTwinsUpdaterFunc;

public class ADTOptions
{
    public string ADTInstanceUrl { get; set; } = string.Empty;

    public string ProcessingLogic { get; set; } = string.Empty;

    public string IDFieldName { get; set; } = string.Empty;

    public string DataFieldName { get; set; } = string.Empty;

    public string DataValueFieldName { get; set; } = string.Empty;
}
