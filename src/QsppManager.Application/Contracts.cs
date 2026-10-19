using QsppManager.Domain;
using QsppManager.Domain.Entities;
using QsppManager.Domain.Services;

namespace QsppManager.Application;

public record ServiceResult
{
    public bool Ok { get; init; }
    public string Error { get; init; } = "";
    public Guid? EntityId { get; init; }

    public static ServiceResult Succeed(Guid? id = null) => new() { Ok = true, EntityId = id };
    public static ServiceResult Fail(string error) => new() { Ok = false, Error = error };
}

/// <summary>Row for the requirement tree/browser.</summary>
public record RequirementRow(
    Guid Id,
    RefKind RefKind,
    string Ref,
    string Title,
    DomainPart Domain,
    Applicability Applicability,
    RequirementReadiness Readiness,
    string OwnerName,
    bool OwnerUnassigned,
    string NextAction,
    int EvidenceCount,
    int OpenTaskCount);

public record RequirementDetail(
    Requirement Requirement,
    RequirementState State,
    string OwnerName,
    SourceDocument? SourceDocument,
    List<EvidenceLinkRow> Links,
    List<TaskItem> Tasks,
    List<DeadlineInfo> Deadlines,
    List<InterpretationIssue> RelatedIssues);

public record EvidenceLinkRow(
    Guid LinkId,
    Guid EvidenceItemId,
    string EvidenceTitle,
    EvidenceKind Kind,
    EvidenceReviewState ReviewState,
    LinkState State,
    string FileName,
    string Sha256,
    long FileBytes,
    DateOnly? EffectiveFrom,
    DateOnly? EffectiveTo,
    string Rationale,
    string Scope,
    bool ContainsPatientData);

public record TodayBriefing(
    string PharmacyName,
    DateOnly Today,
    List<TaskItem> Overdue,
    List<TaskItem> DueThisWeek,
    List<EvidenceLinkRow> EvidenceAwaitingReview,
    List<(EvidenceItem item, DateOnly? effectiveTo)> ExpiringEvidence,
    List<DeadlineInfo> UpcomingDeadlines,
    int UnresolvedIssues,
    int RequirementsVerified,
    int RequirementsInProgress,
    int RequirementsNotStarted,
    List<AuditEvent> RecentWork);

public record SearchResultRow(string Kind, string Title, string Excerpt, Guid Id, string Meta);

/// <summary>Real counts powering the guided journey — never percentages presented as progress.</summary>
public record JourneySnapshot(
    int RequirementsTotal,
    int ApplicabilityDecided,
    int OwnedCount,
    int RequirementsWithEvidence,
    int ManagerVerified,
    int InProgress,
    int NotStarted,
    int EvidenceAwaitingReview,
    int OverdueTasks,
    int UnresolvedIssues,
    int AcknowledgedIssues,
    int TotalIssues,
    DateOnly? OnsiteAssessmentDate,
    DateOnly? AccreditationExpiryDate);

/// <summary>One retired evidence version with its replacement and outstanding re-link work.</summary>
public record SupersessionRow(
    Guid OldId,
    string OldTitle,
    string Reason,
    string ReplacementTitle,
    int AffectedRequirementLinks,
    int OpenTaskCount);

/// <summary>Abstraction so the workspace can keep the search index fresh without a
/// dependency cycle — CorpusSearch (Infrastructure) implements it.</summary>
public interface ICorpusIndexer
{
    void EnsureIndex();
    int RebuildIndex();
    void IndexEvidence(Guid evidenceId);
    void RetireIndexEntry(Guid targetId);

    /// <summary>Indexes (or re-indexes) a source document row whose body includes extracted
    /// full text — after this, palette search hits inside the actual PDFs.</summary>
    void IndexDocumentText(Guid documentId, string displayName, string text);
}