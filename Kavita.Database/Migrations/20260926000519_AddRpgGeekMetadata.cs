using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kavita.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddRpgGeekMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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

            migrationBuilder.AddColumn<bool>(
                name: "EnableRpgGeekMetadata",
                table: "Library",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
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
                name: "EnableRpgGeekMetadata",
                table: "Library");
        }
    }
}
