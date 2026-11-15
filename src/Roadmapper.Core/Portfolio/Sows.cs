namespace Roadmapper.Core.Portfolio;

/// <summary>Where a statement of work stands: being written, sent to the customer, signed, turned down, or withdrawn.</summary>
public enum SowStatus { Draft, Sent, Approved, Declined, Cancelled }

/// <summary>The cost rows on a SOW's summary, in the order they print (the SOW estimation template's categories).</summary>
public enum SowCostCategory
{
    ItInfrastructure, Hardware, Software, Travel,
    LaborItPm, LaborWms, LaborTms, LaborEdi, LaborTesting, LaborInfrastructure,
}

/// <summary>Whether a cost is paid once or every year.</summary>
public enum SowFrequency { OneTime, Annual }

/// <summary>
/// A statement of work: what Kenco IT will do for a customer at a site, and what it costs, by category, one-time and
/// annual. It prints as a branded PDF (summary, description, cost table, signature blocks) in place of the Excel
/// estimation template. The customer is usually a trading partner (<see cref="CustomerId"/>), but the name printed is
/// <see cref="CustomerName"/>, so a SOW keeps its wording if the partner is renamed or deleted.
/// </summary>
public class Sow
{
    public int Id { get; set; }
    /// <summary>The Kenco SOW number, e.g. 953100.</summary>
    public string? Number { get; set; }
    public string ProjectName { get; set; } = "";
    public int? CustomerId { get; set; }
    public TradingPartner? Customer { get; set; }
    public string CustomerName { get; set; } = "";
    public string? Site { get; set; }
    /// <summary>The Kenco SOW manager.</summary>
    public string? Manager { get; set; }
    public SowStatus Status { get; set; } = SowStatus.Draft;
    /// <summary>The date the SOW is written; it prints in the footer.</summary>
    public DateOnly PreparedOn { get; set; }
    /// <summary>The date the offer lapses if it isn't signed (the template's "today + 90").</summary>
    public DateOnly? ExpiresOn { get; set; }

    public string? Overview { get; set; }
    public string? Scope { get; set; }
    public string? DetailedDescription { get; set; }
    public string? Assumptions { get; set; }
    public string? Schedule { get; set; }

    /// <summary>Adds a page to the PDF listing every cost line (hours × rate), after the signatures.</summary>
    public bool IncludeCostDetail { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public List<SowLine> Lines { get; set; } = [];

    public SowTotals Totals => SowRules.Totals(Lines);
}

/// <summary>A cost on a SOW, by category and frequency: hours, licenses or trips (<see cref="Quantity"/>) × <see cref="Rate"/>.</summary>
public interface ISowCost
{
    SowCostCategory Category { get; }
    SowFrequency Frequency { get; }
    decimal Amount { get; }
}

/// <summary>
/// One cost line of a SOW. Its rate is copied from the rate card when it's added, so changing the rate card later
/// doesn't change SOWs already written.
/// </summary>
public class SowLine : ISowCost
{
    public int Id { get; set; }
    public int SowId { get; set; }
    public Sow? Sow { get; set; }
    public SowCostCategory Category { get; set; }
    public SowFrequency Frequency { get; set; } = SowFrequency.OneTime;
    public string Description { get; set; } = "";
    public decimal Quantity { get; set; }
    /// <summary>What the quantity counts: hour, license, trip…</summary>
    public string? Unit { get; set; }
    public decimal Rate { get; set; }
    public string? Note { get; set; }
    public int SortOrder { get; set; }

