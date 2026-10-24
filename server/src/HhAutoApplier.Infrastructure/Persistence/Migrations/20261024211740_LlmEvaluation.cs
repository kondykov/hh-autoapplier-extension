using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace HhAutoApplier.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LlmEvaluation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LlmEvaluatedAt",
                table: "Vacancies",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LlmModel",
                table: "Vacancies",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LlmReason",
                table: "Vacancies",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LlmScore",
                table: "Vacancies",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LlmVerdict",
                table: "Vacancies",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Summary",
                table: "Resumes",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "LlmCalls",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Purpose = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    VacancyId = table.Column<string>(type: "text", nullable: true),
                    Model = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Prompt = table.Column<string>(type: "text", nullable: false),
                    Response = table.Column<string>(type: "text", nullable: true),
                    PromptTokens = table.Column<int>(type: "integer", nullable: false),
                    CompletionTokens = table.Column<int>(type: "integer", nullable: false),
                    Error = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LlmCalls", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LlmCalls_CreatedAt",
                table: "LlmCalls",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_LlmCalls_VacancyId",
                table: "LlmCalls",
                column: "VacancyId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LlmCalls");

            migrationBuilder.DropColumn(
                name: "LlmEvaluatedAt",
                table: "Vacancies");

            migrationBuilder.DropColumn(
                name: "LlmModel",
                table: "Vacancies");

            migrationBuilder.DropColumn(
                name: "LlmReason",
                table: "Vacancies");

            migrationBuilder.DropColumn(
                name: "LlmScore",
                table: "Vacancies");

            migrationBuilder.DropColumn(
                name: "LlmVerdict",
                table: "Vacancies");

            migrationBuilder.DropColumn(
                name: "Summary",
                table: "Resumes");
        }
    }
}
