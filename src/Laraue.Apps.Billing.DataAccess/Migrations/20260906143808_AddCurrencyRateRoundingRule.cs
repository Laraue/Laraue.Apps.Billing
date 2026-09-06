using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Laraue.Apps.Billing.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddCurrencyRateRoundingRule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "rounding_mode",
                table: "currency_rates",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "rounding_step",
                table: "currency_rates",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.UpdateData(
                table: "currency_rates",
                keyColumn: "id",
                keyValue: new Guid("d8b3db18-09f8-4cd8-b4b8-c6d4402e2292"),
                columns: new[] { "rounding_mode", "rounding_step" },
                values: new object[] { 1, 1m });

            migrationBuilder.UpdateData(
                table: "currency_rates",
                keyColumn: "id",
                keyValue: new Guid("e3f15a99-c08e-420c-9aca-8bd35a13b4ec"),
                columns: new[] { "rounding_mode", "rounding_step" },
                values: new object[] { 0, 0.01m });

            migrationBuilder.UpdateData(
                table: "token_packs",
                keyColumn: "id",
                keyValue: new Guid("2a840fe4-175a-4e69-834e-5f5f6c5e2150"),
                column: "expiration_duration",
                value: new TimeSpan(182, 12, 0, 0, 0));

            migrationBuilder.UpdateData(
                table: "token_packs",
                keyColumn: "id",
                keyValue: new Guid("76abd692-c450-4cd6-80e4-b9c012d91610"),
                column: "expiration_duration",
                value: new TimeSpan(182, 12, 0, 0, 0));

            migrationBuilder.UpdateData(
                table: "token_packs",
                keyColumn: "id",
                keyValue: new Guid("bb76c6c6-41b9-4546-972a-a9730456fdf0"),
                column: "expiration_duration",
                value: new TimeSpan(182, 12, 0, 0, 0));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "rounding_mode",
                table: "currency_rates");

            migrationBuilder.DropColumn(
                name: "rounding_step",
                table: "currency_rates");

            migrationBuilder.UpdateData(
                table: "token_packs",
                keyColumn: "id",
                keyValue: new Guid("2a840fe4-175a-4e69-834e-5f5f6c5e2150"),
                column: "expiration_duration",
                value: new TimeSpan(0, 0, 0, 0, 0));

            migrationBuilder.UpdateData(
                table: "token_packs",
                keyColumn: "id",
                keyValue: new Guid("76abd692-c450-4cd6-80e4-b9c012d91610"),
                column: "expiration_duration",
                value: new TimeSpan(0, 0, 0, 0, 0));

            migrationBuilder.UpdateData(
                table: "token_packs",
                keyColumn: "id",
                keyValue: new Guid("bb76c6c6-41b9-4546-972a-a9730456fdf0"),
                column: "expiration_duration",
                value: new TimeSpan(0, 0, 0, 0, 0));
        }
    }
}
