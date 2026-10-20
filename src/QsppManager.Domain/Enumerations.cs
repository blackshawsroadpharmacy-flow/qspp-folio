namespace QsppManager.Domain;

/// <summary>Identifier namespaces for requirements. Same numbers in different documents
/// are NEVER the same requirement (owner brief §4).</summary>
public enum RefKind
{
    Std,          // AS85000:2024 clause / subclause
    Uag,          // UAG section (e.g. UAG 2.4)
    UagKeyTask,   // UAG lettered key task (e.g. UAG 2.4 c)
    Rule,         // QSPP Program Rule
    Tc,           // QSPP Terms & Conditions clause
    Policy,       // QSPP policy provision
    Obligation,   // derived operational obligation (attestation, notifications, fees)
    Hitlist,      // imported planning item from readiness-hitlist.md
    Surveillance, // QCPP remote-assessment portal evidence request (e.g. REM-01)
}

public enum DomainPart
{
    Program = 0,
    Domain1 = 1,
    Domain2 = 2,
    Domain3 = 3,
    Domain4 = 4,
}

public enum ApplicabilityHint
{
    Always,
    IfCompounding,
    IfSterile,
    IfPrescribing,
    IfIndirect,
    Unknown,
}

public enum ServiceScopeFlag
{
    None = 0,
    SimpleCompounding = 1 << 0,
    ComplexCompounding = 1 << 1,
    SterileCompounding = 1 << 2,
    Prescribing = 1 << 3,
    IndirectSupply = 1 << 4,
    Vaccination = 1 << 5,
    FullScopePractice = 1 << 6,
}

public enum EvidenceKind
{
    PolicyOrSop,
    Certificate,
    Record,
    PhotoOrScan,
    Register,
    Spreadsheet,
    TrainingRecord,
    Other,
}

/// <summary>Evidence review state machine (separate from requirement readiness).</summary>
public enum EvidenceReviewState
{
    Draft = 0,
    ReadyForReview = 1,
    NeedsRework = 2,
    ManagerVerified = 3,
    Retired = 4,
}

public enum LinkState
{
    Proposed = 0,
    Active = 1,
    Suspended = 2,
    Superseded = 3,
}

public enum RequirementReadiness
{
    NotStarted = 0,
    InProgress = 1,
    ReadyForReview = 2,
    ManagerVerified = 3,
    AssessorAccepted = 4,   // only when an external assessor outcome is recorded
}

public enum Applicability
{
    Applicable = 0,
    NotApplicable = 1,
    Unknown = 2,
}

public enum TaskState { Open = 0, Done = 1, Cancelled = 2 }

public enum TaskPriority { Normal = 0, High = 1, Critical = 2 }

public enum UserRole
{
    OwnerAdmin = 0,
    ManagerReviewer = 1,
    Contributor = 2,
    ReviewerReadOnly = 3,
}

public enum PrintJobState
{
    Prepared = 0,
    SentToPrinter = 1,
    Failed = 2,
    Cancelled = 3,
}