    public decimal Amount => SowRules.Amount(Quantity, Rate);
}

/// <summary>A standard rate offered when adding cost lines to a SOW, e.g. "WMS Development, $215 per hour".</summary>
public class SowRate
{
    public int Id { get; set; }
    public SowCostCategory Category { get; set; }
    public SowFrequency Frequency { get; set; } = SowFrequency.OneTime;
    public string Name { get; set; } = "";
    public decimal Rate { get; set; }
    public string? Unit { get; set; }
    /// <summary>Guidance shown when it's picked, e.g. "Add 9.25% sales tax for a Blue Yonder license".</summary>
    public string? Note { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
}

/// <summary>The words every SOW shares: who signs for Kenco, the cost footnote and agreement text, and the standard text a new SOW starts with.</summary>
public class SowSettings
{
    public int Id { get; set; } = 1;
    /// <summary>A new SOW expires this many days after it's prepared.</summary>
    public int ExpirationDays { get; set; } = 90;
    public string? DefaultManager { get; set; }
    /// <summary>The Kenco companies that sign, one per line; the customer signs after them.</summary>
    public string Signatories { get; set; } = SowRules.DefaultSignatories;
    /// <summary>The footnote under the cost table (it follows the asterisk on TOTAL*).</summary>
    public string? CostFootnote { get; set; } = SowRules.DefaultCostFootnote;
    /// <summary>The paragraph above the signatures.</summary>
    public string? Agreement { get; set; } = SowRules.DefaultAgreement;
    public string? DefaultScope { get; set; } = SowRules.DefaultScopeText;
    public string? DefaultAssumptions { get; set; } = SowRules.DefaultAssumptionsText;
    public string? DefaultSchedule { get; set; } = SowRules.DefaultScheduleText;
}

/// <summary>One row of a SOW's cost table.</summary>
public sealed record SowCostRow(SowCostCategory Category, decimal OneTime, decimal Annual);

/// <summary>A SOW's cost table: every category (in print order, empty ones included) and the totals.</summary>
public sealed record SowTotals(IReadOnlyList<SowCostRow> Rows, decimal OneTime, decimal Annual)
{
    public SowCostRow Row(SowCostCategory c) => Rows.First(r => r.Category == c);
}

/// <summary>A line of a SOW's description text as it prints: a paragraph or a bullet, indented <see cref="Level"/> steps.</summary>
public sealed record SowTextLine(string Text, bool Bullet, int Level);

public static class SowRules
{
    public const int MaxNumberLength = 50;
    public const int MaxSiteLength = 100;
    public const int MaxTextLength = 4000;
    public const int MaxLineLength = 200;
    public const int MaxUnitLength = 30;
    public const int MaxNoteLength = 500;

    public const string DefaultSignatories = "Kenco Management Services, LLC\nKenco Logistics Services, LLC";
    public const string DefaultCostFootnote =
        "Total costs above do not include any charges from Kenco Logistics Services (KLS) such as management fees, G&A fees, or Cost of Working Capital charges.";
    public const string DefaultAgreement =
        "This Statement of Work describes the services Kenco shall supply, perform, and deliver in accordance with this Statement of Work and the Master Agreement. " +
        "This Statement of Work is executed by a duly authorized representative of the parties as of the date set forth below.";
    public const string DefaultScopeText =
        "- Project management\n- Configuration of the WMS\n- Unit testing, quality assurance testing\n- Production deployment\n- Go-live support";
    public const string DefaultAssumptionsText = "- Standard documents and labels will be used";
    public const string DefaultScheduleText =
        "- Development to commence upon approval\n- QA schedule based on code completion\n- Go-live based upon testing completion\n" +
        "- Once a firm schedule is determined, the customer will be notified";

    public static IReadOnlyList<SowCostCategory> Categories { get; } = Enum.GetValues<SowCostCategory>();

    /// <summary>The same number without trailing zeros (150.0 → 150), so inputs show what was typed.</summary>
    public static decimal Plain(decimal d) => d / 1.000000000000000000000000000000000m;

    public static decimal Amount(decimal quantity, decimal rate) => Math.Round(quantity * rate, 2, MidpointRounding.AwayFromZero);

    public static bool IsLabor(SowCostCategory c) => c >= SowCostCategory.LaborItPm;

    /// <summary>The unit a new line of this category counts in.</summary>
    public static string DefaultUnit(SowCostCategory c) => c switch
    {
        _ when IsLabor(c) => "hour",
        SowCostCategory.Travel => "trip",
        _ => "each",
    };

    public static SowTotals Totals(IEnumerable<ISowCost> lines)
    {
        var list = lines.ToList();
        var rows = Categories.Select(c => new SowCostRow(c,
            list.Where(l => l.Category == c && l.Frequency == SowFrequency.OneTime).Sum(l => l.Amount),
            list.Where(l => l.Category == c && l.Frequency == SowFrequency.Annual).Sum(l => l.Amount))).ToList();
        return new SowTotals(rows, rows.Sum(r => r.OneTime), rows.Sum(r => r.Annual));
    }

    /// <summary>Sent, not signed, and past its expiration date.</summary>
    public static bool IsExpired(Sow s, DateOnly today) => s.Status is SowStatus.Draft or SowStatus.Sent && s.ExpiresOn is { } e && e < today;

    /// <summary>The Kenco companies that sign, from <see cref="SowSettings.Signatories"/>.</summary>
    public static List<string> SignatoryList(string? text) =>
        (text ?? "").Split('\n').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();

