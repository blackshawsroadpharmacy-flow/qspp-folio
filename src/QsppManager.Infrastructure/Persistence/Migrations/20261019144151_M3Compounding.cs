using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QsppManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M3Compounding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CompoundSuppliers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Supplies = table.Column<string>(type: "TEXT", nullable: false),
                    RegistrationNote = table.Column<string>(type: "TEXT", nullable: false),
                    EvidenceItemId = table.Column<Guid>(type: "TEXT", nullable: true),
                    LastReviewed = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Active = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompoundSuppliers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PotencyRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    StaffMemberId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Role = table.Column<string>(type: "TEXT", nullable: false),
                    Drug = table.Column<string>(type: "TEXT", nullable: false),
                    DosageForm = table.Column<string>(type: "TEXT", nullable: false),
                    TestedOn = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Outcome = table.Column<string>(type: "TEXT", nullable: false),
                    EvidenceItemId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PotencyRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProductRecalls",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Product = table.Column<string>(type: "TEXT", nullable: false),
                    Batch = table.Column<string>(type: "TEXT", nullable: false),
                    ReceivedDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Action = table.Column<string>(type: "TEXT", nullable: false),
                    State = table.Column<string>(type: "TEXT", nullable: false),
                    EvidenceItemId = table.Column<Guid>(type: "TEXT", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductRecalls", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CompoundSuppliers");

            migrationBuilder.DropTable(
                name: "PotencyRecords");

            migrationBuilder.DropTable(
                name: "ProductRecalls");
        }
    }
}
