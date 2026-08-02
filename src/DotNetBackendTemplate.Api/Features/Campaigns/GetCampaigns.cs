using DotNetBackendTemplate.Api.Persistence;
using DotNetBackendTemplate.Api.Persistence.Entities;
using MediatR;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DotNetBackendTemplate.Api.Features.Campaigns;

public static class GetCampaigns
{
    public sealed class Parameters
    {
        [FromQuery(Name = "page")]
        public int Page { get; init; } = 1;

        [FromQuery(Name = "pageSize")]
        public int PageSize { get; init; } = 20;
    }

    public sealed record Query(int Page, int PageSize) : IRequest<Response>;

    public sealed record Item(
        Guid Id,
        string Name,
        CampaignStatus Status,
        DateTimeOffset StartsAtUtc,
        DateTimeOffset EndsAtUtc);

    public sealed record Response(
        IReadOnlyList<Item> Items,
        int Page,
        int PageSize,
        int TotalCount);

    internal sealed class Handler(AppDbContext dbContext)
        : IRequestHandler<Query, Response>
    {
        public async Task<Response> Handle(Query request, CancellationToken cancellationToken)
        {
            int page = Math.Max(request.Page, 1);
            int pageSize = Math.Clamp(request.PageSize, 1, 100);
            IQueryable<Campaign> campaigns = dbContext.Campaigns.AsNoTracking();

            int totalCount = await campaigns.CountAsync(cancellationToken);
            List<Item> items = await campaigns
                .OrderByDescending(campaign => campaign.CreatedAtUtc)
                .ThenBy(campaign => campaign.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(campaign => new Item(
                    campaign.Id,
                    campaign.Name,
                    campaign.Status,
                    campaign.StartsAtUtc,
                    campaign.EndsAtUtc))
                .ToListAsync(cancellationToken);

            return new Response(items, page, pageSize, totalCount);
        }
    }

    public static void MapEndpoint(RouteGroupBuilder group)
    {
        group.MapGet("", HandleAsync)
            .WithName(nameof(GetCampaigns))
            .WithSummary("List campaigns")
            .WithDescription(
                "Returns a paginated campaign projection. page defaults to 1 and pageSize defaults to 20 with a maximum of 100.")
            .Produces<Response>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);
    }

    private static async Task<Ok<Response>> HandleAsync(
        [AsParameters] Parameters parameters,
        ISender sender,
        CancellationToken cancellationToken)
    {
        Response response = await sender.Send(
            new Query(parameters.Page, parameters.PageSize),
            cancellationToken);

        return TypedResults.Ok(response);
    }
}
