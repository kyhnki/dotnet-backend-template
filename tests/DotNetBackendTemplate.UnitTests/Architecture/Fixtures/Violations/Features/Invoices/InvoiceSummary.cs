using DotNetBackendTemplate.UnitTests.Architecture.Fixtures.Violations.Features.Orders;
using MediatR;

namespace DotNetBackendTemplate.UnitTests.Architecture.Fixtures.Violations.Features.Invoices;

internal sealed record InvoiceSummary(Guid Id);

// SLICE-01: a handler in the Invoices area that handles an Orders request.
internal sealed class CrossAreaHandler : IRequestHandler<PlaceOrder.Command, Guid>
{
    public Task<Guid> Handle(PlaceOrder.Command request, CancellationToken cancellationToken) =>
        Task.FromResult(request.Id);
}
