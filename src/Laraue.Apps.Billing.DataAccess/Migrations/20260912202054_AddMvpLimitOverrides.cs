using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Laraue.Apps.Billing.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddMvpLimitOverrides : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
                columns: new[] { "limit_free_team_organizations_count", "limit_free_team_organizations_count_mvp_override", "limit_issues_per_month", "limit_issues_per_month_mvp_override" },
                values: new object[] { 1, 1000, 500, 200000 });

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
                columns: new[] { "limit_issues_per_month", "limit_issues_per_month_mvp_override" },
                values: new object[] { 500, 200000 });

            migrationBuilder.UpdateData(
                table: "tariffs",
                keyColumn: "id",
                keyValue: new Guid("33c1fcec-e6ed-47eb-a64c-27e3deb41038"),
                columns: new[] { "included_tokens_count", "included_tokens_count_mvp_override" },
                values: new object[] { 0L, 1200000L });

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
                columns: new[] { "included_tokens_count", "included_tokens_count_mvp_override" },
                values: new object[] { 0L, 2500000L });

            migrationBuilder.UpdateData(
                table: "tariffs",
                keyColumn: "id",
                keyValue: new Guid("d42ebf59-008f-4a1e-8a00-c00f27331e86"),
                columns: new[] { "included_tokens_count", "included_tokens_count_mvp_override" },
                values: new object[] { 0L, 2500000L });

            migrationBuilder.UpdateData(
                table: "tariffs",
                keyColumn: "id",
                keyValue: new Guid("e8e4b409-366d-4803-b116-76c1a4a6c8f1"),
                column: "included_tokens_count_mvp_override",
                value: null);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
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
        }
    }
}
