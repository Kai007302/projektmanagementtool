using Microsoft.EntityFrameworkCore;

namespace ProjectHub.Api.Infrastructure.Database;

/// <summary>
/// Maps onto the schema from database/migrations (ADR 0005); never creates or alters it.
/// Each module contributes its own entity configurations.
/// </summary>
public sealed class ProjectHubDbContext(DbContextOptions<ProjectHubDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ProjectHubDbContext).Assembly);
}
