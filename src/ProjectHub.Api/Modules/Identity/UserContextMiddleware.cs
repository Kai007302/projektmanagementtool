using Microsoft.EntityFrameworkCore;
using ProjectHub.Api.Infrastructure.Database;
using ProjectHub.Api.Infrastructure.Http;
using ProjectHub.Api.Modules.Organizations;
using ProjectHub.Api.Modules.Users;

namespace ProjectHub.Api.Modules.Identity;

/// <summary>
/// Resolves the <see cref="UserContext"/> for every /api/v1 request, after authentication and
/// authorization have run. Callers without an active ProjectHub user in their tenant's
/// organization get 403. Users are not provisioned automatically (docs/OPEN_DECISIONS.md, DEC-013).
/// </summary>
internal sealed class UserContextMiddleware(RequestDelegate next)
{
    private const string ItemKey = "ProjectHub.UserContext";

    public async Task InvokeAsync(HttpContext httpContext, ICurrentUser currentUser, ProjectHubDbContext db)
    {
        if (!currentUser.IsAuthenticated || currentUser.TenantId is null || currentUser.EntraObjectId is null)
        {
            await Results.Unauthorized().ExecuteAsync(httpContext);
            return;
        }

        var userContext = await (
                from user in db.Set<AppUser>().AsNoTracking()
                join organization in db.Set<Organization>().AsNoTracking() on user.OrganizationId equals organization.Id
                where organization.EntraTenantId == currentUser.TenantId
                      && user.EntraObjectId == currentUser.EntraObjectId
                      && user.Status == UserStatus.Active
                select new UserContext(user.Id, user.OrganizationId, user.OrganizationRole, user.DisplayName, user.Email))
            .SingleOrDefaultAsync(httpContext.RequestAborted);

        if (userContext is null)
        {
            await ApiResults.Forbidden("No active ProjectHub user exists for this account.").ExecuteAsync(httpContext);
            return;
        }

        httpContext.Items[ItemKey] = userContext;
        await next(httpContext);
    }

    internal static UserContext FromHttpContext(IHttpContextAccessor accessor) =>
        accessor.HttpContext?.Items[ItemKey] as UserContext
        ?? throw new InvalidOperationException("UserContext is only available on /api/v1 endpoints.");
}
