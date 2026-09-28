using System.Linq.Expressions;
using DotNetBackendTemplate.Api.Persistence.Entities;
using DotNetBackendTemplate.UnitTests.Architecture.Fixtures.Violations;
using DotNetBackendTemplate.UnitTests.Architecture.Fixtures.Violations.Features.Orders;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace DotNetBackendTemplate.UnitTests.Architecture;

public sealed class EnforcementRegressionTests
{
    [Fact]
    public void CreatingAnUnusedStrategyDoesNotExemptTransactionsOrMultipleSaves()
    {
        List<CallSite> sites = CallsForCommand();
        sites.Add(new(typeof(CancelOrder.Handler).FullName!, FixtureRoot.Namespace,
            "Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade", "CreateExecutionStrategy", "unrelated method"));

        List<ArchitectureViolation> violations = CheckCalls(sites);
        ArchitectureAssert.Reports(violations, "PERS-18", "Handle");
        ArchitectureAssert.Reports(violations, "PERS-19", "CancelOrder");
    }

    [Fact]
    public void ReviewedExceptionsAreExactMethodAndRuleScoped()
    {
        List<CallSite> sites = CallsForCommand();
        string method = sites[0].CallerMethod;
        ReviewedMethodException[] exceptions =
        [
            new("PERS-18", method, "Explicit atomic operation", "docs/adr/example.md", "AtomicRollback"),
            new("PERS-19", method, "Two saves share the transaction", "docs/adr/example.md", "AtomicRollback"),
            new("PERS-05", method, "External correlation ID, never an entity key", "docs/adr/example.md", "CorrelationIsNotAKey"),
        ];

        CheckCalls(sites, exceptions).Should().BeEmpty();
        CheckCalls(sites.Select(site => site with { CallerMethod = method + "OtherOverload" }).ToList(), exceptions)
            .Select(violation => violation.RuleId).Should().Contain(["PERS-18", "PERS-19", "PERS-05"]);
        CheckCalls(sites, [exceptions[0] with { Reason = "" }]).Should().Contain(violation => violation.RuleId == "PERS-18");
    }

    [Fact]
    public void AsyncCallsKeepTheFullOriginalMethodIdentity()
    {
        CallSite[] sites = [.. CallSites.Read(typeof(FixtureRoot).Assembly)
            .Where(site => site.Caller == typeof(CancelOrder.Handler).FullName && site.CalleeName == "SaveChangesAsync")];
        sites.Should().HaveCount(2);
        sites.Should().OnlyContain(site => site.CallerMethod.Contains("::Handle(", StringComparison.Ordinal)
            && site.CallerMethod.Contains("System.Threading.CancellationToken", StringComparison.Ordinal));
    }

    [Fact]
    public void RequestNamesMustDeclareCommandOrQueryIntent()
    {
        ArchitectureAssert.Reports(MethodCallRules.Check([], [typeof(ReadHandler)], "Fixture", new HashSet<string>(StringComparer.Ordinal)),
            "SLICE-03", nameof(ReadHandler));
    }

    [Fact]
    public void SingleSaveCommandIsAllowed()
    {
        CheckCalls(CallsForCommand().Where(site => site.CalleeName == "SaveChangesAsync").Take(1).ToList()).Should().BeEmpty();
    }

    [Fact]
    public void QueryWithDirectNoTrackingAndNoSavePassesThePartialCheck()
    {
        Type handler = typeof(GetOrder.Handler);
        CallSite[] sites = [new(handler.FullName!, handler.Namespace!,
            "Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions", "AsNoTracking", "Handle")];
        MethodCallRules.Check(sites, [handler], FixtureRoot.Namespace, new HashSet<string>(StringComparer.Ordinal)).Should().BeEmpty();
    }

