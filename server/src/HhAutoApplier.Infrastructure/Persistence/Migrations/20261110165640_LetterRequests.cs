using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HhAutoApplier.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LetterRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LetterRequests",
                table: "JobApplications",
                type: "jsonb",
                nullable: true,
                defaultValueSql: "'[]'::jsonb");

            migrationBuilder.AddColumn<string>(
                name: "LetterRequestsState",
                table: "JobApplications",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "NotExtracted");

            migrationBuilder.AddColumn<int>(
                name: "DesiredSalaryRub",
                table: "CandidateProfiles",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ReadyForTestTask",
                table: "CandidateProfiles",
                type: "boolean",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LetterRequests",
                table: "JobApplications");

            migrationBuilder.DropColumn(
                name: "LetterRequestsState",
                table: "JobApplications");

            migrationBuilder.DropColumn(
                name: "DesiredSalaryRub",
                table: "CandidateProfiles");

            migrationBuilder.DropColumn(
                name: "ReadyForTestTask",
                table: "CandidateProfiles");
        }
    }
}