    /// <summary>
    /// Splits description text into printed lines. A line starting with "-", "*", "•" or "–" is a bullet; leading spaces
    /// (two per step) or tabs indent it. Blank lines are dropped.
    /// </summary>
    public static List<SowTextLine> TextLines(string? text)
    {
        var result = new List<SowTextLine>();
        foreach (var raw in (text ?? "").Replace("\r", "").Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var indent = 0;
            foreach (var ch in raw)
            {
                if (ch == ' ') indent++;
                else if (ch == '\t') indent += 2;
                else break;
            }
            var line = raw.Trim();
            // '' is the Symbol-font bullet that text pasted from Word or Excel often carries.
            var bullet = line.Length > 0 && line[0] is '-' or '*' or '•' or '–' or '' or '·';
            if (bullet) line = line[1..].Trim();
            if (line.Length == 0) continue;
            result.Add(new SowTextLine(line, bullet, Math.Min(indent / 2, 3)));
        }
        return result;
    }

    /// <summary>"SOW 953100 · McCormick EDI Implementation", for file names and titles.</summary>
    public static string Title(Sow s) => s.Number is { Length: > 0 } n ? $"SOW {n} · {s.ProjectName}" : s.ProjectName;

    /// <summary>A file name for the PDF: "SOW 953100 - McCormick EDI Implementation.pdf", without characters Windows won't allow.</summary>
    public static string FileName(Sow s)
    {
        var name = s.Number is { Length: > 0 } n ? $"SOW {n} - {s.ProjectName}" : $"SOW - {s.ProjectName}";
        var bad = Path.GetInvalidFileNameChars();
        var clean = new string(name.Select(c => bad.Contains(c) ? '-' : c).ToArray()).Trim();
        return (clean.Length > 120 ? clean[..120] : clean) + ".pdf";
    }
}

/// <summary>A cost line being edited.</summary>
public class SowLineDraft : ISowCost
{
    public SowCostCategory Category { get; set; }
    public SowFrequency Frequency { get; set; } = SowFrequency.OneTime;
    public string Description { get; set; } = "";
    public decimal Quantity { get; set; }
    public string? Unit { get; set; }
    public decimal Rate { get; set; }
    public string? Note { get; set; }

    public decimal Amount => SowRules.Amount(Quantity, Rate);

    public static SowLineDraft From(SowLine l) => new()
    {
        Category = l.Category, Frequency = l.Frequency, Description = l.Description, Quantity = SowRules.Plain(l.Quantity), Unit = l.Unit,
        Rate = SowRules.Plain(l.Rate), Note = l.Note,
    };

    /// <summary>A line at the rate card's price, with no quantity yet.</summary>
    public static SowLineDraft From(SowRate r) => new()
    {
        Category = r.Category, Frequency = r.Frequency, Description = r.Name, Unit = r.Unit ?? SowRules.DefaultUnit(r.Category), Rate = SowRules.Plain(r.Rate), Note = r.Note,
    };

    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Description)) return "Every cost line needs a description.";
        if (Description.Trim().Length > SowRules.MaxLineLength) return $"A cost line's description can be at most {SowRules.MaxLineLength} characters.";
        if (Quantity < 0 || Rate < 0) return $"“{Description.Trim()}”: quantity and rate can't be negative.";
        if (Unit?.Trim().Length > SowRules.MaxUnitLength) return $"A unit can be at most {SowRules.MaxUnitLength} characters.";
        if (Note?.Trim().Length > SowRules.MaxNoteLength) return $"A cost line's note can be at most {SowRules.MaxNoteLength} characters.";
        return null;
    }

    public void ApplyTo(SowLine l, int sortOrder)
    {
        l.Category = Category;
        l.Frequency = Frequency;
        l.Description = Description.Trim();
        l.Quantity = Quantity;
        l.Unit = DraftText.Clean(Unit);
        l.Rate = Rate;
        l.Note = DraftText.Clean(Note);
        l.SortOrder = sortOrder;
    }
}

/// <summary>A SOW's editable fields and cost lines.</summary>
public class SowDraft
{
    public int Id { get; set; }
    public string? Number { get; set; }
    public string ProjectName { get; set; } = "";
    public int? CustomerId { get; set; }
    public string CustomerName { get; set; } = "";
    public string? Site { get; set; }
    public string? Manager { get; set; }
    public SowStatus Status { get; set; } = SowStatus.Draft;
    public DateOnly PreparedOn { get; set; }
    public DateOnly? ExpiresOn { get; set; }
    public string? Overview { get; set; }
    public string? Scope { get; set; }
    public string? DetailedDescription { get; set; }
    public string? Assumptions { get; set; }
    public string? Schedule { get; set; }
    public bool IncludeCostDetail { get; set; }
    public List<SowLineDraft> Lines { get; set; } = [];

