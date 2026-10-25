using QsppManager.Domain;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using QuestPDF.Drawing;

namespace QsppManager.Infrastructure.Printing;

/// <summary>QuestPDF document composer. Licence: Community — free where the organisation's
/// gross revenue is under $1M; eligibility pending owner confirmation (docs/decisions.md DD-9).
/// If it fails, this class is swapped behind IPdfComposer for a MigraDoc implementation.</summary>
public class PdfComposer(string fontsDirectory)
{
    public static void ConfigureLicense() => QuestPDF.Settings.License = LicenseType.Community;

    private bool _fontsRegistered;
    private readonly HashSet<string> _knownFamilies = new(StringComparer.OrdinalIgnoreCase);
    private void EnsureFonts()
    {
        if (_fontsRegistered || !Directory.Exists(fontsDirectory)) return;
        foreach (var f in Directory.GetFiles(fontsDirectory, "*.ttf"))
        {
            try
            {
                using var stream = File.OpenRead(f);
                FontManager.RegisterFont(stream);
                if (f.Contains("HostGrotesk", StringComparison.OrdinalIgnoreCase)) _knownFamilies.Add("Host Grotesk");
                if (f.Contains("JetBrainsMono", StringComparison.OrdinalIgnoreCase)) _knownFamilies.Add("JetBrains Mono");
            }
            catch { /* one bad font must not stop printing */ }
        }
        _fontsRegistered = true;
    }

    public record ChecklistModel(
        string PharmacyName,
        string RequirementRef,
        string RequirementTitle,
        string RequirementSummary,
        string WordingExcerpt,
        string SourceDoc,
        string SourceVersion,
        string AnchorNote,
        RequirementReadiness Readiness,
        Applicability Applicability,
        string OwnerName,
        string NextAction,
        IReadOnlyList<EvidenceRow> Evidence,
        IReadOnlyList<TasksRow> Tasks,
        string Variant,          // blank | prefilled | completed
        DateTime PrintedAtUtc);

    public record EvidenceRow(string Ref, string Title, string Kind, string FileName, string Effective, string ReviewState);
    public record TasksRow(string Title, string Owner, string Due, string State);

    private const string Ink = "#163F35";
    private const string Muted = "#52685C";

    public byte[] Compose(ChecklistModel m)
    {
        EnsureFonts();
        var font = _knownFamilies.Contains("Host Grotesk") ? "Host Grotesk" : "Lato";
        var mono = _knownFamilies.Contains("JetBrains Mono") ? "JetBrains Mono" : font;

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);   // portrait; landscape binder pages arrive in M3
                page.Margin(48);
                page.PageColor(Colors.White);
                page.DefaultTextStyle(x => x.FontFamily(font).FontSize(10.5f).FontColor(Ink).LineHeight(1.35f));

                page.Header().Column(col =>
                {
                    col.Item().Row(r =>
                    {
                        r.RelativeItem().Column(c =>
                        {
                            c.Item().Text(m.PharmacyName).FontSize(15).SemiBold();
                            c.Item().Text("Requirement checklist — QSPP Manager").FontSize(9).FontColor(Muted);
                        });
                        r.AutoItem().AlignRight().Column(c2 =>
                        {
                            c2.Item().Text($"Printed {m.PrintedAtUtc:dd MMM yyyy}").FontColor(Muted).FontSize(9);
                            c2.Item().Text($"Variant: {m.Variant}").FontColor(Muted).FontSize(9);
                        });
                    });
                    col.Item().PaddingTop(6).LineHorizontal(1).LineColor(Ink);
                });

