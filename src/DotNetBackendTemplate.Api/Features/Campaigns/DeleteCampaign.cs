using DotNetBackendTemplate.Api.Infrastructure.Authorization;
using DotNetBackendTemplate.Api.Persistence;
using DotNetBackendTemplate.Api.Persistence.Entities;
using MediatR;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace DotNetBackendTemplate.Api.Features.Campaigns;

public static class DeleteCampaign
{
    public sealed record Command(Guid Id) : IRequest<bool>;

    internal sealed class Handler(AppDbContext dbContext)
        : IRequestHandler<Command, bool>
    {
        public async Task<bool> Handle(Command request, CancellationToken cancellationToken)
        {
            // The soft-delete query filter excludes already-deleted rows, so deleting twice
            // reports 404 rather than silently succeeding.
            Campaign? campaign = await dbContext.Campaigns
                .SingleOrDefaultAsync(item => item.Id == request.Id, cancellationToken);

            if (campaign is null)
            {
                return false;
            }

            dbContext.Campaigns.Remove(campaign);
            await dbContext.SaveChangesAsync(cancellationToken);

            return true;
        }
    }

    public static void MapEndpoint(RouteGroupBuilder group)
    {
        group.MapDelete("/{id:guid}", HandleAsync)
            .WithName(nameof(DeleteCampaign))
            .WithSummary("Delete a campaign")
            .WithDescription(
                "Soft-deletes a campaign. The row is retained for audit and recovery but is excluded from every campaign query.")
            .RequireAuthorization(AuthorizationPolicies.CampaignWrite)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<Results<NoContent, NotFound>> HandleAsync(
        Guid id,
        ISender sender,
        CancellationToken cancellationToken)
    {
        bool deleted = await sender.Send(new Command(id), cancellationToken);

        return deleted ? TypedResults.NoContent() : TypedResults.NotFound();
    }
}
