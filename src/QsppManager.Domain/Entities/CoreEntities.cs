namespace QsppManager.Domain.Entities;

/// <summary>Immutable corpus record imported from a source document. Never edited by
/// the pharmacy; the per-pharmacy overlay lives in RequirementState.</summary>
public class Requirement
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public RefKind RefKind { get; set; }
    public string Ref { get; set; } = "";
    public string Title { get; set; } = "";
    public string PlainSummary { get; set; } = "";
    public string SourceDocFile { get; set; } = "";
    public string SourceVersion { get; set; } = "";
    public Guid SourceDocumentId { get; set; }

    /// <summary>Approximate line anchor into the extracted text; always provisional.</summary>
    public string AnchorFile { get; set; } = "";
    public string AnchorLines { get; set; } = "";
    public bool AnchorApproximate { get; set; } = true;

    /// <summary>Page anchor where derivable; null when not (owner brief: do not invent).</summary>
    public int? AnchorPage { get; set; }

    /// <summary>True when the page number was verified by locating the wording on that page
    /// (page-anchors.json provenance). Unverified page numbers keep AnchorApproximate = true.</summary>
    public bool AnchorVerified { get; set; } = false;

    public string WordingExcerpt { get; set; } = "";
    public DomainPart Domain { get; set; }
    public ApplicabilityHint ApplicabilityHint { get; set; }
    public bool LettersVerified { get; set; } = false;
    public string InterpretationFlagsJson { get; set; } = "[]";
    public string Priority { get; set; } = "";
    public string ExtraJson { get; set; } = "{}";

    public List<RequirementEvidenceExample> EvidenceExamples { get; set; } = new();
}

public class RequirementEvidenceExample
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RequirementId { get; set; }
    public string Text { get; set; } = "";
    /// <summary>True only where the source itself demands evidence unambiguously; otherwise an example.</summary>
    public bool Mandatory { get; set; }
}

