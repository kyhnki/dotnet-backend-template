using DotNetBackendTemplate.Api.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DotNetBackendTemplate.Api.Persistence.Configurations;

internal sealed class CampaignConfiguration : IEntityTypeConfiguration<Campaign>
{
    public void Configure(EntityTypeBuilder<Campaign> builder)
    {
        builder.ToTable("Campaigns");
        builder.HasKey(campaign => campaign.Id);

        builder.Property(campaign => campaign.Name)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(campaign => campaign.StartsAtUtc)
            .HasColumnType("datetimeoffset")
            .IsRequired();

        builder.Property(campaign => campaign.EndsAtUtc)
            .HasColumnType("datetimeoffset")
            .IsRequired();

        builder.Property(campaign => campaign.Status)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(campaign => campaign.CreatedAtUtc)
            .HasColumnType("datetimeoffset")
            .IsRequired();

        builder.HasIndex(campaign => new
        {
            campaign.Status,
            campaign.StartsAtUtc,
            campaign.EndsAtUtc,
        });
    }
}
