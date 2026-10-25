using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using QsppManager.Application;
using QsppManager.Domain;
using QsppManager.Domain.Entities;
using QsppManager.Domain.Services;
using QsppManager.Infrastructure.Persistence;
using QsppManager.Infrastructure.Seed;
using QsppManager.Infrastructure.Storage;

namespace QsppManager.Infrastructure.Services;

/// <summary>Single workspace service for requirements, evidence, tasks, search and
/// deadlines. Authorisation and state machines are enforced HERE, not in the UI.</summary>
public class QsppWorkspace(QsppDbContext db, AttachmentStore store, AuditService audit, ICorpusIndexer? indexer = null)
{
    // ---------- baseline ----------
    public Pharmacy EnsurePharmacy(bool demo = false)
    {
        var ph = db.Pharmacies.FirstOrDefault(p => p.IsDemo == demo);
        if (ph != null) return ph;
        var ph2 = new Pharmacy { Name = demo ? "DEMO ONLY — Example Lane Pharmacy" : "Your pharmacy", Jurisdiction = "VIC", TimeZoneId = "Australia/Melbourne", IsDemo = demo };
        db.Pharmacies.Add(ph2);
        db.SaveChanges();
        return ph2;
    }

    // ---------- requirement read/write ----------
    public List<RequirementRow> RequirementRows(UserSession? session)
    {
        var ph = EnsurePharmacy();
        var q = from r in db.Requirements.AsNoTracking()
                join s in db.RequirementStates.AsNoTracking().Where(x => x.PharmacyId == ph.Id)
                    on r.Id equals s.RequirementId into st from s in st.DefaultIfEmpty()
                select new
                {
                    r,
                    state = s,
                    EvidenceCount = db.EvidenceLinks.Count(l => l.RequirementId == r.Id && l.State != LinkState.Superseded),
                    OpenTasks = (from t in db.Tasks where t.RequirementId == r.Id && t.State == TaskState.Open select t).Count(),
                };
        var users = db.Users.AsNoTracking().ToDictionary(u => u.Id, u => u.DisplayName);
        var titles = DisplayTitles().ToDictionary(t => t.Id, t => t);
        var rows = new List<RequirementRow>();
        foreach (var x in q)
        {
            var t = titles[x.r.Id.ToString()];
            if (x.state == null) rows.Add(new RequirementRow(x.r.Id, x.r.RefKind, x.r.Ref, x.r.Title, x.r.Domain, Applicability.Unknown, RequirementReadiness.NotStarted, "", true, "", x.EvidenceCount, x.OpenTasks, t.Title, t.Area, t.KindLabel, t.DomainLabel, t.AreaHueKey));
            else rows.Add(new RequirementRow(x.r.Id, x.r.RefKind, x.r.Ref, x.r.Title, x.r.Domain,
                x.state.Applicability, x.state.Readiness,
                x.state.OwnerUnassigned ? "" : users.GetValueOrDefault(x.state.OwnerUserId, "(unknown)"),
                x.state.OwnerUnassigned, x.state.NextAction, x.EvidenceCount, x.OpenTasks, t.Title, t.Area, t.KindLabel, t.DomainLabel, t.AreaHueKey));
        }
        _ = session; // reviewer read-only still sees the tree; restricted content filters arrive in M4
        return rows;
    }

    public RequirementDetail? RequirementDetail(Guid requirementId, UserSession? session)
    {
        var ph = EnsurePharmacy();
        var r = db.Requirements.AsNoTracking().Include(x => x.EvidenceExamples).FirstOrDefault(x => x.Id == requirementId);
        if (r == null) return null;
        var state = db.RequirementStates.AsNoTracking().FirstOrDefault(s => s.RequirementId == requirementId && s.PharmacyId == ph.Id)
                    ?? new RequirementState { RequirementId = requirementId, PharmacyId = ph.Id };
        var owner = state.OwnerUnassigned ? "" : db.Users.AsNoTracking().FirstOrDefault(u => u.Id == state.OwnerUserId)?.DisplayName ?? "";
        var src = db.SourceDocuments.AsNoTracking().FirstOrDefault(d => d.FileName == r.SourceDocFile)
                  ?? db.SourceDocuments.AsNoTracking().AsEnumerable()
                      .FirstOrDefault(d => d.OriginPath != null && d.OriginPath.EndsWith(r.SourceDocFile, StringComparison.OrdinalIgnoreCase));
        var links = (from l in db.EvidenceLinks.AsNoTracking().Where(l => l.RequirementId == requirementId)
                     join e in db.EvidenceItems.AsNoTracking() on l.EvidenceItemId equals e.Id
                     select new EvidenceLinkRow(l.Id, e.Id, e.Title, e.Kind, e.ReviewState, l.State, e.FileName, e.Sha256, e.FileBytes, e.EffectiveFrom, e.EffectiveTo, l.Rationale, l.Scope, e.ContainsPatientData))
                    .ToList();
        var tasks = db.Tasks.AsNoTracking().Where(t => t.RequirementId == requirementId).OrderBy(t => t.DueDate).ToList();
        var issues = UnresolvedIssuesFor(r);
        _ = session;
        return new RequirementDetail(r, state, owner, src, links, tasks, DeadlinesFor(r, state), issues,
            DisplayTitles().First(t => t.Id == r.Id.ToString()));
    }

    private List<DisplayTitle>? _displayTitlesCache;

    /// <summary>Display titles for the whole corpus, memoized per workspace. The full set is
    /// required every time: heading-derived titles are unique only within their whole kind,
    /// so a single-requirement resolve would silently show an ambiguous shared heading.</summary>
    public List<DisplayTitle> DisplayTitles()
    {
        if (_displayTitlesCache == null)
            _displayTitlesCache = Seed.DisplayTitleSources.ResolveAll(db.Requirements.IgnoreQueryFilters().AsNoTracking().ToList());
        return _displayTitlesCache;
    }

    private List<InterpretationIssue> UnresolvedIssuesFor(Requirement r)
    {
        var keys = JsonSerializer.Deserialize<List<string>>(string.IsNullOrWhiteSpace(r.InterpretationFlagsJson) ? "[]" : r.InterpretationFlagsJson) ?? new();
        if (r.RefKind == RefKind.Rule && r.Ref.Contains("10", StringComparison.Ordinal) && r.Ref.Contains("Rule 10", StringComparison.OrdinalIgnoreCase)) keys.Add("ISSUE-CANCELWINDOW");
        if (r.RefKind == RefKind.Rule && r.Ref.Contains("Rule 8", StringComparison.OrdinalIgnoreCase)) keys.Add("ISSUE-MIDCYCLE");
        if (r.RefKind == RefKind.UagKeyTask && r.InterpretationFlagsJson.Contains("POTENCY", StringComparison.OrdinalIgnoreCase)) keys.Add("ISSUE-POTENCY");
        if (r.PlainSummary.Contains("potency", StringComparison.OrdinalIgnoreCase)) keys.Add("ISSUE-POTENCY");
        keys = keys.Distinct().ToList();
        return db.InterpretationIssues.AsNoTracking().Where(i => keys.Contains(i.Key)).ToList();
    }

    public ServiceResult SetApplicability(Guid requirementId, Applicability decision, string reason, UserSession session)
    {
        if (session.ReadOnly) return ServiceResult.Fail("Read-only reviewer accounts cannot change applicability.");
        if (decision != Applicability.Unknown && string.IsNullOrWhiteSpace(reason))
            return ServiceResult.Fail("Setting applicability requires a recorded reason — assessors and future staff will need it.");
        var ph = EnsurePharmacy();
        var st = GetState(requirementId, ph.Id);
        st.Applicability = decision;
        st.ApplicabilityReason = reason;
        st.ApplicabilityReviewerUserId = session.UserId;
        st.ApplicabilityDecidedAt = DateTime.UtcNow;
        st.UpdatedAtUtc = DateTime.UtcNow;
        db.SaveChanges();
        audit.Append(session.DisplayName, session.UserId, "requirement.applicability", "requirement", requirementId.ToString(), $"decision={decision}; reason={reason}");
        return ServiceResult.Succeed();
    }

    public ServiceResult SetReadiness(Guid requirementId, RequirementReadiness target, UserSession session, string externalOutcomeRef = "")
    {
        if (session.ReadOnly) return ServiceResult.Fail("Read-only reviewer accounts cannot change readiness.");
        var ph = EnsurePharmacy();
        var st = GetState(requirementId, ph.Id);
        if (!StateMachines.CanTransitionReadiness(st.Readiness, target))
            return ServiceResult.Fail($"Requirement cannot move from '{st.Readiness}' to '{target}'.");
        if (!StateMachines.MayApplyReadiness(target, session.Role))
            return ServiceResult.Fail($"Only Owner/Admin or Manager/Reviewer accounts can set readiness to '{target}'.");
        if (target == RequirementReadiness.AssessorAccepted)
        {
            if (!StateMachines.MayConfirmAssessorAcceptance(session.Role))
                return ServiceResult.Fail("Only Owner/Admin or Manager/Reviewer accounts can record an assessor outcome.");
            if (string.IsNullOrWhiteSpace(externalOutcomeRef))
                return ServiceResult.Fail("Recording an assessor outcome requires the external reference (report or letter reference). This is only recorded when the outcome was actually received.");
            st.ExternalOutcomeRef = externalOutcomeRef;
            st.ExternalOutcomeAt = DateTime.UtcNow;
        }
        st.Readiness = target;
        st.UpdatedAtUtc = DateTime.UtcNow;
        db.SaveChanges();
        audit.Append(session.DisplayName, session.UserId, "requirement.readiness", "requirement", requirementId.ToString(),
            $"to={target}" + (target == RequirementReadiness.AssessorAccepted ? $"; externalRef={externalOutcomeRef}" : ""));
        return ServiceResult.Succeed();
    }

