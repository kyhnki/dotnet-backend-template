using System.Diagnostics;
using EntityCampaignStatus = DotNetBackendTemplate.Api.Persistence.Entities.CampaignStatus;

namespace DotNetBackendTemplate.Api.Features.Campaigns;

/// <summary>
/// The campaign status exposed by the API. It is kept separate from the persistence enum so that a
/// storage change cannot silently change the public contract (SLICE-04).
/// </summary>
public enum CampaignStatus
{
    Draft = 0,
}

public static class CampaignStatusMapping
{
    public static CampaignStatus ToContract(this EntityCampaignStatus status) => status switch
    {
        EntityCampaignStatus.Draft => CampaignStatus.Draft,
        _ => throw new UnreachableException($"Campaign status '{status}' has no API contract value."),
    };
}