                page.Content().PaddingTop(14).Column(col =>
                {
                    col.Spacing(9);

                    col.Item().Row(r =>
                    {
                        r.AutoItem().Text(m.RequirementRef).FontFamily(mono).FontSize(10).SemiBold();
                        r.RelativeItem().PaddingLeft(10).Text(m.RequirementTitle).SemiBold().FontSize(12);
                    });
                    col.Item().Text(t =>
                    {
                        t.Span("Readiness: ").FontColor(Muted);
                        t.Span(EnumLabel(m.Readiness.ToString())).SemiBold();
                        t.Span("   ·   Applicability: ").FontColor(Muted);
                        t.Span(EnumLabel(m.Applicability.ToString()));
                        if (!string.IsNullOrWhiteSpace(m.OwnerName))
                        {
                            t.Span("   ·   Owner: ").FontColor(Muted);
                            t.Span(m.OwnerName);
                        }
                    });
                    if (!string.IsNullOrWhiteSpace(m.NextAction))
                        col.Item().Text(t => { t.Span("Next action: ").FontColor(Muted); t.Span(m.NextAction); });

                    col.Item().PaddingTop(4).Text("What the source requires").SemiBold().FontSize(11);
                    col.Item().Text(string.IsNullOrWhiteSpace(m.WordingExcerpt)
                            ? "(source wording excerpt was not imported — open Library for the full document)"
                            : m.WordingExcerpt)
                        .FontSize(9.5f).FontColor(Muted);
                    if (!string.IsNullOrWhiteSpace(m.RequirementSummary))
                        col.Item().Text(t => { t.Span("Plain English: ").FontColor(Muted); t.Span(m.RequirementSummary); });
                    col.Item().Text($"Source: {m.SourceDoc} v{m.SourceVersion} · {m.AnchorNote}").FontSize(8.5f).FontColor(Muted);

                    col.Item().PaddingTop(6).Text("Evidence").SemiBold().FontSize(11);
                    switch (m.Variant)
                    {
                        case "blank":
                            col.Item().Text("Record required evidence by hand; one line per item.").FontColor(Muted);
                            col.Item().PaddingTop(2).Column(rows => RuledEvidenceRows(rows, 8, null));
                            break;
                        case "prefilled":
                            col.Item().Column(rows =>
                            {
                                foreach (var e in m.Evidence)
                                    rows.Item().PaddingBottom(2).Text("· " + e.Title);
                                rows.Item().PaddingBottom(4).Text("Mark a row complete only when the evidence truly exists and is the current approved version").FontColor(Muted).FontSize(8.5f);
                            });
                            col.Item().PaddingTop(4).Column(rows => RuledEvidenceRows(rows, 4, null));
                            break;
                        default:
                            col.Item().Table(table =>
                            {
                                table.ColumnsDefinition(columns =>
                                {
                                    columns.ConstantColumn(14);
                                    columns.RelativeColumn(3);
                                    columns.RelativeColumn(2);
                                    columns.RelativeColumn(1.7f);
                                    columns.RelativeColumn(1.6f);
                                });
                                table.Header(h =>
                                {
                                    h.Cell().Text("");
                                    h.Cell().Text("Evidence").SemiBold();
                                    h.Cell().Text("File").SemiBold();
                                    h.Cell().Text("Effective").SemiBold();
                                    h.Cell().Text("Review").SemiBold();
                                });
                                foreach (var e in m.Evidence)
                                {
                                    table.Cell().Text("X");
                                    table.Cell().Text(string.IsNullOrEmpty(e.Ref) ? e.Title : $"{e.Ref} · {e.Title}");
                                    table.Cell().Text(e.FileName);
                                    table.Cell().Text(e.Effective);
                                    table.Cell().Text(EnumLabel(e.ReviewState));
                                }
                            });
                            break;
                    }

                    col.Item().PaddingTop(8).Text("Open tasks").SemiBold().FontSize(11);
                    if (m.Tasks.Count == 0 && m.Variant != "blank")
                        col.Item().Text("No open tasks.").FontColor(Muted);
                    else if (m.Variant == "blank")
                        col.Item().PaddingTop(2).Column(rows =>
                        {
                            for (var i = 0; i < 5; i++)
                                rows.Item().PaddingBottom(20).LineHorizontal(0.4f).LineColor(Muted);
                        });
                    else
                        col.Item().Column(c =>
                        {
                            foreach (var t in m.Tasks)
                                c.Item().Text($"[{(t.State == "Done" ? "x" : " ")}] {t.Title} — {t.Owner}{(string.IsNullOrEmpty(t.Due) ? "" : ", due " + t.Due)}");
                        });

                    col.Item().PaddingTop(18).Row(sig =>
                    {
                        sig.RelativeItem().Text("Verified by (name and signature): ____________________________________");
                        sig.ConstantItem(24);
                        sig.AutoItem().Text("Date: ____ / ____ / ________");
                    });
                });

