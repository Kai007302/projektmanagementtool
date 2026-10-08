using System.Text.Json.Nodes;
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

        // Departments are looked up by name (see SeedDepartment): an existing database may already have them.
        foreach (var department in DevelopmentSeedData.Departments)
        {
            await ExecuteAsync(connection, """
                insert into department (id, organization_id, name, description) values ($1, $2, $3, $4)
                on conflict do nothing
                """, ct, department.Id, department.OrganizationId, department.Name, department.Description);

            foreach (var (userId, role) in department.Members)
            {
                await ExecuteAsync(connection, """
                    insert into department_member (organization_id, department_id, user_id, role)
                    select $1, d.id, $3, $4 from department d where d.organization_id = $1 and lower(d.name) = lower($2)
                    on conflict do nothing
                    """, ct, department.OrganizationId, department.Name, userId, role);
            }
        }

        foreach (var project in DevelopmentSeedData.Projects)
        {
            await ExecuteAsync(connection, """
                insert into project (id, organization_id, name, owner_id, department_id, visibility)
                select $1, $2, $3, $4, d.id, $6 from department d where d.organization_id = $2 and lower(d.name) = lower($5)
                on conflict do nothing
                """, ct, project.Id, project.OrganizationId, project.Name, project.OwnerId, project.Department.Name, project.Visibility);

            foreach (var (userId, role) in project.Members)
            {
                await ExecuteAsync(connection, """
                    insert into project_member (organization_id, project_id, user_id, role) values ($1, $2, $3, $4)
                    on conflict do nothing
                    """, ct, project.OrganizationId, project.Id, userId, role);
            }
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        object Day(int? offset) => offset is { } days ? today.AddDays(days) : DBNull.Value;
        foreach (var task in DevelopmentSeedData.Tasks)
        {
            await ExecuteAsync(connection, """
                insert into task (id, organization_id, project_id, parent_task_id, title, status, priority, creator_id,
                                  start_date, due_date, progress)
                values ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11)
                on conflict do nothing
                """, ct, task.Id, task.Project.OrganizationId, task.Project.Id, (object?)task.ParentTaskId ?? DBNull.Value,
                task.Title, task.Status, task.Priority, task.CreatorId, Day(task.StartDay), Day(task.DueDay), task.Progress);
            if (task.AssigneeId is { } assigneeId)
            {
                await ExecuteAsync(connection, """
                    insert into task_assignee (organization_id, task_id, user_id) values ($1, $2, $3)
                    on conflict do nothing
                    """, ct, task.Project.OrganizationId, task.Id, assigneeId);
            }
        }

        foreach (var (id, source, target, type) in DevelopmentSeedData.Dependencies)
        {
            await ExecuteAsync(connection, """
                insert into task_dependency (id, organization_id, source_task_id, target_task_id, dependency_type)
                values ($1, $2, $3, $4, $5)
                on conflict do nothing
                """, ct, id, source.Project.OrganizationId, source.Id, target.Id, type);
        }

        foreach (var (id, project, name, day) in DevelopmentSeedData.Milestones)
        {
            await ExecuteAsync(connection, """
                insert into gantt_milestone (id, organization_id, project_id, name, milestone_date) values ($1, $2, $3, $4, $5)
                on conflict do nothing
                """, ct, id, project.OrganizationId, project.Id, name, today.AddDays(day));
        }

        foreach (var (id, project, name, objects) in DevelopmentSeedData.Whiteboards)
        {
            var created = await ExecuteAsync(connection, """
                insert into whiteboard (id, organization_id, project_id, name, collaboration_document_id) values ($1, $2, $3, $4, $5)
                on conflict do nothing
                """, ct, id, project.OrganizationId, project.Id, name, $"yjs:{id:N}");
            if (created == 1)
            {
                // The initial content is one Yjs update; compaction turns it into a snapshot like any other.
                await ExecuteAsync(connection, """
                    insert into whiteboard_update (whiteboard_id, sequence_number, organization_id, payload, created_by)
                    values ($1, 1, $2, $3, $4)
                    """, ct, id, project.OrganizationId, Whiteboard.WhiteboardDocuments.CreateUpdate(objects), DevelopmentSeedData.Ben.Id);
            }
        }

        await SeedKnowledgeAsync(connection, ct);
        await transaction.CommitAsync(ct);
    }

    private static async Task SeedKnowledgeAsync(NpgsqlConnection connection, CancellationToken ct)
    {
        foreach (var space in DevelopmentSeedData.Spaces)
        {
            await ExecuteAsync(connection, """
                insert into knowledge_space (id, organization_id, name, description, department_id)
                select $1, $2, $3, $4, d.id from department d where d.organization_id = $2 and lower(d.name) = lower($5)
                on conflict do nothing
                """, ct, space.Id, space.OrganizationId, space.Name, space.Description, space.Department.Name);
        }

        foreach (var article in DevelopmentSeedData.Articles)
        {
            // The first version gets the article id shifted by 100 in its last digits.
            var lastDigits = long.Parse(article.Id.ToString()[^12..]);
            var versionId = Guid.Parse($"{article.Id.ToString()[..^12]}{lastDigits + 100:D12}");
            var content = Knowledge.BlockContent.Normalize(JsonNode.Parse(article.ContentJson), out var contentError)
                          ?? throw new InvalidOperationException($"Seed article '{article.Title}': {contentError}");
            await ExecuteAsync(connection, """
                insert into knowledge_article (id, organization_id, knowledge_space_id, title, slug, article_type, summary, owner_id,
                                               status, visibility, published_at, search_text, department_id)
                select $1, $2, $3, $4, $5, $6, $7, $8, $9, $10, case when $9 = 'published' then now() end, $11, d.id
                from department d where d.organization_id = $2 and lower(d.name) = lower($12)
                on conflict do nothing
                """, ct, article.Id, article.OrganizationId, (object?)article.SpaceId ?? DBNull.Value, article.Title, article.Slug,
                article.ArticleType, article.Summary, article.OwnerId, article.Status, article.Visibility,
                content.PlainText, article.Department.Name);
            await ExecuteAsync(connection, """
                insert into knowledge_version (id, organization_id, article_id, version_number, content_json, created_by, change_note)
                values ($1, $2, $3, 1, $4::jsonb, $5, 'Testdaten')
                on conflict do nothing
                """, ct, versionId, article.OrganizationId, article.Id, content.Content.ToJsonString(), article.OwnerId);
            await ExecuteAsync(connection, """
                update knowledge_article set current_version_id = $2 where id = $1 and current_version_id is null
                """, ct, article.Id, versionId);

            foreach (var tag in article.Tags)
            {
                await ExecuteAsync(connection, """
                    insert into knowledge_tag (organization_id, name) values ($1, $2) on conflict do nothing
                    """, ct, article.OrganizationId, tag);
                await ExecuteAsync(connection, """
                    insert into knowledge_article_tag (organization_id, article_id, tag_id)
                    select $1, $2, id from knowledge_tag where organization_id = $1 and lower(name) = lower($3)
                    on conflict do nothing
                    """, ct, article.OrganizationId, article.Id, tag);
            }
        }

        foreach (var relation in DevelopmentSeedData.Relations)
        {
            var source = DevelopmentSeedData.Articles.Single(a => a.Id == relation.SourceId);
            await ExecuteAsync(connection, """
                insert into knowledge_relation (organization_id, source_article_id, target_article_id, relation_type, created_by)
                values ($1, $2, $3, $4, $5)
                on conflict do nothing
                """, ct, source.OrganizationId, relation.SourceId, relation.TargetId, relation.RelationType, source.OwnerId);
        }

        foreach (var (articleId, principalType, principal, permission) in DevelopmentSeedData.KnowledgeGrants)
        {
            var article = DevelopmentSeedData.Articles.Single(a => a.Id == articleId);
            await ExecuteAsync(connection, """
                insert into knowledge_permission (organization_id, article_id, principal_type, principal_id, permission)
                select $1, $2, $3, d.id, $5 from department d where d.organization_id = $1 and lower(d.name) = lower($4)
                on conflict do nothing
                """, ct, article.OrganizationId, articleId, principalType, principal.Name, permission);
        }

        foreach (var (articleId, resourceType, resourceId, createdBy) in DevelopmentSeedData.KnowledgeReferences)
        {
            var article = DevelopmentSeedData.Articles.Single(a => a.Id == articleId);
            await ExecuteAsync(connection, """
                insert into knowledge_reference (organization_id, article_id, resource_type, resource_id, created_by)
                values ($1, $2, $3, $4, $5)
                on conflict do nothing
                """, ct, article.OrganizationId, articleId, resourceType, resourceId, createdBy);
        }
    }

    private static async Task<int> ExecuteAsync(NpgsqlConnection connection, string sql, CancellationToken ct, params object[] values)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var value in values)
        {
            command.Parameters.Add(new NpgsqlParameter { Value = value });
        }

        return await command.ExecuteNonQueryAsync(ct);
    }
}
