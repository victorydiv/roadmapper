using ClosedXML.Excel;
using Roadmapper.Core.Portfolio;

namespace Roadmapper.Data.Portfolio;

/// <summary>The whole portfolio as a workbook, one sheet per kind of record, for Excel and Power BI.</summary>
public static class PortfolioExport
{
    public static async Task<byte[]> ToXlsxAsync(PortfolioService svc)
    {
        var today = svc.Today;
        var settings = await svc.GetSettingsAsync();
        var health = await svc.GetHealthAsync();
        var apps = await svc.GetApplicationsAsync();
        var vendors = await svc.GetVendorsAsync();
        var partners = await svc.GetPartnersAsync();
        var integrations = await svc.GetIntegrationsAsync();
        var processes = await svc.GetProcessesAsync();
        var benefits = await svc.GetBenefitsAsync();
        var decisions = await svc.GetDecisionsAsync();
        var contacts = await svc.GetContactsAsync();

        using var wb = new XLWorkbook();

        Sheet(wb, "Applications",
            ["Id", "Name", "Category", "Tier", "Status", "Hosting", "Vendor", "Contract", "Renewal", "Notice deadline", "Annual cost",
             "Business owner", "Technical owner", "Business fit", "Technical fit", "TIME", "Health", "Health score", "Health issues", "Roadmap", "Description"],
            apps.Select(a =>
            {
                var h = health.GetValueOrDefault(a.Id);
                return new object?[]
                {
                    a.Id, a.Name, PortfolioText.Label(a.Category), PortfolioText.Label(a.Criticality), PortfolioText.Label(a.Status),
                    PortfolioText.Label(a.Hosting), a.Vendor?.Name, a.Contract?.Name, a.RenewalDate, a.NoticeDeadline, a.AnnualCost,
                    a.BusinessOwner?.Name, a.TechnicalOwner?.Name, a.BusinessFit, a.TechnicalFit, a.TimeQuadrant?.ToString(),
                    h?.Rating.ToString(), h?.Score, h?.Summary, a.Product?.Name, a.Description,
                };
            }));

        Sheet(wb, "Vendors",
            ["Id", "Name", "Website", "Account manager", "Technical contact", "Support phone", "Support portal", "Applications"],
            vendors.Select(v => new object?[]
            {
                v.Id, v.Name, v.Website, v.AccountManager?.Name, v.TechnicalContact?.Name, v.SupportPhone, v.SupportPortalUrl,
                string.Join("; ", v.Applications.Select(a => a.Name).Order()),
            }));

        Sheet(wb, "Contracts",
            ["Id", "Vendor", "Name", "Number", "Start", "End", "Renewal", "Notice days", "Notice deadline", "Auto-renews", "Annual value"],
            vendors.SelectMany(v => v.Contracts.Select(c => new object?[]
            {
                c.Id, v.Name, c.Name, c.ContractNumber, c.StartDate, c.EndDate, c.RenewalDate, c.NoticePeriodDays, c.NoticeDeadline,
                c.AutoRenews ? "Yes" : "No", c.AnnualValue,
            })));

        Sheet(wb, "Trading partners",
            ["Id", "Name", "Type", "Status", "Importance", "Account code", "EDI ID", "GS ID", "Relationship owner"],
            partners.Select(p => new object?[]
            {
                p.Id, p.Name, PortfolioText.Label(p.Type), PortfolioText.Label(p.Status), PortfolioText.Label(p.Importance),
                p.AccountCode, p.EdiAddress, p.GsId, p.RelationshipOwner?.Name,
            }));

        Sheet(wb, "Integrations",
            ["Id", "Name", "Source", "Via", "Target", "Type", "Direction", "Frequency", "Format", "Tier", "Status", "Owner",
             "Last review", "Next review", "Review overdue", "Risk level", "Top risk score", "Business purpose"],
            integrations.Select(i => new object?[]
            {
                i.Id, i.Name, i.SourceName, i.Via?.Name, i.TargetName, PortfolioText.Label(i.Type), PortfolioText.Label(i.Direction),
                PortfolioText.Label(i.Frequency), i.MessageFormat, PortfolioText.Label(i.Criticality), PortfolioText.Label(i.Status),
                i.Owner?.Name, i.LastReviewDate, i.NextReviewDate, PortfolioRules.ReviewOverdue(i, today) ? "Yes" : "No",
                PortfolioRules.RiskLevel(i, today).ToString(), PortfolioRules.TopRiskScore(i), i.BusinessPurpose,
            }));

        Sheet(wb, "Risks",
            ["Integration", "Risk", "Likelihood", "Impact", "Score", "Status", "Mitigation", "Owner", "Raised on"],
            integrations.SelectMany(i => i.Risks.OrderByDescending(r => r.Score).Select(r => new object?[]
            {
                i.Name, r.Description, r.Likelihood, r.Impact, r.Score, PortfolioText.Label(r.Status), r.Mitigation, r.Owner?.Name, r.RaisedOn,
            })));

        Sheet(wb, "Processes",
            ["Id", "Name", "Part of", "Domain", "Owner", "Applications", "Integrations", "Open pain points", "Description"],
            processes.Select(p => new object?[]
            {
                p.Id, p.Name, p.ParentProcess?.Name, PortfolioText.Label(p.Domain), p.ProcessOwner?.Name,
                string.Join("; ", p.Applications.Select(a => a.Application?.Name + (a.IsPrimary ? " (primary)" : ""))),
                string.Join("; ", p.Integrations.Select(i => i.Integration?.Name)),
                p.PainPoints.Count(pp => pp.Status == PainPointStatus.Open), p.Description,
            }));

        var appNames = apps.ToDictionary(a => a.Id, a => a.Name);
        Sheet(wb, "Pain points",
            ["Process", "Pain point", "Severity", "Status", "Application"],
            processes.SelectMany(p => p.PainPoints.Select(pp => new object?[]
            {
                p.Name, pp.Description, pp.Severity.ToString(), PortfolioText.Label(pp.Status),
                pp.ApplicationId is { } aid ? appNames.GetValueOrDefault(aid) : null,
            })));

        Sheet(wb, "KPIs",
            ["Process", "KPI", "Unit", "Direction", "Baseline", "Target", "Current", "Measured on", "Progress %", "On target"],
            processes.SelectMany(p => p.Kpis.Select(k => new object?[]
            {
                p.Name, k.Name, k.Unit, PortfolioText.Label(k.Direction), k.Baseline, k.Target, k.Current, k.MeasuredOn, k.ProgressPct,
                k.OnTarget switch { true => "Yes", false => "No", _ => null },
            })));

        Sheet(wb, "Benefits",
            ["Id", "Roadmap", "Delivered by", "Benefit", "Category", "Unit", "Application", "Baseline", "Expected", "Expected by",
             "Actual", "Measured on", "Realized %", "Status", "Expected $/yr", "Realized $/yr"],
            benefits.Select(b => new object?[]
            {
                b.Id, b.RoadmapItem?.Product?.Name, b.RoadmapItem?.Title, b.Description, PortfolioText.Label(b.Category), b.Unit.ToString(),
                b.Application?.Name, BenefitMath.Baseline(b), b.ExpectedValue, b.ExpectedBy, BenefitMath.Actual(b), BenefitMath.ActualOn(b),
                BenefitMath.RealizationPct(b), PortfolioText.Label(BenefitMath.Status(b, today)),
                BenefitMath.ExpectedUsd(b, settings.LaborRate), BenefitMath.RealizedUsd(b, settings.LaborRate),
            }));

        Sheet(wb, "Decisions",
            ["Id", "Decided on", "Title", "Status", "Decided by", "Decision", "Context", "Outcome", "Alternatives", "Linked to", "Superseded by"],
            decisions.Select(d => new object?[]
            {
                d.Id, d.DecidedOn, d.Title, PortfolioText.Label(d.Status), d.DecidedBy, d.DecisionText, d.Context, d.Outcome,
                string.Join("; ", d.Alternatives.OrderBy(a => a.SortOrder).Select(a => a.Option)),
                string.Join("; ", d.Links.Select(l => $"{PortfolioText.Label(l.Target.Kind)}: {l.TargetName}")),
                d.SupersededBy?.Title,
            }));

        Sheet(wb, "Documents",
            ["Id", "Type", "Title", "Date", "Product", "Attached to", "Name", "SharePoint link", "File", "Notes"],
            (await svc.GetDocumentsAsync()).Select(d => new object?[]
            {
                d.Id, PortfolioText.Label(d.Category), d.Title, d.DocumentDate, d.Product?.Name ?? "Shared", d.Target is { } t ? PortfolioText.Label(t.Kind) : null,
                d.TargetName, d.Url, d.FileName, d.Notes,
            }));

        var features = (await svc.GetCapabilityOverviewAsync()).Where(o => o.Features > 0).Select(o => o.Category).ToList();
        var featureRows = new List<object?[]>();
        foreach (var c in features)
        {
            var m = await svc.GetCapabilityMatrixAsync(c, includeRetired: true);
            foreach (var f in m.Features)
                foreach (var a in m.Apps)
                {
                    var r = m.Cell(f.Id, a.Id);
                    featureRows.Add([PortfolioText.Label(c), f.Name, a.Name, a.Added ? "Yes" : "", r?.Availability is { } av ? PortfolioText.Label(av) : null,
                        r?.Strength is { } st ? PortfolioText.Label(st) : null, r?.Note]);
                }
        }
        Sheet(wb, "Features",
            ["Category", "Feature", "Application", "Added from another category", "Has it", "Strength", "Note"], featureRows);

        Sheet(wb, "Contacts",
            ["Id", "Name", "Title", "Company", "Department", "Email", "Phone", "Active"],
            contacts.Select(c => new object?[]
            {
                c.Id, c.Name, c.Title, c.CompanyName ?? "Internal", c.Department, c.Email, c.Phone, c.IsActive ? "Yes" : "No",
            }));

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private static void Sheet(XLWorkbook wb, string name, string[] headers, IEnumerable<object?[]> rows)
    {
        var ws = wb.AddWorksheet(name);
        for (var c = 0; c < headers.Length; c++) ws.Cell(1, c + 1).Value = headers[c];
        var r = 2;
        foreach (var row in rows)
        {
            for (var c = 0; c < row.Length; c++)
            {
                var cell = ws.Cell(r, c + 1);
                switch (row[c])
                {
                    case null: break;
                    case DateOnly d: cell.Value = d.ToDateTime(TimeOnly.MinValue); cell.Style.DateFormat.Format = "yyyy-mm-dd"; break;
                    case decimal m: cell.Value = m; break;
                    case int n: cell.Value = n; break;
                    case string s: cell.Value = s.Length > 0 && "=+-@".Contains(s[0]) ? "'" + s : s; break;
                    default: cell.Value = row[c]!.ToString(); break;
                }
            }
            r++;
        }
        var header = ws.Row(1);
        header.Style.Font.Bold = true;
        header.Style.Fill.BackgroundColor = XLColor.FromHtml("#f1f3f6");
        ws.SheetView.FreezeRows(1);
        if (r > 2) ws.Range(1, 1, r - 1, headers.Length).SetAutoFilter();
        ws.Columns().AdjustToContents(1, Math.Min(r, 200), 8, 60);
    }
}
