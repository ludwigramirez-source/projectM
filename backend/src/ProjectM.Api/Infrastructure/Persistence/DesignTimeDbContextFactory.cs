using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ProjectM.Api.Infrastructure.Persistence;

/// <summary>Usado solo por `dotnet ef` para generar migraciones sin arrancar la aplicación.</summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=design-time;Database=projectm;Username=postgres")
            .Options);
}
