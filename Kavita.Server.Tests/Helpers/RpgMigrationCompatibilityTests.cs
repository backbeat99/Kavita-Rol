using Kavita.Server.Helpers;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kavita.Server.Tests.Helpers;

public class RpgMigrationCompatibilityTests
{
    private const string ConsolidatedMigration = "20260926124533_AddRpgCatalogModel";

    private static readonly string[] LegacyMigrations =
    [
        "20260925082217_AddDriveThruRpgMetadata",
        "20260925150000_AddDriveThruRpgIdToVolume",
        "20260925223906_AddRpgMaterialType",
        "20260925232511_AddDriveThruRpgMatchStatus",
        "20260925233111_AddVolumeBibliographyFields",
        "20260926000519_AddRpgGeekMetadata",
    ];

    [Fact]
    public async Task CompleteLegacyRpgMigrationSequenceIsStampedInsteadOfReapplied()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = CreateContext(connection);
        await CreateHistoryTableAsync(context);
        foreach (var migration in LegacyMigrations)
        {
            await AddHistoryRowAsync(context, migration);
        }

        var changed = await RpgMigrationCompatibility.MarkConsolidatedMigrationAppliedForLegacyDatabaseAsync(
            context.Database, NullLogger.Instance);

        Assert.True(changed);
        Assert.Contains(ConsolidatedMigration, await context.Database.GetAppliedMigrationsAsync());
        Assert.False(await RpgMigrationCompatibility.MarkConsolidatedMigrationAppliedForLegacyDatabaseAsync(
            context.Database, NullLogger.Instance));
    }

    [Fact]
    public async Task PartialLegacySequenceIsNotStamped()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = CreateContext(connection);
        await CreateHistoryTableAsync(context);
        await AddHistoryRowAsync(context, LegacyMigrations[1]);

        var changed = await RpgMigrationCompatibility.MarkConsolidatedMigrationAppliedForLegacyDatabaseAsync(
            context.Database, NullLogger.Instance);

        Assert.False(changed);
        Assert.DoesNotContain(ConsolidatedMigration, await context.Database.GetAppliedMigrationsAsync());
    }

    private static DbContext CreateContext(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<DbContext>().UseSqlite(connection).Options;
        return new DbContext(options);
    }

    private static Task<int> CreateHistoryTableAsync(DbContext context) => context.Database.ExecuteSqlRawAsync("""
        CREATE TABLE "__EFMigrationsHistory" (
            "MigrationId" TEXT NOT NULL PRIMARY KEY,
            "ProductVersion" TEXT NOT NULL
        );
        """);

    private static Task<int> AddHistoryRowAsync(DbContext context, string migrationId) =>
        context.Database.ExecuteSqlRawAsync(
            "INSERT INTO \"__EFMigrationsHistory\" (\"MigrationId\", \"ProductVersion\") VALUES ({0}, {1})",
            migrationId, "10.0.11");
}