    [Fact]
    public void QueryFilterRequiresMandatoryActiveRowCondition()
    {
        Expression<Func<Campaign, bool>> active = row => !row.IsDeleted;
        Expression<Func<Campaign, bool>> combined = row => row.Name != "hidden" && row.IsDeleted == false;
        Expression<Func<Campaign, bool>> unrelated = row => row.Name != "hidden";
        Expression<Func<Campaign, bool>> reversed = row => row.IsDeleted;
        Expression<Func<Campaign, bool>> optional = row => !row.IsDeleted || row.Name == "visible";
        Expression<Func<Campaign, bool>> otherRow = row => !new Campaign { Name = "other" }.IsDeleted;

        PersistenceModelRules.RequiresActiveRow(active).Should().BeTrue();
        PersistenceModelRules.RequiresActiveRow(combined).Should().BeTrue();
        foreach (LambdaExpression filter in new LambdaExpression[] { unrelated, reversed, optional, otherRow })
        {
            PersistenceModelRules.RequiresActiveRow(filter).Should().BeFalse();
        }
    }

    [Theory]
    [InlineData("[IsDeleted] = 0", true)]
    [InlineData(" ([IsDeleted] = (0)) ", true)]
    [InlineData("[IsDeleted] = 1", false)]
    [InlineData("[IsDeleted] = 0 OR [Code] = 'public'", false)]
    [InlineData("[Code] IS NOT NULL", false)]
    [InlineData(null, false)]
    public void IndexFilterMustSelectActiveRows(string? filter, bool expected) =>
        PersistenceModelRules.IsActiveRowIndexFilter(filter).Should().Be(expected);

    [Fact]
    public async Task SplitSliceCanUseASeparateTopLevelHttpHandler()
    {
        await using WebApplication app = WebApplication.CreateSlimBuilder().Build();
        SplitSlice.MapEndpoint(app.MapGroup("/api/orders"));
        IReadOnlyList<ArchitectureViolation> present = await EndpointRules.CheckRegistrationAsync(
            [typeof(SplitSlice)], ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints), FixtureRoot.Namespace);
        present.Should().BeEmpty();
        IReadOnlyList<ArchitectureViolation> absent = await EndpointRules.CheckRegistrationAsync([typeof(SplitSlice)], [], FixtureRoot.Namespace);
        ArchitectureAssert.Reports(absent, "SLICE-08", nameof(SplitSlice));
    }

    [Theory]
    [InlineData(".WithMessage(_ => \"English\")")]
    [InlineData(".WithMessage(message)")]
    public void NonLiteralValidatorMessagesRemainAReview(string source) =>
        LocalizationRules.CheckValidatorMessages([("Feature.cs", source)]).Should().BeEmpty();

    [Theory]
    [InlineData("messages[\"Missing\"]")]
    [InlineData("localizer[key]")]
    public void NonstandardLocalizerAccessRemainsAReview(string source) =>
        LocalizationRules.CheckUsedKeys([("Feature.cs", source)], new HashSet<string>(StringComparer.Ordinal)).Should().BeEmpty();

    private static List<CallSite> CallsForCommand()
    {
        string caller = typeof(CancelOrder.Handler).FullName!;
        string ns = typeof(CancelOrder.Handler).Namespace!;
        const string Method = "System.Threading.Tasks.Task`1<System.Guid> Fixture.CancelOrder/Handler::Handle(Fixture.Command,System.Threading.CancellationToken)";
        return
        [
            new(caller, ns, "Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade", "BeginTransactionAsync", Method),
            new(caller, ns, "Microsoft.EntityFrameworkCore.DbContext", "SaveChangesAsync", Method),
            new(caller, ns, "Microsoft.EntityFrameworkCore.DbContext", "SaveChangesAsync", Method),
            new(caller, ns, "System.Guid", "NewGuid", Method),
        ];
    }

    private static List<ArchitectureViolation> CheckCalls(List<CallSite> sites, IReadOnlyList<ReviewedMethodException>? exceptions = null) =>
        [.. MethodCallRules.Check(sites, [typeof(CancelOrder.Handler)], FixtureRoot.Namespace, new HashSet<string>(StringComparer.Ordinal), exceptions)];

    private sealed record Read : IRequest<int>;

    private sealed class ReadHandler : IRequestHandler<Read, int>
    {
        public Task<int> Handle(Read request, CancellationToken cancellationToken) => Task.FromResult(0);
    }
}
