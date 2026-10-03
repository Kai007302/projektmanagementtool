using ProjectHub.Api.Modules.Identity.Authorization;

namespace ProjectHub.Api.UnitTests;

public class ProjectPermissionsTests
{
    [Theory]
    [InlineData(ProjectRole.Admin, true, true, true, true)]
    [InlineData(ProjectRole.Editor, true, true, true, false)]
    [InlineData(ProjectRole.Member, true, true, false, false)]
    [InlineData(ProjectRole.Viewer, true, false, false, false)]
    [InlineData(ProjectRole.Guest, true, false, false, false)]
    public void Role_grants_expected_permissions(string role, bool view, bool contribute, bool edit, bool manage)
    {
        Assert.Equal(view, ProjectPermissions.Grants(role, ProjectPermission.View));
        Assert.Equal(contribute, ProjectPermissions.Grants(role, ProjectPermission.Contribute));
        Assert.Equal(edit, ProjectPermissions.Grants(role, ProjectPermission.Edit));
        Assert.Equal(manage, ProjectPermissions.Grants(role, ProjectPermission.Manage));
    }

    [Fact]
    public void Unknown_role_grants_nothing()
    {
        foreach (var permission in Enum.GetValues<ProjectPermission>())
        {
            Assert.False(ProjectPermissions.Grants("owner", permission));
        }
    }
}