    public ServiceResult AssignOwner(Guid requirementId, Guid? userId, UserSession session)
    {
        if (session.ReadOnly) return ServiceResult.Fail("Read-only reviewer accounts cannot assign owners.");
        var ph = EnsurePharmacy();
        if (userId.HasValue && !db.Users.Any(u => u.Id == userId))
            return ServiceResult.Fail("The selected user does not exist.");
        var st = GetState(requirementId, ph.Id);
        st.OwnerUserId = userId ?? Guid.Empty;
        st.OwnerUnassigned = userId is null;
        st.UpdatedAtUtc = DateTime.UtcNow;
        db.SaveChanges();
        audit.Append(session.DisplayName, session.UserId, "requirement.owner", "requirement", requirementId.ToString(), userId is null ? "cleared owner" : $"owner={userId}");
        return ServiceResult.Succeed();
    }

    /// <summary>One-click "claim the backlog": every still-unassigned requirement of this
    /// pharmacy is assigned to one user with a single audit entry, so the audit trail
    /// stays readable and the Requirements list loses its wall of "owner not assigned".</summary>
    public int AssignAllOwnersToUser(Guid userId, UserSession session)
    {
        if (session.ReadOnly) return 0;
        if (userId == Guid.Empty || !db.Users.Any(u => u.Id == userId)) return 0;
        var ph = EnsurePharmacy();
        // Get-or-create per requirement: state rows are created lazily on first detail
        // view, so a fresh import has none — a bulk claim must touch every requirement.
        var reqIds = db.Requirements.AsNoTracking().Select(r => r.Id).ToList();
        var existing = db.RequirementStates.Where(s => s.PharmacyId == ph.Id).ToDictionary(s => s.RequirementId);
        var states = new List<RequirementState>();
        foreach (var reqId in reqIds)
        {
            if (existing.TryGetValue(reqId, out var st))
            {
                if (!st.OwnerUnassigned) continue;
                st.OwnerUserId = userId;
                st.OwnerUnassigned = false;
                st.UpdatedAtUtc = DateTime.UtcNow;
            }
            else
            {
                st = new RequirementState { RequirementId = reqId, PharmacyId = ph.Id, OwnerUserId = userId, OwnerUnassigned = false };
                db.RequirementStates.Add(st);
            }
            states.Add(st);
        }
        if (states.Count > 0)
        {
            db.SaveChanges();
            audit.Append(session.DisplayName, session.UserId, "requirement.owner.bulk", "pharmacy", ph.Id.ToString(),
                $"assigned={states.Count}; to={session.UserName}");
        }
        return states.Count;
    }

    public ServiceResult SetNextAction(Guid requirementId, string nextAction, UserSession session)
    {
        if (session.ReadOnly) return ServiceResult.Fail("Read-only reviewer accounts cannot edit next actions.");
        var ph = EnsurePharmacy();
        var st = GetState(requirementId, ph.Id);
        st.NextAction = nextAction[..Math.Min(nextAction.Length, 400)];
        st.UpdatedAtUtc = DateTime.UtcNow;
        db.SaveChanges();
        audit.Append(session.DisplayName, session.UserId, "requirement.nextaction", "requirement", requirementId.ToString(), nextAction);
        return ServiceResult.Succeed();
    }

    private RequirementState GetState(Guid requirementId, Guid pharmacyId)
    {
        var st = db.RequirementStates.FirstOrDefault(s => s.RequirementId == requirementId && s.PharmacyId == pharmacyId);
        if (st == null)
        {
            st = new RequirementState { RequirementId = requirementId, PharmacyId = pharmacyId };
            db.RequirementStates.Add(st);
            db.SaveChanges();
        }
        return st;
    }

    // ---------- evidence ----------
    public (ServiceResult result, EvidenceItem? item) ImportEvidence(string sourcePath, string title, EvidenceKind kind,
        DateOnly? effectiveFrom, DateOnly? effectiveTo, bool containsPatientData, string patientDataAttestation, UserSession session)
    {
        if (session == null || session.ReadOnly) return (ServiceResult.Fail("Sign in as Contributor or above to add evidence."), null);
        if (!File.Exists(sourcePath)) return (ServiceResult.Fail($"File not found: {sourcePath}"), null);
        var head = new byte[10240];
        using (var fs = File.OpenRead(sourcePath)) _ = fs.Read(head, 0, head.Length);
        var (ok, sigErr, mime) = AttachmentStore.ValidateSignature(Path.GetFileName(sourcePath), head);
        if (!ok) return (ServiceResult.Fail(sigErr + " Changes before this import have NOT been lost."), null);
        if (containsPatientData && string.IsNullOrWhiteSpace(patientDataAttestation))
            return (ServiceResult.Fail("Evidence flagged as containing patient data needs an explicit redaction attestation (what was redacted, and who reviewed it)."), null);
        if (string.IsNullOrWhiteSpace(title)) return (ServiceResult.Fail("Give the evidence a title."), null);

        var (rel, sha, size) = store.StoreAsync(sourcePath).GetAwaiter().GetResult();
        var item = new EvidenceItem
        {
            Title = title.Trim(),
            Kind = kind,
            FileName = Path.GetFileName(sourcePath),
            MimeType = mime,
            StoredRelativePath = rel,
            Sha256 = sha,
            FileBytes = size,
            CreatedByUserId = session.UserId,
            ContainsPatientData = containsPatientData,
            PatientDataAttestation = containsPatientData ? patientDataAttestation : "",
            EffectiveFrom = effectiveFrom,
            EffectiveTo = effectiveTo,
        };
        db.EvidenceItems.Add(item);
        db.SaveChanges();
        audit.Append(session.DisplayName, session.UserId, "evidence.imported", "evidence", item.Id.ToString(), $"{title}; sha256 {sha[..12]}…; patientData={containsPatientData}");
        indexer?.IndexEvidence(item.Id);
        return (ServiceResult.Succeed(item.Id), item);
    }

    public ServiceResult LinkEvidence(Guid evidenceId, Guid requirementId, string scope, DateOnly? from, DateOnly? to, string rationale, UserSession session)
    {
        if (session == null || session.ReadOnly) return ServiceResult.Fail("Sign in as Contributor or above to link evidence.");
        var item = db.EvidenceItems.FirstOrDefault(e => e.Id == evidenceId);
        if (item == null) return ServiceResult.Fail("Evidence not found.");
        if (!db.Requirements.Any(r => r.Id == requirementId)) return ServiceResult.Fail("Requirement not found.");
        if (item.ReviewState is EvidenceReviewState.Retired) return ServiceResult.Fail("Retired (superseded) evidence cannot be linked. Link the current version instead.");
        if (string.IsNullOrWhiteSpace(rationale) && item.ReviewState != EvidenceReviewState.ManagerVerified)
            return ServiceResult.Fail("Add a short rationale for this link (why this document supports this requirement).");
        var state = item.ReviewState == EvidenceReviewState.ManagerVerified ? LinkState.Active : LinkState.Proposed;
        if (db.EvidenceLinks.Any(l => l.EvidenceItemId == evidenceId && l.RequirementId == requirementId))
            return ServiceResult.Fail("This evidence is already linked to that requirement.");
        db.EvidenceLinks.Add(new EvidenceLink { EvidenceItemId = evidenceId, RequirementId = requirementId, Scope = scope, PeriodFrom = from, PeriodTo = to, Rationale = rationale, State = state, CreatedByUserId = session.UserId });
        db.SaveChanges();
        audit.Append(session.DisplayName, session.UserId, "evidence.linked", "evidence-link", $"{evidenceId}:{requirementId}", $"state={state}; rationale={rationale}");
        return ServiceResult.Succeed();
    }

