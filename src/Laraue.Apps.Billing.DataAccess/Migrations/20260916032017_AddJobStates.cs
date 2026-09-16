using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Laraue.Apps.Billing.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddJobStates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "job_states",
                columns: table => new
                {
                    job_name = table.Column<string>(type: "text", nullable: false),
                    job_data = table.Column<string>(type: "text", nullable: true),
                    last_execution_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    next_execution_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_job_states", x => x.job_name);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "job_states");
        }
    }
}
