# CE Integration Reference
> Source: MS Docs — "Azure extensions for Dynamics 365 CE", "Use webhooks",
> "Virtual entities overview", "Asynchronous service", "Access external
> web resources" (CE on-premises v9.1 + Dataverse Developer docs)

---

## Integration Decision Tree

```
Need to react to a Dataverse event externally?
├── Near-real-time + guaranteed delivery → Azure Service Bus (topic/queue)
├── Near-real-time + lightweight HTTP endpoint → Webhook
├── Fire-and-forget side effect, no external → Async PostOp Plugin
└── External data surfaced inside Dataverse → Virtual Entity

Need to push data INTO Dataverse from external?
├── .NET app/service → ServiceClient (Org Service SDK)
├── Non-.NET / language-agnostic → Web API (OData v4)
└── High-volume batch → Web API + $batch or ExecuteMultiple (external only)

Need to extend message processing itself?
└── Custom API (custom action / custom function) → defines new message in pipeline
```

---

## Azure Service Bus Integration

D365 CE can post execution context payloads to Azure Service Bus queues,
topics, or Event Hubs via a registered Service Endpoint.

**When to use:** Reliable async integration to external systems (ERP, MDM,
data warehouse). Guaranteed delivery with retry. Decoupled receiver.

```bash
# Register Service Endpoint via Plug-in Registration Tool
pac tool prt
# → New Service Endpoint
# → Designation: OneWay (queue) | TwoWay (queue, returns value) | Topic | EventHub
# → SAS Key connection string from Azure portal
# → Message format: XML | JSON | DotNetBinary
```

```csharp
// Optional: Plugin step that posts to Service Bus on demand
// Register on SdkMessageProcessingStep → Service Endpoint (not a class plugin)
// The platform handles serialization of IPluginExecutionContext to RemoteExecutionContext

// Receiver side (Azure Function example)
public static async Task Run(
    [ServiceBusTrigger("d365-topic", "d365-subscription")] ServiceBusReceivedMessage msg,
    ILogger log)
{
    // Payload is RemoteExecutionContext serialized as JSON/XML
    var body = msg.Body.ToString();
    var ctx = JsonSerializer.Deserialize<RemoteExecutionContext>(body);
    log.LogInformation("Entity: {0}, Message: {1}", ctx.PrimaryEntityName, ctx.MessageName);
}
```

**Key properties of RemoteExecutionContext** (same as IPluginExecutionContext):
- `MessageName`, `PrimaryEntityName`, `PrimaryEntityId`
- `InputParameters` (contains Target entity snapshot)
- `PreEntityImages`, `PostEntityImages` (if registered)
- `InitiatingUserId`, `UserId`, `OrganizationId`

---

## Webhooks

Simpler alternative to Service Bus. Dataverse POSTs execution context as
JSON to an HTTPS endpoint you control.

**When to use:** Lightweight HTTP integration; you own a public HTTPS endpoint;
don't need guaranteed delivery or complex retry logic.

```bash
# Register webhook via Plug-in Registration Tool
pac tool prt
# → New Webhook
# → Endpoint URL: https://your-function.azurewebsites.net/api/D365Webhook
# → Authentication: HttpHeader | WebhookKey | HttpQueryString
# → Register step: same as plugin step (Message, Entity, Stage, Mode)
```

```csharp
// Azure Function webhook receiver
[FunctionName("D365Webhook")]
public static async Task<IActionResult> Run(
    [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequest req,
    ILogger log)
{
    // Validate webhook key from header
    if (!req.Headers.TryGetValue("x-webhook-key", out var key) ||
        key != Environment.GetEnvironmentVariable("WEBHOOK_KEY"))
        return new UnauthorizedResult();

    var body = await new StreamReader(req.Body).ReadToEndAsync();
    // body is RemoteExecutionContext JSON
    log.LogInformation("Received: {0}", body);
    return new OkResult();
}
```

