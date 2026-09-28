using DotNetBackendTemplate.Api.Persistence;
using MediatR;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace DotNetBackendTemplate.Api.Features.Campaigns;

public static class GetCampaign
{
    public sealed record Query(Guid Id) : IRequest<Response?>;

    public sealed record Response(
        Guid Id,
        string Name,
        DateTimeOffset StartsAtUtc,
        DateTimeOffset EndsAtUtc,
        CampaignStatus Status);

    internal sealed class Handler(AppDbContext dbContext)
        : IRequestHandler<Query, Response?>
    {
        public Task<Response?> Handle(Query request, CancellationToken cancellationToken) =>
            dbContext.Campaigns
                .AsNoTracking()
                .Where(campaign => campaign.Id == request.Id)
                .Select(campaign => new Response(
                    campaign.Id,
                    campaign.Name,
                    campaign.StartsAtUtc,
                    campaign.EndsAtUtc,
                    campaign.Status.ToContract()))
                .SingleOrDefaultAsync(cancellationToken);
    }

    public static void MapEndpoint(RouteGroupBuilder group)
    {
        group.MapGet("/{id:guid}", HandleAsync)
            .WithName(nameof(GetCampaign))
            .WithSummary("Get a campaign by ID")
            .WithDescription("Returns the projected campaign details without exposing the EF Core entity.")
            .Produces<Response>()
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status401Unauthorized);
    }

    private static async Task<Results<Ok<Response>, NotFound>> HandleAsync(
        Guid id,
        ISender sender,
        CancellationToken cancellationToken)
    {
        Response? response = await sender.Send(new Query(id), cancellationToken);
        return response is null ? TypedResults.NotFound() : TypedResults.Ok(response);
    }
}
