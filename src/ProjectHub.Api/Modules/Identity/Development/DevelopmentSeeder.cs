using Npgsql;

namespace ProjectHub.Api.Modules.Identity.Development;

/// <summary>Idempotently inserts <see cref="DevelopmentSeedData"/>. Development and tests only.</summary>
public static class DevelopmentSeeder
{
    public static async Task SeedAsync(string connectionString, CancellationToken ct = default)
    {
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);

        foreach (var org in DevelopmentSeedData.Organizations)
        {
            await ExecuteAsync(connection, """
                insert into organization (id, name, slug, entra_tenant_id) values ($1, $2, $3, $4)
                on conflict do nothing
                """, ct, org.Id, org.Name, org.Slug, org.TenantId);
        }

        foreach (var user in DevelopmentSeedData.Users)
        {
            await ExecuteAsync(connection, """
                insert into app_user (id, organization_id, entra_object_id, email, display_name, department, organization_role)
                values ($1, $2, $3, $4, $5, $6, $7)
                on conflict do nothing
                """, ct, user.Id, user.OrganizationId, user.ObjectId, user.Email, user.DisplayName, (object?)user.Department ?? DBNull.Value, user.OrganizationRole);
        }

        foreach (var team in DevelopmentSeedData.Teams)
        {
            await ExecuteAsync(connection, """
                insert into team (id, organization_id, name, description) values ($1, $2, $3, $4)
                on conflict do nothing
                """, ct, team.Id, team.OrganizationId, team.Name, team.Description);

            foreach (var (userId, role) in team.Members)
            {
                await ExecuteAsync(connection, """
                    insert into team_member (organization_id, team_id, user_id, role) values ($1, $2, $3, $4)
                    on conflict do nothing
                    """, ct, team.OrganizationId, team.Id, userId, role);
            }
        }

        foreach (var project in DevelopmentSeedData.Projects)
        {
            await ExecuteAsync(connection, """
                insert into project (id, organization_id, name, owner_id) values ($1, $2, $3, $4)
                on conflict do nothing
                """, ct, project.Id, project.OrganizationId, project.Name, project.OwnerId);

            foreach (var (userId, role) in project.Members)
            {
                await ExecuteAsync(connection, """
                    insert into project_member (organization_id, project_id, user_id, role) values ($1, $2, $3, $4)
                    on conflict do nothing
                    """, ct, project.OrganizationId, project.Id, userId, role);
            }
        }

        await transaction.CommitAsync(ct);
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql, CancellationToken ct, params object[] values)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var value in values)
        {
            command.Parameters.Add(new NpgsqlParameter { Value = value });
        }

        await command.ExecuteNonQueryAsync(ct);
    }
}
