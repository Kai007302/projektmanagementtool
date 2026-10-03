using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ProjectHub.Api.Modules.Organizations;

public sealed class Organization
{
    public Guid Id { get; init; }
    public required string Name { get; set; }
    public required string Slug { get; set; }
    public string? EntraTenantId { get; set; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long Version { get; set; }
}

internal sealed class OrganizationConfiguration : IEntityTypeConfiguration<Organization>
{
    public void Configure(EntityTypeBuilder<Organization> builder)
    {
        builder.ToTable("organization");
        builder.Property(o => o.Version).IsConcurrencyToken();
    }
}