/// <summary>Per-pharmacy overlay: applicability, readiness, ownership, next action.</summary>
public class RequirementState
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RequirementId { get; set; }
    public Guid PharmacyId { get; set; }

    public Applicability Applicability { get; set; } = Applicability.Unknown;
    public string ApplicabilityReason { get; set; } = "";
    public Guid? ApplicabilityReviewerUserId { get; set; }
    public DateTime? ApplicabilityDecidedAt { get; set; }

    public RequirementReadiness Readiness { get; set; } = RequirementReadiness.NotStarted;
    public Guid OwnerUserId { get; set; }
    public bool OwnerUnassigned { get; set; } = true;   // hitlist imports arrive with blank owners
    public string NextAction { get; set; } = "";
    public string Notes { get; set; } = "";

    /// <summary>For AssessorAccepted only: the external outcome reference (report, letter ref).</summary>
    public string ExternalOutcomeRef { get; set; } = "";
    public DateTime? ExternalOutcomeAt { get; set; }

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class EvidenceItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "";
    public EvidenceKind Kind { get; set; }
    public string FileName { get; set; } = "";
    public string MimeType { get; set; } = "";
    public string StoredRelativePath { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public long FileBytes { get; set; }

    public Guid CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public EvidenceReviewState ReviewState { get; set; } = EvidenceReviewState.Draft;
    public Guid? ReviewerUserId { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
    public string ReviewNote { get; set; } = "";

    public DateOnly? EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public string ExpiresHintText { get; set; } = "";

    public Guid? SupersededById { get; set; }
    public string SupersededReason { get; set; } = "";

    /// <summary>Redaction gate: patient-identifying content is not accepted as reviewable evidence
    /// (QSPP deletes unredacted evidence unread — Rule 31 / PPI page).</summary>
    public bool ContainsPatientData { get; set; }
    public string PatientDataAttestation { get; set; } = "";
}

public class EvidenceLink
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EvidenceItemId { get; set; }
    public Guid RequirementId { get; set; }
    public string Scope { get; set; } = "";
    public DateOnly? PeriodFrom { get; set; }
    public DateOnly? PeriodTo { get; set; }
    public string Rationale { get; set; } = "";
    public LinkState State { get; set; } = LinkState.Proposed;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public Guid CreatedByUserId { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
    public Guid? ReviewedByUserId { get; set; }
}

public class TaskItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "";
    public string Details { get; set; } = "";
    public Guid? RequirementId { get; set; }
    public Guid? EvidenceItemId { get; set; }
    public Guid AssignedToUserId { get; set; }
    public TaskPriority Priority { get; set; } = TaskPriority.Normal;
    public DateOnly? DueDate { get; set; }
    public TaskState State { get; set; } = TaskState.Open;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public Guid CreatedByUserId { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public Guid? CompletedByUserId { get; set; }
    public string CompletionNote { get; set; } = "";

    /// <summary>M3 recurring-task machinery: the definition that spawned this occurrence and
    /// the date it is FOR (occurrence). Together they make generation idempotent.</summary>
    public Guid? RecurrenceDefId { get; set; }
    public DateOnly? OccurrenceDate { get; set; }
}

public class AppUser
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string UserName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public UserRole Role { get; set; }
    /// <summary>PBKDF2-SHA256; salt and verifier hex. Stored here is only the verifier.</summary>
    public string PassphraseSalt { get; set; } = "";
    public string PassphraseHash { get; set; } = "";
    public int PassphraseIterations { get; set; } = 120_000;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class Pharmacy
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Jurisdiction { get; set; } = "VIC";
    public string TimeZoneId { get; set; } = "Australia/Melbourne";
    public long ServiceScopeFlags { get; set; }
    public DateOnly? CycleStartDate { get; set; }
    public DateOnly? AccreditationExpiryDate { get; set; }
    public DateOnly? OnsiteAssessmentDate { get; set; }
    public bool IsDemo { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class SourceDocument
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string DisplayName { get; set; } = "";
    public string FileName { get; set; } = "";
    public string Role { get; set; } = "";
    public string SourceVersion { get; set; } = "";
    public string OriginPath { get; set; } = "";
    public string StoredRelativePath { get; set; } = "";
    public string Sha256Hex { get; set; } = "";
    public long FileBytes { get; set; }
    public string ProvenanceNote { get; set; } = "";
    public string RetrievedDate { get; set; } = "";
    public string PackVersion { get; set; } = "";
    public DateTime ImportedAtUtc { get; set; } = DateTime.UtcNow;
}

public class InterpretationIssue
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Key { get; set; } = "";
    public string Title { get; set; } = "";
    public string ConservativePlanning { get; set; } = "";
    public string Status { get; set; } = "unresolved";   // unresolved | acknowledged | resolved
    /// <summary>Each competing source: {ref, text verbatim, file, approxLines}</summary>
    public string SourcesJson { get; set; } = "[]";
    public string RetrievedDate { get; set; } = "";
    public string PackVersion { get; set; } = "";
    public DateTime? LastReviewedAtUtc { get; set; }
    public Guid? ReviewedByUserId { get; set; }
    public string ResolutionNote { get; set; } = "";
}

public class AuditEvent
{
    public long Id { get; set; }                    // append-only, hash-chained (tamper-EVIDENCE, not tamper-proof)
    public string ActorName { get; set; } = "";
    public Guid? ActorUserId { get; set; }
    public DateTime AtUtc { get; set; } = DateTime.UtcNow;
    public string Action { get; set; } = "";
    public string EntityType { get; set; } = "";
    public string EntityId { get; set; } = "";
    public string Details { get; set; } = "";       // never contains document content or secrets
    public string PreviousDigest { get; set; } = "";
    public string Digest { get; set; } = "";
}