    public bool IsNew => Id == 0;
    public SowTotals Totals => SowRules.Totals(Lines);

    /// <summary>A new SOW prepared today, with the settings' manager, standard text and expiration.</summary>
    public static SowDraft New(SowSettings settings, DateOnly today) => new()
    {
        PreparedOn = today,
        ExpiresOn = settings.ExpirationDays > 0 ? today.AddDays(settings.ExpirationDays) : null,
        Manager = settings.DefaultManager,
        Scope = settings.DefaultScope,
        Assumptions = settings.DefaultAssumptions,
        Schedule = settings.DefaultSchedule,
    };

    public static SowDraft From(Sow s) => new()
    {
        Id = s.Id, Number = s.Number, ProjectName = s.ProjectName, CustomerId = s.CustomerId, CustomerName = s.CustomerName, Site = s.Site,
        Manager = s.Manager, Status = s.Status, PreparedOn = s.PreparedOn, ExpiresOn = s.ExpiresOn, Overview = s.Overview, Scope = s.Scope,
        DetailedDescription = s.DetailedDescription, Assumptions = s.Assumptions, Schedule = s.Schedule, IncludeCostDetail = s.IncludeCostDetail,
        Lines = s.Lines.OrderBy(l => l.SortOrder).ThenBy(l => l.Id).Select(SowLineDraft.From).ToList(),
    };

    /// <summary>
    /// A new draft copied from <paramref name="s"/>: same customer, text and costs, prepared today with a fresh
    /// expiration, no number, and "(copy)" after the name.
    /// </summary>
    public static SowDraft Copy(Sow s, SowSettings settings, DateOnly today)
    {
        var d = From(s);
        d.Id = 0;
        d.Number = null;
        d.Status = SowStatus.Draft;
        d.ProjectName = s.ProjectName.Length + 7 > PortfolioRules.MaxNameLength ? s.ProjectName : $"{s.ProjectName} (copy)";
        d.PreparedOn = today;
        d.ExpiresOn = settings.ExpirationDays > 0 ? today.AddDays(settings.ExpirationDays) : null;
        return d;
    }

    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(ProjectName)) return "Give the project a name.";
        if (ProjectName.Trim().Length > PortfolioRules.MaxNameLength) return $"A project name can be at most {PortfolioRules.MaxNameLength} characters.";
        if (string.IsNullOrWhiteSpace(CustomerName)) return "Say who the customer is.";
        if (CustomerName.Trim().Length > PortfolioRules.MaxNameLength) return $"A customer name can be at most {PortfolioRules.MaxNameLength} characters.";
        if (Number?.Trim().Length > SowRules.MaxNumberLength) return $"A SOW number can be at most {SowRules.MaxNumberLength} characters.";
        if (Site?.Trim().Length > SowRules.MaxSiteLength) return $"A site can be at most {SowRules.MaxSiteLength} characters.";
        if (Manager?.Trim().Length > PortfolioRules.MaxNameLength) return $"The SOW manager's name can be at most {PortfolioRules.MaxNameLength} characters.";
        if (ExpiresOn is { } e && e < PreparedOn) return "The SOW can't expire before it's prepared.";
        foreach (var (label, text) in new[] { ("Overview", Overview), ("Scope of services", Scope), ("Detailed description", DetailedDescription), ("Assumptions", Assumptions), ("Schedule", Schedule) })
            if (text?.Trim().Length > SowRules.MaxTextLength) return $"{label} can be at most {SowRules.MaxTextLength} characters.";
        foreach (var l in Lines)
            if (l.Validate() is { } error) return error;
        return null;
    }

    /// <summary>Copies the fields (not the lines; the service replaces those).</summary>
    public void ApplyTo(Sow s)
    {
        s.Number = DraftText.Clean(Number);
        s.ProjectName = ProjectName.Trim();
        s.CustomerId = CustomerId;
        s.CustomerName = CustomerName.Trim();
        s.Site = DraftText.Clean(Site);
        s.Manager = DraftText.Clean(Manager);
        s.Status = Status;
        s.PreparedOn = PreparedOn;
        s.ExpiresOn = ExpiresOn;
        s.Overview = DraftText.Clean(Overview);
        s.Scope = DraftText.Clean(Scope);
        s.DetailedDescription = DraftText.Clean(DetailedDescription);
        s.Assumptions = DraftText.Clean(Assumptions);
        s.Schedule = DraftText.Clean(Schedule);
        s.IncludeCostDetail = IncludeCostDetail;
    }
}

