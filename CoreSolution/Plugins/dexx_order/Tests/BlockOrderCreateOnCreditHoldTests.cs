using System;
using Moq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Xunit;
using Dexx.CoreSolution.Plugins.dexx_order;

namespace Dexx.CoreSolution.Plugins.dexx_order.Tests
{
    public class BlockOrderCreateOnCreditHoldTests
    {
        private (Mock<IServiceProvider> ServiceProvider, Mock<IOrganizationService> OrgService)
            BuildServiceProvider(
                string messageName   = "Create",
                int    stage         = 10,
                string entityName    = "dexx_order",
                Entity target        = null,
                Entity accountEntity = null)
        {
            var mockContext         = new Mock<IPluginExecutionContext>();
            var mockTracingService  = new Mock<ITracingService>();
            var mockServiceFactory  = new Mock<IOrganizationServiceFactory>();
            var mockOrgService      = new Mock<IOrganizationService>();
            var mockServiceProvider = new Mock<IServiceProvider>();

            mockContext.Setup(c => c.MessageName).Returns(messageName);
            mockContext.Setup(c => c.Stage).Returns(stage);
            mockContext.Setup(c => c.PrimaryEntityName).Returns(entityName);
            mockContext.Setup(c => c.UserId).Returns(Guid.NewGuid());

            var inputParams = new ParameterCollection();
            if (target != null) inputParams["Target"] = target;
            mockContext.Setup(c => c.InputParameters).Returns(inputParams);

            mockServiceFactory
                .Setup(f => f.CreateOrganizationService(It.IsAny<Guid?>()))
                .Returns(mockOrgService.Object);

            if (accountEntity != null)
                mockOrgService
                    .Setup(s => s.Retrieve("account", It.IsAny<Guid>(), It.IsAny<ColumnSet>()))
                    .Returns(accountEntity);

            mockServiceProvider.Setup(sp => sp.GetService(typeof(IPluginExecutionContext))).Returns(mockContext.Object);
            mockServiceProvider.Setup(sp => sp.GetService(typeof(ITracingService))).Returns(mockTracingService.Object);
            mockServiceProvider.Setup(sp => sp.GetService(typeof(IOrganizationServiceFactory))).Returns(mockServiceFactory.Object);

            return (mockServiceProvider, mockOrgService);
        }

        private static Entity BuildOrderTarget(Guid? accountId = null)
        {
            var order = new Entity("dexx_order") { Id = Guid.NewGuid() };
            if (accountId.HasValue)
                order["dexx_accountid"] = new EntityReference("account", accountId.Value);
            return order;
        }

        private static Entity BuildAccount(string name, bool creditOnHold)
        {
            var account = new Entity("account") { Id = Guid.NewGuid() };
            account["name"]        = name;
            account["creditonhold"] = creditOnHold;
            return account;
        }

        [Theory]
        [InlineData("Update", 10, "dexx_order")]
        [InlineData("Create", 20, "dexx_order")]
        [InlineData("Create", 10, "account")]
        public void Execute_WrongPipelinePosition_ExitsWithoutCallingRetrieve(string message, int stage, string entity)
        {
            var (provider, orgService) = BuildServiceProvider(messageName: message, stage: stage, entityName: entity, target: BuildOrderTarget(Guid.NewGuid()));
            new BlockOrderCreateOnCreditHold().Execute(provider.Object);
            orgService.Verify(s => s.Retrieve(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<ColumnSet>()), Times.Never);
        }

        [Fact]
        public void Execute_NoTargetInInputParameters_ExitsWithoutCallingRetrieve()
        {
            var (provider, orgService) = BuildServiceProvider(target: null);
            new BlockOrderCreateOnCreditHold().Execute(provider.Object);
            orgService.Verify(s => s.Retrieve(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<ColumnSet>()), Times.Never);
        }

        [Fact]
        public void Execute_TargetMissingAccountLookup_ExitsWithoutCallingRetrieve()
        {
            var (provider, orgService) = BuildServiceProvider(target: BuildOrderTarget(accountId: null));
            new BlockOrderCreateOnCreditHold().Execute(provider.Object);
            orgService.Verify(s => s.Retrieve(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<ColumnSet>()), Times.Never);
        }

