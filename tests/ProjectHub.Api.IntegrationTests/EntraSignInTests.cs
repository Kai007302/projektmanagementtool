using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using ProjectHub.Api.Infrastructure.Http;
using ProjectHub.Api.Modules.Departments;
using ProjectHub.Api.Modules.Identity;
using ProjectHub.Api.Modules.Notifications;
using ProjectHub.Api.Modules.Users;

namespace ProjectHub.Api.IntegrationTests;

/// <summary>
/// Sign-in with Entra ID access tokens outside Development and provisioning at first sign-in (DEC-013, ADR 0014).
/// Tokens are signed with a test key instead of Entra's; everything else is the production pipeline.
/// </summary>
[Collection(InfrastructureCollection.Name)]
public sealed class EntraSignInTests(InfrastructureFixture infrastructure)
{
    private const string ClientId = "11111111-1111-1111-1111-111111111111";
    private static readonly SymmetricSecurityKey SigningKey = new(RandomNumberGenerator.GetBytes(32));

    // Every test gets its own tenant, so the first person of each is a fresh first sign-in.
    private readonly string tenantId = Guid.NewGuid().ToString();

    private ProjectHubApiFactory Production(bool provisioning, string? organizationName = null, bool departmentsFromGroups = false) =>
        new(infrastructure.Postgres.GetConnectionString(), infrastructure.Redis.GetConnectionString(), configure: builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting(EntraIdRegistration.TenantIdKey, tenantId);
            builder.UseSetting(EntraIdRegistration.ClientIdKey, ClientId);
            if (provisioning)
            {
                builder.UseSetting(UserProvisioningOptions.ModeKey, UserProvisioningOptions.FirstSignIn);
            }

            if (departmentsFromGroups)
            {
                builder.UseSetting(EntraDepartmentOptions.EnabledKey, "true");
            }

            if (organizationName is not null)
            {
                builder.UseSetting(UserProvisioningOptions.OrganizationNameKey, organizationName);
            }

            builder.ConfigureTestServices(services => services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.Configuration = new OpenIdConnectConfiguration();
                options.TokenValidationParameters.IssuerSigningKey = SigningKey;
            }));
        });

    private string Token(
        string objectId, string name, string email, string? tenant = null, bool v1 = false, string[]? groups = null, bool groupOverage = false)
    {
        var tid = tenant ?? tenantId;
        var claims = new Dictionary<string, object> { ["oid"] = objectId, ["tid"] = tid, ["name"] = name };
        claims[v1 ? "upn" : "preferred_username"] = email;
        if (groups is not null)
        {
            claims["groups"] = groups;
        }

        if (groupOverage)
        {
            claims["_claim_names"] = new Dictionary<string, object> { ["groups"] = "src1" };
        }

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = v1 ? $"https://sts.windows.net/{tid}/" : $"https://login.microsoftonline.com/{tid}/v2.0",
            Audience = v1 ? $"api://{ClientId}" : ClientId,
            Claims = claims,
            Expires = DateTime.UtcNow.AddMinutes(10),
            SigningCredentials = new SigningCredentials(SigningKey, SecurityAlgorithms.HmacSha256),
        });
    }

    private static HttpClient Client(ProjectHubApiFactory factory, string token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Fact]
    public async Task The_web_app_learns_how_to_sign_in_without_being_signed_in()
    {
        await using var production = Production(provisioning: false);
        await using var development = new ProjectHubApiFactory(infrastructure.Postgres.GetConnectionString(), infrastructure.Redis.GetConnectionString());

        var entra = await production.CreateClient().GetFromJsonAsync<SignInConfiguration>("/api/v1/sign-in");
        var dev = await development.CreateClient().GetFromJsonAsync<SignInConfiguration>("/api/v1/sign-in");

        Assert.Equal(new SignInConfiguration(SignInEndpoints.EntraId, tenantId, ClientId, $"api://{ClientId}/access_as_user"), entra);
        Assert.Equal(SignInEndpoints.Development, dev!.Mode);
    }

    [Fact]
    public async Task Without_provisioning_unknown_people_are_forbidden()
    {
        await using var factory = Production(provisioning: false);

        var response = await Client(factory, Token(Guid.NewGuid().ToString(), "Unbekannt", "unbekannt@example.com")).GetAsync("/api/v1/me");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task The_first_person_becomes_admin_and_everyone_after_a_member()
    {
        await using var factory = Production(provisioning: true, organizationName: "Schönwald GmbH");
        var kai = Client(factory, Token(Guid.NewGuid().ToString(), "Kai", "kai@example.com"));
        var colleague = Client(factory, Token(Guid.NewGuid().ToString(), "Kollegin", "kollegin@example.com", v1: true));

        var first = await kai.GetFromJsonAsync<MeResponse>("/api/v1/me");
        var second = await colleague.GetFromJsonAsync<MeResponse>("/api/v1/me");
        var again = await kai.GetFromJsonAsync<MeResponse>("/api/v1/me");

        Assert.Equal(("Kai", "kai@example.com", "admin"), (first!.DisplayName, first.Email, first.OrganizationRole));
        Assert.Equal(("kollegin@example.com", "member"), (second!.Email, second.OrganizationRole));
        Assert.Equal(first.OrganizationId, second.OrganizationId);
        Assert.Equal(first.Id, again!.Id);
        var organization = await kai.GetFromJsonAsync<Dictionary<string, object>>("/api/v1/organization");
        Assert.Equal("Schönwald GmbH", organization!["name"].ToString());
    }

    [Fact]
    public async Task Other_tenants_and_deactivated_people_stay_out()
    {
        await using var factory = Production(provisioning: true);
        var objectId = Guid.NewGuid().ToString();
        var person = Client(factory, Token(objectId, "Person", "person@example.com"));
        Assert.Equal(HttpStatusCode.OK, (await person.GetAsync("/api/v1/me")).StatusCode);

        // A token from another tenant fails validation before provisioning is even considered.
        var stranger = Client(factory, Token(Guid.NewGuid().ToString(), "Fremd", "fremd@example.com", tenant: Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.Unauthorized, (await stranger.GetAsync("/api/v1/me")).StatusCode);

        await using (var connection = new NpgsqlConnection(infrastructure.Postgres.GetConnectionString()))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("update app_user set status = 'inactive' where entra_object_id = @oid", connection);
            command.Parameters.AddWithValue("oid", objectId);
            Assert.Equal(1, await command.ExecuteNonQueryAsync());
        }

        Assert.Equal(HttpStatusCode.Forbidden, (await person.GetAsync("/api/v1/me")).StatusCode);
    }

    [Fact]
    public async Task Simultaneous_first_sign_ins_create_one_organization_and_one_admin()
    {
        await using var factory = Production(provisioning: true);
        var clients = Enumerable.Range(0, 6)
            .Select(i => Client(factory, Token(Guid.NewGuid().ToString(), $"Person {i}", $"person{i}@example.com")))
            .ToList();

        var people = await Task.WhenAll(clients.Select(c => c.GetFromJsonAsync<MeResponse>("/api/v1/me")));

        Assert.Single(people.Select(p => p!.OrganizationId).Distinct());
        Assert.Single(people, p => p!.OrganizationRole == "admin");
    }

    [Fact]
    public async Task Changed_names_and_addresses_from_Entra_are_taken_over_unless_the_address_is_taken()
    {
        await using var factory = Production(provisioning: true);
        var objectId = Guid.NewGuid().ToString();
        var first = await Client(factory, Token(objectId, "Anna Alt", "anna.alt@example.com")).GetFromJsonAsync<MeResponse>("/api/v1/me");
        await Client(factory, Token(Guid.NewGuid().ToString(), "Bernd", "bernd@example.com")).GetFromJsonAsync<MeResponse>("/api/v1/me");

        var renamed = await Client(factory, Token(objectId, "Anna Neu", "anna.neu@example.com")).GetFromJsonAsync<MeResponse>("/api/v1/me");
        var taken = await Client(factory, Token(objectId, "Anna Neu", "bernd@example.com")).GetFromJsonAsync<MeResponse>("/api/v1/me");

        Assert.Equal(first!.Id, renamed!.Id);
        Assert.Equal(("Anna Neu", "anna.neu@example.com"), (renamed.DisplayName, renamed.Email));
        Assert.Equal(("Anna Neu", "anna.neu@example.com"), (taken!.DisplayName, taken.Email));
    }

    [Fact]
    public async Task The_first_person_leads_the_general_department_and_newcomers_without_one_are_announced()
    {
        await using var factory = Production(provisioning: true);
        var kai = Client(factory, Token(Guid.NewGuid().ToString(), "Kai", "kai@example.com"));

        var first = await kai.GetFromJsonAsync<MeResponse>("/api/v1/me");
        var newcomer = await Client(factory, Token(Guid.NewGuid().ToString(), "Neu", "neu@example.com")).GetFromJsonAsync<MeResponse>("/api/v1/me");

        Assert.Equal(("Allgemein", "lead"), (Assert.Single(first!.Departments).Name, first.Departments[0].Role));
        Assert.Empty(newcomer!.Departments);
        var notifications = await kai.GetFromJsonAsync<PagedResponse<NotificationResponse>>("/api/v1/me/notifications");
        var notice = Assert.Single(notifications!.Items, n => n.Type == NotificationTypes.PersonWithoutDepartment);
        Assert.Equal(("user", newcomer.Id), (notice.ResourceType, notice.ResourceId));
        var unassigned = await kai.GetFromJsonAsync<List<UserResponse>>("/api/v1/departments/unassigned");
        Assert.Contains(unassigned!, u => u.Id == newcomer.Id);
    }

    [Fact]
    public async Task Departments_follow_the_Entra_groups_but_keep_memberships_added_by_hand()
    {
        await using var factory = Production(provisioning: true, departmentsFromGroups: true);
        var kai = Client(factory, Token(Guid.NewGuid().ToString(), "Kai", "kai@example.com"));
        await kai.GetFromJsonAsync<MeResponse>("/api/v1/me");
        var group = Guid.NewGuid().ToString();
        var purchasing = await (await kai.PostAsJsonAsync("/api/v1/departments", new CreateDepartmentRequest("Einkauf", null, group)))
            .Content.ReadFromJsonAsync<DepartmentSummary>();
        var sales = await (await kai.PostAsJsonAsync("/api/v1/departments", new CreateDepartmentRequest("Vertrieb", null)))
            .Content.ReadFromJsonAsync<DepartmentSummary>();
        var objectId = Guid.NewGuid().ToString();

        var joined = await Client(factory, Token(objectId, "Eva", "eva@example.com", groups: [Guid.NewGuid().ToString(), group.ToUpperInvariant()]))
            .GetFromJsonAsync<MeResponse>("/api/v1/me");
        Assert.Equal(HttpStatusCode.NoContent, (await kai.PostAsJsonAsync(
            $"/api/v1/departments/{sales!.Id}/members", new AddDepartmentMemberRequest(joined!.Id, "member"))).StatusCode);
        var overage = await Client(factory, Token(objectId, "Eva", "eva@example.com", groupOverage: true)).GetFromJsonAsync<MeResponse>("/api/v1/me");
        var left = await Client(factory, Token(objectId, "Eva", "eva@example.com", groups: [])).GetFromJsonAsync<MeResponse>("/api/v1/me");

        Assert.Equal(purchasing!.Id, Assert.Single(joined.Departments).Id);
        Assert.Equal(2, overage!.Departments.Count);
        Assert.Equal(sales.Id, Assert.Single(left!.Departments).Id);
        var details = await kai.GetFromJsonAsync<DepartmentDetails>($"/api/v1/departments/{sales.Id}");
        Assert.Equal("manual", Assert.Single(details!.Members, m => m.UserId == joined.Id).Source);
        // Someone the group put into a department is not announced as waiting for one.
        var notifications = await kai.GetFromJsonAsync<PagedResponse<NotificationResponse>>("/api/v1/me/notifications");
        Assert.DoesNotContain(notifications!.Items, n => n.ResourceId == joined.Id);
    }

    [Fact]
    public async Task Without_the_setting_Entra_groups_change_nothing()
    {
        await using var factory = Production(provisioning: true);
        var kai = Client(factory, Token(Guid.NewGuid().ToString(), "Kai", "kai@example.com"));
        await kai.GetFromJsonAsync<MeResponse>("/api/v1/me");
        var group = Guid.NewGuid().ToString();
        await kai.PostAsJsonAsync("/api/v1/departments", new CreateDepartmentRequest("Einkauf", null, group));

        var person = await Client(factory, Token(Guid.NewGuid().ToString(), "Eva", "eva@example.com", groups: [group])).GetFromJsonAsync<MeResponse>("/api/v1/me");

        Assert.Empty(person!.Departments);
    }
}
