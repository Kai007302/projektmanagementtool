using Microsoft.EntityFrameworkCore;
using ProjectHub.Api.Infrastructure.Database;
using ProjectHub.Api.Infrastructure.Http;
using ProjectHub.Api.Modules.Identity;

namespace ProjectHub.Api.Modules.Organizations;

public sealed record OrganizationResponse(Guid Id, string Name, string Slug);

public static class OrganizationEndpoints
{
    public static RouteGroupBuilder MapOrganizationEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/organization", async (UserContext user, ProjectHubDbContext db, CancellationToken ct) =>
        {
            var organization = await db.Set<Organization>().AsNoTracking()
                .Where(o => o.Id == user.OrganizationId)
                .Select(o => new OrganizationResponse(o.Id, o.Name, o.Slug))
                .SingleOrDefaultAsync(ct);

            return organization is null ? ApiResults.NotFound("Organization") : Results.Ok(organization);
        });

        return api;
    }
}
