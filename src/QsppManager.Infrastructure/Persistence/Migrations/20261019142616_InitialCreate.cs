using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QsppManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ActorName = table.Column<string>(type: "TEXT", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    AtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Action = table.Column<string>(type: "TEXT", nullable: false),
                    EntityType = table.Column<string>(type: "TEXT", nullable: false),
                    EntityId = table.Column<string>(type: "TEXT", nullable: false),
                    Details = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    PreviousDigest = table.Column<string>(type: "TEXT", maxLength: 70, nullable: false),
                    Digest = table.Column<string>(type: "TEXT", maxLength: 70, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EvidenceItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    FileName = table.Column<string>(type: "TEXT", nullable: false),
                    MimeType = table.Column<string>(type: "TEXT", nullable: false),
                    StoredRelativePath = table.Column<string>(type: "TEXT", nullable: false),
                    Sha256 = table.Column<string>(type: "TEXT", nullable: false),
                    FileBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ReviewState = table.Column<int>(type: "INTEGER", nullable: false),
                    ReviewerUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ReviewedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ReviewNote = table.Column<string>(type: "TEXT", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    EffectiveTo = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    ExpiresHintText = table.Column<string>(type: "TEXT", nullable: false),
                    SupersededById = table.Column<Guid>(type: "TEXT", nullable: true),
                    SupersededReason = table.Column<string>(type: "TEXT", nullable: false),
                    ContainsPatientData = table.Column<bool>(type: "INTEGER", nullable: false),
                    PatientDataAttestation = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EvidenceItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EvidenceLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EvidenceItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RequirementId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Scope = table.Column<string>(type: "TEXT", nullable: false),
                    PeriodFrom = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    PeriodTo = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Rationale = table.Column<string>(type: "TEXT", nullable: false),
                    State = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ReviewedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ReviewedByUserId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EvidenceLinks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InterpretationIssues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Key = table.Column<string>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    ConservativePlanning = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    SourcesJson = table.Column<string>(type: "TEXT", nullable: false),
                    RetrievedDate = table.Column<string>(type: "TEXT", nullable: false),
                    PackVersion = table.Column<string>(type: "TEXT", nullable: false),
                    LastReviewedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ReviewedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ResolutionNote = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InterpretationIssues", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Pharmacies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Jurisdiction = table.Column<string>(type: "TEXT", nullable: false),
                    TimeZoneId = table.Column<string>(type: "TEXT", nullable: false),
                    ServiceScopeFlags = table.Column<long>(type: "INTEGER", nullable: false),
                    CycleStartDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    AccreditationExpiryDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    OnsiteAssessmentDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    IsDemo = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Pharmacies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PrintJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DocumentKind = table.Column<string>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    SubjectId = table.Column<Guid>(type: "TEXT", nullable: true),
                    PdfRelativePath = table.Column<string>(type: "TEXT", nullable: false),
                    PrinterName = table.Column<string>(type: "TEXT", nullable: false),
                    Copies = table.Column<int>(type: "INTEGER", nullable: false),
                    Duplex = table.Column<bool>(type: "INTEGER", nullable: false),
                    State = table.Column<int>(type: "INTEGER", nullable: false),
                    ResultDetail = table.Column<string>(type: "TEXT", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SpoolJobId = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrintJobs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PrintPresets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    PrinterName = table.Column<string>(type: "TEXT", nullable: false),
                    Copies = table.Column<int>(type: "INTEGER", nullable: false),
                    Duplex = table.Column<bool>(type: "INTEGER", nullable: false),
                    Landscape = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsDefault = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrintPresets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Requirements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RefKind = table.Column<int>(type: "INTEGER", nullable: false),
                    Ref = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    PlainSummary = table.Column<string>(type: "TEXT", nullable: false),
                    SourceDocFile = table.Column<string>(type: "TEXT", nullable: false),
                    SourceVersion = table.Column<string>(type: "TEXT", nullable: false),
                    SourceDocumentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AnchorFile = table.Column<string>(type: "TEXT", nullable: false),
                    AnchorLines = table.Column<string>(type: "TEXT", nullable: false),
                    AnchorApproximate = table.Column<bool>(type: "INTEGER", nullable: false),
                    AnchorPage = table.Column<int>(type: "INTEGER", nullable: true),
                    WordingExcerpt = table.Column<string>(type: "TEXT", maxLength: 600, nullable: false),
                    Domain = table.Column<int>(type: "INTEGER", nullable: false),
                    ApplicabilityHint = table.Column<int>(type: "INTEGER", nullable: false),
                    LettersVerified = table.Column<bool>(type: "INTEGER", nullable: false),
                    InterpretationFlagsJson = table.Column<string>(type: "TEXT", nullable: false),
                    Priority = table.Column<string>(type: "TEXT", nullable: false),
                    ExtraJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Requirements", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RequirementStates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RequirementId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PharmacyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Applicability = table.Column<int>(type: "INTEGER", nullable: false),
                    ApplicabilityReason = table.Column<string>(type: "TEXT", nullable: false),
                    ApplicabilityReviewerUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ApplicabilityDecidedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Readiness = table.Column<int>(type: "INTEGER", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OwnerUnassigned = table.Column<bool>(type: "INTEGER", nullable: false),
                    NextAction = table.Column<string>(type: "TEXT", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    ExternalOutcomeRef = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ExternalOutcomeAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RequirementStates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SourceDocuments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", nullable: false),
                    FileName = table.Column<string>(type: "TEXT", nullable: false),
                    Role = table.Column<string>(type: "TEXT", nullable: false),
                    SourceVersion = table.Column<string>(type: "TEXT", nullable: false),
                    OriginPath = table.Column<string>(type: "TEXT", nullable: false),
                    StoredRelativePath = table.Column<string>(type: "TEXT", nullable: false),
                    Sha256Hex = table.Column<string>(type: "TEXT", nullable: false),
                    FileBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    ProvenanceNote = table.Column<string>(type: "TEXT", nullable: false),
                    RetrievedDate = table.Column<string>(type: "TEXT", nullable: false),
                    PackVersion = table.Column<string>(type: "TEXT", nullable: false),
                    ImportedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SourceDocuments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Tasks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Details = table.Column<string>(type: "TEXT", nullable: false),
                    RequirementId = table.Column<Guid>(type: "TEXT", nullable: true),
                    EvidenceItemId = table.Column<Guid>(type: "TEXT", nullable: true),
                    AssignedToUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Priority = table.Column<int>(type: "INTEGER", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    State = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CompletedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CompletionNote = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tasks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserName = table.Column<string>(type: "TEXT", nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", nullable: false),
                    Role = table.Column<int>(type: "INTEGER", nullable: false),
                    PassphraseSalt = table.Column<string>(type: "TEXT", nullable: false),
                    PassphraseHash = table.Column<string>(type: "TEXT", nullable: false),
                    PassphraseIterations = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RequirementEvidenceExamples",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RequirementId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Text = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Mandatory = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RequirementEvidenceExamples", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RequirementEvidenceExamples_Requirements_RequirementId",
                        column: x => x.RequirementId,
                        principalTable: "Requirements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EvidenceItems_Sha256",
                table: "EvidenceItems",
                column: "Sha256");

            migrationBuilder.CreateIndex(
                name: "IX_EvidenceLinks_EvidenceItemId_RequirementId",
                table: "EvidenceLinks",
                columns: new[] { "EvidenceItemId", "RequirementId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EvidenceLinks_RequirementId",
                table: "EvidenceLinks",
                column: "RequirementId");

            migrationBuilder.CreateIndex(
                name: "IX_InterpretationIssues_Key",
                table: "InterpretationIssues",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RequirementEvidenceExamples_RequirementId",
                table: "RequirementEvidenceExamples",
                column: "RequirementId");

            migrationBuilder.CreateIndex(
                name: "IX_Requirements_Domain",
                table: "Requirements",
                column: "Domain");

            migrationBuilder.CreateIndex(
                name: "IX_Requirements_RefKind_Ref",
                table: "Requirements",
                columns: new[] { "RefKind", "Ref" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RequirementStates_PharmacyId_RequirementId",
                table: "RequirementStates",
                columns: new[] { "PharmacyId", "RequirementId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SourceDocuments_Sha256Hex",
                table: "SourceDocuments",
                column: "Sha256Hex");

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_State_DueDate",
                table: "Tasks",
                columns: new[] { "State", "DueDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Users_UserName",
                table: "Users",
                column: "UserName",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditEvents");

            migrationBuilder.DropTable(
                name: "EvidenceItems");

            migrationBuilder.DropTable(
                name: "EvidenceLinks");

            migrationBuilder.DropTable(
                name: "InterpretationIssues");

            migrationBuilder.DropTable(
                name: "Pharmacies");

            migrationBuilder.DropTable(
                name: "PrintJobs");

            migrationBuilder.DropTable(
                name: "PrintPresets");

            migrationBuilder.DropTable(
                name: "RequirementEvidenceExamples");

            migrationBuilder.DropTable(
                name: "RequirementStates");

            migrationBuilder.DropTable(
                name: "SourceDocuments");

            migrationBuilder.DropTable(
                name: "Tasks");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "Requirements");
        }
    }
}
