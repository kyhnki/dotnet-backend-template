using DotNetBackendTemplate.UnitTests.Architecture.Fixtures.Violations.Features.Orders;
using FluentValidation;

namespace DotNetBackendTemplate.UnitTests.Architecture.Fixtures.Violations.Common.Behaviors;

// DEP-03: a generic behavior that knows about a concrete feature.
internal static class BehaviorFeatureLeak
{
    public static bool IsOrder(object request) => request is PlaceOrder.Command;
}

// SLICE-01: a feature validator outside Features.
internal sealed class MisplacedValidator : AbstractValidator<PlaceOrder.Command>;
