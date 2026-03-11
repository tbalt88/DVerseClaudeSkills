using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace Dexx.CoreSolution.Plugins.dexx_order
{
    /// <summary>
    /// PreValidation plugin on dexx_order Create.
    /// Blocks order creation when the related Account has Credit Hold = true.
    ///
    /// Registration:
    ///   Entity  : dexx_order
    ///   Message : Create
    ///   Stage   : PreValidation (10)
    ///   Mode    : Synchronous
    ///   Rank    : 1
    /// </summary>
    public class BlockOrderCreateOnCreditHold : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
            // Declared outside the try so it is accessible in the catch block for tracing.
            ITracingService tracingService = null;

            try
            {
                // ── Infrastructure ────────────────────────────────────────────
                var context = (IPluginExecutionContext)serviceProvider
                    .GetService(typeof(IPluginExecutionContext));
                tracingService = (ITracingService)serviceProvider
                    .GetService(typeof(ITracingService));
                var serviceFactory = (IOrganizationServiceFactory)serviceProvider
                    .GetService(typeof(IOrganizationServiceFactory));
                var orgService = serviceFactory.CreateOrganizationService(context.UserId);

                // ── Guard: correct pipeline position ──────────────────────────
                if (context.MessageName != "Create" ||
                    context.Stage != 10 ||               // 10 = PreValidation
                    context.PrimaryEntityName != "dexx_order")
                {
                    tracingService.Trace("Plugin fired outside expected pipeline position — exiting.");
                    return;
                }

                // ── Target ────────────────────────────────────────────────────
                if (!context.InputParameters.Contains("Target") ||
                    context.InputParameters["Target"] is not Entity target)
                {
                    tracingService.Trace("Target not found in InputParameters — exiting.");
                    return;
                }

                tracingService.Trace("BlockOrderCreateOnCreditHold: evaluating dexx_order Id={0}",
                    target.Id);

                // ── Resolve account lookup ────────────────────────────────────
                if (!target.Contains("dexx_accountid") ||
                    target["dexx_accountid"] is not EntityReference accountRef)
                {
                    tracingService.Trace("dexx_accountid not present on target — skipping credit-hold check.");
                    return;
                }

                tracingService.Trace("Retrieving account Id={0} to check creditonhold.", accountRef.Id);

                // ── Retrieve account creditonhold ─────────────────────────────
                var account = orgService.Retrieve(
                    "account",
                    accountRef.Id,
                    new ColumnSet("creditonhold", "name"));

                var creditOnHold = account.GetAttributeValue<bool>("creditonhold");
                var accountName  = account.GetAttributeValue<string>("name") ?? accountRef.Id.ToString();

                tracingService.Trace("Account '{0}' creditonhold={1}", accountName, creditOnHold);

                // ── Block if credit hold is active ────────────────────────────
                if (creditOnHold)
                {
                    throw new InvalidPluginExecutionException(
                        PluginHttpStatusCode.BadRequest,
                        $"Order cannot be created because account '{accountName}' is on credit hold. " +
                        "Please contact the finance team to release the credit hold before placing an order.");
                }

                tracingService.Trace("Credit-hold check passed — allowing Create.");
            }
            catch (InvalidPluginExecutionException)
            {
                throw;
            }
            catch (Exception ex)
            {
                tracingService?.Trace(
                    "[BlockOrderCreateOnCreditHold] Unhandled {0}: {1}\n{2}",
                    ex.GetType().Name, ex.Message, ex.StackTrace);

                throw new InvalidPluginExecutionException(
                    PluginHttpStatusCode.InternalServerError,
                    $"An unexpected error occurred in BlockOrderCreateOnCreditHold: {ex.Message}",
                    ex);
            }
        }
    }
}