                page.Footer().Row(r =>
                {
                    r.RelativeItem().Text($"QSPP Manager · Requirement checklist · {m.RequirementRef} · Printing a document does not approve it. Draft/superseded status must remain visible.").FontSize(8).FontColor(Muted);
                    r.AutoItem().Text(t =>
                    {
                        t.Span("Page ").FontSize(8).FontColor(Muted);
                        t.CurrentPageNumber().FontSize(8);
                        t.Span(" of ").FontSize(8).FontColor(Muted);
                        t.TotalPages().FontSize(8).FontColor(Muted);
                    });
                });
            });
        }).GeneratePdf();
    }

    private static void RuledEvidenceRows(ColumnDescriptor rows, int count, string? _)
    {
        for (var i = 0; i < count; i++)
        {
            var first = i == 0;
            rows.Item().PaddingBottom(6).Row(r =>
            {
                r.ConstantItem(14).Text("( )");
                r.RelativeItem().Column(rr =>
                {
                    rr.Item().PaddingTop(10).LineHorizontal(0.4f).LineColor(Muted);
                    if (first) rr.Item().Text("Evidence title / description").FontSize(8).FontColor(Muted);
                });
                r.ConstantItem(110).Column(rr =>
                {
                    rr.Item().PaddingTop(10).LineHorizontal(0.4f).LineColor(Muted);
                    if (first) rr.Item().Text("version / date").FontSize(8).FontColor(Muted);
                });
            });
        }
    }

    /// <summary>M7: the remote assessment response pack — one A4 page-block per REM
    /// evidence request: verbatim ask, plain English, expected evidence, the recorded
    /// response notes and next action, linked evidence with review states, sign-off.</summary>
    public record RoundPackModel(
        string PharmacyName,
        string RoundTitle,        // e.g. QCPP remote assessment portal — surveillance round
        string RoundSource,       // source file + version of the questionnaire
        string SummaryLine,       // counts, e.g. "19 items · 4 with evidence · 1 verified"
        IReadOnlyList<QsppManager.Infrastructure.Services.QsppWorkspace.RoundPackItem> Items,
        DateTime PrintedAtUtc);

    public byte[] ComposeRoundPack(RoundPackModel m)
    {
        EnsureFonts();
        var font = _knownFamilies.Contains("Host Grotesk") ? "Host Grotesk" : "Lato";
        var mono = _knownFamilies.Contains("JetBrains Mono") ? "JetBrains Mono" : font;

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(48);
                page.PageColor(Colors.White);
                page.DefaultTextStyle(x => x.FontFamily(font).FontSize(10.5f).FontColor(Ink).LineHeight(1.35f));

                page.Header().Column(col =>
                {
                    col.Item().Row(r =>
                    {
                        r.RelativeItem().Column(c =>
                        {
                            c.Item().Text(m.PharmacyName).FontSize(15).SemiBold();
                            c.Item().Text("Remote assessment response pack — QSPP Manager").FontSize(9).FontColor(Muted);
                        });
                        r.AutoItem().AlignRight().Text($"Printed {m.PrintedAtUtc:dd MMM yyyy}").FontColor(Muted).FontSize(9);
                    });
                    col.Item().PaddingTop(6).LineHorizontal(1).LineColor(Ink);
                });

                page.Content().PaddingTop(14).Column(col =>
                {
                    col.Spacing(10);

                    col.Item().Text(m.RoundTitle).SemiBold().FontSize(13);
                    col.Item().Text($"Source: {m.RoundSource}").FontSize(8.5f).FontColor(Muted);
                    col.Item().Text(m.SummaryLine).FontSize(10);
                    col.Item().Text("Prepared to work through the portal's evidence requests one by one. Printing a document does not approve it; states shown here are the app's recorded states.")
                        .FontSize(9).FontColor(Muted);

                    foreach (var item in m.Items)
                    {
                        col.Item().PaddingTop(10).Column(sec =>
                        {
                            sec.Spacing(5);
                            sec.Item().Row(r =>
                            {
                                r.AutoItem().Text(item.Ref).FontFamily(mono).FontSize(10).SemiBold();
                                r.RelativeItem().PaddingLeft(10).Text(item.Title).SemiBold().FontSize(11.5f);
                            });
                            sec.Item().Text(t =>
                            {
                                t.Span("Readiness: ").FontColor(Muted);
                                t.Span(EnumLabel(item.Readiness)).SemiBold();
                                t.Span("   ·   Applicability: ").FontColor(Muted);
                                t.Span(EnumLabel(item.Applicability));
                                if (!string.IsNullOrWhiteSpace(item.OwnerName))
                                {
                                    t.Span("   ·   Owner: ").FontColor(Muted);
                                    t.Span(item.OwnerName);
                                }
                            });
                            if (!string.IsNullOrWhiteSpace(item.Wording))
                                sec.Item().Text(item.Wording).FontSize(9.5f).FontColor(Muted);
                            if (!string.IsNullOrWhiteSpace(item.PlainSummary))
                                sec.Item().Text(t => { t.Span("Plain English: ").FontColor(Muted); t.Span(item.PlainSummary); });
                            if (item.ExpectedExamples.Count > 0)
                                sec.Item().Column(c =>
                                {
                                    c.Item().Text("Expected evidence (from the source)").SemiBold().FontSize(9.5f);
                                    foreach (var e in item.ExpectedExamples)
                                        c.Item().Text((e.Mandatory ? "• required: " : "• example: ") + e.Text).FontSize(9);
                                });
                            if (!string.IsNullOrWhiteSpace(item.NextAction))
                                sec.Item().Text(t => { t.Span("Next action: ").FontColor(Muted); t.Span(item.NextAction); });
                            if (!string.IsNullOrWhiteSpace(item.Notes))
                                sec.Item().Text(t => { t.Span("Recorded notes: ").FontColor(Muted); t.Span(item.Notes); });

                            if (item.Evidence.Count > 0)
                            {
                                sec.Item().PaddingTop(2).Table(table =>
                                {
                                    table.ColumnsDefinition(columns =>
                                    {
                                        columns.RelativeColumn(2.4f);
                                        columns.RelativeColumn(1.6f);
                                        columns.RelativeColumn(1.2f);
                                        columns.RelativeColumn(1.1f);
                                    });
                                    table.Header(h =>
                                    {
                                        h.Cell().Text("Linked evidence").SemiBold().FontSize(9.5f);
                                        h.Cell().Text("File").SemiBold().FontSize(9.5f);
                                        h.Cell().Text("Effective").SemiBold().FontSize(9.5f);
                                        h.Cell().Text("Review").SemiBold().FontSize(9.5f);
                                    });
                                    foreach (var e in item.Evidence)
                                    {
                                        table.Cell().Text(e.EvidenceTitle).FontSize(9);
                                        table.Cell().Text(e.FileName).FontSize(9);
                                        table.Cell().Text(e.Effective).FontSize(9);
                                        table.Cell().Text(EnumLabel(e.ReviewState)).FontSize(9);
                                    }
                                });
                            }
                            else
                            {
                                sec.Item().Text("No evidence linked yet.").FontSize(9).FontColor(Muted);
                            }
                            sec.Item().PaddingTop(2).LineHorizontal(0.6f).LineColor(Muted);
                        });
                    }

                    col.Item().PaddingTop(18).Row(sig =>
                    {
                        sig.RelativeItem().Text("Prepared by (name and signature): ____________________________________");
                        sig.ConstantItem(24);
                        sig.AutoItem().Text("Date: ____ / ____ / ________");
                    });
                });

                page.Footer().Row(r =>
                {
                    r.RelativeItem().Text("QSPP Manager · Remote assessment response pack · Printing a document does not approve it. Draft/superseded status remains visible.")
                        .FontSize(8).FontColor(Muted);
                    r.AutoItem().Text(t =>
                    {
                        t.Span("Page ").FontSize(8).FontColor(Muted);
                        t.CurrentPageNumber().FontSize(8);
                        t.Span(" of ").FontSize(8).FontColor(Muted);
                        t.TotalPages().FontSize(8).FontColor(Muted);
                    });
                });
            });
        }).GeneratePdf();
    }

    private static string EnumLabel(string raw) => raw switch
    {
        "ManagerVerified" => "Manager verified",
        "ReadyForReview" => "Ready for review",
        "InProgress" => "In progress",
        "NotStarted" => "Not started",
        "AssessorAccepted" => "Assessor accepted (external outcome recorded)",
        "Applicable" => "Applicable",
        "NotApplicable" => "Not applicable",
        "Unknown" => "Unknown",
        "Retired" => "Retired (superseded)",
        "NeedsRework" => "Needs rework",
        "Draft" => "Draft",
        "Done" => "Done",
        "Open" => "Open",
        _ => raw,
    };
}