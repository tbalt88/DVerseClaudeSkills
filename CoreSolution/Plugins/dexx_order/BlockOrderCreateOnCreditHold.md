# BlockOrderCreateOnCreditHold

## Plugin Definition

| Property | Value |
|---|---|
| Entity | `dexx_order` |
| Message | `Create` |
| Stage | `PreValidation` (10) |
| Mode | Synchronous |
| Rank | 1 |

## Description

Blocks the creation of a `dexx_order` record when the related account is on credit hold. The plugin retrieves the account referenced by `dexx_accountid` on the incoming order and inspects the `creditonhold` flag. If the flag is `true`, execution is halted with a user-facing error before any database write occurs.

## Input Parameters

| Parameter | Type | Source | Description |
|---|---|---|---|
| `Target` | `Entity` (`dexx_order`) | `InputParameters["Target"]` | The order record being created. Must contain `dexx_accountid`. |
| `dexx_accountid` | `EntityReference` (`account`) | Attribute on `Target` | Lookup to the account against which the credit-hold check is performed. |

## Output Parameters

None. The plugin either completes silently (order allowed) or throws `InvalidPluginExecutionException` (order blocked).

## Error Conditions

| Condition | Behaviour |
|---|---|
| `dexx_accountid` not present on target | Skips check — order is allowed |
| Account `creditonhold = false` | Completes silently — order is allowed |
| Account `creditonhold = true` | Throws `InvalidPluginExecutionException` (HTTP 400) with message: *"Order cannot be created because account '{name}' is on credit hold."* |
