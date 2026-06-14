using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ContactConnection.Infrastructure.Data;

/// <summary>
/// Used only by EF Core tooling (dotnet ef migrations add/update) for the public schema context.
/// At runtime the context is resolved from DI with the real connection string from user secrets.
/// </summary>
public class ContactConnectionDbContextDesignTimeFactory : IDesignTimeDbContextFactory<ContactConnectionDbContext>
{
    public ContactConnectionDbContext CreateDbContext(string[] args)
    {
        const string connStr =
            "Host=localhost;Port=5432;Database=hubion_master;" +
            "Username=hubion;Password=hubion_dev";

        var options = new DbContextOptionsBuilder<ContactConnectionDbContext>()
            .UseNpgsql(connStr, opts => opts.MigrationsAssembly("ContactConnection.Infrastructure"))
            .Options;

        return new ContactConnectionDbContext(options);
    }
}