    public ServiceResult SetEvidenceReview(Guid evidenceId, EvidenceReviewState target, UserSession session, string note)
    {
        if (session == null || session.ReadOnly) return ServiceResult.Fail("Read-only reviewer accounts cannot change evidence review state.");
        var item = db.EvidenceItems.FirstOrDefault(e => e.Id == evidenceId);
        if (item == null) return ServiceResult.Fail("Evidence not found.");
        if (!StateMachines.CanTransitionEvidence(item.ReviewState, target))
            return ServiceResult.Fail($"Evidence cannot move from '{item.ReviewState}' to '{target}'.");
        if (!StateMachines.MayVerifyEvidence(target, session.Role))
            return ServiceResult.Fail($"Only Owner/Admin or Manager/Reviewer accounts can mark evidence '{target}'.");
        if (target == EvidenceReviewState.ReadyForReview && item.ContainsPatientData && string.IsNullOrWhiteSpace(item.PatientDataAttestation))
            return ServiceResult.Fail("Privacy gate: evidence contains patient data without a redaction attestation. Redact the underlying content (not a black box), replace the file, then retry.");
        item.ReviewState = target;
        item.ReviewerUserId = target is EvidenceReviewState.ManagerVerified ? session.UserId : item.ReviewerUserId;
        item.ReviewedAtUtc = target is EvidenceReviewState.ManagerVerified or EvidenceReviewState.NeedsRework ? DateTime.UtcNow : item.ReviewedAtUtc;
        item.ReviewNote = note ?? item.ReviewNote;
        if (target == EvidenceReviewState.ManagerVerified)
        {
            // Manager verification activates the evidence's Proposed links — the link review
            // rides on the evidence review, which is what the brief intends.
            foreach (var l in db.EvidenceLinks.Where(l => l.EvidenceItemId == evidenceId && l.State == LinkState.Proposed))
                l.State = LinkState.Active;
        }
        db.SaveChanges();
        audit.Append(session.DisplayName, session.UserId, "evidence.review", "evidence", evidenceId.ToString(), $"to={target}; note={note}");
        indexer?.IndexEvidence(evidenceId);
        return ServiceResult.Succeed();
    }

    /// <summary>Supersedes an approved evidence version with a new one: the old item is
    /// retired, affected links are marked Superseded, and review tasks are created.
    /// History is preserved.</summary>
    public ServiceResult SupersedeEvidence(Guid oldId, Guid newId, string reason, UserSession session)
    {
        if (session == null || !session.CanVerify) return ServiceResult.Fail("Only Owner/Admin or Manager/Reviewer accounts can supersede evidence.");
        var old = db.EvidenceItems.FirstOrDefault(e => e.Id == oldId);
        var nu = db.EvidenceItems.FirstOrDefault(e => e.Id == newId);
        if (old == null || nu == null) return ServiceResult.Fail("Evidence item not found.");
        if (newId == oldId) return ServiceResult.Fail("Choose a different, current version to supersede with.");
        if (old.ReviewState != EvidenceReviewState.ManagerVerified)
            return ServiceResult.Fail("Only ManagerVerified evidence gets the formal supersession flow; retire drafts instead.");
        old.ReviewState = EvidenceReviewState.Retired;
        old.SupersededById = newId;
        old.SupersededReason = reason;
        indexer?.IndexEvidence(old.Id);
        indexer?.IndexEvidence(nu.Id);
        var affected = db.EvidenceLinks.Where(l => l.EvidenceItemId == oldId && l.State != LinkState.Superseded).ToList();
        foreach (var l in affected)
        {
            l.State = LinkState.Superseded;
            var req = db.Requirements.FirstOrDefault(r => r.Id == l.RequirementId);
            var st = db.RequirementStates.FirstOrDefault(s => s.RequirementId == l.RequirementId);
            var task = new TaskItem
            {
                Title = $"Evidence superseded — re-link current version for {req?.Ref ?? "requirement"}",
                Details = $"Superseded evidence '{old.Title}' was replaced ({reason}). Choose the current version, link it with a rationale, and mark the link reviewed so readiness reflects reality.",
                RequirementId = l.RequirementId,
                EvidenceItemId = oldId,
                AssignedToUserId = st?.OwnerUnassigned == false ? st.OwnerUserId : EnsurePharmacyOwner(),
                Priority = TaskPriority.High,
                CreatedByUserId = session.UserId,
            };
            db.Tasks.Add(task);
        }
        db.SaveChanges();
        audit.Append(session.DisplayName, session.UserId, "evidence.superseded", "evidence", oldId.ToString(), $"old={oldId} new={newId} reason={reason} affectedLinks={affected.Count}");
        return ServiceResult.Succeed();
    }

    private Guid EnsurePharmacyOwner()
    {
        var ph = EnsurePharmacy();
        var owner = db.Users.FirstOrDefault(u => u.Role == UserRole.OwnerAdmin);
        if (owner == null) { var u = new AppUser { UserName = "owner", DisplayName = "Pharmacy Owner", Role = UserRole.OwnerAdmin, PassphraseHash = "-" }; db.Users.Add(u); db.SaveChanges(); return u.Id; }
        _ = ph;
        return owner.Id;
    }

    public List<EvidenceLinkRow> EvidenceAwaitingReview() =>
        (from e in db.EvidenceItems.AsNoTracking().Where(e => e.ReviewState == EvidenceReviewState.ReadyForReview)
         select new EvidenceLinkRow(Guid.Empty, e.Id, e.Title, e.Kind, e.ReviewState, LinkState.Active, e.FileName, e.Sha256, e.FileBytes, e.EffectiveFrom, e.EffectiveTo, "", "", e.ContainsPatientData)).ToList();

    public List<(EvidenceItem item, DateOnly? effectiveTo)> ExpiringEvidence(int withinDays = 60)
    {
        var today = Clock.Today();
        return db.EvidenceItems.AsNoTracking()
            .Where(e => e.ReviewState == EvidenceReviewState.ManagerVerified && e.EffectiveTo != null)
            .ToList()
            .Where(e => e.EffectiveTo!.Value < today.AddDays(withinDays))
            .Select(e => (e, (DateOnly?)e.EffectiveTo!.Value))
            .ToList();
    }

    // ---------- tasks ----------
    public ServiceResult CreateTask(string title, string details, Guid? requirementId, Guid assignedTo, TaskPriority priority, DateOnly? due, UserSession session)
    {
        if (session == null || session.ReadOnly) return ServiceResult.Fail("Sign in as Contributor or above to create tasks.");
        if (string.IsNullOrWhiteSpace(title)) return ServiceResult.Fail("Give the task a title.");
        if (requirementId.HasValue && !db.Requirements.Any(r => r.Id == requirementId)) return ServiceResult.Fail("Linked requirement not found.");
        var task = new TaskItem { Title = title.Trim(), Details = details, RequirementId = requirementId, AssignedToUserId = assignedTo, Priority = priority, DueDate = due, CreatedByUserId = session.UserId };
        db.Tasks.Add(task);
        db.SaveChanges();
        audit.Append(session.DisplayName, session.UserId, "task.created", "task", task.Id.ToString(), title);
        return ServiceResult.Succeed(task.Id);
    }

    public ServiceResult CompleteTask(Guid taskId, string note, UserSession session)
    {
        if (session == null || session.ReadOnly) return ServiceResult.Fail("Sign in as Contributor or above to complete tasks.");
        var t = db.Tasks.FirstOrDefault(x => x.Id == taskId);
        if (t == null) return ServiceResult.Fail("Task not found.");
        if (t.State == TaskState.Done) return ServiceResult.Fail("Task is already complete.");
        if (t.State == TaskState.Cancelled) return ServiceResult.Fail("Cancelled tasks cannot be completed.");
        t.State = TaskState.Done;
        t.CompletedAtUtc = DateTime.UtcNow;
        t.CompletedByUserId = session.UserId;
        t.CompletionNote = note;
        db.SaveChanges();
        audit.Append(session.DisplayName, session.UserId, "task.completed", "task", taskId.ToString(), note);
        return ServiceResult.Succeed();
    }

    public List<TaskItem> OpenTasks(UserSession? session = null)
        => db.Tasks.AsNoTracking().Where(t => t.State == TaskState.Open).OrderBy(t => t.DueDate ?? DateOnly.MaxValue).ToList();

    public List<TaskItem> RecentTasks(int count) => db.Tasks.AsNoTracking().OrderByDescending(t => t.CompletedAtUtc ?? t.CreatedAtUtc).Take(count).ToList();

    // ---------- interpretation issues ----------
    public List<InterpretationIssue> Issues() => db.InterpretationIssues.AsNoTracking().OrderBy(i => i.Key).ToList();

    public ServiceResult AcknowledgeIssue(Guid issueId, UserSession session)
    {
        if (session == null || !session.CanVerify) return ServiceResult.Fail("Only Owner/Admin or Manager/Reviewer accounts can record an acknowledgement.");
        var i = db.InterpretationIssues.FirstOrDefault(x => x.Id == issueId);
        if (i == null || i.Status == "resolved") return ServiceResult.Fail("Interpretation issue not found or already resolved.");
        i.Status = "acknowledged";
        i.LastReviewedAtUtc = DateTime.UtcNow;
        i.ReviewedByUserId = session.UserId;
        db.SaveChanges();
        audit.Append(session.DisplayName, session.UserId, "issue.acknowledged", "interpretation-issue", issueId.ToString(), i.Key);
        return ServiceResult.Succeed();
    }

