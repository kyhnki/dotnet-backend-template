using DotNetBackendTemplate.Api.Features.Campaigns;
using EntityCampaignStatus = DotNetBackendTemplate.Api.Persistence.Entities.CampaignStatus;

namespace DotNetBackendTemplate.UnitTests.Campaigns;

public sealed class CampaignStatusMappingTests
{
    public static TheoryData<EntityCampaignStatus> EntityStatuses => new(Enum.GetValues<EntityCampaignStatus>());

    // A persistence status added without a contract mapping would otherwise surface as a 500 at runtime.
    [Theory]
    [MemberData(nameof(EntityStatuses))]
    public void EveryPersistenceStatusMapsToADefinedContractStatus(EntityCampaignStatus status)
    {
        CampaignStatus contract = status.ToContract();

        Enum.IsDefined(contract).Should().BeTrue();
    }
}
