using CharacterSheet.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CharacterSheet.IntegrationTests;

/// <summary>
/// The committed design-time snapshot must match the active EF Core relational
/// model; otherwise PostgreSQL migration attempts will fail before any test.
/// This comparison requires no database connection or source authorization.
/// </summary>
public sealed class CharacterSheetMigrationModelParityTests
{
    [Fact]
    public void CurrentRelationalModelMatchesCommittedMigrationSnapshot()
    {
        var options = new DbContextOptionsBuilder<CharacterSheetDbContext>()
            .UseNpgsql("Host=localhost;Database=parity_only;Username=test;Password=test")
            .Options;
        using var db = new CharacterSheetDbContext(options);
        var committed = db.GetService<IMigrationsAssembly>().ModelSnapshot?.Model;
        Assert.NotNull(committed);
        // A migration snapshot is a mutable design-time model, not an
        // initialized runtime model. Finalize it before relational diffing.
        var snapshot = db.GetService<IModelRuntimeInitializer>()
            .Initialize(committed, designTime: true);
        var runtime = db.GetService<IDesignTimeModel>().Model;
        var differ = db.GetService<IMigrationsModelDiffer>();
        var changes = differ.GetDifferences(
            snapshot.GetRelationalModel(), runtime.GetRelationalModel());
        Assert.True(changes.Count == 0,
            "EF migration snapshot differs from runtime model: " +
            string.Join("; ", changes.Select(op =>
                op.GetType().Name + " " +
                op.GetType().GetProperty("Name")?.GetValue(op) +
                " table=" + op.GetType().GetProperty("Table")?.GetValue(op))));
    }
}
