using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FireflyHR.API.Migrations
{
    /// <inheritdoc />
    public partial class AddLatLongToTimeRecord : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "Latitude",
                table: "TimeRecords",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "Longitude",
                table: "TimeRecords",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Latitude",
                table: "TimeRecords");

            migrationBuilder.DropColumn(
                name: "Longitude",
                table: "TimeRecords");
        }
    }
}
