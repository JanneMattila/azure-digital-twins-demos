using Microsoft.Azure.DigitalTwins.Parser;

namespace AzureDigitalTwinsUpdaterFunc.Data
{
    public interface IModelsRepository
    {
        Task<List<DTEntityInfo>> GetModelAsync(string modelId);
    }
}