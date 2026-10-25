using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace HhAutoApplier.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Resumes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Match = table.Column<string>(type: "text", nullable: false),
                    Keywords = table.Column<List<string>>(type: "text[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Resumes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Searches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Query = table.Column<string>(type: "text", nullable: false),
                    Area = table.Column<string>(type: "text", nullable: true),
                    ExtraParams = table.Column<string>(type: "text", nullable: true),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    Rules = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Searches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Vacancies",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Company = table.Column<string>(type: "text", nullable: false),
                    Url = table.Column<string>(type: "text", nullable: false),
                    Area = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    SalaryFrom = table.Column<int>(type: "integer", nullable: true),
                    SalaryTo = table.Column<int>(type: "integer", nullable: true),
                    SalaryCurrency = table.Column<string>(type: "text", nullable: true),
                    WorkFormat = table.Column<string>(type: "text", nullable: false),
                    Stack = table.Column<string[]>(type: "text[]", nullable: false),
                    HasTest = table.Column<bool>(type: "boolean", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CollectedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SearchName = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Score = table.Column<int>(type: "integer", nullable: false),
                    StatusReason = table.Column<string>(type: "text", nullable: true),
                    ResumeProfile = table.Column<string>(type: "text", nullable: true),
                    AppliedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Vacancies", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Resumes_Name",
                table: "Resumes",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Searches_Name",
                table: "Searches",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Vacancies_SearchName",
                table: "Vacancies",
                column: "SearchName");

            migrationBuilder.CreateIndex(
                name: "IX_Vacancies_Status_Score",
                table: "Vacancies",
                columns: new[] { "Status", "Score" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Resumes");

            migrationBuilder.DropTable(
                name: "Searches");

            migrationBuilder.DropTable(
                name: "Vacancies");
        }
    }
}
