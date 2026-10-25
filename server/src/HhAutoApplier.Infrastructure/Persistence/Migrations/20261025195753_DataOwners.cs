using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HhAutoApplier.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DataOwners : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Searches_Name",
                table: "Searches");

            migrationBuilder.DropIndex(
                name: "IX_Resumes_Name",
                table: "Resumes");

            migrationBuilder.AddColumn<bool>(
                name: "Imported",
                table: "Vacancies",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "UserId",
                table: "Searches",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "UserId",
                table: "Resumes",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SearchId",
                table: "JobApplications",
                type: "integer",
                nullable: true);

            // Поиски, резюме и отклики остаются у владельца по умолчанию (UserId = NULL) — при старте их получит первый
            // администратор (ADMIN_EMAIL). Отклику — поиск, который нашёл вакансию: до #9 он был только по имени в Vacancies.SearchName,
            // а имена поисков были уникальны во всей базе
            migrationBuilder.Sql("""
                UPDATE "JobApplications" a SET "SearchId" = s."Id"
                FROM "Vacancies" v
                JOIN "Searches" s ON s."Name" = v."SearchName"
                WHERE v."Id" = a."VacancyId"
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Searches_UserId_Name",
                table: "Searches",
                columns: new[] { "UserId", "Name" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_Resumes_UserId_Name",
                table: "Resumes",
                columns: new[] { "UserId", "Name" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_JobApplications_SearchId",
                table: "JobApplications",
                column: "SearchId");

            migrationBuilder.AddForeignKey(
                name: "FK_JobApplications_Searches_SearchId",
                table: "JobApplications",
                column: "SearchId",
                principalTable: "Searches",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Resumes_AspNetUsers_UserId",
                table: "Resumes",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Searches_AspNetUsers_UserId",
                table: "Searches",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Код до #9 читает только отклики владельца по умолчанию: возвращаем ему отклики первого администратора
            // (их ему отдал старт), если на ту же вакансию нет своего. Отклики остальных пользователей прежний код не увидит
            migrationBuilder.Sql("""
                UPDATE "JobApplications" a SET "UserId" = NULL
                WHERE a."UserId" = (
                    SELECT u."Id" FROM "AspNetUsers" u
                    JOIN "AspNetUserRoles" ur ON ur."UserId" = u."Id"
                    JOIN "AspNetRoles" r ON r."Id" = ur."RoleId"
                    WHERE r."Name" = 'Admin'
                    ORDER BY u."CreatedAt" LIMIT 1)
                AND NOT EXISTS (SELECT 1 FROM "JobApplications" o WHERE o."VacancyId" = a."VacancyId" AND o."UserId" IS NULL)
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_JobApplications_Searches_SearchId",
                table: "JobApplications");

            migrationBuilder.DropForeignKey(
                name: "FK_Resumes_AspNetUsers_UserId",
                table: "Resumes");

            migrationBuilder.DropForeignKey(
                name: "FK_Searches_AspNetUsers_UserId",
                table: "Searches");

            migrationBuilder.DropIndex(
                name: "IX_Searches_UserId_Name",
                table: "Searches");

            migrationBuilder.DropIndex(
                name: "IX_Resumes_UserId_Name",
                table: "Resumes");

            migrationBuilder.DropIndex(
                name: "IX_JobApplications_SearchId",
                table: "JobApplications");

            migrationBuilder.DropColumn(
                name: "Imported",
                table: "Vacancies");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "Searches");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "Resumes");

            migrationBuilder.DropColumn(
                name: "SearchId",
                table: "JobApplications");

            // Прежняя схема — одна на всех: имена снова уникальны в базе. У разных пользователей совпадают — откат падает
            // и откатывается целиком (миграция в транзакции), данные не теряются
            migrationBuilder.CreateIndex(
                name: "IX_Searches_Name",
                table: "Searches",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Resumes_Name",
                table: "Resumes",
                column: "Name",
                unique: true);
        }
    }
}
