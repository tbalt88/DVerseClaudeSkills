using System;
using Moq;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Xunit;
using Dexx.CoreSolution.Plugins.account;

namespace Dexx.CoreSolution.Plugins.account.Tests
{
    public class CreateOpportunityOnStatusChangeTests
    {
        private const int TriggerStatusCode = 100000;
        private const int ResetStatusCode   = 1;
        private const int ActiveStateCode   = 0;
        private const int FollowUpDays      = 15;

        private static (Mock<IOrganizationService> OrgService, Mock<ITracingService> TracingService)
            BuildMocks(string accountName, int statusCode)
        {
            var mockOrgService     = new Mock<IOrganizationService>();
            var mockTracingService = new Mock<ITracingService>();

            var accountEntity = new Entity("account") { Id = Guid.NewGuid() };
            accountEntity["name"]       = accountName;
            accountEntity["statuscode"] = new OptionSetValue(statusCode);

            mockOrgService.Setup(s => s.Retrieve("account", It.IsAny<Guid>(), It.IsAny<ColumnSet>())).Returns(accountEntity);
            mockOrgService.Setup(s => s.Create(It.Is<Entity>(e => e.LogicalName == "opportunity"))).Returns(Guid.NewGuid());
            mockOrgService.Setup(s => s.Create(It.Is<Entity>(e => e.LogicalName == "task"))).Returns(Guid.NewGuid());

            return (mockOrgService, mockTracingService);
        }

        private static CreateOpportunityOnStatusChange CreateSut() => new CreateOpportunityOnStatusChange();

        [Fact]
        public void ExecuteBusinessLogic_StatusCodeNotTriggerValue_ReturnsFalseSuccess()
        {
            var (orgService, tracing) = BuildMocks("Contoso Ltd", 1);
            var result = CreateSut().ExecuteBusinessLogic(Guid.NewGuid(), orgService.Object, tracing.Object);
            Assert.False(result.Success);
        }

        [Fact]
        public void ExecuteBusinessLogic_StatusCodeNotTriggerValue_DoesNotCreateAnyRecord()
        {
            var (orgService, tracing) = BuildMocks("Contoso Ltd", 1);
            CreateSut().ExecuteBusinessLogic(Guid.NewGuid(), orgService.Object, tracing.Object);
            orgService.Verify(s => s.Create(It.IsAny<Entity>()), Times.Never);
        }

        [Fact]
        public void ExecuteBusinessLogic_StatusCodeNotTriggerValue_DoesNotResetAccountStatus()
        {
            var (orgService, tracing) = BuildMocks("Contoso Ltd", 1);
            CreateSut().ExecuteBusinessLogic(Guid.NewGuid(), orgService.Object, tracing.Object);
            orgService.Verify(s => s.Execute(It.IsAny<OrganizationRequest>()), Times.Never);
        }

        [Fact]
        public void ExecuteBusinessLogic_ValidStatusCode_CreatesOpportunity()
        {
            var (orgService, tracing) = BuildMocks("Contoso Ltd", TriggerStatusCode);
            CreateSut().ExecuteBusinessLogic(Guid.NewGuid(), orgService.Object, tracing.Object);
            orgService.Verify(s => s.Create(It.Is<Entity>(e => e.LogicalName == "opportunity")), Times.Once);
        }

        [Fact]
        public void ExecuteBusinessLogic_ValidStatusCode_OpportunityNameContainsAccountName()
        {
            const string accountName = "Fabrikam Inc";
            var (orgService, tracing) = BuildMocks(accountName, TriggerStatusCode);
            Entity captured = null;
            orgService.Setup(s => s.Create(It.Is<Entity>(e => e.LogicalName == "opportunity"))).Callback<Entity>(e => captured = e).Returns(Guid.NewGuid());
            CreateSut().ExecuteBusinessLogic(Guid.NewGuid(), orgService.Object, tracing.Object);
            Assert.Contains(accountName, captured?["name"]?.ToString());
        }

        [Fact]
        public void ExecuteBusinessLogic_ValidStatusCode_OpportunityEstimatedCloseDateIs15DaysFromNow()
        {
            var (orgService, tracing) = BuildMocks("Contoso Ltd", TriggerStatusCode);
            Entity captured = null;
            orgService.Setup(s => s.Create(It.Is<Entity>(e => e.LogicalName == "opportunity"))).Callback<Entity>(e => captured = e).Returns(Guid.NewGuid());
            CreateSut().ExecuteBusinessLogic(Guid.NewGuid(), orgService.Object, tracing.Object);
            Assert.Equal(DateTime.UtcNow.Date.AddDays(FollowUpDays), (DateTime?)captured?["estimatedclosedate"]);
        }

        [Fact]
        public void ExecuteBusinessLogic_ValidStatusCode_OpportunityLinkedToAccount()
        {
            var accountId = Guid.NewGuid();
            var (orgService, tracing) = BuildMocks("Contoso Ltd", TriggerStatusCode);
            Entity captured = null;
            orgService.Setup(s => s.Create(It.Is<Entity>(e => e.LogicalName == "opportunity"))).Callback<Entity>(e => captured = e).Returns(Guid.NewGuid());
            CreateSut().ExecuteBusinessLogic(accountId, orgService.Object, tracing.Object);
            var parentRef = captured?["parentaccountid"] as EntityReference;
            Assert.NotNull(parentRef);
            Assert.Equal(accountId, parentRef.Id);
        }