/// <summary>A rate card entry being edited.</summary>
public class SowRateDraft
{
    public int Id { get; set; }
    public SowCostCategory Category { get; set; }
    public SowFrequency Frequency { get; set; } = SowFrequency.OneTime;
    public string Name { get; set; } = "";
    public decimal Rate { get; set; }
    public string? Unit { get; set; }
    public string? Note { get; set; }
    public bool IsActive { get; set; } = true;

    public bool IsNew => Id == 0;

    public static SowRateDraft New(SowCostCategory c) => new()
    {
        Category = c, Unit = SowRules.DefaultUnit(c), Frequency = SowFrequency.OneTime,
    };

    public static SowRateDraft From(SowRate r) => new()
    {
        Id = r.Id, Category = r.Category, Frequency = r.Frequency, Name = r.Name, Rate = SowRules.Plain(r.Rate), Unit = r.Unit, Note = r.Note, IsActive = r.IsActive,
    };

    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Name)) return "Give the rate a name.";
        if (Name.Trim().Length > SowRules.MaxLineLength) return $"A rate's name can be at most {SowRules.MaxLineLength} characters.";
        if (Rate < 0) return "A rate can't be negative.";
        if (Unit?.Trim().Length > SowRules.MaxUnitLength) return $"A unit can be at most {SowRules.MaxUnitLength} characters.";
        if (Note?.Trim().Length > SowRules.MaxNoteLength) return $"A note can be at most {SowRules.MaxNoteLength} characters.";
        return null;
    }

    public void ApplyTo(SowRate r)
    {
        r.Category = Category;
        r.Frequency = Frequency;
        r.Name = Name.Trim();
        r.Rate = Rate;
        r.Unit = DraftText.Clean(Unit);
        r.Note = DraftText.Clean(Note);
        r.IsActive = IsActive;
    }
}

/// <summary>The SOW settings being edited.</summary>
public class SowSettingsDraft
{
    public int ExpirationDays { get; set; } = 90;
    public string? DefaultManager { get; set; }
    public string Signatories { get; set; } = SowRules.DefaultSignatories;
    public string? CostFootnote { get; set; }
    public string? Agreement { get; set; }
    public string? DefaultScope { get; set; }
    public string? DefaultAssumptions { get; set; }
    public string? DefaultSchedule { get; set; }

    public static SowSettingsDraft From(SowSettings s) => new()
    {
        ExpirationDays = s.ExpirationDays, DefaultManager = s.DefaultManager, Signatories = s.Signatories, CostFootnote = s.CostFootnote,
        Agreement = s.Agreement, DefaultScope = s.DefaultScope, DefaultAssumptions = s.DefaultAssumptions, DefaultSchedule = s.DefaultSchedule,
    };

    public string? Validate()
    {
        if (ExpirationDays is < 0 or > 3650) return "Expiration must be between 0 and 3,650 days (0 means no expiration date).";
        if (DefaultManager?.Trim().Length > PortfolioRules.MaxNameLength) return $"The SOW manager's name can be at most {PortfolioRules.MaxNameLength} characters.";
        if (SowRules.SignatoryList(Signatories).Count == 0) return "Name at least one Kenco company that signs.";
        if (SowRules.SignatoryList(Signatories).Any(s => s.Length > PortfolioRules.MaxNameLength)) return $"A signatory can be at most {PortfolioRules.MaxNameLength} characters.";
        foreach (var t in new[] { Signatories, CostFootnote, Agreement, DefaultScope, DefaultAssumptions, DefaultSchedule })
            if (t?.Trim().Length > SowRules.MaxTextLength) return $"Each text can be at most {SowRules.MaxTextLength} characters.";
        return null;
    }

    public void ApplyTo(SowSettings s)
    {
        s.ExpirationDays = ExpirationDays;
        s.DefaultManager = DraftText.Clean(DefaultManager);
        s.Signatories = string.Join("\n", SowRules.SignatoryList(Signatories));
        s.CostFootnote = DraftText.Clean(CostFootnote);
        s.Agreement = DraftText.Clean(Agreement);
        s.DefaultScope = DraftText.Clean(DefaultScope);
        s.DefaultAssumptions = DraftText.Clean(DefaultAssumptions);
        s.DefaultSchedule = DraftText.Clean(DefaultSchedule);
    }
}
