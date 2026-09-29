using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging;

namespace Kavita.Server.Helpers;

/// <summary>Bridges the previously released, incremental RPG migrations to the consolidated catalog migration.</summary>
public static class RpgMigrationCompatibility
{
    private const string ConsolidatedRpgCatalogMigration = "20260926124533_AddRpgCatalogModel";
    private const string LastLegacyRpgMigration = "20260926000519_AddRpgGeekMetadata";

    private static readonly string[] LegacyRpgMigrations =
    [
        "20260925082217_AddDriveThruRpgMetadata",
        "20260925150000_AddDriveThruRpgIdToVolume",
        "20260925223906_AddRpgMaterialType",
        "20260925232511_AddDriveThruRpgMatchStatus",
        "20260925233111_AddVolumeBibliographyFields",
        LastLegacyRpgMigration,
    ];

    /// <summary>
    /// Records the equivalent consolidated migration when the complete legacy RPG migration sequence is present.
    /// This avoids re-adding columns already created by the earlier release.
    /// </summary>
    public static async Task<bool> MarkConsolidatedMigrationAppliedForLegacyDatabaseAsync(
        DatabaseFacade database,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        var applied = (await database.GetAppliedMigrationsAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);
        if (applied.Contains(ConsolidatedRpgCatalogMigration) ||
            !LegacyRpgMigrations.All(applied.Contains)) return false;

        var inserted = await database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
            SELECT {ConsolidatedRpgCatalogMigration}, "ProductVersion"
            FROM "__EFMigrationsHistory"
            WHERE "MigrationId" = {LastLegacyRpgMigration}
              AND NOT EXISTS (
                  SELECT 1 FROM "__EFMigrationsHistory"
                  WHERE "MigrationId" = {ConsolidatedRpgCatalogMigration}
              );
            """, cancellationToken);

        if (inserted == 0) return false;

        logger.LogInformation(
            "Recognized the legacy RPG migration sequence; skipping duplicate catalog migration {MigrationId}",
            ConsolidatedRpgCatalogMigration);
        return true;
    }
}