**Webhook vs Service Bus:**
| | Webhook | Service Bus |
|---|---|---|
| Delivery guarantee | No (best-effort) | Yes (retry + dead-letter) |
| Receiver uptime required | Yes | No |
| Latency | Lower | Slightly higher |
| Complexity | Low | Medium |
| Cost | Receiver hosting | Azure Service Bus pricing |

---

## Virtual Entities

Surface external data inside Dataverse as native entities — no data sync,
no storage cost. Read-only by default; writable with custom provider.

**When to use:** External data needs to appear in D365 UI or be queried via
Web API/FetchXML without replicating it into Dataverse.

**Architecture:**
```
D365 UI / Web API / FetchXML query
    ↓
Virtual Entity Data Provider (plugin implementing IPlugin on Retrieve/RetrieveMultiple)
    ↓
External system (REST API, SQL, SAP, etc.)
    ↓
Returns Entity / EntityCollection back to Dataverse
```

```csharp
// Virtual Entity data provider plugin
// Register on: RetrieveMultiple message, your virtual entity
// Stage: MainOperation (30) — only valid stage for virtual entity providers
public class VirtualHcpDataProvider : IPlugin
{
    public void Execute(IServiceProvider serviceProvider)
    {
        var context = (IPluginExecutionContext)serviceProvider
            .GetService(typeof(IPluginExecutionContext));
        var tracingService = (ITracingService)serviceProvider
            .GetService(typeof(ITracingService));

        // Get query from context
        var query = (QueryExpression)context.InputParameters["Query"];

        // Call external system
        var externalData = CallExternalApi(query);

        // Build EntityCollection response
        var collection = new EntityCollection();
        foreach (var item in externalData)
        {
            var entity = new Entity("dexx_externalhcp");
            entity["dexx_externalhcpid"] = item.Id;
            entity["dexx_name"] = item.Name;
            collection.Entities.Add(entity);
        }

        context.OutputParameters["BusinessEntityCollection"] = collection;
        tracingService.Trace("Returned {0} records", collection.Entities.Count);
    }
}
```

**Virtual Entity caveats:**
- Hard 30-second timeout on external calls (OData hard limit)
- No offline support
- Performance depends entirely on external system
- Sorting/filtering must be translated to external query language in provider
- Availability tied to external system uptime
- Not supported for use in workflows or business rules (read-only in automation)

---

## Asynchronous Service

PostOperation async plugins and workflows run via the Dataverse Async Service
(queue-based job processor). Not the same as Azure Service Bus.

```csharp
// Register as: Stage=PostOperation (40), Mode=Asynchronous (1)
// Runs OUTSIDE the database transaction — safe for long operations
// Time limit still applies but is more lenient than sync plugins

// Required for: SystemUser Create event → UserSettings record not yet created
// when sync PostOp fires, but IS created by time async PostOp runs
```

**Use async PostOp when:**
- Side effects don't need to be atomic with the main operation
- Work touches related records created in a separate pipeline
- External calls that must happen post-commit (email, logging, notification)

---

## Access External Web Resources from Plugins

```csharp
// BP: "Set Timeout when making external calls in a plug-in"
// BP: "Set KeepAlive to false when interacting with external hosts"
// Only in async PostOp plugins — NEVER in sync PreVal/PreOp/sync PostOp

using (var client = new HttpClient())
{
    client.Timeout = TimeSpan.FromSeconds(15); // explicit timeout required

    // KeepAlive = false (via HttpClientHandler)
    var handler = new HttpClientHandler();
    var request = new HttpRequestMessage(HttpMethod.Post, "https://api.external.com/data");
    request.Headers.Connection.Clear();
    request.Headers.ConnectionClose = true; // KeepAlive = false

    var response = await client.SendAsync(request);
    response.EnsureSuccessStatusCode();
}
```

> **On-premises note:** "Access external web resources" in on-premises requires
> that the CRM server can reach the external endpoint. Proxy configuration may
> be needed at the server level.
