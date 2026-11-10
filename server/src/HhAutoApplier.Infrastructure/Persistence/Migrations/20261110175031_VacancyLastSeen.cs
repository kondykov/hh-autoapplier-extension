using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HhAutoApplier.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class VacancyLastSeen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ArchiveCheckedAt",
                table: "Vacancies",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastSeenAt",
                table: "Vacancies",
                type: "timestamp with time zone",
                nullable: true);

            // У старых строк последнего вхождения нет: начальное значение — дата первого появления
            migrationBuilder.Sql("""UPDATE "Vacancies" SET "LastSeenAt" = "CollectedAt";""");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "LastSeenAt",
                table: "Vacancies",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ArchiveCheckedAt",
                table: "Vacancies");

            migrationBuilder.DropColumn(
                name: "LastSeenAt",
                table: "Vacancies");
        }
    }
}
