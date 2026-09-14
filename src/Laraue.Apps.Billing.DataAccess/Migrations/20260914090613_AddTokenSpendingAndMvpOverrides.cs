using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Laraue.Apps.Billing.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddTokenSpendingAndMvpOverrides : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_token_transaction_purchased_token_packs_token_transactions_",
                table: "token_transaction_purchased_token_packs");

            migrationBuilder.DropIndex(
                name: "ix_subscriptions_service_id",
                table: "subscriptions");

            migrationBuilder.AddColumn<string>(
                name: "error",
                table: "token_transactions",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "input_tokens_count",
                table: "token_transactions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "reserved_amount",
                table: "token_transactions",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AlterColumn<Guid>(
                name: "token_transaction_id",
                table: "token_transaction_purchased_token_packs",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddColumn<long>(
                name: "included_tokens_count_mvp_override",
                table: "tariffs",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "limit_issues_per_month_mvp_override",
                table: "laraue_boards_team_tariffs",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "limit_free_team_organizations_count_mvp_override",
                table: "laraue_boards_personal_tariffs",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "limit_issues_per_month_mvp_override",
                table: "laraue_boards_personal_tariffs",
                type: "integer",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "laraue_boards_personal_tariffs",
                keyColumn: "id",
                keyValue: new Guid("bd5f3457-601d-4ef1-92b2-47353f6b5a8f"),
                columns: new[] { "limit_free_team_organizations_count_mvp_override", "limit_issues_per_month_mvp_override" },
                values: new object[] { 1000, 200000 });

            migrationBuilder.UpdateData(
                table: "laraue_boards_personal_tariffs",
                keyColumn: "id",
                keyValue: new Guid("e8e4b409-366d-4803-b116-76c1a4a6c8f1"),
                columns: new[] { "limit_free_team_organizations_count_mvp_override", "limit_issues_per_month_mvp_override" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "laraue_boards_team_tariffs",
                keyColumn: "id",
                keyValue: new Guid("89111f8d-292b-4f04-8766-b521e19e6964"),
                column: "limit_issues_per_month_mvp_override",
                value: null);

            migrationBuilder.UpdateData(
                table: "laraue_boards_team_tariffs",
                keyColumn: "id",
                keyValue: new Guid("bb78563f-631c-4f96-afdd-c35ab02b077e"),
                column: "limit_issues_per_month_mvp_override",
                value: null);

            migrationBuilder.UpdateData(
                table: "laraue_boards_team_tariffs",
                keyColumn: "id",
                keyValue: new Guid("d42ebf59-008f-4a1e-8a00-c00f27331e86"),
                column: "limit_issues_per_month_mvp_override",
                value: 200000);

            migrationBuilder.UpdateData(
                table: "tariffs",
                keyColumn: "id",
                keyValue: new Guid("33c1fcec-e6ed-47eb-a64c-27e3deb41038"),
                column: "included_tokens_count_mvp_override",
                value: 1200000L);

            migrationBuilder.UpdateData(
                table: "tariffs",
                keyColumn: "id",
                keyValue: new Guid("67307208-94fa-4da8-8ad4-d6902ba2a1a5"),
                column: "included_tokens_count_mvp_override",
                value: null);

            migrationBuilder.UpdateData(
                table: "tariffs",
                keyColumn: "id",
                keyValue: new Guid("7aa60cba-ee52-4150-922d-2ea9b6c7aeb5"),
                column: "included_tokens_count_mvp_override",
                value: null);

            migrationBuilder.UpdateData(
                table: "tariffs",
                keyColumn: "id",
                keyValue: new Guid("89111f8d-292b-4f04-8766-b521e19e6964"),
                column: "included_tokens_count_mvp_override",
                value: null);

            migrationBuilder.UpdateData(
                table: "tariffs",
                keyColumn: "id",
                keyValue: new Guid("bb78563f-631c-4f96-afdd-c35ab02b077e"),
                column: "included_tokens_count_mvp_override",
                value: null);

            migrationBuilder.UpdateData(
                table: "tariffs",
                keyColumn: "id",
                keyValue: new Guid("bd5f3457-601d-4ef1-92b2-47353f6b5a8f"),
                column: "included_tokens_count_mvp_override",
                value: 2500000L);

            migrationBuilder.UpdateData(
                table: "tariffs",
                keyColumn: "id",
                keyValue: new Guid("d42ebf59-008f-4a1e-8a00-c00f27331e86"),
                column: "included_tokens_count_mvp_override",
                value: 2500000L);

            migrationBuilder.UpdateData(
                table: "tariffs",
                keyColumn: "id",
                keyValue: new Guid("e8e4b409-366d-4803-b116-76c1a4a6c8f1"),
                column: "included_tokens_count_mvp_override",
                value: null);

            migrationBuilder.CreateIndex(
                name: "ix_subscriptions_service_id_paid_entity_id",
                table: "subscriptions",
                columns: new[] { "service_id", "paid_entity_id" });

            migrationBuilder.CreateIndex(
                name: "ix_purchased_token_packs_paid_entity_id_expired_at",
                table: "purchased_token_packs",
                columns: new[] { "paid_entity_id", "expired_at" });

            migrationBuilder.AddForeignKey(
                name: "fk_token_transaction_purchased_token_packs_token_transactions_",
                table: "token_transaction_purchased_token_packs",
                column: "token_transaction_id",
                principalTable: "token_transactions",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_token_transaction_purchased_token_packs_token_transactions_",
                table: "token_transaction_purchased_token_packs");

            migrationBuilder.DropIndex(
                name: "ix_subscriptions_service_id_paid_entity_id",
                table: "subscriptions");

            migrationBuilder.DropIndex(
                name: "ix_purchased_token_packs_paid_entity_id_expired_at",
                table: "purchased_token_packs");

            migrationBuilder.DropColumn(
                name: "error",
                table: "token_transactions");

            migrationBuilder.DropColumn(
                name: "input_tokens_count",
                table: "token_transactions");

            migrationBuilder.DropColumn(
                name: "reserved_amount",
                table: "token_transactions");

            migrationBuilder.DropColumn(
                name: "included_tokens_count_mvp_override",
                table: "tariffs");

            migrationBuilder.DropColumn(
                name: "limit_issues_per_month_mvp_override",
                table: "laraue_boards_team_tariffs");

            migrationBuilder.DropColumn(
                name: "limit_free_team_organizations_count_mvp_override",
                table: "laraue_boards_personal_tariffs");

            migrationBuilder.DropColumn(
                name: "limit_issues_per_month_mvp_override",
                table: "laraue_boards_personal_tariffs");

            migrationBuilder.AlterColumn<Guid>(
                name: "token_transaction_id",
                table: "token_transaction_purchased_token_packs",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.CreateIndex(
                name: "ix_subscriptions_service_id",
                table: "subscriptions",
                column: "service_id");

            migrationBuilder.AddForeignKey(
                name: "fk_token_transaction_purchased_token_packs_token_transactions_",
                table: "token_transaction_purchased_token_packs",
                column: "token_transaction_id",
                principalTable: "token_transactions",
                principalColumn: "id");
        }
    }
}
