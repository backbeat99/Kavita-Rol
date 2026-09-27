using Kavita.Database;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kavita.Database.Migrations;

[DbContext(typeof(DataContext))]
[Migration("20260925150000_AddDriveThruRpgIdToVolume")]
public partial class AddDriveThruRpgIdToVolume : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "DriveThruRpgId",
            table: "Volume",
            type: "INTEGER",
            nullable: true);

        // Preserve an existing product association when all versions of a Manual agree on one ID.
        // Conflicting legacy Chapter IDs remain untouched for manual resolution.
        migrationBuilder.Sql("""
            UPDATE "Volume"
            SET "DriveThruRpgId" = (
                SELECT MIN(chapter."DriveThruRpgId")
                FROM "Chapter" AS chapter
                WHERE chapter."VolumeId" = "Volume"."Id"
                  AND chapter."DriveThruRpgId" IS NOT NULL
                HAVING COUNT(DISTINCT chapter."DriveThruRpgId") = 1
            )
            WHERE EXISTS (
                SELECT 1
                FROM "Chapter" AS chapter
                WHERE chapter."VolumeId" = "Volume"."Id"
                  AND chapter."DriveThruRpgId" IS NOT NULL
            );
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "DriveThruRpgId",
            table: "Volume");
    }
}