    /// <summary>Resolving an issue REQUIRES a recorded outcome: who settled it and which
    /// source is authoritative. The plan-only flags in deadline views lift only then.</summary>
    public ServiceResult ResolveIssue(Guid issueId, string resolutionNote, UserSession session)
    {
        if (session == null || !session.CanVerify) return ServiceResult.Fail("Only Owner/Admin or Manager/Reviewer accounts can resolve an interpretation issue.");
        if (string.IsNullOrWhiteSpace(resolutionNote))
            return ServiceResult.Fail("Record how the question was settled — who confirmed it, and which source is taken as authoritative.");
        var i = db.InterpretationIssues.FirstOrDefault(x => x.Id == issueId);
        if (i == null) return ServiceResult.Fail("Interpretation issue not found.");
        i.Status = "resolved";
        i.ResolutionNote = resolutionNote[..Math.Min(resolutionNote.Length, 2000)];
        i.LastReviewedAtUtc = DateTime.UtcNow;
        i.ReviewedByUserId = session.UserId;
        db.SaveChanges();
        audit.Append(session.DisplayName, session.UserId, "issue.resolved", "interpretation-issue", issueId.ToString(), $"{i.Key}; {i.ResolutionNote}");
        return ServiceResult.Succeed();
    }

    // ---------- M3: recurring tasks ----------
    /// <summary>Materialises due recurring occurrences as ordinary tasks. Idempotent per
    /// (definition, occurrence date) — safe to call on every boot and screen refresh.
    /// Missed occurrences stay open with past due dates, where Today shows them.</summary>
    public int GenerateRecurringOccurrences(DateOnly? horizonDate = null, UserSession? session = null)
    {
        var horizon = horizonDate ?? Clock.Today().AddDays(14);
        var created = 0;
        foreach (var def in db.RecurringTaskDefs.AsNoTracking().Where(d => d.Active))
        {
            var existing = db.Tasks.Where(t => t.RecurrenceDefId == def.Id)
                .Select(t => t.OccurrenceDate).ToHashSet();
            foreach (var d in RecurrenceCalculator.Occurrences(def, horizon))
            {
                if (existing.Contains(d)) continue;
                db.Tasks.Add(new TaskItem
                {
                    Title = def.Title,
                    Details = def.Details,
                    RequirementId = def.RequirementId,
                    RecurrenceDefId = def.Id,
                    OccurrenceDate = d,
                    DueDate = d,
                    AssignedToUserId = def.AssignedToUserId,
                    Priority = def.Priority,
                    CreatedByUserId = def.CreatedByUserId != Guid.Empty ? def.CreatedByUserId : session?.UserId ?? Guid.Empty,
                });
                created++;
            }
        }
        if (created > 0)
        {
            db.SaveChanges();
            audit.Append(session?.DisplayName ?? "system", session?.UserId, "recurring.generated", "task", "*", $"{created} recurring occurrence(s) generated to {horizon:dd MMM yyyy}");
        }
        return created;
    }

    public ServiceResult CreateRecurringDef(string title, string details, Guid? requirementId, Guid? equipmentId,
        Guid assignedTo, RecurrenceCadence cadence, int customDays, DateOnly anchor, TaskPriority priority, UserSession session)
    {
        if (session == null || session.ReadOnly) return ServiceResult.Fail("Read-only reviewer accounts cannot create schedules.");
        if (string.IsNullOrWhiteSpace(title)) return ServiceResult.Fail("Give the schedule a title.");
        if (cadence == RecurrenceCadence.CustomDays && customDays < 1) return ServiceResult.Fail("Custom cadence needs an interval of at least 1 day.");
        var def = new RecurringTaskDef
        {
            Title = title.Trim(), Details = details ?? "", RequirementId = requirementId, EquipmentId = equipmentId,
            AssignedToUserId = assignedTo, Cadence = cadence, CustomDays = cadence == RecurrenceCadence.CustomDays ? customDays : 0,
            AnchorDate = anchor, Priority = priority, CreatedByUserId = session.UserId,
        };
        db.RecurringTaskDefs.Add(def);
        db.SaveChanges();
        audit.Append(session.DisplayName, session.UserId, "recurring.created", "recurring-def", def.Id.ToString(), $"{cadence} from {anchor:dd MMM yyyy}: {title}");
        return ServiceResult.Succeed(def.Id);
    }

    public ServiceResult SetRecurringDefActive(Guid defId, bool active, UserSession session)
    {
        if (session == null || !session.CanVerify) return ServiceResult.Fail("Only Owner/Admin or Manager/Reviewer accounts can pause or resume schedules.");
        var def = db.RecurringTaskDefs.FirstOrDefault(d => d.Id == defId);
        if (def == null) return ServiceResult.Fail("Schedule not found.");
        if (def.Active == active) return ServiceResult.Fail($"Schedule is already {(active ? "active" : "paused")}.");
        def.Active = active;
        db.SaveChanges();
        audit.Append(session.DisplayName, session.UserId, active ? "recurring.resumed" : "recurring.paused", "recurring-def", defId.ToString(), def.Title);
        return ServiceResult.Succeed();
    }

    public List<RecurringTaskDef> RecurringDefs() => db.RecurringTaskDefs.AsNoTracking().OrderBy(d => d.Active ? 0 : 1).ThenBy(d => d.Title).ToList();

    // ---------- M3: registers ----------
    public List<Equipment> Equipment() => db.Equipment.AsNoTracking().OrderBy(e => e.State).ThenBy(e => e.Name).ToList();

    public ServiceResult AddEquipment(string name, string model, string serial, string location, string notes, UserSession session)
    {
        if (session == null || session.ReadOnly) return ServiceResult.Fail("Read-only reviewer accounts cannot edit registers.");
        if (string.IsNullOrWhiteSpace(name)) return ServiceResult.Fail("Give the equipment a name.");
        var eq = new Equipment { Name = name.Trim(), Model = model ?? "", Serial = serial ?? "", Location = location ?? "", Notes = notes ?? "" };
        db.Equipment.Add(eq);
        db.SaveChanges();
        audit.Append(session.DisplayName, session.UserId, "equipment.added", "equipment", eq.Id.ToString(), $"{name} ({model} {serial}) {location}".Trim());
        return ServiceResult.Succeed(eq.Id);
    }

    public ServiceResult SetEquipmentState(Guid equipmentId, string state, UserSession session)
    {
        if (session == null || !session.CanVerify) return ServiceResult.Fail("Only Owner/Admin or Manager/Reviewer accounts can change equipment state.");
        if (state is not ("InService" or "Maintenance" or "Retired")) return ServiceResult.Fail("Unknown equipment state.");
        var eq = db.Equipment.FirstOrDefault(e => e.Id == equipmentId);
        if (eq == null) return ServiceResult.Fail("Equipment not found.");
        eq.State = state;
        db.SaveChanges();
        audit.Append(session.DisplayName, session.UserId, "equipment.state", "equipment", equipmentId.ToString(), $"{eq.Name} → {state}");
        return ServiceResult.Succeed();
    }

    /// <summary>Sets calibration-due and attaches a monthly calibration-check schedule
    /// linked to the equipment (creating it once if needed).</summary>
    public ServiceResult SetCalibrationDue(Guid equipmentId, DateOnly due, UserSession session)
    {
        if (session == null || !session.CanVerify) return ServiceResult.Fail("Only Owner/Admin or Manager/Reviewer accounts can record calibration dates.");
        var eq = db.Equipment.FirstOrDefault(e => e.Id == equipmentId);
        if (eq == null) return ServiceResult.Fail("Equipment not found.");
        eq.CalibrationDue = due;
        if (eq.CalibrationTaskDefId is null)
        {
            var def = new RecurringTaskDef
            {
                Title = $"Calibration check — {eq.Name}",
                Details = $"Verify {eq.Name} ({eq.Model} {eq.Serial}) calibration is current. Record the result and file the certificate as evidence.",
                EquipmentId = eq.Id, AssignedToUserId = EnsurePharmacyOwner(), Cadence = RecurrenceCadence.Monthly,
                AnchorDate = due, Priority = TaskPriority.Normal, CreatedByUserId = session.UserId,
            };
            db.RecurringTaskDefs.Add(def);
            db.SaveChanges();
            eq.CalibrationTaskDefId = def.Id;
        }
        db.SaveChanges();
        audit.Append(session.DisplayName, session.UserId, "equipment.calibration", "equipment", equipmentId.ToString(), $"{eq.Name} calibration due {due:dd MMM yyyy}; monthly check schedule ensured");
        return ServiceResult.Succeed();
    }

    public List<Incident> Incidents() => db.Incidents.AsNoTracking().OrderByDescending(i => i.Date).ToList();

    public ServiceResult AddIncident(DateOnly date, string severity, string description, string immediateAction, string reportedBy, UserSession session)
    {
        if (session == null || session.ReadOnly) return ServiceResult.Fail("Read-only reviewer accounts cannot record incidents.");
        if (string.IsNullOrWhiteSpace(description)) return ServiceResult.Fail("Describe the incident — the register is the evidence that reporting happened.");
        var inc = new Incident { Date = date.ToDateTime(TimeOnly.MinValue), Severity = severity ?? "", Description = description, ImmediateAction = immediateAction ?? "", ReportedBy = reportedBy ?? session.DisplayName };
        db.Incidents.Add(inc);
        db.SaveChanges();
        var ph = EnsurePharmacy();
        // Link to the incident-reporting requirement (1.1.2.4) so the record is findable from the clause.
        var req = db.Requirements.AsNoTracking().FirstOrDefault(r => r.Ref == "1.1.2.4");
        if (req != null) { inc.RequirementId = req.Id; db.SaveChanges(); }
        _ = ph;
        audit.Append(session.DisplayName, session.UserId, "incident.reported", "incident", inc.Id.ToString(), $"severity={severity}; {description[..Math.Min(120, description.Length)]}");
        return ServiceResult.Succeed(inc.Id);
    }

