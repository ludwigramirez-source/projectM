using Microsoft.AspNetCore.Identity;
using ProjectM.Domain.Users;

namespace ProjectM.Api.Infrastructure.Seed;

/// <summary>Crea los roles y el Admin inicial (credenciales desde variables de entorno). Idempotente.</summary>
public class IdentitySeeder(RoleManager<AppRole> roles, UserManager<AppUser> users, IConfiguration config,
    TimeProvider clock, ILogger<IdentitySeeder> logger)
{
    public async Task SeedAsync()
    {
        foreach (var role in Roles.All)
            if (!await roles.RoleExistsAsync(role))
                await roles.CreateAsync(new AppRole(role));

        var email = config["Seed:AdminEmail"];
        var password = config["Seed:AdminPassword"];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            logger.LogWarning("Seed:AdminEmail/Seed:AdminPassword no configurados: no se crea el Admin inicial.");
            return;
        }
        if (await users.FindByEmailAsync(email) is not null) return;

        var admin = new AppUser
        {
            Id = Guid.NewGuid(),
            Email = email,
            UserName = email,
            FullName = "Administrador",
            EmailConfirmed = true,
            CreatedAt = clock.GetUtcNow()
        };
        var result = await users.CreateAsync(admin, password);
        if (!result.Succeeded)
            throw new InvalidOperationException("No se pudo crear el Admin inicial: " +
                                                string.Join("; ", result.Errors.Select(e => e.Description)));
        await users.AddToRoleAsync(admin, Roles.Admin);
        logger.LogInformation("Admin inicial creado: {Email}", email);
    }
}
