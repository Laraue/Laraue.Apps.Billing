using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Laraue.Apps.Billing.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddIsFreeAndDailyGrantTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_free",
                table: "tariffs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateOnly>(
                name: "last_daily_grant_at",
                table: "balance_subscription_tokens",
                type: "date",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "tariffs",
                keyColumn: "id",
                keyValue: new Guid("33c1fcec-e6ed-47eb-a64c-27e3deb41038"),
                column: "is_free",
                value: true);

            migrationBuilder.UpdateData(
                table: "tariffs",
                keyColumn: "id",
                keyValue: new Guid("67307208-94fa-4da8-8ad4-d6902ba2a1a5"),
                column: "is_free",
                value: false);

            migrationBuilder.UpdateData(
                table: "tariffs",
                keyColumn: "id",
                keyValue: new Guid("7aa60cba-ee52-4150-922d-2ea9b6c7aeb5"),
                column: "is_free",
                value: false);

            migrationBuilder.UpdateData(
                table: "tariffs",
                keyColumn: "id",
                keyValue: new Guid("89111f8d-292b-4f04-8766-b521e19e6964"),
                column: "is_free",
                value: false);

            migrationBuilder.UpdateData(
                table: "tariffs",
                keyColumn: "id",
                keyValue: new Guid("bb78563f-631c-4f96-afdd-c35ab02b077e"),
                column: "is_free",
                value: false);

            migrationBuilder.UpdateData(
                table: "tariffs",
                keyColumn: "id",
                keyValue: new Guid("bd5f3457-601d-4ef1-92b2-47353f6b5a8f"),
                column: "is_free",
                value: true);

            migrationBuilder.UpdateData(
                table: "tariffs",
                keyColumn: "id",
                keyValue: new Guid("d42ebf59-008f-4a1e-8a00-c00f27331e86"),
                column: "is_free",
                value: true);

            migrationBuilder.UpdateData(
                table: "tariffs",
                keyColumn: "id",
                keyValue: new Guid("e8e4b409-366d-4803-b116-76c1a4a6c8f1"),
                column: "is_free",
                value: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "is_free",
                table: "tariffs");

            migrationBuilder.DropColumn(
                name: "last_daily_grant_at",
                table: "balance_subscription_tokens");
        }
    }
}
