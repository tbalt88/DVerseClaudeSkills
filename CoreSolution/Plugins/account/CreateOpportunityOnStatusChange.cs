using System;
using System.Activities;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Xrm.Sdk.Workflow;

namespace Dexx.CoreSolution.Plugins.account
{
    /// <summary>
    /// Custom Workflow Activity triggered when Account.statuscode = 100000 (custom value).
    ///
    /// Registration:
    ///   Trigger Entity    : account
    ///   Trigger Event     : Update (statuscode field)
    ///   Trigger Condition : statuscode = 100000
    ///   Type              : Custom Workflow Activity (CodeActivity)
    /// </summary>
    [WorkflowActivity("Create Opportunity on Status Change", "Dexx Workflow Activities")]
    public class CreateOpportunityOnStatusChange : CodeActivity
    {
        private const int TriggerStatusCode = 100000;
        private const int ResetStatusCode   = 1;
        private const int ActiveStateCode   = 0;
        private const int FollowUpDays      = 15;

        [Output("Success")]
        public OutArgument<bool> OutSuccess { get; set; } = new OutArgument<bool>();

        [Output("Opportunity ID")]
        [ReferenceTarget("opportunity")]
        public OutArgument<EntityReference> OutOpportunityId { get; set; } = new OutArgument<EntityReference>();

        protected override void Execute(CodeActivityContext context)
        {
            ITracingService tracingService = null;

            try
            {
                var workflowContext = context.GetExtension<IWorkflowContext>();
                var serviceFactory  = context.GetExtension<IOrganizationServiceFactory>();
                tracingService      = context.GetExtension<ITracingService>();
                var orgService      = serviceFactory.CreateOrganizationService(workflowContext.UserId);

                var result = ExecuteBusinessLogic(
                    workflowContext.PrimaryEntityId,
                    orgService,
                    tracingService);

                OutSuccess.Set(context, result.Success);
                OutOpportunityId.Set(context, result.OpportunityRef);
            }
            catch (InvalidPluginExecutionException) { throw; }
            catch (Exception ex)
            {
                tracingService?.Trace(
                    "[CreateOpportunityOnStatusChange] Unhandled {0} in Execute: {1}\n{2}",
                    ex.GetType().Name, ex.Message, ex.StackTrace);
                throw new InvalidPluginExecutionException(
                    $"An unexpected error occurred in CreateOpportunityOnStatusChange: {ex.Message}", ex);
            }
        }

        internal ExecutionResult ExecuteBusinessLogic(
            Guid                 accountId,
            IOrganizationService orgService,
            ITracingService      tracingService)
        {
            try
            {
                tracingService.Trace("CreateOpportunityOnStatusChange: start. AccountId={0}", accountId);

                var account       = orgService.Retrieve("account", accountId, new ColumnSet("name", "statuscode"));
                var currentStatus = account.GetAttributeValue<OptionSetValue>("statuscode")?.Value ?? -1;
                var accountName   = account.GetAttributeValue<string>("name") ?? accountId.ToString();

                tracingService.Trace("Account '{0}' statuscode={1}", accountName, currentStatus);

                if (currentStatus != TriggerStatusCode)
                {
                    tracingService.Trace("statuscode is not {0} — exiting without action.", TriggerStatusCode);
                    return new ExecutionResult { Success = false };
                }

                var today           = DateTime.UtcNow.Date;
                var followUpDate    = today.AddDays(FollowUpDays);
                var opportunityName = $"Opportunity - {accountName} - {today:yyyy-MM-dd}";

                var opportunityId = orgService.Create(new Entity("opportunity")
                {
                    ["name"]               = opportunityName,
                    ["parentaccountid"]    = new EntityReference("account", accountId),
                    ["estimatedclosedate"] = followUpDate
                });
                var opportunityRef = new EntityReference("opportunity", opportunityId);
                tracingService.Trace("Opportunity created. Id={0}", opportunityId);

                orgService.Create(new Entity("task")
                {
                    ["subject"]           = $"Follow up - {opportunityName}",
                    ["regardingobjectid"] = opportunityRef,
                    ["scheduledend"]      = followUpDate,
                    ["description"]       = $"15-day follow-up task for opportunity '{opportunityName}' linked to account '{accountName}'."
                });
                tracingService.Trace("Follow-up task created.");

                orgService.Execute(new SetStateRequest
                {
                    EntityMoniker = new EntityReference("account", accountId),
                    State         = new OptionSetValue(ActiveStateCode),
                    Status        = new OptionSetValue(ResetStatusCode)
                });
                tracingService.Trace("Account statuscode reset. Workflow activity completing successfully.");

                return new ExecutionResult { Success = true, OpportunityRef = opportunityRef };
            }
            catch (InvalidPluginExecutionException) { throw; }
            catch (Exception ex)
            {
                tracingService?.Trace(
                    "[CreateOpportunityOnStatusChange] Unhandled {0} in ExecuteBusinessLogic: {1}\n{2}",
                    ex.GetType().Name, ex.Message, ex.StackTrace);
                throw new InvalidPluginExecutionException(
                    $"An unexpected error occurred processing account '{accountId}': {ex.Message}", ex);
            }
        }
    }

    internal sealed class ExecutionResult
    {
        public bool            Success        { get; set; }
        public EntityReference OpportunityRef { get; set; }
    }
}
