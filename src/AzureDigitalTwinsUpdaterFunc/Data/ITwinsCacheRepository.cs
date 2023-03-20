namespace AzureDigitalTwinsUpdaterFunc.Data
{
    public interface ITwinsCacheRepository
    {
        Task<string> GetChildFromCacheAsync(string key, string field);
    }
}