using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace HhAutoApplier.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class JobApplications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "JobApplications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    VacancyId = table.Column<string>(type: "text", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Score = table.Column<int>(type: "integer", nullable: false),
                    StatusReason = table.Column<string>(type: "text", nullable: true),
                    ResumeProfile = table.Column<string>(type: "text", nullable: true),
                    AppliedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LlmVerdict = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    LlmScore = table.Column<int>(type: "integer", nullable: true),
                    LlmReason = table.Column<string>(type: "text", nullable: true),
                    LlmModel = table.Column<string>(type: "text", nullable: true),
                    LlmEvaluatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CoverLetter = table.Column<string>(type: "text", nullable: true),
                    CoverLetterModel = table.Column<string>(type: "text", nullable: true),
                    CoverLetterAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    History = table.Column<string>(type: "jsonb", nullable: true),
                    Questions = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobApplications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_JobApplications_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_JobApplications_Vacancies_VacancyId",
                        column: x => x.VacancyId,
                        principalTable: "Vacancies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_JobApplications_Status_Score",
                table: "JobApplications",
                columns: new[] { "Status", "Score" });

            migrationBuilder.CreateIndex(
                name: "IX_JobApplications_UserId",
                table: "JobApplications",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_JobApplications_VacancyId_UserId",
                table: "JobApplications",
                columns: new[] { "VacancyId", "UserId" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            // Отклик владельца по умолчанию (UserId = NULL) на каждую вакансию — со статусом, оценкой, письмом и анкетой.
            // История начинается с переноса: прежних переходов вакансия не хранила
            migrationBuilder.Sql("""
                INSERT INTO "JobApplications" ("VacancyId", "UserId", "CreatedAt", "Status", "Score", "StatusReason", "ResumeProfile", "AppliedAt",
                    "LlmVerdict", "LlmScore", "LlmReason", "LlmModel", "LlmEvaluatedAt", "CoverLetter", "CoverLetterModel", "CoverLetterAt",
                    "Questions", "History")
                SELECT "Id", NULL, "CollectedAt", "Status", "Score", "StatusReason", "ResumeProfile", "AppliedAt",
                    "LlmVerdict", "LlmScore", "LlmReason", "LlmModel", "LlmEvaluatedAt", "CoverLetter", "CoverLetterModel", "CoverLetterAt",
                    COALESCE("Questions", '[]'::jsonb),
                    jsonb_build_array(jsonb_build_object('At', COALESCE("AppliedAt", "LlmEvaluatedAt", "CollectedAt"), 'From', NULL, 'To', "Status", 'Reason', 'перенос из вакансии (#8)'))
                FROM "Vacancies"
                """);

            migrationBuilder.DropIndex(
                name: "IX_Vacancies_Status_Score",
                table: "Vacancies");

            migrationBuilder.DropColumn(
                name: "AppliedAt",
                table: "Vacancies");

            migrationBuilder.DropColumn(
                name: "CoverLetter",
                table: "Vacancies");

            migrationBuilder.DropColumn(
                name: "CoverLetterAt",
                table: "Vacancies");

            migrationBuilder.DropColumn(
                name: "CoverLetterModel",
                table: "Vacancies");

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
                name: "Questions",
                table: "Vacancies");

            migrationBuilder.DropColumn(
                name: "ResumeProfile",
                table: "Vacancies");

            migrationBuilder.DropColumn(
                name: "Score",
                table: "Vacancies");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Vacancies");

            migrationBuilder.DropColumn(
                name: "StatusReason",
                table: "Vacancies");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AppliedAt",
                table: "Vacancies",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CoverLetter",
                table: "Vacancies",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CoverLetterAt",
                table: "Vacancies",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CoverLetterModel",
                table: "Vacancies",
                type: "text",
                nullable: true);

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
                name: "Questions",
                table: "Vacancies",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResumeProfile",
                table: "Vacancies",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Score",
                table: "Vacancies",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "Vacancies",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "New");

            migrationBuilder.AddColumn<string>(
                name: "StatusReason",
                table: "Vacancies",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Vacancies_Status_Score",
                table: "Vacancies",
                columns: new[] { "Status", "Score" });

            // Обратно — только отклики владельца по умолчанию: откликов других пользователей прежняя схема не знала
            migrationBuilder.Sql("""
                UPDATE "Vacancies" v SET "Status" = a."Status", "Score" = a."Score", "StatusReason" = a."StatusReason",
                    "ResumeProfile" = a."ResumeProfile", "AppliedAt" = a."AppliedAt", "LlmVerdict" = a."LlmVerdict", "LlmScore" = a."LlmScore",
                    "LlmReason" = a."LlmReason", "LlmModel" = a."LlmModel", "LlmEvaluatedAt" = a."LlmEvaluatedAt", "CoverLetter" = a."CoverLetter",
                    "CoverLetterModel" = a."CoverLetterModel", "CoverLetterAt" = a."CoverLetterAt", "Questions" = a."Questions"
                FROM "JobApplications" a
                WHERE a."VacancyId" = v."Id" AND a."UserId" IS NULL
                """);

            migrationBuilder.DropTable(
                name: "JobApplications");
        }
    }
}
