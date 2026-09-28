using Microsoft.AspNetCore.Http;

namespace DotNetBackendTemplate.UnitTests.Architecture.Fixtures.Violations.Persistence.Entities;

// DEP-02: an entity that refers to an HTTP concern. Also the persistence type the SLICE-04 fixtures leak.
internal sealed class Order
{
    public Guid Id { get; set; }

    public HttpContext? Context { get; set; }
}

internal enum OrderStatus
{
    Open = 0,
}
