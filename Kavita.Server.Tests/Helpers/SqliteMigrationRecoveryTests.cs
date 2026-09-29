using Kavita.Database;
using Kavita.Server.Helpers;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kavita.Server.Tests.Helpers;

public class SqliteMigrationRecoveryTests
{
    [Fact]
    public async Task InterruptedEmptyInstallCanCompleteRealMigrations()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = CreateContext(connection);
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync("20210102165536_EntityTimestamps");
        await context.Database.ExecuteSqlRawAsync("CREATE TABLE \"ef_temp_Volume\" (\"Id\" INTEGER NOT NULL);");

        var failure = await Assert.ThrowsAsync<SqliteException>(() =>
            migrator.MigrateAsync("20210102173326_VolumeNumberRefactor"));
        Assert.Contains("ef_temp_Volume", failure.Message);
        Assert.False(await SqliteMigrationRecovery.HasManualMigrationHistoryAsync(context.Database));
        Assert.True(await SqliteMigrationRecovery.RemoveEmptyOrphanedVolumeTableAsync(
            context.Database, NullLogger.Instance));
        await migrator.MigrateAsync();

        Assert.True(await SqliteMigrationRecovery.HasManualMigrationHistoryAsync(context.Database));
        Assert.False(await SqliteMigrationRecovery.RemoveEmptyOrphanedVolumeTableAsync(
            context.Database, NullLogger.Instance));
        Assert.Equal(0, await CountScratchTablesAsync(connection));
    }

    [Fact]
    public async Task DoesNotRemoveScratchTableContainingData()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = CreateContext(connection);
        await CreateEmptyVolumeAndScratchAsync(context);
        await context.Database.ExecuteSqlRawAsync("INSERT INTO \"ef_temp_Volume\" VALUES (1);");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SqliteMigrationRecovery.RemoveEmptyOrphanedVolumeTableAsync(context.Database, NullLogger.Instance));
        Assert.Equal(1, await CountScratchTablesAsync(connection));
    }

    [Fact]
    public async Task DoesNotRemoveScratchTableWhenVolumeContainsData()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = CreateContext(connection);
        await CreateEmptyVolumeAndScratchAsync(context);
        await context.Database.ExecuteSqlRawAsync("INSERT INTO \"Volume\" VALUES (1);");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SqliteMigrationRecovery.RemoveEmptyOrphanedVolumeTableAsync(context.Database, NullLogger.Instance));
        Assert.Equal(1, await CountScratchTablesAsync(connection));
    }

    [Fact]
    public async Task DoesNotRemoveScratchTableFromExistingInstall()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = CreateContext(connection);
        await CreateEmptyVolumeAndScratchAsync(context);
        await context.Database.ExecuteSqlRawAsync("CREATE TABLE \"ManualMigrationHistory\" (\"Id\" INTEGER);");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SqliteMigrationRecovery.RemoveEmptyOrphanedVolumeTableAsync(context.Database, NullLogger.Instance));
        Assert.Equal(1, await CountScratchTablesAsync(connection));
    }

    private static DataContext CreateContext(SqliteConnection connection) =>
        new(new DbContextOptionsBuilder<DataContext>().UseSqlite(connection).Options);

    private static async Task CreateEmptyVolumeAndScratchAsync(DataContext context)
    {
        await context.Database.ExecuteSqlRawAsync("CREATE TABLE \"Volume\" (\"Id\" INTEGER);");
        await context.Database.ExecuteSqlRawAsync("CREATE TABLE \"ef_temp_Volume\" (\"Id\" INTEGER);");
    }

    private static async Task<int> CountScratchTablesAsync(SqliteConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE name = 'ef_temp_Volume';";
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }
}
