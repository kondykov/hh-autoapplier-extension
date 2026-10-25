using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HhAutoApplier.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Questionnaires : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Questions",
                table: "Vacancies",
                type: "jsonb",
                nullable: true);

            // Старые вакансии — с пустой анкетой, а не NULL
            migrationBuilder.Sql("""UPDATE "Vacancies" SET "Questions" = '[]' WHERE "Questions" IS NULL""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Questions",
                table: "Vacancies");
        }
    }
}
