using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kavita.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddRpgCatalogModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DriveThruRpgId",
                table: "Volume",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DriveThruRpgLastCheckedUtc",
                table: "Volume",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DriveThruRpgMatchStatus",
                table: "Volume",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "NameLocked",
                table: "Volume",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "RpgGeekId",
                table: "Volume",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RpgGeekLastCheckedUtc",
                table: "Volume",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RpgGeekMatchStatus",
                table: "Volume",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RpgMaterialType",
                table: "Volume",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RpgPublicationYear",
                table: "Volume",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RpgPublicationYearLocked",
                table: "Volume",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "RpgPublishers",
                table: "Volume",
                type: "TEXT",
                nullable: true,
                defaultValue: "[]");

            migrationBuilder.AddColumn<bool>(
                name: "RpgPublishersLocked",
                table: "Volume",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "RpgWriters",
                table: "Volume",
                type: "TEXT",
                nullable: true,
                defaultValue: "[]");

            migrationBuilder.AddColumn<bool>(
                name: "RpgWritersLocked",
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

            migrationBuilder.AddColumn<bool>(
                name: "EnableDriveThruRpgMetadata",
                table: "Library",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "EnableRpgGeekMetadata",
                table: "Library",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "DriveThruRpgId",
                table: "Chapter",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DriveThruRpgLastCheckedUtc",
                table: "Chapter",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DriveThruRpgMatchStatus",
                table: "Chapter",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DriveThruRpgId",
                table: "Volume");

            migrationBuilder.DropColumn(
                name: "DriveThruRpgLastCheckedUtc",
                table: "Volume");

            migrationBuilder.DropColumn(
                name: "DriveThruRpgMatchStatus",
                table: "Volume");

            migrationBuilder.DropColumn(
                name: "NameLocked",
                table: "Volume");

            migrationBuilder.DropColumn(
                name: "RpgGeekId",
                table: "Volume");

            migrationBuilder.DropColumn(
                name: "RpgGeekLastCheckedUtc",
                table: "Volume");

            migrationBuilder.DropColumn(
                name: "RpgGeekMatchStatus",
                table: "Volume");

            migrationBuilder.DropColumn(
                name: "RpgMaterialType",
                table: "Volume");

            migrationBuilder.DropColumn(
                name: "RpgPublicationYear",
                table: "Volume");

            migrationBuilder.DropColumn(
                name: "RpgPublicationYearLocked",
                table: "Volume");

            migrationBuilder.DropColumn(
                name: "RpgPublishers",
                table: "Volume");

            migrationBuilder.DropColumn(
                name: "RpgPublishersLocked",
                table: "Volume");

            migrationBuilder.DropColumn(
                name: "RpgWriters",
                table: "Volume");

            migrationBuilder.DropColumn(
                name: "RpgWritersLocked",
                table: "Volume");

            migrationBuilder.DropColumn(
                name: "Summary",
                table: "Volume");

            migrationBuilder.DropColumn(
                name: "SummaryLocked",
                table: "Volume");

            migrationBuilder.DropColumn(
                name: "EnableDriveThruRpgMetadata",
                table: "Library");

            migrationBuilder.DropColumn(
                name: "EnableRpgGeekMetadata",
                table: "Library");

            migrationBuilder.DropColumn(
                name: "DriveThruRpgId",
                table: "Chapter");

            migrationBuilder.DropColumn(
                name: "DriveThruRpgLastCheckedUtc",
                table: "Chapter");

            migrationBuilder.DropColumn(
                name: "DriveThruRpgMatchStatus",
                table: "Chapter");
        }
    }
}
