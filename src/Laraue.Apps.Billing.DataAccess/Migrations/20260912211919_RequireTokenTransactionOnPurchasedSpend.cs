using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Laraue.Apps.Billing.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class RequireTokenTransactionOnPurchasedSpend : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_token_transaction_purchased_token_packs_token_transactions_",
                table: "token_transaction_purchased_token_packs");

            migrationBuilder.AlterColumn<Guid>(
                name: "token_transaction_id",
                table: "token_transaction_purchased_token_packs",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

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

            migrationBuilder.AlterColumn<Guid>(
                name: "token_transaction_id",
                table: "token_transaction_purchased_token_packs",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddForeignKey(
                name: "fk_token_transaction_purchased_token_packs_token_transactions_",
                table: "token_transaction_purchased_token_packs",
                column: "token_transaction_id",
                principalTable: "token_transactions",
                principalColumn: "id");
        }
    }
}
