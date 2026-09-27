using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kavita.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddDriveThruRpgMatchStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DriveThruRpgLastCheckedUtc",
                table: "Volume");

            migrationBuilder.DropColumn(
                name: "DriveThruRpgMatchStatus",
                table: "Volume");
        }
    }
}
