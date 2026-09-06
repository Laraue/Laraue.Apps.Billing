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
                    rate_to_usd = table.Column<decimal>(type: "numeric", nullable: false),
                    rounding_step = table.Column<decimal>(type: "numeric", nullable: false),
                    rounding_mode = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_currency_rates", x => x.id);
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
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
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
                    title = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    tokens_count = table.Column<long>(type: "bigint", nullable: false),
                    price = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    expiration_duration = table.Column<int>(type: "integer", nullable: false),
                    expiration_period = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_token_packs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "laraue_boards_personal_tariffs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    limit_issues_per_month = table.Column<int>(type: "integer", nullable: true),
                    limit_free_team_organizations_count = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_laraue_boards_personal_tariffs", x => x.id);
                    table.ForeignKey(
                        name: "fk_laraue_boards_personal_tariffs_tariffs_id",
                        column: x => x.id,
                        principalTable: "tariffs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "laraue_boards_team_tariffs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    limit_issues_per_month = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_laraue_boards_team_tariffs", x => x.id);
                    table.ForeignKey(
                        name: "fk_laraue_boards_team_tariffs_tariffs_id",
                        column: x => x.id,
                        principalTable: "tariffs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "markdown_translator_personal_tariffs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    included_daily_free_tokens_count = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_markdown_translator_personal_tariffs", x => x.id);
                    table.ForeignKey(
                        name: "fk_markdown_translator_personal_tariffs_tariffs_id",
                        column: x => x.id,
                        principalTable: "tariffs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "subscriptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_id = table.Column<int>(type: "integer", nullable: false),
                    tariff_id = table.Column<Guid>(type: "uuid", nullable: false),
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
                columns: new[] { "id", "code", "rate_to_usd", "rounding_mode", "rounding_step" },
                values: new object[,]
                {
                    { new Guid("d8b3db18-09f8-4cd8-b4b8-c6d4402e2292"), "RUB", 0.012m, 1, 1m },
                    { new Guid("e3f15a99-c08e-420c-9aca-8bd35a13b4ec"), "USD", 1m, 0, 0.01m }
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
                columns: new[] { "id", "billing_period", "included_tokens_count", "is_active", "price", "title", "type" },
                values: new object[,]
                {
                    { new Guid("33c1fcec-e6ed-47eb-a64c-27e3deb41038"), 1, 0L, true, 0, "Free", 0 },
                    { new Guid("67307208-94fa-4da8-8ad4-d6902ba2a1a5"), 0, 300000L, true, 400, "Plus", 0 },
                    { new Guid("7aa60cba-ee52-4150-922d-2ea9b6c7aeb5"), 0, 1200000L, true, 1000, "Pro", 0 },
                    { new Guid("89111f8d-292b-4f04-8766-b521e19e6964"), 0, 750000L, true, 600, "Team", 1 },
                    { new Guid("bb78563f-631c-4f96-afdd-c35ab02b077e"), 0, 2500000L, true, 1400, "Business", 1 },
                    { new Guid("bd5f3457-601d-4ef1-92b2-47353f6b5a8f"), 1, 0L, true, 0, "Free", 0 },
                    { new Guid("d42ebf59-008f-4a1e-8a00-c00f27331e86"), 1, 0L, true, 0, "Free", 1 },
                    { new Guid("e8e4b409-366d-4803-b116-76c1a4a6c8f1"), 0, 300000L, true, 400, "Plus", 0 }
                });

            migrationBuilder.InsertData(
                table: "token_packs",
                columns: new[] { "id", "code", "expiration_duration", "expiration_period", "is_active", "price", "title", "tokens_count" },
                values: new object[,]
                {
                    { new Guid("2a840fe4-175a-4e69-834e-5f5f6c5e2150"), "large", 6, 0, true, 4000, "Large", 3000000L },
                    { new Guid("76abd692-c450-4cd6-80e4-b9c012d91610"), "small", 6, 0, true, 300, "Small", 100000L },
                    { new Guid("bb76c6c6-41b9-4546-972a-a9730456fdf0"), "medium", 6, 0, true, 1200, "Medium", 600000L }
                });

            migrationBuilder.InsertData(
                table: "laraue_boards_personal_tariffs",
                columns: new[] { "id", "limit_free_team_organizations_count", "limit_issues_per_month" },
                values: new object[,]
                {
                    { new Guid("bd5f3457-601d-4ef1-92b2-47353f6b5a8f"), 1, 500 },
                    { new Guid("e8e4b409-366d-4803-b116-76c1a4a6c8f1"), null, 50000 }
                });

            migrationBuilder.InsertData(
                table: "laraue_boards_team_tariffs",
                columns: new[] { "id", "limit_issues_per_month" },
                values: new object[,]
                {
                    { new Guid("89111f8d-292b-4f04-8766-b521e19e6964"), 50000 },
                    { new Guid("bb78563f-631c-4f96-afdd-c35ab02b077e"), 200000 },
                    { new Guid("d42ebf59-008f-4a1e-8a00-c00f27331e86"), 500 }
                });

            migrationBuilder.InsertData(
                table: "markdown_translator_personal_tariffs",
                columns: new[] { "id", "included_daily_free_tokens_count" },
                values: new object[,]
                {
                    { new Guid("33c1fcec-e6ed-47eb-a64c-27e3deb41038"), 10000L },
                    { new Guid("67307208-94fa-4da8-8ad4-d6902ba2a1a5"), 10000L },
                    { new Guid("7aa60cba-ee52-4150-922d-2ea9b6c7aeb5"), 10000L }
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
