using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Laraue.Apps.Billing.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "balance_purchased_tokens",
                columns: table => new
                {
                    paid_entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    balance = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_balance_purchased_tokens", x => x.paid_entity_id);
                });

            migrationBuilder.CreateTable(
                name: "balance_subscription_tokens",
                columns: table => new
                {
                    subscription_id = table.Column<Guid>(type: "uuid", nullable: false),
                    free_tokens_count = table.Column<long>(type: "bigint", nullable: false),
                    subscription_tokens_count = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_balance_subscription_tokens", x => x.subscription_id);
                });

            migrationBuilder.CreateTable(
                name: "currency_rates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    rate_to_usd = table.Column<decimal>(type: "numeric", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_currency_rates", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "laraue_boards_personal_tariffs",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    limit_issues_per_month = table.Column<int>(type: "integer", nullable: true),
                    limit_free_team_organizations_count = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_laraue_boards_personal_tariffs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "laraue_boards_team_tariffs",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    limit_issues_per_month = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_laraue_boards_team_tariffs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "markdown_translator_personal_tariffs",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    included_daily_free_tokens_count = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_markdown_translator_personal_tariffs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "services",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    name = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_services", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tariffs",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    code = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    price = table.Column<int>(type: "integer", nullable: false),
                    billing_period = table.Column<int>(type: "integer", nullable: false),
                    included_tokens_count = table.Column<long>(type: "bigint", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tariffs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "token_packs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    tokens_count = table.Column<long>(type: "bigint", nullable: false),
                    price = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    expiration_duration = table.Column<TimeSpan>(type: "interval", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_token_packs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "subscriptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_id = table.Column<int>(type: "integer", nullable: false),
                    tariff_id = table.Column<int>(type: "integer", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    paid_entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    seats = table.Column<int>(type: "integer", nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    current_period_started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    current_period_finishes_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_subscriptions", x => x.id);
                    table.ForeignKey(
                        name: "fk_subscriptions_services_service_id",
                        column: x => x.service_id,
                        principalTable: "services",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_subscriptions_tariffs_tariff_id",
                        column: x => x.tariff_id,
                        principalTable: "tariffs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "purchased_token_packs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    paid_entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_pack_id = table.Column<Guid>(type: "uuid", nullable: false),
                    purchased_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    expired_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_purchased_token_packs", x => x.id);
                    table.ForeignKey(
                        name: "fk_purchased_token_packs_token_packs_token_pack_id",
                        column: x => x.token_pack_id,
                        principalTable: "token_packs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "token_transaction_subscription_token_packs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    subscription_id = table.Column<Guid>(type: "uuid", nullable: false),
                    charged_free_amount = table.Column<long>(type: "bigint", nullable: true),
                    charged_amount = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_token_transaction_subscription_token_packs", x => x.id);
                    table.ForeignKey(
                        name: "fk_token_transaction_subscription_token_packs_subscriptions_su",
                        column: x => x.subscription_id,
                        principalTable: "subscriptions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "token_transactions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    paid_entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    finished_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    delta = table.Column<long>(type: "bigint", nullable: false),
                    reason = table.Column<int>(type: "integer", nullable: false),
                    subscription_tokens_spent_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_token_transactions", x => x.id);
                    table.ForeignKey(
                        name: "fk_token_transactions_token_transaction_subscription_token_pac",
                        column: x => x.subscription_tokens_spent_id,
                        principalTable: "token_transaction_subscription_token_packs",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "token_transaction_purchased_token_packs",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    purchased_token_pack_id = table.Column<Guid>(type: "uuid", nullable: false),
                    charged_amount = table.Column<long>(type: "bigint", nullable: false),
                    balance_after = table.Column<long>(type: "bigint", nullable: false),
                    token_transaction_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_token_transaction_purchased_token_packs", x => x.id);
                    table.ForeignKey(
                        name: "fk_token_transaction_purchased_token_packs_purchased_token_pac",
                        column: x => x.purchased_token_pack_id,
                        principalTable: "purchased_token_packs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_token_transaction_purchased_token_packs_token_transactions_",
                        column: x => x.token_transaction_id,
                        principalTable: "token_transactions",
                        principalColumn: "id");
                });

            migrationBuilder.InsertData(
                table: "currency_rates",
                columns: new[] { "id", "code", "rate_to_usd" },
                values: new object[,]
                {
                    { new Guid("d8b3db18-09f8-4cd8-b4b8-c6d4402e2292"), "RUB", 0.012m },
                    { new Guid("e3f15a99-c08e-420c-9aca-8bd35a13b4ec"), "USD", 1m }
                });

            migrationBuilder.InsertData(
                table: "laraue_boards_personal_tariffs",
                columns: new[] { "id", "limit_free_team_organizations_count", "limit_issues_per_month" },
                values: new object[,]
                {
                    { 0, 1, 500 },
                    { 1, null, 50000 }
                });

            migrationBuilder.InsertData(
                table: "laraue_boards_team_tariffs",
                columns: new[] { "id", "limit_issues_per_month" },
                values: new object[,]
                {
                    { 2, 500 },
                    { 3, 50000 },
                    { 4, 200000 }
                });

            migrationBuilder.InsertData(
                table: "markdown_translator_personal_tariffs",
                columns: new[] { "id", "included_daily_free_tokens_count" },
                values: new object[,]
                {
                    { 5, 10000L },
                    { 6, 10000L },
                    { 7, 10000L }
                });

            migrationBuilder.InsertData(
                table: "services",
                columns: new[] { "id", "code", "name" },
                values: new object[,]
                {
                    { 1, "laraue_boards", "Laraue Boards" },
                    { 2, "markdown_translator", "Markdown Translator" }
                });

            migrationBuilder.InsertData(
                table: "tariffs",
                columns: new[] { "id", "billing_period", "code", "included_tokens_count", "is_active", "price", "type" },
                values: new object[,]
                {
                    { 0, 0, "personal_free", 0L, true, 0, 0 },
                    { 1, 0, "personal_plus", 300000L, true, 400, 0 },
                    { 2, 0, "team_free", 0L, true, 0, 1 },
                    { 3, 0, "team", 750000L, true, 600, 1 },
                    { 4, 0, "team_business", 2500000L, true, 1400, 1 },
                    { 5, 0, "free", 0L, true, 0, 0 },
                    { 6, 0, "plus", 300000L, true, 400, 0 },
                    { 7, 0, "pro", 1200000L, true, 1000, 0 }
                });

            migrationBuilder.InsertData(
                table: "token_packs",
                columns: new[] { "id", "code", "expiration_duration", "is_active", "price", "tokens_count" },
                values: new object[,]
                {
                    { new Guid("2a840fe4-175a-4e69-834e-5f5f6c5e2150"), "large", new TimeSpan(0, 0, 0, 0, 0), true, 4000, 3000000L },
                    { new Guid("76abd692-c450-4cd6-80e4-b9c012d91610"), "small", new TimeSpan(0, 0, 0, 0, 0), true, 300, 100000L },
                    { new Guid("bb76c6c6-41b9-4546-972a-a9730456fdf0"), "medium", new TimeSpan(0, 0, 0, 0, 0), true, 1200, 600000L }
                });

            migrationBuilder.CreateIndex(
                name: "ix_purchased_token_packs_token_pack_id",
                table: "purchased_token_packs",
                column: "token_pack_id");

            migrationBuilder.CreateIndex(
                name: "ix_subscriptions_service_id",
                table: "subscriptions",
                column: "service_id");

            migrationBuilder.CreateIndex(
                name: "ix_subscriptions_tariff_id",
                table: "subscriptions",
                column: "tariff_id");

            migrationBuilder.CreateIndex(
                name: "ix_token_transaction_purchased_token_packs_purchased_token_pac",
                table: "token_transaction_purchased_token_packs",
                column: "purchased_token_pack_id");

            migrationBuilder.CreateIndex(
                name: "ix_token_transaction_purchased_token_packs_token_transaction_id",
                table: "token_transaction_purchased_token_packs",
                column: "token_transaction_id");

            migrationBuilder.CreateIndex(
                name: "ix_token_transaction_subscription_token_packs_subscription_id",
                table: "token_transaction_subscription_token_packs",
                column: "subscription_id");

            migrationBuilder.CreateIndex(
                name: "ix_token_transactions_subscription_tokens_spent_id",
                table: "token_transactions",
                column: "subscription_tokens_spent_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "balance_purchased_tokens");

            migrationBuilder.DropTable(
                name: "balance_subscription_tokens");

            migrationBuilder.DropTable(
                name: "currency_rates");

            migrationBuilder.DropTable(
                name: "laraue_boards_personal_tariffs");

            migrationBuilder.DropTable(
                name: "laraue_boards_team_tariffs");

            migrationBuilder.DropTable(
                name: "markdown_translator_personal_tariffs");

            migrationBuilder.DropTable(
                name: "token_transaction_purchased_token_packs");

            migrationBuilder.DropTable(
                name: "purchased_token_packs");

            migrationBuilder.DropTable(
                name: "token_transactions");

            migrationBuilder.DropTable(
                name: "token_packs");

            migrationBuilder.DropTable(
                name: "token_transaction_subscription_token_packs");

            migrationBuilder.DropTable(
                name: "subscriptions");

            migrationBuilder.DropTable(
                name: "services");

            migrationBuilder.DropTable(
                name: "tariffs");
        }
    }
}
