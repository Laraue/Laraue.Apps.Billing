using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Laraue.Apps.Billing.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_id = table.Column<int>(type: "integer", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    paid_entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tariff_id = table.Column<Guid>(type: "uuid", nullable: true),
                    token_pack_id = table.Column<Guid>(type: "uuid", nullable: true),
                    amount_minor_units = table.Column<long>(type: "bigint", nullable: false),
                    currency_code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    provider_payment_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    provider_data = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    paid_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payments", x => x.id);
                    table.ForeignKey(
                        name: "fk_payments_tariffs_tariff_id",
                        column: x => x.tariff_id,
                        principalTable: "tariffs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_payments_token_packs_token_pack_id",
                        column: x => x.token_pack_id,
                        principalTable: "token_packs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_payments_paid_entity_id_created_at",
                table: "payments",
                columns: new[] { "paid_entity_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_payments_provider_provider_payment_id",
                table: "payments",
                columns: new[] { "provider", "provider_payment_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payments_tariff_id",
                table: "payments",
                column: "tariff_id");

            migrationBuilder.CreateIndex(
                name: "ix_payments_token_pack_id",
                table: "payments",
                column: "token_pack_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payments");
        }
    }
}
