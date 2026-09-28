using DotNetBackendTemplate.UnitTests.Architecture.Fixtures.Violations.Persistence;
using MediatR;
using Microsoft.AspNetCore.Authorization;

namespace DotNetBackendTemplate.UnitTests.Architecture.Fixtures.Violations.Features.Orders;

// SLICE-03: a query handler that saves and reads with change tracking.
internal static class GetOrder
{
    internal sealed record Query(Guid Id) : IRequest<Guid>;

    internal sealed class Handler(ViolatingDbContext database) : IRequestHandler<Query, Guid>
    {
        public async Task<Guid> Handle(Query request, CancellationToken cancellationToken)
        {
            await database.SaveChangesAsync(cancellationToken);
            return request.Id;
        }
    }
}

// PERS-19, PERS-05, TIME-01, SEC-02: a command handler that saves twice without a transaction, assigns its own
// key, reads the system clock directly, and builds a role requirement outside Infrastructure.
internal static class CancelOrder
{
    internal sealed record Command(Guid Id) : IRequest<Guid>;

    internal sealed class Handler(ViolatingDbContext database) : IRequestHandler<Command, Guid>
    {
        public async Task<Guid> Handle(Command request, CancellationToken cancellationToken)
        {
            Guid key = Guid.NewGuid();
            DateTimeOffset now = TimeProvider.System.GetUtcNow();
            _ = new AuthorizationPolicyBuilder().RequireRole("OrderManager");

            await database.SaveChangesAsync(cancellationToken);
            await database.SaveChangesAsync(cancellationToken);
            return now.Offset == TimeSpan.Zero ? key : request.Id;
        }
    }
}