    public ServiceResult SetIncidentState(Guid incidentId, string state, string correctiveAction, UserSession session)
    {
        if (session == null || !session.CanVerify) return ServiceResult.Fail("Only Owner/Admin or Manager/Reviewer accounts can close incidents.");
        if (state is not ("Open" or "Actioned" or "Closed")) return ServiceResult.Fail("Unknown incident state.");
        var inc = db.Incidents.FirstOrDefault(i => i.Id == incidentId);
        if (inc == null) return ServiceResult.Fail("Incident not found.");
        if (state == "Closed" && string.IsNullOrWhiteSpace(correctiveAction) && string.IsNullOrWhiteSpace(inc.CorrectiveAction))
            return ServiceResult.Fail("An incident cannot close without a recorded corrective action.");
        inc.State = state;
        if (!string.IsNullOrWhiteSpace(correctiveAction)) inc.CorrectiveAction = correctiveAction;
        inc.UpdatedAtUtc = DateTime.UtcNow;
        db.SaveChanges();
        audit.Append(session.DisplayName, session.UserId, "incident.state", "incident", incidentId.ToString(), $"{inc.State}" + (correctiveAction != null ? $"; action={correctiveAction}" : ""));
        return ServiceResult.Succeed();
    }

    // ---------- M3: staff & training ----------
    public List<StaffMember> Staff() => db.StaffMembers.AsNoTracking().Where(s => s.Active).OrderBy(s => s.Name).ToList();

    public ServiceResult AddStaffMember(string name, string role, UserSession session)
    {
        if (session == null || session.ReadOnly) return ServiceResult.Fail("Read-only reviewer accounts cannot manage staff records.");
        if (string.IsNullOrWhiteSpace(name)) return ServiceResult.Fail("Give the staff member a name.");
        var s = new StaffMember { Name = name.Trim(), Role = role ?? "" };
        db.StaffMembers.Add(s);
        db.SaveChanges();
        audit.Append(session.DisplayName, session.UserId, "staff.added", "staff", s.Id.ToString(), $"{name} · {role}");
        return ServiceResult.Succeed(s.Id);
    }

    public ServiceResult SetStaffTraining(Guid staffId, DateOnly? firstSupply, DateOnly? initialCompleted, DateOnly? refresherCompleted, UserSession session)
    {
        if (session == null || session.ReadOnly) return ServiceResult.Fail("Read-only reviewer accounts cannot update training records.");
        var s = db.StaffMembers.FirstOrDefault(x => x.Id == staffId);
        if (s == null) return ServiceResult.Fail("Staff member not found.");
        s.S2S3FirstSupplyDate = firstSupply ?? s.S2S3FirstSupplyDate;
        s.S2S3InitialCompleted = initialCompleted ?? s.S2S3InitialCompleted;
        s.S2S3RefresherCompleted = refresherCompleted ?? s.S2S3RefresherCompleted;
        db.SaveChanges();
        audit.Append(session.DisplayName, session.UserId, "staff.training", "staff", staffId.ToString(),
            $"supply={firstSupply?.ToString("dd MMM yyyy") ?? "-"} initial={initialCompleted?.ToString("dd MMM yyyy") ?? "-"} refresher={refresherCompleted?.ToString("dd MMM yyyy") ?? "-"}");
        return ServiceResult.Succeed();
    }

    /// <summary>Records a version-tied policy acknowledgement; one row per staff+policy+version.</summary>
    public ServiceResult AcknowledgePolicy(Guid staffId, string policyName, string policyVersion, Guid? evidenceItemId, UserSession session)
    {
        if (session == null || session.ReadOnly) return ServiceResult.Fail("Read-only reviewer accounts cannot record acknowledgements.");
        if (string.IsNullOrWhiteSpace(policyName) || string.IsNullOrWhiteSpace(policyVersion))
            return ServiceResult.Fail("Record WHICH policy and WHICH version was acknowledged.");
        if (evidenceItemId.HasValue && !db.EvidenceItems.Any(e => e.Id == evidenceItemId))
            return ServiceResult.Fail("Linked evidence not found.");
        if (db.PolicyAcknowledgements.Any(a => a.StaffMemberId == staffId && a.PolicyName == policyName.Trim() && a.PolicyVersion == policyVersion.Trim()))
            return ServiceResult.Fail("That acknowledgement is already recorded (staff, policy, version).");
        var s = db.StaffMembers.FirstOrDefault(x => x.Id == staffId);
        if (s == null) return ServiceResult.Fail("Staff member not found.");
        var ack = new PolicyAcknowledgement { StaffMemberId = staffId, PolicyName = policyName.Trim(), PolicyVersion = policyVersion.Trim(), EvidenceItemId = evidenceItemId, RecordedByUserId = session.UserId };
        db.PolicyAcknowledgements.Add(ack);
        db.SaveChanges();
        audit.Append(session.DisplayName, session.UserId, "policy.acknowledged", "staff", staffId.ToString(), $"{s.Name} · {policyName} v{policyVersion}");
        return ServiceResult.Succeed(ack.Id);
    }

    public List<PolicyAcknowledgement> Acknowledgements() => db.PolicyAcknowledgements.AsNoTracking().OrderByDescending(a => a.AcknowledgedOn).ToList();

    // ---------- M3: compounding records ----------
    public List<CompoundSupplier> Suppliers(bool activeOnly = true) =>
        db.CompoundSuppliers.AsNoTracking().Where(s => !activeOnly || s.Active).OrderBy(s => s.Name).ToList();

    public ServiceResult AddSupplier(string name, string supplies, string registrationNote, UserSession session)
    {
        if (session == null || session.ReadOnly) return ServiceResult.Fail("Read-only reviewer accounts cannot edit the supplier register.");
        if (string.IsNullOrWhiteSpace(name)) return ServiceResult.Fail("Give the supplier a name.");
        var s = new CompoundSupplier { Name = name.Trim(), Supplies = supplies ?? "", RegistrationNote = registrationNote ?? "" };
        db.CompoundSuppliers.Add(s);
        db.SaveChanges();
        audit.Append(session.DisplayName, session.UserId, "supplier.added", "compound-supplier", s.Id.ToString(), $"{name}; {supplies}");
        return ServiceResult.Succeed(s.Id);
    }

    public ServiceResult SetSupplierReviewed(Guid supplierId, Guid? evidenceItemId, DateOnly lastReviewed, UserSession session)
    {
        if (session == null || session.ReadOnly) return ServiceResult.Fail("Read-only reviewer accounts cannot update supplier reviews.");
        if (evidenceItemId.HasValue && !db.EvidenceItems.Any(e => e.Id == evidenceItemId)) return ServiceResult.Fail("Linked evidence not found.");
        var s = db.CompoundSuppliers.FirstOrDefault(x => x.Id == supplierId);
        if (s == null) return ServiceResult.Fail("Supplier not found.");
        s.LastReviewed = lastReviewed;
        if (evidenceItemId.HasValue) s.EvidenceItemId = evidenceItemId;
        db.SaveChanges();
        audit.Append(session.DisplayName, session.UserId, "supplier.reviewed", "compound-supplier", supplierId.ToString(), $"{s.Name} reviewed {lastReviewed:dd MMM yyyy}");
        return ServiceResult.Succeed();
    }

    public List<ProductRecall> Recalls() => db.ProductRecalls.AsNoTracking().OrderByDescending(r => r.ReceivedDate).ToList();

    public ServiceResult AddRecall(string product, string batch, DateOnly received, string action, UserSession session)
    {
        if (session == null || session.ReadOnly) return ServiceResult.Fail("Read-only reviewer accounts cannot record recalls.");
        if (string.IsNullOrWhiteSpace(product)) return ServiceResult.Fail("Name the recalled product.");
        var r = new ProductRecall { Product = product.Trim(), Batch = batch ?? "", ReceivedDate = received, Action = action ?? "" };
        db.ProductRecalls.Add(r);
        db.SaveChanges();
        audit.Append(session.DisplayName, session.UserId, "recall.recorded", "product-recall", r.Id.ToString(), $"{product} batch {batch}");
        return ServiceResult.Succeed(r.Id);
    }

