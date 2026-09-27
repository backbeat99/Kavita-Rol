using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kavita.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddVolumeBibliographyFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Language",
                table: "Volume",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "LanguageLocked",
                table: "Volume",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "NameLocked",
                table: "Volume",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReleaseDate",
                table: "Volume",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ReleaseDateLocked",
                table: "Volume",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Summary",
                table: "Volume",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SummaryLocked",
                table: "Volume",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            // Conservative data copy: shared bibliography moves to Volume only when every
            // version (Chapter) agrees on the value. Conflicting values stay on the Chapters
            // for manual review; no arbitrary first-version copy is made.
            migrationBuilder.Sql("""
                UPDATE Volume
                SET Summary = (
                    SELECT MIN(c.Summary) FROM Chapter c
                    WHERE c.VolumeId = Volume.Id
                    GROUP BY c.VolumeId
                    HAVING COUNT(DISTINCT c.Summary) = 1
                );

                UPDATE Volume
                SET ReleaseDate = (
                    SELECT MIN(c.ReleaseDate) FROM Chapter c
                    WHERE c.VolumeId = Volume.Id
                    GROUP BY c.VolumeId
                    HAVING COUNT(DISTINCT c.ReleaseDate) = 1
                );

                UPDATE Volume
                SET Language = (
                    SELECT MIN(c.Language) FROM Chapter c
                    WHERE c.VolumeId = Volume.Id
                    GROUP BY c.VolumeId
                    HAVING COUNT(DISTINCT c.Language) = 1
                );
            """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Language",
                table: "Volume");

            migrationBuilder.DropColumn(
                name: "LanguageLocked",
                table: "Volume");

            migrationBuilder.DropColumn(
                name: "NameLocked",
                table: "Volume");

            migrationBuilder.DropColumn(
                name: "ReleaseDate",
                table: "Volume");

            migrationBuilder.DropColumn(
                name: "ReleaseDateLocked",
                table: "Volume");

            migrationBuilder.DropColumn(
                name: "Summary",
                table: "Volume");

            migrationBuilder.DropColumn(
                name: "SummaryLocked",
                table: "Volume");
        }
    }
}
