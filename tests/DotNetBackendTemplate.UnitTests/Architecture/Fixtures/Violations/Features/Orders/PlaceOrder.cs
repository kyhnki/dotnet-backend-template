using DotNetBackendTemplate.UnitTests.Architecture.Fixtures.Violations.Features.Invoices;
using DotNetBackendTemplate.UnitTests.Architecture.Fixtures.Violations.Persistence.Entities;
using MediatR;

namespace DotNetBackendTemplate.UnitTests.Architecture.Fixtures.Violations.Features.Orders;

// DEP-05: the Orders feature reaches into the Invoices feature.
internal static class PlaceOrder
{
    internal sealed record Command(Guid Id) : IRequest<Guid>;

    internal sealed record Response(Guid Id, InvoiceSummary? Invoice);
}

// SLICE-04: responses exposing a persistence type directly, inside a collection, and as an enum.
internal sealed record OrderResponse(Order Order);

internal sealed record OrderPage(IReadOnlyList<Order> Items);

internal sealed record OrderStatusResponse(OrderStatus Status);

// PERS-11: an EF entity declared in a feature instead of under Persistence.
internal sealed class EntityOutsidePersistence
{
    public Guid Id { get; set; }
}
