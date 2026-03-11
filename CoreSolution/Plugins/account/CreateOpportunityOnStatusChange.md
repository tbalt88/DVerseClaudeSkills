# CreateOpportunityOnStatusChange

## Plugin Definition

| Property | Value |
|---|---|
| Type | Custom Workflow Activity (`CodeActivity`) |
| Trigger Entity | `account` |
| Trigger Event | `Update` — `statuscode` field |
| Trigger Condition | `statuscode = 100000` (custom optionset value) |
| Group | `Dexx Workflow Activities` |

## Description

When the Account `statuscode` is set to `100000`, this workflow activity creates an Opportunity, a follow-up Task, resets the Account status, and returns typed output parameters to the workflow designer.

## Input Parameters

| Parameter | Type | Source | Description |
|---|---|---|---|
| *(Account ID)* | `Guid` | `IWorkflowContext.PrimaryEntityId` | Resolved internally — not a designer input. |

## Output Parameters

| Parameter | Designer Label | Type | Description |
|---|---|---|---|
| `OutSuccess` | `Success` | `bool` | `true` if completed successfully; `false` if guard rejected. |
| `OutOpportunityId` | `Opportunity ID` | `EntityReference` (`opportunity`) | Reference to the created Opportunity. `null` when `OutSuccess = false`. |

## Error Conditions

| Condition | Behaviour |
|---|---|
| `statuscode ≠ 100000` at execution time | Guard exits early; no records created. |
| Any `IOrganizationService` failure | Exception wrapped and propagated; workflow marks activity as Failed. |

## Records Created

| Entity | Key Fields |
|---|---|
| `opportunity` | `name = "Opportunity - {AccountName} - {yyyy-MM-dd}"`, `parentaccountid`, `estimatedclosedate = today + 15 days` |
| `task` | `subject = "Follow up - {OpportunityName}"`, `regardingobjectid = opportunity`, `scheduledend = today + 15 days` |