    public ServiceResult CloseRecall(Guid recallId, string action, UserSession session)
    {
        if (session == null || !session.CanVerify) return ServiceResult.Fail("Only Owner/Admin or Manager/Reviewer accounts can close recalls.");
        if (string.IsNullOrWhiteSpace(action)) return ServiceResult.Fail("Record what was done with the affected stock before closing (quarantined/returned/destroyed).");
        var r = db.ProductRecalls.FirstOrDefault(x => x.Id == recallId);
        if (r == null) return ServiceResult.Fail("Recall not found.");
        if (r.State == "Closed") return ServiceResult.Fail("Recall is already closed.");
        r.Action = action;
        r.State = "Closed";
        r.UpdatedAtUtc = DateTime.UtcNow;
        db.SaveChanges();
        audit.Append(session.DisplayName, session.UserId, "recall.closed", "product-recall", recallId.ToString(), $"{r.Product}: {action}");
        return ServiceResult.Succeed();
    }

    public List<PotencyRecord> PotencyRecords() => db.PotencyRecords.AsNoTracking().OrderByDescending(p => p.TestedOn).ToList();

    public ServiceResult AddPotencyRecord(Guid staffId, string role, string drug, string dosageForm, DateOnly testedOn, string outcome, UserSession session)
    {
        if (session == null || session.ReadOnly) return ServiceResult.Fail("Read-only reviewer accounts cannot record potency testing.");
        if (!db.StaffMembers.Any(s => s.Id == staffId)) return ServiceResult.Fail("Select the person who performed the testing.");
        if (string.IsNullOrWhiteSpace(drug) || string.IsNullOrWhiteSpace(dosageForm)) return ServiceResult.Fail("Record WHICH drug and WHICH dosage form were tested.");
        var rec = new PotencyRecord { StaffMemberId = staffId, Role = role == "Compounding pharmacist" ? role : "Compounding technician", Drug = drug.Trim(), DosageForm = dosageForm.Trim(), TestedOn = testedOn, Outcome = outcome ?? "" };
        db.PotencyRecords.Add(rec);
        db.SaveChanges();
        audit.Append(session.DisplayName, session.UserId, "potency.recorded", "potency-record", rec.Id.ToString(), $"{drug}/{dosageForm} on {testedOn:dd MMM yyyy} by {staffId}");
        return ServiceResult.Succeed(rec.Id);
    }

    /// <summary>Per-person current-year coverage against the ISSUE-POTENCY planning reading:
    /// per person per year, two different drugs across two different dosage forms. The app
    /// reports coverage only — it never asserts the unresolved interpretation as resolved.</summary>
    public (List<(string name, List<string> drugs, List<string> forms, string status)> rows, string note) PotencyCoverage()
    {
        var year = DateTime.Today.Year;
        var staff = db.StaffMembers.AsNoTracking().Where(s => s.Active).ToList();
        var records = db.PotencyRecords.AsNoTracking().Where(p => p.TestedOn.Year == year).ToList();
        var rows = new List<(string, List<string>, List<string>, string)>();
        foreach (var s in staff)
        {
            var mine = records.Where(p => p.StaffMemberId == s.Id).ToList();
            var drugs = mine.Select(p => p.Drug).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var forms = mine.Select(p => p.DosageForm).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var status = (drugs.Count, forms.Count) switch
            {
                ( >= 2, >= 2) => $"coverage met: {drugs.Count} drugs × {forms.Count} forms",
                ( 0, 0 ) => "no testing recorded this year",
                ( var d, var f ) => $"partial: {d} distinct drug(s), {f} distinct form(s) — planning target is two different drugs across two different forms per person",
            };
            rows.Add((s.Name, drugs, forms, status));
        }
        var note = "Cadence interpretation is ISSUE-POTENCY (unresolved): the UAG wording reads 'two different drugs and two different dosage forms per compounding technician and per compounding pharmacist per year'. This screen shows per-person coverage and does not resolve the interpretation.";
        return (rows, note);
    }

    // ---------- M4: assessment events & findings ----------
    public List<AssessmentEvent> AssessmentEvents() =>
        db.AssessmentEvents.AsNoTracking().OrderBy(e => e.ScheduledDate).ToList();

    public ServiceResult AddAssessmentEvent(string kind, DateOnly date, string note, UserSession session)
    {
        if (session == null || session.ReadOnly) return ServiceResult.Fail("Read-only reviewer accounts cannot record assessment events.");
        if (kind is not ("Onsite" or "MidCycle" or "ReAccreditation" or "Other")) return ServiceResult.Fail("Unknown assessment kind.");
        var ev = new AssessmentEvent { Kind = kind, ScheduledDate = date, Note = note ?? "", CreatedByUserId = session.UserId };
        db.AssessmentEvents.Add(ev);
        db.SaveChanges();
        audit.Append(session.DisplayName, session.UserId, "assessment.recorded", "assessment-event", ev.Id.ToString(), $"{kind} on {date:dd MMM yyyy}");
        return ServiceResult.Succeed(ev.Id);
    }

    /// <summary>OutcomeReceived REQUIRES the external reference — the app must never record
    /// an assessor outcome that hasn't physically arrived.</summary>
    public ServiceResult SetAssessmentEventState(Guid eventId, string state, string outcomeRef, UserSession session)
    {
        if (session == null || !session.CanVerify) return ServiceResult.Fail("Only Owner/Admin or Manager/Reviewer accounts can update assessment events.");
        if (state is not ("Planned" or "InProgress" or "Completed" or "OutcomeReceived")) return ServiceResult.Fail("Unknown event state.");
        if (state == "OutcomeReceived" && string.IsNullOrWhiteSpace(outcomeRef))
            return ServiceResult.Fail("Recording an outcome requires the report or letter reference. Never claim an outcome that has not arrived.");
        var ev = db.AssessmentEvents.FirstOrDefault(e => e.Id == eventId);
        if (ev == null) return ServiceResult.Fail("Assessment event not found.");
        ev.State = state;
        ev.OutcomeRef = state == "OutcomeReceived" ? outcomeRef.Trim() : ev.OutcomeRef;
        db.SaveChanges();
        audit.Append(session.DisplayName, session.UserId, "assessment.state", "assessment-event", eventId.ToString(), $"{ev.State}" + (state == "OutcomeReceived" ? $"; ref={ev.OutcomeRef}" : ""));
        return ServiceResult.Succeed();
    }

    public List<Finding> FindingsFor(Guid eventId) =>
        db.Findings.AsNoTracking().Where(f => f.EventId == eventId).OrderBy(f => f.State).ThenBy(f => f.CreatedAtUtc).ToList();

    public ServiceResult AddFinding(Guid eventId, string requirementRef, string description, string severity, Guid ownerUser, UserSession session)
    {
        if (session == null || session.ReadOnly) return ServiceResult.Fail("Read-only reviewer accounts cannot record findings.");
        if (!db.AssessmentEvents.Any(e => e.Id == eventId)) return ServiceResult.Fail("Assessment event not found.");
        if (string.IsNullOrWhiteSpace(description)) return ServiceResult.Fail("Describe the finding — the record is what an assessor (and future staff) must see.");
        var f = new Finding { EventId = eventId, RequirementRef = requirementRef?.Trim() ?? "", Description = description, Severity = severity ?? "", OwnerUserId = ownerUser };
        db.Findings.Add(f);
        db.SaveChanges();
        audit.Append(session.DisplayName, session.UserId, "finding.recorded", "finding", f.Id.ToString(), $"{severity}; {description[..Math.Min(120, description.Length)]}");
        return ServiceResult.Succeed(f.Id);
    }

    /// <summary>Closure requires a corrective action — findings disappear if they can be
    /// closed empty, and the action is the evidence that something actually changed.</summary>
    public ServiceResult SetFindingState(Guid findingId, string state, string correctiveAction, UserSession session)
    {
        if (session == null || !session.CanVerify) return ServiceResult.Fail("Only Owner/Admin or Manager/Reviewer accounts can update findings.");
        if (state is not ("Open" or "Actioned" or "Closed")) return ServiceResult.Fail("Unknown finding state.");
        var f = db.Findings.FirstOrDefault(x => x.Id == findingId);
        if (f == null) return ServiceResult.Fail("Finding not found.");
        if (state == "Closed" && string.IsNullOrWhiteSpace(correctiveAction) && string.IsNullOrWhiteSpace(f.CorrectiveAction))
            return ServiceResult.Fail("A finding cannot close without a recorded corrective action.");
        f.State = state;
        if (!string.IsNullOrWhiteSpace(correctiveAction)) f.CorrectiveAction = correctiveAction;
        f.UpdatedAtUtc = DateTime.UtcNow;
        db.SaveChanges();
        audit.Append(session.DisplayName, session.UserId, "finding.state", "finding", findingId.ToString(), state + (correctiveAction != null && correctiveAction != "" ? $"; action={correctiveAction}" : ""));
        return ServiceResult.Succeed();
    }

    // ---------- deadlines ----------
    public List<DeadlineInfo> DeadlinesFor(Requirement r, RequirementState state)
    {
        List<DeadlineInfo> list = new();
        if (state.Readiness == RequirementReadiness.ReadyForReview || state.Readiness == RequirementReadiness.InProgress)
            list.Add(DeadlineInfo.Internal(DateOnly.FromDateTime(DateTime.Today).AddDays(14), "Next internal review checkpoint (14 days from today)"));
        return list;
    }

