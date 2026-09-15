using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FireflyHR.API.Migrations
{
    /// <inheritdoc />
    public partial class AddDailyAllowanceToEmployee : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "MonthlyAllowance",
                table: "Employees",
                newName: "DailyAllowance");

            migrationBuilder.AddColumn<decimal>(
                name: "DailyAllowance",
                table: "PaySlips",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "PagIbigDeduction",
                table: "PaySlips",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "PhilHealthDeduction",
                table: "PaySlips",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "SssDeduction",
                table: "PaySlips",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<bool>(
                name: "HasGovernmentDeductions",
                table: "Employees",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DailyAllowance",
                table: "PaySlips");

            migrationBuilder.DropColumn(
                name: "PagIbigDeduction",
                table: "PaySlips");

            migrationBuilder.DropColumn(
                name: "PhilHealthDeduction",
                table: "PaySlips");

            migrationBuilder.DropColumn(
                name: "SssDeduction",
                table: "PaySlips");

            migrationBuilder.DropColumn(
                name: "HasGovernmentDeductions",
                table: "Employees");

            migrationBuilder.RenameColumn(
                name: "DailyAllowance",
                table: "Employees",
                newName: "MonthlyAllowance");
        }
    }
}
