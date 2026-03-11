# CE Data Access Reference
> Source: MS Docs — "Use the Dynamics 365 CE Web API", "Organization Service",
> "Use messages with SDK for .NET", "FetchXML", "Client scripting (Client API)"

---

## Web API vs Organization Service — Decision Table

| Scenario | Use | Why |
|---|---|---|
| Inside a plugin or custom workflow activity | **Organization Service** | Web API not supported inside plugins (docs explicit) |
| External .NET app or Azure Function | **ServiceClient** (Org Service SDK) | Same NuGet, full message support, type safety |
| Node.js, Python, Java, non-.NET external | **Web API** (OData v4) | Language-agnostic, open standard |
| Browser/client-side JavaScript | **Web API via Xrm.WebApi** (Client API) | Secure, session-auth, no token management |
| Bulk load (millions of rows) | **Web API** + `ExecuteMultiple` batching | High-throughput; don't use ExecuteMultiple *inside* plugins |
| Power Platform canvas app connector | Dataverse connector | No-code path |

> **Doc note:** "Don't try to use the Web API [inside plug-ins] as it isn't
> supported. Also, don't authenticate the user before accessing the web services
> as the user is preauthenticated before plug-in execution." (Write a plug-in)

---

## Organization Service — Inside Plugin

```csharp
// Retrieve single record
var account = orgService.Retrieve("account", accountId,
    new ColumnSet("name", "emailaddress1", "telephone1"));

// Create
var newContact = new Entity("contact");
newContact["firstname"] = "Jane";
newContact["lastname"] = "Doe";
newContact["parentcustomerid"] = new EntityReference("account", accountId);
var newId = orgService.Create(newContact);

// Update
var update = new Entity("contact", contactId);
update["jobtitle"] = "Director";
orgService.Update(update);

// Delete
orgService.Delete("contact", contactId);

// Associate (N:N)
orgService.Associate("account", accountId,
    new Relationship("contact_customer_accounts"),
    new EntityReferenceCollection { new EntityReference("contact", contactId) });

// RetrieveMultiple with QueryExpression
var qe = new QueryExpression("contact")
{
    ColumnSet = new ColumnSet("fullname", "emailaddress1"),
    Criteria = new FilterExpression()
};
qe.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
qe.TopCount = 50;
var results = orgService.RetrieveMultiple(qe);
```

---

## ServiceClient — External .NET (Azure Function / Windows Service)

```csharp
using Microsoft.PowerPlatform.Dataverse.Client;

// Client Secret (service principal — preferred for CI/CD and Azure Functions)
var connectionString =
    "AuthType=ClientSecret;" +
    "Url=https://yourorg.crm.dynamics.com;" +
    "ClientId=YOUR_APP_REGISTRATION_ID;" +
    "ClientSecret=YOUR_SECRET;";

using var client = new ServiceClient(connectionString);

// Same Org Service API — Retrieve, Create, Update, Delete, Associate
var account = client.Retrieve("account", accountId,
    new ColumnSet("name", "emailaddress1"));
```

---

## Web API (OData v4) — External / Non-.NET

```bash
# Base URL pattern
# https://yourorg.crm.dynamics.com/api/data/v9.2/

# GET — Retrieve single record with field selection
GET /api/data/v9.2/accounts(GUID)?$select=name,emailaddress1

# GET — Query with filter and ordering
GET /api/data/v9.2/contacts?$select=fullname,emailaddress1
    &$filter=statecode eq 0 and _parentcustomerid_value eq GUID
    &$orderby=fullname asc&$top=50

# POST — Create
POST /api/data/v9.2/contacts
Content-Type: application/json
{"firstname":"Jane","lastname":"Doe","emailaddress1":"jane@example.com"}

# PATCH — Update (merge; only supplied fields updated)
PATCH /api/data/v9.2/contacts(GUID)
{"jobtitle":"Director"}

# DELETE
DELETE /api/data/v9.2/contacts(GUID)

# Associate N:N
POST /api/data/v9.2/accounts(GUID)/contact_customer_accounts/$ref
{"@odata.id":"https://yourorg.crm.dynamics.com/api/data/v9.2/contacts(GUID)"}

# Batch (up to 1000 ops per batch request)
POST /api/data/v9.2/$batch
Content-Type: multipart/mixed; boundary=batch_1
```