    public List<DeadlineInfo> PharmacyDeadlines(bool includePlanningOnly = true)
    {
        var ph = EnsurePharmacy();
        List<DeadlineInfo> list = new()
        {
            DeadlineInfo.Confirmed(new DateOnly(2027, 6, 30), "PPA: deadline for uploading the QSPP certificate (ppaonline.com.au notice, pack KB 'Key dates') — applies from 1 Jul 2027 8CPA programs"),
        };
        if (ph.AccreditationExpiryDate is { } exp)
        {
            list.Add(DeadlineInfo.Calculated(exp.AddDays(-90), "internal planning", "90 days before accreditation expiry — start re-accreditation preparation"));
            list.Add(DeadlineInfo.Calculated(exp, "Program Rules definitions: Accreditation Expiry Date", "Accreditation expires; re-accreditation assessment must occur within 3 calendar months BEFORE expiry per Rule 5"));
            var cutoff = DeadlineCalculator.ConservativeCancelCutoff(exp);
            list.Add(DeadlineInfo.Calculated(DeadlineCalculator.AddCalendarDays(exp, -120), "internal planning", "Latest sensible date to book a re-accreditation assessment safely outside cancellation fee windows"));
            _ = cutoff;
        }
        if (ph.OnsiteAssessmentDate is { } onsite)
        {
            list.Add(DeadlineInfo.Confirmed(onsite, "Assessment provider (recorded booking)"));
            var cutoff = DeadlineCalculator.ConservativeCancelCutoff(onsite);
            list.Add(DeadlineInfo.Calculated(cutoff, "ISSUE-CANCELWINDOW (conservative planning)", "Last date to change/cancel this assessment without an assumed cancellation fee — PLANNING ONLY: sources disagree (10 business days per Rule 10 vs 10 calendar days per Assessments Policy/Fees page)", planningOnly: true, issueKey: "ISSUE-CANCELWINDOW"));
        }
        if (ph.CycleStartDate is { } cs)
        {
            var (wStart, wEnd) = DeadlineCalculator.ConservativeMidCycleWindow(cs);
            list.Add(DeadlineInfo.Calculated(wStart, "ISSUE-MIDCYCLE (conservative planning)", $"Mid-Cycle Review window under the conservative reading (months 15–21, Rule 8 + T&C definition). Window: {wStart:dd MMM yyyy} – {wEnd:dd MMM yyyy}. PLANNING ONLY — sources disagree.", planningOnly: true, issueKey: "ISSUE-MIDCYCLE"));
            for (var y = 0; y < 3; y++)
                list.Add(DeadlineInfo.Calculated(cs.AddYears(y), "T&C Clause 5c — Annual Attestation Statement", $"Annual attestation due at the cycle anniversary (year {y + 1})"));
        }
        if (!includePlanningOnly) list.RemoveAll(d => d.PlanningOnly);
        return list.OrderBy(d => d.Date).ToList();
    }

    // ---------- Today redesign (24 Oct): featured deadline + 12-month obligations ----------

    public record TodayObligation(
        Guid RecordId, string Category, string CategoryKey, string Item, string Caption,
        string Action, DateOnly? Due, string Owner, string EvidenceState);

    public record TodayScreenBrief(
        DeadlineInfo? Featured, TodayPlanning.FeaturedState? FeaturedTone, DeadlineInfo? NextAfter,
        DateOnly? RunStart, List<(DateOnly Date, string Label, bool PlanningOnly)> Milestones,
        List<TodayObligation> Obligations, List<(int Year, int Month)> YearAhead);

    /// <summary>Deadline candidates the featured picker may choose from: the workspace's
    /// PharmacyDeadlines() minus internal task rows. The picker itself is pure (TodayPlanning)
    /// and testable; plan markers never win unless nothing eligible exists.</summary>
    public List<DeadlineInfo> TodayDeadlineCandidates(DateOnly today) =>
        PharmacyDeadlines(true).Where(d => d.Kind != DeadlineKind.InternalTask).ToList();

    /// <summary>Read-only 12-month obligations ledger for Today: real rows only, every row
    /// carries its record id so the UI can open it. Overdue items appear with dates in the
    /// past; the buckets are the pure TodayPlanning.Bucket(). Reference texts are pending the
    /// approved schema addendum (stop condition 2), so no rows exist for them yet.</summary>
    public List<TodayObligation> TodayObligations(DateOnly today)
    {
        var ph = EnsurePharmacy();
        var horizon = today.AddYears(1);
        var lower = today.AddDays(-90);
        var list = new List<TodayObligation>();

        // Equipment: calibration due (recurring maintenance/test of safety systems, AS85000 2.1.1.4b-c).
        foreach (var e in db.Equipment.AsNoTracking().Where(x => x.State != "Retired").ToList())
        {
            if (e.CalibrationDue is { } d && d >= lower && d <= horizon)
                list.Add(new(e.Id, "Equipment", "equipment", e.Name, e.Model, "Recalibrate", d, "", EvidenceForTaskDef(e.CalibrationTaskDefId)));
            else if (e.CalibrationDue is null)
                list.Add(new(e.Id, "Equipment", "equipment", e.Name, e.Model, "Recalibrate", null, "", ""));
        }

        // People: S2/S3 refresher on the anniversary of the last completed training
        // (rows only where some S2/S3 training is actually recorded — no invented dates).
        foreach (var s in db.StaffMembers.AsNoTracking().Where(x => x.Active).ToList())
        {
            if (s.S2S3InitialCompleted is null && s.S2S3RefresherCompleted is null) continue;
            var (due, _) = TrainingRules.RefresherStatus(s, today);
            list.Add(new(s.Id, "People", "people", $"{s.Name} — S2/S3 refresher", s.Role, "Recertify", due, s.Name, ""));
        }

        // Evidence: items that expire (EffectiveTo), excluding retired ones.
        foreach (var e in db.EvidenceItems.AsNoTracking()
                     .Where(x => x.EffectiveTo != null && x.ReviewState != EvidenceReviewState.Retired).ToList())
        {
            var effTo = e.EffectiveTo!.Value;
            if (effTo >= lower && effTo <= horizon)
                list.Add(new(e.Id, "Evidence", "documents", e.Title, e.Kind.ToString(), "Expires", effTo, "", e.ReviewState.ToString()));
        }

        // Program and premises: accreditation expiry, booked assessments, annual attestation.
        if (ph.AccreditationExpiryDate is { } exp && exp >= lower && exp <= horizon)
            list.Add(new(ph.Id, "Program", "program", "Accreditation expiry", "Program Rules Rule 5", "Re-accredit", exp, "", ""));
        foreach (var ev in db.AssessmentEvents.AsNoTracking()
                     .Where(x => x.State == "Planned" || x.State == "InProgress").ToList())
        {
            if (ev.ScheduledDate >= today.AddDays(-30) && ev.ScheduledDate <= horizon)
                list.Add(new(ev.Id, "Program", "program", $"Assessment — {ev.Kind}", ev.State, "Attend", ev.ScheduledDate, "", ""));
        }
        foreach (var d in PharmacyDeadlines(true).Where(d => d.Source.StartsWith("T&C Clause 5c")).ToList())
        {
            if (d.Date >= today && d.Date <= horizon)
                list.Add(new(Guid.NewGuid(), "Program", "program", "Annual attestation statement", "T&C Clause 5c", "Lodge", d.Date, "", ""));
        }

        return list.OrderBy(o => o.Due ?? DateOnly.MaxValue).ToList();
    }

    /// <summary>Latest evidence attached through the calibration recurring task of a piece of
    /// equipment, if any (empty string when none — the UI renders the empty chip).</summary>
    private string EvidenceForTaskDef(Guid? taskDefId)
    {
        if (taskDefId is null) return "";
        var evId = db.Tasks.AsNoTracking()
            .Where(t => t.RecurrenceDefId == taskDefId && t.EvidenceItemId != null)
            .OrderByDescending(t => t.CompletedAtUtc).Select(t => t.EvidenceItemId).FirstOrDefault();
        if (evId is null) return "";
        var state = db.EvidenceItems.AsNoTracking().Where(e => e.Id == evId).Select(e => e.ReviewState).FirstOrDefault();
        return state.ToString();
    }

    /// <summary>Everything the redesigned Today screen renders, assembled once per refresh:
    /// featured deadline (pure pick), its tone, "after this", run-line anchors, milestone
    /// ticks, and the 12-month obligations ledger. Read-only.</summary>
    public TodayScreenBrief TodayBrief(DateOnly today)
    {
        var ph = EnsurePharmacy();
        var candidates = TodayDeadlineCandidates(today);
        var featured = TodayPlanning.PickFeatured(candidates, today);
        var next = featured is { } f ? TodayPlanning.NextAfterFeatured(candidates, f) : null;
        var tone = featured is { } ff ? TodayPlanning.FeaturedTone(ff.Date, today) : null;

        var runStart = ph.CycleStartDate is { } cs && cs <= today ? cs : (DateOnly?)null;
        var milestones = new List<(DateOnly Date, string Label, bool PlanningOnly)>();
        if (runStart is { } anchor && featured is { } fd)
        {
            foreach (var ev in db.AssessmentEvents.AsNoTracking().Where(x => x.State == "Planned" || x.State == "InProgress").ToList())
                if (ev.ScheduledDate >= anchor && ev.ScheduledDate <= fd.Date)
                    milestones.Add((ev.ScheduledDate, $"Planned {ev.Kind}", false));
            var (wStart, wEnd) = DeadlineCalculator.ConservativeMidCycleWindow(anchor);
            if (wStart <= fd.Date) milestones.Add((wStart, "Mid-cycle window opens (conservative reading)", true));
        }

        return new TodayScreenBrief(featured, tone, next, runStart, milestones,
            TodayObligations(today), TodayPlanning.YearAheadMonths(today));
    }