        [Fact]
        public void ExecuteBusinessLogic_ValidStatusCode_CreatesFollowUpTask()
        {
            var (orgService, tracing) = BuildMocks("Contoso Ltd", TriggerStatusCode);
            CreateSut().ExecuteBusinessLogic(Guid.NewGuid(), orgService.Object, tracing.Object);
            orgService.Verify(s => s.Create(It.Is<Entity>(e => e.LogicalName == "task")), Times.Once);
        }

        [Fact]
        public void ExecuteBusinessLogic_ValidStatusCode_FollowUpTaskRegardingIsOpportunity()
        {
            var (orgService, tracing) = BuildMocks("Contoso Ltd", TriggerStatusCode);
            var opportunityId = Guid.NewGuid();
            orgService.Setup(s => s.Create(It.Is<Entity>(e => e.LogicalName == "opportunity"))).Returns(opportunityId);
            Entity capturedTask = null;
            orgService.Setup(s => s.Create(It.Is<Entity>(e => e.LogicalName == "task"))).Callback<Entity>(e => capturedTask = e).Returns(Guid.NewGuid());
            CreateSut().ExecuteBusinessLogic(Guid.NewGuid(), orgService.Object, tracing.Object);
            var regardingRef = capturedTask?["regardingobjectid"] as EntityReference;
            Assert.NotNull(regardingRef);
            Assert.Equal(opportunityId, regardingRef.Id);
        }

        [Fact]
        public void ExecuteBusinessLogic_ValidStatusCode_ResetsAccountStatusViaSetStateRequest()
        {
            var accountId = Guid.NewGuid();
            var (orgService, tracing) = BuildMocks("Contoso Ltd", TriggerStatusCode);
            CreateSut().ExecuteBusinessLogic(accountId, orgService.Object, tracing.Object);
            orgService.Verify(s => s.Execute(It.Is<SetStateRequest>(r =>
                r.EntityMoniker.Id == accountId &&
                r.State.Value      == ActiveStateCode &&
                r.Status.Value     == ResetStatusCode)), Times.Once);
        }

        [Fact]
        public void ExecuteBusinessLogic_ValidStatusCode_ReturnsTrueSuccess()
        {
            var (orgService, tracing) = BuildMocks("Contoso Ltd", TriggerStatusCode);
            var result = CreateSut().ExecuteBusinessLogic(Guid.NewGuid(), orgService.Object, tracing.Object);
            Assert.True(result.Success);
        }

        [Fact]
        public void ExecuteBusinessLogic_ValidStatusCode_ReturnsOpportunityReference()
        {
            var (orgService, tracing) = BuildMocks("Contoso Ltd", TriggerStatusCode);
            var opportunityId = Guid.NewGuid();
            orgService.Setup(s => s.Create(It.Is<Entity>(e => e.LogicalName == "opportunity"))).Returns(opportunityId);
            var result = CreateSut().ExecuteBusinessLogic(Guid.NewGuid(), orgService.Object, tracing.Object);
            Assert.NotNull(result.OpportunityRef);
            Assert.Equal(opportunityId, result.OpportunityRef.Id);
        }

        [Fact]
        public void ExecuteBusinessLogic_RetrieveThrows_WrapsInInvalidPluginExecutionException()
        {
            var (orgService, tracing) = BuildMocks("Contoso Ltd", TriggerStatusCode);
            orgService.Setup(s => s.Retrieve(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<ColumnSet>())).Throws(new TimeoutException("Timed out."));
            var ex = Assert.Throws<InvalidPluginExecutionException>(() => CreateSut().ExecuteBusinessLogic(Guid.NewGuid(), orgService.Object, tracing.Object));
            Assert.IsType<TimeoutException>(ex.InnerException);
        }

        [Fact]
        public void ExecuteBusinessLogic_OpportunityCreateThrows_WrapsInInvalidPluginExecutionException()
        {
            var (orgService, tracing) = BuildMocks("Contoso Ltd", TriggerStatusCode);
            orgService.Setup(s => s.Create(It.Is<Entity>(e => e.LogicalName == "opportunity"))).Throws(new InvalidOperationException("Create failed."));
            var ex = Assert.Throws<InvalidPluginExecutionException>(() => CreateSut().ExecuteBusinessLogic(Guid.NewGuid(), orgService.Object, tracing.Object));
            Assert.IsType<InvalidOperationException>(ex.InnerException);
        }

        [Fact]
        public void ExecuteBusinessLogic_SetStateThrows_WrapsInInvalidPluginExecutionException()
        {
            var (orgService, tracing) = BuildMocks("Contoso Ltd", TriggerStatusCode);
            orgService.Setup(s => s.Execute(It.IsAny<SetStateRequest>())).Throws(new InvalidOperationException("SetState failed."));
            var ex = Assert.Throws<InvalidPluginExecutionException>(() => CreateSut().ExecuteBusinessLogic(Guid.NewGuid(), orgService.Object, tracing.Object));
            Assert.IsType<InvalidOperationException>(ex.InnerException);
        }

        [Fact]
        public void ExecuteBusinessLogic_ThrowsInvalidPluginExecutionException_PropagatesAsIs()
        {
            var original = new InvalidPluginExecutionException("Deliberate IPEE.");
            var (orgService, tracing) = BuildMocks("Contoso Ltd", TriggerStatusCode);
            orgService.Setup(s => s.Retrieve(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<ColumnSet>())).Throws(original);
            var ex = Assert.Throws<InvalidPluginExecutionException>(() => CreateSut().ExecuteBusinessLogic(Guid.NewGuid(), orgService.Object, tracing.Object));
            Assert.Same(original, ex);
        }
    }
}
