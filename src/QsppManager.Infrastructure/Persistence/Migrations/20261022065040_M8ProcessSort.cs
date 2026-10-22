using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QsppManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M8ProcessSort : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FolioBatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourceDirectory = table.Column<string>(type: "TEXT", maxLength: 800, nullable: false),
                    OutputDirectory = table.Column<string>(type: "TEXT", maxLength: 800, nullable: false),
                    State = table.Column<int>(type: "INTEGER", nullable: false),
                    ScannedCount = table.Column<int>(type: "INTEGER", nullable: false),
                    SkippedCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ScanErrorsJson = table.Column<string>(type: "TEXT", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    FinishedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ModeDescription = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    SettingsJson = table.Column<string>(type: "TEXT", nullable: false),
                    ManifestRelativePath = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FolioBatches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FolioItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BatchId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourceRelativePath = table.Column<string>(type: "TEXT", maxLength: 600, nullable: false),
                    SourceSha256 = table.Column<string>(type: "TEXT", nullable: false),
                    FileBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    MimeType = table.Column<string>(type: "TEXT", nullable: false),
                    ParserUsed = table.Column<string>(type: "TEXT", nullable: false),
                    PageCount = table.Column<int>(type: "INTEGER", nullable: false),
                    TextCacheRelative = table.Column<string>(type: "TEXT", nullable: false),
                    State = table.Column<int>(type: "INTEGER", nullable: false),
                    ProposalsJson = table.Column<string>(type: "TEXT", nullable: false),
                    ApprovedJson = table.Column<string>(type: "TEXT", nullable: false),
                    EvidenceItemId = table.Column<Guid>(type: "TEXT", nullable: true),
                    DestinationClonesJson = table.Column<string>(type: "TEXT", nullable: false),
                    ErrorText = table.Column<string>(type: "TEXT", nullable: false),
                    SensitiveReasonsJson = table.Column<string>(type: "TEXT", nullable: false),
                    AddedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PublishedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    FolioBatchId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FolioItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FolioItems_EvidenceItems_EvidenceItemId",
                        column: x => x.EvidenceItemId,
                        principalTable: "EvidenceItems",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_FolioItems_FolioBatches_FolioBatchId",
                        column: x => x.FolioBatchId,
                        principalTable: "FolioBatches",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_FolioItems_BatchId",
                table: "FolioItems",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_FolioItems_EvidenceItemId",
                table: "FolioItems",
                column: "EvidenceItemId");

            migrationBuilder.CreateIndex(
                name: "IX_FolioItems_FolioBatchId",
                table: "FolioItems",
                column: "FolioBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_FolioItems_SourceSha256",
                table: "FolioItems",
                column: "SourceSha256");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FolioItems");

            migrationBuilder.DropTable(
                name: "FolioBatches");
        }
    }
}
