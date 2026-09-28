using DotNetBackendTemplate.UnitTests.Architecture.Fixtures.Violations.Features.Orders;
using MediatR;

namespace DotNetBackendTemplate.UnitTests.Architecture.Fixtures.Violations.Infrastructure;

// DEP-01 through a method signature.
internal static class InfrastructureSignatureLeak
{
    public static PlaceOrder.Response Echo(PlaceOrder.Response response) => response;
}

// DEP-01 only inside an async method body, which the compiler moves into a state machine type.
internal static class InfrastructureAsyncBodyLeak
{
    public static async Task<int> CountAsync()
    {
        await Task.Yield();
        return new PlaceOrder.Response(Guid.Empty, null).GetHashCode();
    }
}

// DEP-01 only inside a lambda, which the compiler moves into a closure type.
internal static class InfrastructureLambdaLeak
{
    public static Func<Guid, object> Factory() => id => new PlaceOrder.Response(id, null);
}

// DEP-01 only as a generic argument.
internal static class InfrastructureGenericArgumentLeak
{
    public static IReadOnlyList<PlaceOrder.Response>? None() => null;
}

// SLICE-01: a handler outside Features.
internal sealed class MisplacedHandler : IRequestHandler<PlaceOrder.Command, Guid>
{
    public Task<Guid> Handle(PlaceOrder.Command request, CancellationToken cancellationToken) =>
        Task.FromResult(request.Id);
}
