# Azure Digital Twins demos

Azure Digital Twins demos

## Updates From Event Hub to Azure Digital Twins

You can find example models in [models](./models) folder for `Car` and `Tyre`.

```mermaid
sequenceDiagram
    Note right of On-premises App: See JSON<br/>payload below
    On-premises App->>+Event Hub: Send event
    Azure Functions-->>+Event Hub: Receive event
    Note right of Azure Functions: Map fields from<br/>payload to digital<br/>twin model definitions
    Azure Functions->>Azure Digital Twins: Update digital twin
```

Example payloads:

```json
{
  "_id": "Matiz",
  "_model": "dtmi:com:janneexample:car;1",
  "carStatus": "Stopped",
  "speed": 121.8
}
```

```json
{
  "_id": "LeftFront",
  "_model": "dtmi:com:janneexample:tyre;1",
  "tyreStatus": "OK",
  "pressure": 2.3
}
```

Notice two special fields in the payload:

- `_id` is identifier of the digital twin
- `_model` is model name of the payload

There are picked by [AzureDigitalTwinsUpdaterFunc](./src/AzureDigitalTwinsUpdaterFunc) which
then processes mapping of incoming data to the target digital twin.

You can send events to Event Hub using [examples.ps1](./examples.ps1) script.

## Links

[Learn about twin models and how to define them in Azure Digital Twins](https://learn.microsoft.com/en-us/azure/digital-twins/concepts-models)