    // ---------- summaries (Today screen + Documents) ----------
    public (int verified, int total) ReadinessSummary()
    {
        var ph = EnsurePharmacy();
        var total = db.Requirements.Count();
        var verified = db.RequirementStates.AsNoTracking().Count(s => s.PharmacyId == ph.Id && s.Readiness == RequirementReadiness.ManagerVerified);
        return (verified, total);
    }

    /// <summary>M7: the surveillance round brief — real counts over the REM evidence
    /// requests (RefKind.Surveillance) plus the nearest PLANNED assessment event that
    /// could carry the round's response window. No invented percentages.</summary>
    public record RemoteRoundSummary(int Items, int WithEvidence, int InProgress, int Verified, string? NextEvent);

    public RemoteRoundSummary SurveillanceRound()
    {
        var ph = EnsurePharmacy();
        var remIds = db.Requirements.AsNoTracking()
            .Where(r => r.RefKind == RefKind.Surveillance).Select(r => r.Id).ToList();
        var states = db.RequirementStates.AsNoTracking()
            .Where(s => s.PharmacyId == ph.Id && remIds.Contains(s.RequirementId)).ToList();
        var withEvidence = db.EvidenceLinks.AsNoTracking()
            .Where(l => remIds.Contains(l.RequirementId) && l.State != LinkState.Superseded)
            .Select(l => l.RequirementId).Distinct().Count();
        var openEvent = db.AssessmentEvents.AsNoTracking()
            .Where(e => e.State == "Planned" && e.ScheduledDate >= DateOnly.FromDateTime(DateTime.Today))
            .OrderBy(e => e.ScheduledDate).FirstOrDefault();
        var nextEvent = openEvent is null
            ? null
            : $"{openEvent.Kind} · {openEvent.ScheduledDate:dd MMM yyyy}" +
              (string.IsNullOrWhiteSpace(openEvent.Note) ? "" : $" · {openEvent.Note}");
        return new RemoteRoundSummary(
            remIds.Count, withEvidence,
            states.Count(s => s.Readiness is RequirementReadiness.InProgress or RequirementReadiness.ReadyForReview),
            states.Count(s => s.Readiness is RequirementReadiness.ManagerVerified or RequirementReadiness.AssessorAccepted),
            nextEvent);
    }

    /// <summary>M7: per-REM rows for the response-pack PDF. Same read paths as the UI
    /// (wording, expected-evidence examples, linked evidence with review states) so the
    /// printed pack can never disagree with the app. Read-only, no state writes.</summary>
    public record RoundPackItem(string Ref, string Title, string Wording, string PlainSummary,
        IReadOnlyList<(string Text, bool Mandatory)> ExpectedExamples,
        string Notes, string NextAction, string Readiness, string Applicability, string OwnerName,
        IReadOnlyList<(string EvidenceTitle, string FileName, string Effective, string ReviewState)> Evidence);

    public List<RoundPackItem> SurveillanceRoundPackItems()
    {
        var ph = EnsurePharmacy();
        var rems = db.Requirements.AsNoTracking().Where(r => r.RefKind == RefKind.Surveillance).OrderBy(r => r.Ref).ToList();
        var remIds = rems.Select(r => r.Id).ToList();
        var users = db.Users.AsNoTracking().ToDictionary(u => u.Id, u => u.DisplayName);
        var states = db.RequirementStates.AsNoTracking().Where(s => s.PharmacyId == ph.Id).ToDictionary(s => s.RequirementId);
        var examples = db.RequirementEvidenceExamples.AsNoTracking().Where(e => remIds.Contains(e.RequirementId)).ToList();
        var links = db.EvidenceLinks.AsNoTracking().Where(l => remIds.Contains(l.RequirementId) && l.State != LinkState.Superseded)
            .Join(db.EvidenceItems.AsNoTracking(), l => l.EvidenceItemId, e => e.Id,
                (l, e) => new { l.RequirementId, Title = e.Title, e.FileName, e.ReviewState, e.EffectiveFrom, e.EffectiveTo })
            .ToList();

        var rows = new List<RoundPackItem>();
        foreach (var r in rems)
        {
            states.TryGetValue(r.Id, out var st);
            var itemExamples = examples.Where(e => e.RequirementId == r.Id).OrderBy(e => e.Mandatory ? 0 : 1)
                .Select(e => (e.Text, e.Mandatory)).ToList();
            var itemLinks = links.Where(l => l.RequirementId == r.Id)
                .Select(l => (l.Title, l.FileName ?? "",
                    (l.EffectiveFrom?.ToString("dd MMM yyyy") ?? "") + (l.EffectiveTo is null ? "" : " → " + l.EffectiveTo?.ToString("dd MMM yyyy")),
                    l.ReviewState.ToString()))
                .ToList();
            rows.Add(new RoundPackItem(
                r.Ref, r.Title, r.WordingExcerpt, r.PlainSummary ?? "",
                itemExamples,
                st?.Notes ?? "", st?.NextAction ?? "",
                (st?.Readiness ?? RequirementReadiness.NotStarted).ToString(),
                (st?.Applicability ?? Applicability.Unknown).ToString(),
                st is { OwnerUnassigned: false } && users.TryGetValue(st.OwnerUserId, out var on) ? on : "",
                itemLinks));
        }
        return rows;
    }

    // ---------- preparation journey (Assessments screen) ----------
    /// <summary>Counts only — the journey view turns them into narrative. No invented
    /// percentages; every number is a real query result.</summary>
    public JourneySnapshot Journey()
    {
        var ph = EnsurePharmacy();
        var states = db.RequirementStates.AsNoTracking().Where(s => s.PharmacyId == ph.Id).ToList();
        var reqsTotal = db.Requirements.Count();
        var owned = states.Count(s => !s.OwnerUnassigned);
        var decided = states.Count(s => s.Applicability != Applicability.Unknown);
        var withEvidence = db.EvidenceLinks.AsNoTracking()
            .Where(l => l.State != LinkState.Superseded).Select(l => l.RequirementId).Distinct().Count();
        var verified = states.Count(s => s.Readiness == RequirementReadiness.ManagerVerified);
        var pendingReview = db.EvidenceItems.Count(e => e.ReviewState == EvidenceReviewState.ReadyForReview);
        var today = Clock.Today();
        var overdue = db.Tasks.Count(t => t.State == TaskState.Open && t.DueDate != null && t.DueDate < today);
        var issues = db.InterpretationIssues.AsNoTracking().ToList();
        return new JourneySnapshot(
            reqsTotal, decided, owned, withEvidence, verified,
            states.Count(s => s.Readiness == RequirementReadiness.InProgress),
            states.Count(s => s.Readiness == RequirementReadiness.NotStarted),
            pendingReview, overdue,
            issues.Count(i => i.Status != "resolved"), issues.Count(i => i.Status == "acknowledged"),
            issues.Count, ph.OnsiteAssessmentDate, ph.AccreditationExpiryDate);
    }

    /// <summary>Supersession impact review: retired evidence with its replacement and the
    /// count of requirement links that were marked Superseded (each got a re-link task).</summary>
    public List<SupersessionRow> SupersessionReview()
    {
        var rows = new List<SupersessionRow>();
        foreach (var old in db.EvidenceItems.AsNoTracking().Where(e => e.ReviewState == EvidenceReviewState.Retired))
        {
            var replacement = old.SupersededById.HasValue
                ? db.EvidenceItems.AsNoTracking().FirstOrDefault(e => e.Id == old.SupersededById) : null;
            var affected = db.EvidenceLinks.Count(l => l.EvidenceItemId == old.Id && l.State == LinkState.Superseded);
            var openTasks = db.Tasks.Count(t => t.EvidenceItemId == old.Id && t.State == TaskState.Open);
            rows.Add(new SupersessionRow(old.Id, old.Title, old.SupersededReason,
                replacement?.Title ?? "(no replacement recorded)", affected, openTasks));
        }
        return rows.OrderBy(r => r.OpenTaskCount == 0).ThenBy(r => r.OldTitle).ToList();
    }

    public List<EvidenceItem> AllEvidence() => db.EvidenceItems.AsNoTracking().OrderByDescending(e => e.CreatedAtUtc).ToList();

    public List<SourceDocument> AllSourceDocuments() => db.SourceDocuments.AsNoTracking().OrderBy(d => d.Role).ThenBy(d => d.DisplayName).ToList();

    public EvidenceItem? EvidenceById(Guid id) => db.EvidenceItems.AsNoTracking().FirstOrDefault(e => e.Id == id);
}