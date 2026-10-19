using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using QsppManager.Application;
using QsppManager.Domain;
using QsppManager.Domain.Entities;
using QsppManager.Domain.Services;
using QsppManager.Infrastructure.Persistence;
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
        var ph2 = new Pharmacy { Name = demo ? "DEMO ONLY — Example Lane Pharmacy" : "Burke Road Compounding", Jurisdiction = "VIC", TimeZoneId = "Australia/Melbourne", IsDemo = demo };
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
        var rows = new List<RequirementRow>();
        foreach (var x in q)
        {
            if (x.state == null) rows.Add(new RequirementRow(x.r.Id, x.r.RefKind, x.r.Ref, x.r.Title, x.r.Domain, Applicability.Unknown, RequirementReadiness.NotStarted, "", true, "", x.EvidenceCount, x.OpenTasks));
            else rows.Add(new RequirementRow(x.r.Id, x.r.RefKind, x.r.Ref, x.r.Title, x.r.Domain,
                x.state.Applicability, x.state.Readiness,
                x.state.OwnerUnassigned ? "" : users.GetValueOrDefault(x.state.OwnerUserId, "(unknown)"),
                x.state.OwnerUnassigned, x.state.NextAction, x.EvidenceCount, x.OpenTasks));
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
        return new RequirementDetail(r, state, owner, src, links, tasks, DeadlinesFor(r, state), issues);
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
        var today = DateOnly.FromDateTime(DateTime.Today);
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

    // ---------- summaries (Today screen + Documents) ----------
    public (int verified, int total) ReadinessSummary()
    {
        var ph = EnsurePharmacy();
        var total = db.Requirements.Count();
        var verified = db.RequirementStates.AsNoTracking().Count(s => s.PharmacyId == ph.Id && s.Readiness == RequirementReadiness.ManagerVerified);
        return (verified, total);
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
        var today = DateOnly.FromDateTime(DateTime.Today);
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