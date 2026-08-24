using DotNetBackendTemplate.Api.Infrastructure.Authorization;
using DotNetBackendTemplate.Api.Infrastructure.Localization;
using DotNetBackendTemplate.Api.Persistence;
using DotNetBackendTemplate.Api.Persistence.Entities;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Localization;

namespace DotNetBackendTemplate.Api.Features.Campaigns;

public static class CreateCampaign
{
    public sealed record Request(
        string Name,
        DateTimeOffset StartsAtUtc,
        DateTimeOffset EndsAtUtc);

    public sealed record Command(
        string Name,
        DateTimeOffset StartsAtUtc,
        DateTimeOffset EndsAtUtc) : IRequest<Response>;

    public sealed record Response(Guid Id);

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

    internal sealed class Handler(AppDbContext dbContext, TimeProvider timeProvider)
        : IRequestHandler<Command, Response>
    {
        public async Task<Response> Handle(Command request, CancellationToken cancellationToken)
        {
            // Id is left unset: EF assigns a SQL Server-ordered sequential GUID when the entity
            // is added, which keeps the key available before SaveChanges for auditing.
            Campaign campaign = new()
            {
                Name = request.Name.Trim(),
                StartsAtUtc = request.StartsAtUtc,
                EndsAtUtc = request.EndsAtUtc,
                Status = CampaignStatus.Draft,
                CreatedAtUtc = timeProvider.GetUtcNow(),
            };

            dbContext.Campaigns.Add(campaign);
            await dbContext.SaveChangesAsync(cancellationToken);

            return new Response(campaign.Id);
        }
    }

    public static void MapEndpoint(RouteGroupBuilder group)
    {
        group.MapPost("", HandleAsync)
            .WithName(nameof(CreateCampaign))
            .WithSummary("Create a draft campaign")
            .WithDescription(
                "Creates a draft campaign. startsAtUtc and endsAtUtc must use the explicit UTC offset +00:00.")
            .RequireAuthorization(AuthorizationPolicies.CampaignWrite)
            .Produces<Response>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);
    }

    private static async Task<Created<Response>> HandleAsync(
        Request request,
        ISender sender,
        CancellationToken cancellationToken)
    {
        Command command = new(request.Name, request.StartsAtUtc, request.EndsAtUtc);
        Response response = await sender.Send(command, cancellationToken);

        return TypedResults.Created($"/api/v1/campaigns/{response.Id}", response);
    }
}
