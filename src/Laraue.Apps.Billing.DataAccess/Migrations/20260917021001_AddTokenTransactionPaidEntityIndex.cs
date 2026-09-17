using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Laraue.Apps.Billing.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddTokenTransactionPaidEntityIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_token_transactions_paid_entity_id_created_at_id",
                table: "token_transactions",
                columns: new[] { "paid_entity_id", "created_at", "id" },
                descending: new[] { false, true, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_token_transactions_paid_entity_id_created_at_id",
                table: "token_transactions");
        }
    }
}
