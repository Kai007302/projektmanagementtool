using Microsoft.EntityFrameworkCore;
using ProjectHub.Api.Infrastructure.Database;
using ProjectHub.Api.Infrastructure.Http;
using ProjectHub.Api.Modules.Organizations;
using ProjectHub.Api.Modules.Users;

namespace ProjectHub.Api.Modules.Identity;

/// <summary>
/// Resolves the <see cref="UserContext"/> for every /api/v1 request, after authentication and
/// authorization have run. Callers without an active ProjectHub user in their tenant's
/// organization get 403, unless provisioning at first sign-in is switched on (DEC-013, <see cref="UserProvisioning"/>).
/// </summary>
internal sealed class UserContextMiddleware(RequestDelegate next)
{
    private const string ItemKey = "ProjectHub.UserContext";

    public async Task InvokeAsync(HttpContext httpContext, ICurrentUser currentUser, ProjectHubDbContext db, UserProvisioning provisioning)
    {
        if (!currentUser.IsAuthenticated || currentUser.TenantId is null || currentUser.EntraObjectId is null)
        {
            await Results.Unauthorized().ExecuteAsync(httpContext);
            return;
        }

        var userContext = await FindAsync(db, currentUser, httpContext.RequestAborted);
        if (userContext is null)
        {
            await provisioning.TryProvisionAsync(currentUser, httpContext.RequestAborted);
            userContext = await FindAsync(db, currentUser, httpContext.RequestAborted);
        }

        if (userContext is null)
        {
            await ApiResults.Forbidden("No active ProjectHub user exists for this account.").ExecuteAsync(httpContext);
            return;
        }

        httpContext.Items[ItemKey] = userContext;
        await next(httpContext);
    }

    private static Task<UserContext?> FindAsync(ProjectHubDbContext db, ICurrentUser currentUser, CancellationToken ct) =>
        (
            from user in db.Set<AppUser>().AsNoTracking()
            join organization in db.Set<Organization>().AsNoTracking() on user.OrganizationId equals organization.Id
            where organization.EntraTenantId == currentUser.TenantId
                  && user.EntraObjectId == currentUser.EntraObjectId
                  && user.Status == UserStatus.Active
            select new UserContext(user.Id, user.OrganizationId, user.OrganizationRole, user.DisplayName, user.Email))
        .SingleOrDefaultAsync(ct);

    internal static UserContext FromHttpContext(IHttpContextAccessor accessor) =>
        FromHttpContext(accessor.HttpContext)
        ?? throw new InvalidOperationException("UserContext is only available on /api/v1 endpoints.");

    /// <summary>The user resolved for this request (for SignalR: the request that opened the connection).</summary>
    internal static UserContext? FromHttpContext(HttpContext? httpContext) =>
        httpContext?.Items[ItemKey] as UserContext;
}
