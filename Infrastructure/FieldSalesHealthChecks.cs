using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace EcnesoftFieldSales.Infrastructure;

public sealed class SqliteHealthCheck(IConfiguration configuration) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var connectionString = configuration.GetConnectionString("SalesDb") ?? "Data Source=App_Data/ecnesoft-sales.db";
            await using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1";
            await command.ExecuteScalarAsync(cancellationToken);

            return HealthCheckResult.Healthy("SQLite connection is available.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Degraded("SQLite connection check failed.", ex);
        }
    }
}

public sealed class UploadDirectoryHealthCheck(IWebHostEnvironment environment) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var webRoot = environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot");
            var uploadsDirectory = Path.Combine(webRoot, "uploads");
            Directory.CreateDirectory(uploadsDirectory);

            var probePath = Path.Combine(uploadsDirectory, $".health-{Guid.NewGuid():N}.tmp");
            await File.WriteAllTextAsync(probePath, "ok", cancellationToken);
            File.Delete(probePath);

            return HealthCheckResult.Healthy("Upload directory is writable.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Degraded("Upload directory write check failed.", ex);
        }
    }
}