public class PrintJob
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string DocumentKind { get; set; } = "";
    public string Title { get; set; } = "";
    public Guid? SubjectId { get; set; }
    public string PdfRelativePath { get; set; } = "";
    public string PrinterName { get; set; } = "";
    public int Copies { get; set; } = 1;
    public bool Duplex { get; set; }
    public PrintJobState State { get; set; } = PrintJobState.Prepared;
    public string ResultDetail { get; set; } = "";
    public Guid ActorUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public string SpoolJobId { get; set; } = "";
}

public class PrintPreset
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string Name { get; set; } = "";
    public string PrinterName { get; set; } = "";
    public int Copies { get; set; } = 1;
    public bool Duplex { get; set; }
    public bool Landscape { get; set; }
    public bool IsDefault { get; set; }
}

// ---------------- M3: daily operations ----------------

public enum RecurrenceCadence { Weekly = 0, Fortnightly = 1, Monthly = 2, Quarterly = 3, PerAnnum = 4, CustomDays = 5 }

/// <summary>A durable recurring-task definition (equipment testing, maintenance, counts).
/// Occurrences materialise as ordinary TaskItems; generation is idempotent per (def, date).</summary>
public class RecurringTaskDef
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "";
    public string Details { get; set; } = "";
    public Guid? RequirementId { get; set; }
    public Guid? EquipmentId { get; set; }
    public Guid AssignedToUserId { get; set; }
    public TaskPriority Priority { get; set; } = TaskPriority.Normal;
    public RecurrenceCadence Cadence { get; set; } = RecurrenceCadence.Monthly;
    /// <summary>Only for CustomDays.</summary>
    public int CustomDays { get; set; } = 0;
    /// <summary>First occurrence date; month-end anchors clamp into shorter months.</summary>
    public DateOnly AnchorDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public bool Active { get; set; } = true;
    public Guid CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>Equipment register (AS85000 2.1.1.4b-c recurring maintenance/testing of safety systems).</summary>
public class Equipment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Model { get; set; } = "";
    public string Serial { get; set; } = "";
    public string Location { get; set; } = "";
    public string State { get; set; } = "InService";  // InService | Maintenance | Retired
    public DateOnly? CalibrationDue { get; set; }
    public Guid? CalibrationTaskDefId { get; set; }
    public string Notes { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>Incident register (AS85000 1.1.2.4 reporting to a person responsible).
/// Severity is free text so the register never invents a taxonomy the program doesn't define.</summary>
public class Incident
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime Date { get; set; } = DateTime.UtcNow;
    public string Severity { get; set; } = "";
    public string Description { get; set; } = "";
    public string ImmediateAction { get; set; } = "";
    public string CorrectiveAction { get; set; } = "";
    public string ReportedBy { get; set; } = "";
    public string State { get; set; } = "Open";      // Open | Actioned | Closed
    public Guid? RequirementId { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>Staff are not necessarily app users — the matrix tracks pharmacy staff.</summary>
public class StaffMember
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Role { get; set; } = "";
    /// <summary>S2/S3 training must be completed within 6 months of first supply.</summary>
    public DateOnly? S2S3FirstSupplyDate { get; set; }
    public DateOnly? S2S3InitialCompleted { get; set; }
    /// <summary>Refresher is anniversary-based (approved list, 6 Oct 2026); due each year after initial.</summary>
    public DateOnly? S2S3RefresherCompleted { get; set; }
    public bool Active { get; set; } = true;
    public string Notes { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>Per-policy acknowledgement with the version acknowledged — QSPP asks for
/// policy-version-tied acknowledgement, not a blanket "yes I've read things".</summary>
public class PolicyAcknowledgement
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid StaffMemberId { get; set; }
    public string PolicyName { get; set; } = "";
    public string PolicyVersion { get; set; } = "";
    public Guid? EvidenceItemId { get; set; }
    public DateOnly AcknowledgedOn { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public Guid RecordedByUserId { get; set; }
}