using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace DotNetBackendTemplate.UnitTests.Architecture.Fixtures.Violations.Features.Orders;

// A valid arrangement: used as a registration probe without coupling mapping to delegate ownership.
internal static class SplitSlice
{
    public static void MapEndpoint(RouteGroupBuilder group) => group.MapGet("/split", SplitHttpHandler.Handle);
}

internal static class SplitHttpHandler
{
    public static string Handle() => "separate handler";
}