> **CE on-premises doc note:** "Both Customer Engagement (on-premises) and
> Dataverse share the same Web API. The Web API implements OData v4.0."

---

## FetchXML

FetchXML is the proprietary Dataverse query language. Use when QueryExpression
is insufficient (complex aggregates, multi-level links, specific operators).

```xml
<!-- Standard query: entity + join + filter + order -->
<fetch version="1.0" output-format="xml-platform" mapping="logical"
       distinct="false" top="50">
  <entity name="dexx_hcpvisit">
    <attribute name="dexx_hcpvisitid" />
    <attribute name="dexx_visitdate" />
    <attribute name="dexx_status" />
    <link-entity name="contact" from="contactid" to="dexx_hcpid"
                 link-type="inner" alias="hcp">
      <attribute name="fullname" />
      <attribute name="emailaddress1" />
    </link-entity>
    <filter type="and">
      <condition attribute="dexx_status" operator="eq" value="1" />
      <condition attribute="dexx_visitdate" operator="on-or-after"
                 value="2025-01-01" />
    </filter>
    <order attribute="dexx_visitdate" descending="true" />
  </entity>
</fetch>

<!-- Aggregate: count visits per HCP -->
<fetch aggregate="true">
  <entity name="dexx_hcpvisit">
    <attribute name="dexx_hcpvisitid" aggregate="count" alias="visit_count" />
    <attribute name="dexx_hcpid" groupby="true" alias="hcp_id" />
    <filter>
      <condition attribute="statecode" operator="eq" value="0" />
    </filter>
  </entity>
</fetch>
```

### FetchXML common operators
```
eq, ne, lt, le, gt, ge               — comparison
like, not-like                        — wildcard (%value%)
in, not-in                            — list membership
null, not-null                        — null checks
eq-userid, eq-userteams               — current user / user's teams
on-or-after, on-or-before             — date comparisons
last-x-days, next-x-days              — rolling windows
between                               — range
link-type="inner" / "outer"           — JOIN type
```

### Execute FetchXML via Web API (bash_tool)
```bash
ENV_URL="https://yourorg.crm.dynamics.com"
TOKEN=$(az account get-access-token --resource "$ENV_URL" --query accessToken -o tsv)
ENCODED=$(python3 -c "import urllib.parse; print(urllib.parse.quote(open('query.xml').read()))")
curl -s "$ENV_URL/api/data/v9.2/dexx_hcpvisits?\$fetchXml=$ENCODED" \
  -H "Authorization: Bearer $TOKEN" -H "Accept: application/json" | jq '.value | length'
```

---

## Client Scripting (Client API)

For form/view logic — browser-side JavaScript using the Xrm Client API.

```javascript
// Form OnLoad handler
function onFormLoad(executionContext) {
    const formContext = executionContext.getFormContext();

    // Read field value
    const status = formContext.getAttribute("dexx_status").getValue();

    // Set field value
    formContext.getAttribute("dexx_priority").setValue(2);

    // Show/hide control
    formContext.getControl("dexx_creditlimit").setVisible(status === 1);

    // Lock field
    formContext.getControl("dexx_orderdate").setDisabled(true);

    // Get lookup value
    const owner = formContext.getAttribute("ownerid").getValue();
    if (owner) console.log("Owner:", owner[0].id, owner[0].name);
}

// Call Web API from client-side (Xrm.WebApi — no token management needed)
async function fetchRelatedRecords(primaryId) {
    const result = await Xrm.WebApi.retrieveMultipleRecords(
        "dexx_hcpvisit",
        `?$select=dexx_visitdate,dexx_status&$filter=_dexx_hcpid_value eq ${primaryId}&$top=10`
    );
    return result.entities;
}
```

---

## Custom Table Naming Conventions

| Element | Pattern | Example |
|---|---|---|
| Table schema name | `prefix_entityname` | `dexx_hcpvisit` |
| Column schema name | `prefix_columnname` | `dexx_visitdate` |
| Publisher prefix | 2–8 lowercase letters | `dexx` |
| Plugin class | `Namespace.Plugins.OnVerbEntity` | `Contoso.Plugins.OnCreateOrder` |
| Web resource | `prefix_/folder/file.ext` | `dexx_/js/orderform.js` |

Always use publisher prefix. Never modify OOB tables with a different prefix.
