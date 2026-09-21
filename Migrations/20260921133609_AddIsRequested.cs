using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FireflyHR.API.Migrations
{
    /// <inheritdoc />
    public partial class AddIsRequested : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsRequested",
                table: "TimeRecords",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsRequested",
                table: "TimeRecords");
        }
    }
}
