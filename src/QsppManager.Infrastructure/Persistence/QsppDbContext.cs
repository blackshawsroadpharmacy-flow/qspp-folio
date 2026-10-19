using Microsoft.EntityFrameworkCore;
using QsppManager.Domain.Entities;

namespace QsppManager.Infrastructure.Persistence;

public class QsppDbContext(DbContextOptions<QsppDbContext> options) : DbContext(options)
{
    public DbSet<Pharmacy> Pharmacies => Set<Pharmacy>();
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<Requirement> Requirements => Set<Requirement>();
    public DbSet<RequirementEvidenceExample> RequirementEvidenceExamples => Set<RequirementEvidenceExample>();
    public DbSet<RequirementState> RequirementStates => Set<RequirementState>();
    public DbSet<EvidenceItem> EvidenceItems => Set<EvidenceItem>();
    public DbSet<EvidenceLink> EvidenceLinks => Set<EvidenceLink>();
    public DbSet<TaskItem> Tasks => Set<TaskItem>();
    public DbSet<SourceDocument> SourceDocuments => Set<SourceDocument>();
    public DbSet<InterpretationIssue> InterpretationIssues => Set<InterpretationIssue>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<PrintJob> PrintJobs => Set<PrintJob>();
    public DbSet<PrintPreset> PrintPresets => Set<PrintPreset>();
    public DbSet<RecurringTaskDef> RecurringTaskDefs => Set<RecurringTaskDef>();
    public DbSet<Equipment> Equipment => Set<Equipment>();
    public DbSet<Incident> Incidents => Set<Incident>();
    public DbSet<StaffMember> StaffMembers => Set<StaffMember>();
    public DbSet<PolicyAcknowledgement> PolicyAcknowledgements => Set<PolicyAcknowledgement>();
    public DbSet<CompoundSupplier> CompoundSuppliers => Set<CompoundSupplier>();
    public DbSet<ProductRecall> ProductRecalls => Set<ProductRecall>();
    public DbSet<PotencyRecord> PotencyRecords => Set<PotencyRecord>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.Entity<Requirement>(e =>
        {
            e.HasIndex(x => new { x.RefKind, x.Ref }).IsUnique();
            e.HasIndex(x => x.Domain);
            e.HasMany(x => x.EvidenceExamples).WithOne().HasForeignKey(x => x.RequirementId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Ref).HasMaxLength(80);
            e.Property(x => x.Title).HasMaxLength(300);
            e.Property(x => x.WordingExcerpt).HasMaxLength(600);
        });
        mb.Entity<RequirementEvidenceExample>(e => e.Property(x => x.Text).HasMaxLength(300));
        mb.Entity<RequirementState>(e =>
        {
            e.HasIndex(x => new { x.PharmacyId, x.RequirementId }).IsUnique();
            e.Property(x => x.Notes).HasMaxLength(4000);
            e.Property(x => x.ExternalOutcomeRef).HasMaxLength(200);
        });
        mb.Entity<EvidenceItem>(e =>
        {
            e.HasIndex(x => x.Sha256);
            e.Property(x => x.Title).HasMaxLength(300);
            e.Property(x => x.PatientDataAttestation).HasMaxLength(500);
        });
        mb.Entity<EvidenceLink>(e =>
        {
            e.HasIndex(x => new { x.EvidenceItemId, x.RequirementId }).IsUnique();
            e.HasIndex(x => x.RequirementId);
        });
        mb.Entity<TaskItem>(e =>
        {
            e.HasIndex(x => new { x.State, x.DueDate });
            e.HasIndex(x => new { x.RecurrenceDefId, x.OccurrenceDate }).IsUnique();
            e.Property(x => x.Title).HasMaxLength(300);
            e.Property(x => x.CompletionNote).HasMaxLength(2000);
        });
        mb.Entity<AppUser>(e =>
        {
            e.HasIndex(x => x.UserName).IsUnique();
        });
        mb.Entity<SourceDocument>(e => e.HasIndex(x => x.Sha256Hex));
        mb.Entity<InterpretationIssue>(e => e.HasIndex(x => x.Key).IsUnique());
        mb.Entity<AuditEvent>(e =>
        {
            e.Property(x => x.Details).HasMaxLength(2000);
            e.Property(x => x.Digest).HasMaxLength(70);
            e.Property(x => x.PreviousDigest).HasMaxLength(70);
        });
        mb.Entity<PrintJob>(e => e.Property(x => x.Title).HasMaxLength(300));
        mb.Entity<PrintPreset>(e => e.Property(x => x.Name).HasMaxLength(120));
        mb.Entity<Pharmacy>(e => e.Property(x => x.Name).HasMaxLength(200));
        mb.Entity<RecurringTaskDef>(e =>
        {
            e.Property(x => x.Title).HasMaxLength(300);
            e.Property(x => x.Details).HasMaxLength(2000);
        });
        mb.Entity<Equipment>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Notes).HasMaxLength(2000);
        });
        mb.Entity<Incident>(e =>
        {
            e.Property(x => x.Description).HasMaxLength(4000);
            e.Property(x => x.CorrectiveAction).HasMaxLength(4000);
        });
        mb.Entity<StaffMember>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Notes).HasMaxLength(2000);
        });
        mb.Entity<PolicyAcknowledgement>(e => e.HasIndex(x => new { x.StaffMemberId, x.PolicyName, x.PolicyVersion }).IsUnique());
    }
}