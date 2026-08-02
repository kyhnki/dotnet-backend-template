using DotNetBackendTemplate.Api.Infrastructure.Authorization;
using DotNetBackendTemplate.Api.Infrastructure.Localization;
using DotNetBackendTemplate.Api.Persistence;
using DotNetBackendTemplate.Api.Persistence.Entities;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace DotNetBackendTemplate.Api.Features.Campaigns;

public static class UpdateCampaign
{
    public sealed record Request(
        string Name,
        DateTimeOffset StartsAtUtc,
        DateTimeOffset EndsAtUtc);

    public sealed record Command(
        Guid Id,
        string Name,
        DateTimeOffset StartsAtUtc,
        DateTimeOffset EndsAtUtc) : IRequest<bool>;

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator(IStringLocalizer<ApiResources> localizer)
        {
            RuleFor(command => command.Name)
                .NotEmpty()
                .WithMessage(localizer["CampaignNameRequired"])
                .MaximumLength(200)
                .WithMessage(localizer["CampaignNameMaximumLength", 200]);

            RuleFor(command => command.StartsAtUtc)
                .Must(BeUtc)
                .WithMessage(localizer["CampaignStartsAtUtcMustUseUtcOffset"]);

            RuleFor(command => command.EndsAtUtc)
                .Must(BeUtc)
                .WithMessage(localizer["CampaignEndsAtUtcMustUseUtcOffset"])
                .GreaterThan(command => command.StartsAtUtc)
                .WithMessage(localizer["CampaignEndsAtUtcMustBeAfterStartsAtUtc"]);
        }

        private static bool BeUtc(DateTimeOffset value) => value.Offset == TimeSpan.Zero;
    }

    internal sealed class Handler(AppDbContext dbContext)
        : IRequestHandler<Command, bool>
    {
        public async Task<bool> Handle(Command request, CancellationToken cancellationToken)
        {
            Campaign? campaign = await dbContext.Campaigns
                .SingleOrDefaultAsync(item => item.Id == request.Id, cancellationToken);

            if (campaign is null)
            {
                return false;
            }

            campaign.Name = request.Name.Trim();
            campaign.StartsAtUtc = request.StartsAtUtc;
            campaign.EndsAtUtc = request.EndsAtUtc;
            await dbContext.SaveChangesAsync(cancellationToken);

            return true;
        }
    }

    public static void MapEndpoint(RouteGroupBuilder group)
    {
        group.MapPut("/{id:guid}", HandleAsync)
            .WithName(nameof(UpdateCampaign))
            .WithSummary("Update a campaign")
            .WithDescription(
                "Updates the name and schedule of an existing campaign. startsAtUtc and endsAtUtc must use the explicit UTC offset +00:00.")
            .RequireAuthorization(AuthorizationPolicies.CampaignWrite)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);
    }

    private static async Task<Results<NoContent, NotFound>> HandleAsync(
        Guid id,
        Request request,
        ISender sender,
        CancellationToken cancellationToken)
    {
        Command command = new(id, request.Name, request.StartsAtUtc, request.EndsAtUtc);
        bool updated = await sender.Send(command, cancellationToken);

        return updated ? TypedResults.NoContent() : TypedResults.NotFound();
    }
}
