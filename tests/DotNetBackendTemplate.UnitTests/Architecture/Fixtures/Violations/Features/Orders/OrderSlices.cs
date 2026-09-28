using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace DotNetBackendTemplate.UnitTests.Architecture.Fixtures.Violations.Features.Orders;

// SLICE-08: a slice whose MapEndpoint is never called, so its endpoint silently does not exist.
internal static class UnregisteredSlice
{
    public static void MapEndpoint(RouteGroupBuilder group) => group.MapGet("/unregistered", HandleAsync);

    private static string HandleAsync() => "never mapped";
}

// DEP-08: a handler that dispatches another request, hiding cross-feature coordination.
internal static class NotifyOrder
{
    internal sealed record Command(Guid Id) : IRequest;

    internal sealed class Handler(ISender sender) : IRequestHandler<Command>
    {
        public Task Handle(Command request, CancellationToken cancellationToken) =>
            sender.Send(new PlaceOrder.Command(request.Id), cancellationToken);
    }
}

// TIME-02: a response exposing a DateTime instead of a UTC DateTimeOffset.
internal sealed record DatedResponse(DateTime At);
