using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FireflyHR.API.Migrations
{
    /// <inheritdoc />
    public partial class ExpandPaySlipDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "AbsentDeduction",
                table: "PaySlips",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "CashAdvanceDeduction",
                table: "PaySlips",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "DailySalary",
                table: "PaySlips",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "GovernmentContributions",
                table: "PaySlips",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "LateDeduction",
                table: "PaySlips",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "LeavePay",
                table: "PaySlips",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "OvertimePay",
                table: "PaySlips",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "RegularHolidayPay",
                table: "PaySlips",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "SpecialHolidayPay",
                table: "PaySlips",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "UndertimeDeduction",
                table: "PaySlips",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AbsentDeduction",
                table: "PaySlips");

            migrationBuilder.DropColumn(
                name: "CashAdvanceDeduction",
                table: "PaySlips");

            migrationBuilder.DropColumn(
                name: "DailySalary",
                table: "PaySlips");

            migrationBuilder.DropColumn(
                name: "GovernmentContributions",
                table: "PaySlips");

            migrationBuilder.DropColumn(
                name: "LateDeduction",
                table: "PaySlips");

            migrationBuilder.DropColumn(
                name: "LeavePay",
                table: "PaySlips");

            migrationBuilder.DropColumn(
                name: "OvertimePay",
                table: "PaySlips");

            migrationBuilder.DropColumn(
                name: "RegularHolidayPay",
                table: "PaySlips");

            migrationBuilder.DropColumn(
                name: "SpecialHolidayPay",
                table: "PaySlips");

            migrationBuilder.DropColumn(
                name: "UndertimeDeduction",
                table: "PaySlips");
        }
    }
}
