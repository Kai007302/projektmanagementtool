using Microsoft.Extensions.DependencyInjection;
using ProjectHub.Api.Modules.Identity;
using ProjectHub.Api.Modules.Identity.Authorization;
using static ProjectHub.Api.Modules.Identity.Development.DevelopmentSeedData;

namespace ProjectHub.Api.IntegrationTests;

/// <summary>Project scoping of the central authorization service against the seeded memberships.</summary>
[Collection(InfrastructureCollection.Name)]
public sealed class AuthorizationTests(InfrastructureFixture infrastructure) : IAsyncLifetime
{
    private ProjectHubApiFactory factory = null!;

    public Task InitializeAsync()
    {
        factory = new ProjectHubApiFactory(infrastructure.Postgres.GetConnectionString(), infrastructure.Redis.GetConnectionString());
        factory.CreateClient(); // starts the host: migrations and seed data
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await factory.DisposeAsync();

    [Theory]
    [InlineData("dev-ben", true, true, true, true)]
    [InlineData("dev-clara", true, true, true, false)]
    [InlineData("dev-david", true, true, false, false)]
    [InlineData("dev-eva", true, false, false, false)]
    [InlineData("dev-gina", true, false, false, false)]
    [InlineData("dev-felix", false, false, false, false)]
    [InlineData("dev-ada", true, true, true, true)]
    [InlineData("dev-fritz", false, false, false, false)]
    public async Task Project_permissions_follow_membership_and_organization(string objectId, bool view, bool contribute, bool edit, bool manage)
    {
        var user = ContextOf(Users.Single(u => u.ObjectId == objectId));
        await using var scope = factory.Services.CreateAsyncScope();
        var authorization = scope.ServiceProvider.GetRequiredService<IProjectHubAuthorization>();
        var project = IntranetProject.Id;

        Assert.Equal(view, await authorization.CanViewProjectAsync(user, project, default));
        Assert.Equal(contribute, await authorization.HasProjectPermissionAsync(user, project, ProjectPermission.Contribute, default));
        Assert.Equal(edit, await authorization.CanEditProjectAsync(user, project, default));
        Assert.Equal(manage, await authorization.CanManageProjectAsync(user, project, default));
    }

    [Fact]
    public async Task Organization_admin_has_no_access_to_projects_of_another_organization()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var authorization = scope.ServiceProvider.GetRequiredService<IProjectHubAuthorization>();

        Assert.False(await authorization.CanViewProjectAsync(ContextOf(Ada), FabrikamProject.Id, default));
        Assert.True(await authorization.CanManageProjectAsync(ContextOf(Fritz), FabrikamProject.Id, default));
    }

    [Fact]
    public async Task Unknown_project_grants_nothing()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var authorization = scope.ServiceProvider.GetRequiredService<IProjectHubAuthorization>();

        Assert.False(await authorization.CanViewProjectAsync(ContextOf(Ada), Guid.CreateVersion7(), default));
    }

    [Fact]
    public void Only_organization_admins_manage_the_organization()
    {
        using var scope = factory.Services.CreateScope();
        var authorization = scope.ServiceProvider.GetRequiredService<IProjectHubAuthorization>();

        Assert.True(authorization.CanManageOrganization(ContextOf(Ada)));
        Assert.False(authorization.CanManageOrganization(ContextOf(Ben)));
    }

    private static UserContext ContextOf(SeedUser user) =>
        new(user.Id, user.OrganizationId, user.OrganizationRole, user.DisplayName, user.Email);
}
