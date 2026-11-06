using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Laraue.Apps.Billing.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddStorageAddons : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "storage_addon_id",
                table: "subscriptions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "storage_addon_id",
                table: "payments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "included_storage_megabytes",
                table: "laraue_boards_team_tariffs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "included_storage_megabytes",
                table: "laraue_boards_personal_tariffs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "storage_addons",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_id = table.Column<int>(type: "integer", nullable: false),
                    code = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    title = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    storage_megabytes = table.Column<int>(type: "integer", nullable: false),
                    price = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_storage_addons", x => x.id);
                    table.ForeignKey(
                        name: "fk_storage_addons_services_service_id",
                        column: x => x.service_id,
                        principalTable: "services",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.UpdateData(
                table: "laraue_boards_personal_tariffs",
                keyColumn: "id",
                keyValue: new Guid("bd5f3457-601d-4ef1-92b2-47353f6b5a8f"),
                column: "included_storage_megabytes",
                value: 100);

            migrationBuilder.UpdateData(
                table: "laraue_boards_personal_tariffs",
                keyColumn: "id",
                keyValue: new Guid("e8e4b409-366d-4803-b116-76c1a4a6c8f1"),
                column: "included_storage_megabytes",
                value: 1024);

            migrationBuilder.UpdateData(
                table: "laraue_boards_team_tariffs",
                keyColumn: "id",
                keyValue: new Guid("89111f8d-292b-4f04-8766-b521e19e6964"),
                column: "included_storage_megabytes",
                value: 1024);

            migrationBuilder.UpdateData(
                table: "laraue_boards_team_tariffs",
                keyColumn: "id",
                keyValue: new Guid("bb78563f-631c-4f96-afdd-c35ab02b077e"),
                column: "included_storage_megabytes",
                value: 1024);

            migrationBuilder.UpdateData(
                table: "laraue_boards_team_tariffs",
                keyColumn: "id",
                keyValue: new Guid("d42ebf59-008f-4a1e-8a00-c00f27331e86"),
                column: "included_storage_megabytes",
                value: 100);

            migrationBuilder.InsertData(
                table: "storage_addons",
                columns: new[] { "id", "code", "is_active", "price", "service_id", "storage_megabytes", "title" },
                values: new object[,]
                {
                    { new Guid("5c0d1d0e-8a4f-4f2b-9a3e-1f6b7c2d4e10"), "storage_10gb", true, 100, 1, 10240, "+10 GB" },
                    { new Guid("9e2a6b3f-4c1d-4e8a-b7f0-3d5c8a1e6b50"), "storage_50gb", true, 300, 1, 51200, "+50 GB" }
                });

            migrationBuilder.CreateIndex(
                name: "ix_subscriptions_storage_addon_id",
                table: "subscriptions",
                column: "storage_addon_id");

            migrationBuilder.CreateIndex(
                name: "ix_payments_storage_addon_id",
                table: "payments",
                column: "storage_addon_id");

            migrationBuilder.CreateIndex(
                name: "ix_storage_addons_service_id",
                table: "storage_addons",
                column: "service_id");

            migrationBuilder.AddForeignKey(
                name: "fk_payments_storage_addons_storage_addon_id",
                table: "payments",
                column: "storage_addon_id",
                principalTable: "storage_addons",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_subscriptions_storage_addons_storage_addon_id",
                table: "subscriptions",
                column: "storage_addon_id",
                principalTable: "storage_addons",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_payments_storage_addons_storage_addon_id",
                table: "payments");

            migrationBuilder.DropForeignKey(
                name: "fk_subscriptions_storage_addons_storage_addon_id",
                table: "subscriptions");

            migrationBuilder.DropTable(
                name: "storage_addons");

            migrationBuilder.DropIndex(
                name: "ix_subscriptions_storage_addon_id",
                table: "subscriptions");

            migrationBuilder.DropIndex(
                name: "ix_payments_storage_addon_id",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "storage_addon_id",
                table: "subscriptions");

            migrationBuilder.DropColumn(
                name: "storage_addon_id",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "included_storage_megabytes",
                table: "laraue_boards_team_tariffs");

            migrationBuilder.DropColumn(
                name: "included_storage_megabytes",
                table: "laraue_boards_personal_tariffs");
        }
    }
}
