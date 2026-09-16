using CharacterSheet.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CharacterSheet.IntegrationTests;

internal sealed class PostgresTestDatabase : IDisposable, IAsyncDisposable
{
    private readonly string _adminConnectionString;
    private readonly string _databaseName;
    private bool _disposed;

    private PostgresTestDatabase(
        string adminConnectionString,
        string databaseName,
        string connectionString)
    {
        _adminConnectionString = adminConnectionString;
        _databaseName = databaseName;
        ConnectionString = connectionString;
    }

    public string ConnectionString { get; }

    public static PostgresTestDatabase Create() =>
        CreateAsync().GetAwaiter().GetResult();

    public static async Task<PostgresTestDatabase> CreateAsync()
    {
        var configured = Environment.GetEnvironmentVariable("ConnectionStrings__CharacterSheet");
        if (string.IsNullOrWhiteSpace(configured))
        {
            throw new InvalidOperationException(
                "ConnectionStrings__CharacterSheet must point to the disposable PostgreSQL test server.");
        }

        var baseBuilder = new NpgsqlConnectionStringBuilder(configured);
        if (string.IsNullOrWhiteSpace(baseBuilder.Host))
        {
            throw new InvalidOperationException(
                "ConnectionStrings__CharacterSheet must be a PostgreSQL connection string.");
        }

        var databaseName = $"character_sheet_test_{Guid.NewGuid():N}";
        var adminBuilder = new NpgsqlConnectionStringBuilder(configured)
        {
            Database = "postgres",
            Pooling = false
        };

        await using (var admin = new NpgsqlConnection(adminBuilder.ConnectionString))
        {
            await admin.OpenAsync();
            await using var command = admin.CreateCommand();
            command.CommandText = $"CREATE DATABASE \"{databaseName}\"";
            await command.ExecuteNonQueryAsync();
        }

        var testBuilder = new NpgsqlConnectionStringBuilder(configured)
        {
            Database = databaseName,
            Pooling = false
        };

        return new PostgresTestDatabase(
            adminBuilder.ConnectionString,
            databaseName,
            testBuilder.ConnectionString);
    }

    public DbContextOptions<CharacterSheetDbContext> CreateOptions() =>
        new DbContextOptionsBuilder<CharacterSheetDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;

    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        NpgsqlConnection.ClearAllPools();

        await using var admin = new NpgsqlConnection(_adminConnectionString);
        await admin.OpenAsync();

        await using (var terminate = admin.CreateCommand())
        {
            terminate.CommandText = """
                SELECT pg_terminate_backend(pid)
                FROM pg_stat_activity
                WHERE datname = @databaseName
                  AND pid <> pg_backend_pid();
                """;
            terminate.Parameters.AddWithValue("databaseName", _databaseName);
            await terminate.ExecuteNonQueryAsync();
        }

        await using (var drop = admin.CreateCommand())
        {
            drop.CommandText = $"DROP DATABASE IF EXISTS \"{_databaseName}\"";
            await drop.ExecuteNonQueryAsync();
        }
    }
}

public sealed class PostgresWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly PostgresTestDatabase _database = PostgresTestDatabase.Create();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:CharacterSheet", _database.ConnectionString);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _database.Dispose();
        }
    }
}
