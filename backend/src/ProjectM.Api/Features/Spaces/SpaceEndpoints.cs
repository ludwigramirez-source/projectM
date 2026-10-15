using FluentValidation;
using Microsoft.EntityFrameworkCore;
using ProjectM.Api.Infrastructure;
using ProjectM.Api.Infrastructure.Auth;
using ProjectM.Api.Infrastructure.Authorization;
using ProjectM.Api.Infrastructure.Errors;
using ProjectM.Api.Infrastructure.Persistence;
using ProjectM.Api.Infrastructure.Validation;
using ProjectM.Domain.Projects;

namespace ProjectM.Api.Features.Spaces;

public record SpaceDto(Guid Id, string Name, string? Description, string Color, int ProjectCount);
public record SaveSpaceRequest(string Name, string? Description, string? Color);
public record ClientDto(Guid Id, string Name);
public record CreateClientRequest(string Name);

public record TreeListDto(Guid Id, string Name);
public record TreeProjectDto(Guid Id, string Code, string Name, ProjectStatus Status, List<TreeListDto> Lists);
public record TreeSpaceDto(Guid Id, string Name, string Color, List<TreeProjectDto> Projects);

public class SaveSpaceRequestValidator : AbstractValidator<SaveSpaceRequest>
{
    public static readonly string[] Colors = ["primary", "secondary", "accent", "success", "danger", "neutral"];

    public SaveSpaceRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100).WithMessage("El nombre es obligatorio (máx. 100).");
        RuleFor(x => x.Description).MaximumLength(500);
        RuleFor(x => x.Color).Must(c => c is null || Colors.Contains(c)).WithMessage("Color no válido.");
    }
}

public class CreateClientRequestValidator : AbstractValidator<CreateClientRequest>
{
    public CreateClientRequestValidator() =>
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150).WithMessage("El nombre es obligatorio (máx. 150).");
}

public static class SpaceEndpoints
{
    public static RouteGroupBuilder MapSpaceEndpoints(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/spaces").WithTags("Espacios");
        g.MapGet("/", List);
        g.MapGet("/tree", Tree);
        g.MapPost("/", Create).RequireAuthorization(Policies.AdminOrGestor).Validate<SaveSpaceRequest>();
        g.MapPut("/{id:guid}", Update).RequireAuthorization(Policies.AdminOrGestor).Validate<SaveSpaceRequest>();
        g.MapDelete("/{id:guid}", Delete).RequireAuthorization(Policies.Admin);

        var c = api.MapGroup("/clients").WithTags("Clientes");
        c.MapGet("/", ListClients);
        c.MapPost("/", CreateClient).RequireAuthorization(Policies.AdminOrGestor).Validate<CreateClientRequest>();
        return api;
    }

    private static async Task<IResult> List(AppDbContext db, ProjectGuard guard, CancellationToken ct)
    {
        var visible = guard.VisibleProjects();
        var items = await db.Spaces.AsNoTracking().OrderBy(s => s.Position).ThenBy(s => s.Name)
            .Select(s => new SpaceDto(s.Id, s.Name, s.Description, s.Color, visible.Count(p => p.SpaceId == s.Id)))
            .ToListAsync(ct);
        return Results.Ok(items);
    }

    /// <summary>Árbol Espacio &gt; Proyecto &gt; Lista para la barra lateral (solo proyectos visibles).</summary>
    private static async Task<IResult> Tree(AppDbContext db, ProjectGuard guard, ICurrentUser user, CancellationToken ct)
    {
        var projects = await guard.VisibleProjects().AsNoTracking()
            .OrderBy(p => p.Name)
            .Select(p => new
            {
                p.SpaceId,
                Dto = new TreeProjectDto(p.Id, p.Code, p.Name, p.Status,
                    p.Lists.OrderBy(l => l.Position).Select(l => new TreeListDto(l.Id, l.Name)).ToList())
            })
            .ToListAsync(ct);
        var spaces = await db.Spaces.AsNoTracking().OrderBy(s => s.Position).ThenBy(s => s.Name).ToListAsync(ct);
        var canCreate = user.IsInRole(Domain.Users.Roles.Admin) || user.IsInRole(Domain.Users.Roles.Gestor);

        var tree = spaces
            .Select(s => new TreeSpaceDto(s.Id, s.Name, s.Color,
                projects.Where(p => p.SpaceId == s.Id).Select(p => p.Dto).ToList()))
            .Where(s => canCreate || s.Projects.Count > 0)
            .ToList();
        return Results.Ok(tree);
    }

    private static async Task<IResult> Create(SaveSpaceRequest req, AppDbContext db, AppClock clock, CancellationToken ct)
    {
        await EnsureUniqueName(db, req.Name, null, ct);
        var space = new Space
        {
            Name = req.Name.Trim(),
            Description = req.Description?.Trim(),
            Color = req.Color ?? "primary",
            Position = await db.Spaces.CountAsync(ct),
            CreatedAt = clock.Now
        };
        db.Spaces.Add(space);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/v1/spaces/{space.Id}", new SpaceDto(space.Id, space.Name, space.Description, space.Color, 0));
    }

    private static async Task<IResult> Update(Guid id, SaveSpaceRequest req, AppDbContext db, CancellationToken ct)
    {
        var space = await db.Spaces.FindAsync([id], ct) ?? throw AppException.NotFound("El espacio");
        await EnsureUniqueName(db, req.Name, id, ct);
        space.Name = req.Name.Trim();
        space.Description = req.Description?.Trim();
        space.Color = req.Color ?? space.Color;
        await db.SaveChangesAsync(ct);
        return Results.Ok(new SpaceDto(space.Id, space.Name, space.Description, space.Color,
            await db.Projects.CountAsync(p => p.SpaceId == id, ct)));
    }

    private static async Task<IResult> Delete(Guid id, AppDbContext db, CancellationToken ct)
    {
        var space = await db.Spaces.FindAsync([id], ct) ?? throw AppException.NotFound("El espacio");
        if (await db.Projects.AnyAsync(p => p.SpaceId == id, ct))
            throw AppException.Conflict("ESPACIO_CON_PROYECTOS", "No se puede eliminar un espacio que contiene proyectos.");
        db.Spaces.Remove(space);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task EnsureUniqueName(AppDbContext db, string name, Guid? exceptId, CancellationToken ct)
    {
        var n = name.Trim();
        if (await db.Spaces.AnyAsync(s => s.Name == n && s.Id != exceptId, ct))
            throw AppException.Validation("name", "Ya existe un espacio con ese nombre.");
    }

    private static async Task<IResult> ListClients(AppDbContext db, string? q, CancellationToken ct)
    {
        var query = db.Clients.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q)) query = query.Where(c => c.Name.Contains(q.Trim()));
        return Results.Ok(await query.OrderBy(c => c.Name).Take(100).Select(c => new ClientDto(c.Id, c.Name)).ToListAsync(ct));
    }

    private static async Task<IResult> CreateClient(CreateClientRequest req, AppDbContext db, AppClock clock, CancellationToken ct)
    {
        var name = req.Name.Trim();
        var existing = await db.Clients.FirstOrDefaultAsync(c => c.Name == name, ct);
        if (existing is not null) return Results.Ok(new ClientDto(existing.Id, existing.Name));
        var client = new Client { Name = name, CreatedAt = clock.Now };
        db.Clients.Add(client);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/v1/clients/{client.Id}", new ClientDto(client.Id, client.Name));
    }
}
