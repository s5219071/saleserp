using EcnesoftFieldSales.Domain;
using EcnesoftFieldSales.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EcnesoftFieldSales.Infrastructure;

public static class SalesDbInitializer
{
    public static async Task InitializeAsync(IServiceProvider services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("SalesDb") ?? "Data Source=App_Data/ecnesoft-sales.db";
        EnsureSqliteDirectory(connectionString);

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SalesDbContext>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        await db.Database.EnsureCreatedAsync();

        if (!await db.Users.AnyAsync())
        {
            var admin = passwordHasher.HashPassword("Ecnesoft");
            var sales = passwordHasher.HashPassword("Ecnesoft");
            db.Users.AddRange(
                new UserAccount
                {
                    Email = "Ecnesoft",
                    PasswordHash = admin.Hash,
                    Salt = admin.Salt,
                    FullName = "ECNESOFT Admin",
                    Role = UserRole.ADMIN,
                    IsActive = true
                },
                new UserAccount
                {
                    Email = "sales@ecnesoft.local",
                    PasswordHash = sales.Hash,
                    Salt = sales.Salt,
                    FullName = "Sydney Field Rep",
                    Role = UserRole.SALES,
                    IsActive = true
                });
        }

        if (!await db.ClientGroups.AnyAsync())
        {
            db.ClientGroups.AddRange(
                new ClientGroup { GroupName = "Korean Grocery", HexColor = "#16A34A", Description = "Korean and Asian grocery accounts" },
                new ClientGroup { GroupName = "Restaurant POS", HexColor = "#0EA5E9", Description = "Restaurant POS and table-service prospects" },
                new ClientGroup { GroupName = "Retail POS", HexColor = "#F59E0B", Description = "Retail POS prospects" },
                new ClientGroup { GroupName = "High Touch", HexColor = "#E11D48", Description = "Owner-led, high-value opportunities" });
        }

        await db.SaveChangesAsync();
    }

    private static void EnsureSqliteDirectory(string connectionString)
    {
        var builder = new SqliteConnectionStringBuilder(connectionString);
        if (string.IsNullOrWhiteSpace(builder.DataSource) ||
            builder.DataSource.Equals(":memory:", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var path = Path.GetFullPath(builder.DataSource);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }
}