        [Fact]
        public void Execute_AccountNotOnCreditHold_DoesNotThrow()
        {
            var accountId = Guid.NewGuid();
            var (provider, _) = BuildServiceProvider(target: BuildOrderTarget(accountId), accountEntity: BuildAccount("Contoso Ltd", false));
            Assert.Null(Record.Exception(() => new BlockOrderCreateOnCreditHold().Execute(provider.Object)));
        }

        [Fact]
        public void Execute_AccountOnCreditHold_ThrowsInvalidPluginExecutionException()
        {
            var accountId = Guid.NewGuid();
            var (provider, _) = BuildServiceProvider(target: BuildOrderTarget(accountId), accountEntity: BuildAccount("Contoso Ltd", true));
            Assert.Throws<InvalidPluginExecutionException>(() => new BlockOrderCreateOnCreditHold().Execute(provider.Object));
        }

        [Fact]
        public void Execute_AccountOnCreditHold_ExceptionMessageContainsAccountName()
        {
            const string accountName = "Fabrikam Inc";
            var (provider, _) = BuildServiceProvider(target: BuildOrderTarget(Guid.NewGuid()), accountEntity: BuildAccount(accountName, true));
            var ex = Assert.Throws<InvalidPluginExecutionException>(() => new BlockOrderCreateOnCreditHold().Execute(provider.Object));
            Assert.Contains(accountName, ex.Message);
        }

        [Fact]
        public void Execute_AccountOnCreditHold_RetrievesOnlyRequiredColumns()
        {
            var (provider, orgService) = BuildServiceProvider(target: BuildOrderTarget(Guid.NewGuid()), accountEntity: BuildAccount("Contoso Ltd", true));
            try { new BlockOrderCreateOnCreditHold().Execute(provider.Object); } catch { }
            orgService.Verify(s => s.Retrieve("account", It.IsAny<Guid>(),
                It.Is<ColumnSet>(cs => cs.Columns.Contains("creditonhold") && cs.Columns.Contains("name") && cs.Columns.Count == 2)), Times.Once);
        }

        [Fact]
        public void Execute_HappyPath_RetrievesAccountByCorrectId()
        {
            var accountId = Guid.NewGuid();
            var (provider, orgService) = BuildServiceProvider(target: BuildOrderTarget(accountId), accountEntity: BuildAccount("Contoso Ltd", false));
            new BlockOrderCreateOnCreditHold().Execute(provider.Object);
            orgService.Verify(s => s.Retrieve("account", accountId, It.IsAny<ColumnSet>()), Times.Once);
        }

        [Fact]
        public void Execute_RetrieveThrowsUnexpectedException_WrapsInInvalidPluginExecutionException()
        {
            var (provider, orgService) = BuildServiceProvider(target: BuildOrderTarget(Guid.NewGuid()));
            orgService.Setup(s => s.Retrieve(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<ColumnSet>())).Throws(new TimeoutException("Timed out."));
            var ex = Assert.Throws<InvalidPluginExecutionException>(() => new BlockOrderCreateOnCreditHold().Execute(provider.Object));
            Assert.IsType<TimeoutException>(ex.InnerException);
        }

        [Fact]
        public void Execute_RetrieveThrowsUnexpectedException_ExceptionMessageContainsOriginalMessage()
        {
            const string originalMessage = "Network failure during Retrieve.";
            var (provider, orgService) = BuildServiceProvider(target: BuildOrderTarget(Guid.NewGuid()));
            orgService.Setup(s => s.Retrieve(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<ColumnSet>())).Throws(new InvalidOperationException(originalMessage));
            var ex = Assert.Throws<InvalidPluginExecutionException>(() => new BlockOrderCreateOnCreditHold().Execute(provider.Object));
            Assert.Contains(originalMessage, ex.Message);
        }

        [Fact]
        public void Execute_RetrieveThrowsInvalidPluginExecutionException_PropagatesAsIs()
        {
            var original = new InvalidPluginExecutionException(PluginHttpStatusCode.BadRequest, "Deliberate IPEE.");
            var (provider, orgService) = BuildServiceProvider(target: BuildOrderTarget(Guid.NewGuid()));
            orgService.Setup(s => s.Retrieve(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<ColumnSet>())).Throws(original);
            var ex = Assert.Throws<InvalidPluginExecutionException>(() => new BlockOrderCreateOnCreditHold().Execute(provider.Object));
            Assert.Same(original, ex);
        }
    }
}
