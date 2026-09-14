using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FireflyHR.API.Migrations
{
    /// <inheritdoc />
    public partial class AddRemainingBalanceToCashAdvance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "RemainingBalance",
                table: "CashAdvances",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RemainingBalance",
                table: "CashAdvances");
        }
    }
}
