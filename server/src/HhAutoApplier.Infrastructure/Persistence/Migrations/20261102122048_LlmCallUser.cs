using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HhAutoApplier.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LlmCallUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "UserId",
                table: "LlmCalls",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_LlmCalls_UserId_VacancyId",
                table: "LlmCalls",
                columns: new[] { "UserId", "VacancyId" });

            migrationBuilder.AddForeignKey(
                name: "FK_LlmCalls_AspNetUsers_UserId",
                table: "LlmCalls",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LlmCalls_AspNetUsers_UserId",
                table: "LlmCalls");

            migrationBuilder.DropIndex(
                name: "IX_LlmCalls_UserId_VacancyId",
                table: "LlmCalls");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "LlmCalls");
        }
    }
}
