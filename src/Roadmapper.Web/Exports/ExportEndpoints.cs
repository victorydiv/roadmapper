using System.Text;
using Roadmapper.Core;
using Roadmapper.Core.Exchange;
using Roadmapper.Data;
using Roadmapper.Data.Exchange;

namespace Roadmapper.Web.Exports;

/// <summary>File downloads as plain GET endpoints, so the UI can use ordinary links.</summary>
public static class ExportEndpoints
{
    public static void MapExportEndpoints(this WebApplication app)
    {
        app.MapGet("/export/template.csv", () =>
            Results.File(Encoding.UTF8.GetBytes(TemplateCsv()), "text/csv", "roadmap-import-template.csv"));

        var export = app.MapGroup("/export/{productId:int}");

        export.MapGet("/roadmap.csv", async (int productId, RoadmapService svc) =>
            await svc.GetRoadmapAsync(productId) is { } r
                ? Results.File(Encoding.UTF8.GetBytes(RoadmapExport.ToCsv(r)), "text/csv", FileName(r, "csv"))
                : Results.NotFound());

        export.MapGet("/roadmap.xlsx", async (int productId, RoadmapService svc) =>
            await svc.GetRoadmapAsync(productId) is { } r
                ? Results.File(RoadmapExport.ToXlsx(r, await svc.GetTemplatesAsync(productId)),
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", FileName(r, "xlsx"))
                : Results.NotFound());

        // Served inline so it opens in the browser's PDF viewer; save or print from there.
        // depth and baseline match the Timeline tab's pickers so its PDF button prints the Gantt as shown.
        export.MapGet("/roadmap.pdf", async (int productId, RoadmapService svc, HttpContext http, ItemType? depth, int? baseline) =>
        {
            if (await svc.GetRoadmapAsync(productId) is not { } r) return Results.NotFound();
            var snapshot = baseline is { } id ? await svc.GetSnapshotAsync(id) : null;
            if (snapshot is { } s && s.Snapshot.ProductId != productId) snapshot = null;
            var gantt = new GanttOptions(depth ?? ItemType.Task, snapshot?.Items.ToDictionary(i => i.Id), snapshot?.Snapshot.Name);
            http.Response.Headers.ContentDisposition = $"inline; filename=\"{FileName(r, "pdf")}\"";
            return Results.Bytes(RoadmapPdf.Render(r, DateTime.Now, gantt), "application/pdf");
        });

        export.MapGet("/timeline.svg", async (int productId, RoadmapService svc, string? scale, ItemType? depth, int? baseline, bool? download) =>
        {
            if (await svc.GetRoadmapAsync(productId) is not { } r) return Results.NotFound();
            var snapshot = baseline is { } id ? await svc.GetSnapshotAsync(id) : null;
            if (snapshot is { } s && s.Snapshot.ProductId != productId) snapshot = null;
            var svg = TimelineSvg.Render(r, scale == "quarter", depth ?? ItemType.Task, DateOnly.FromDateTime(DateTime.Today),
                snapshot?.Items.ToDictionary(i => i.Id), snapshot?.Snapshot.Name);
            var bytes = Encoding.UTF8.GetBytes(svg);
            return download == true
                ? Results.File(bytes, "image/svg+xml", FileName(r, "svg", "timeline"))
                : Results.Bytes(bytes, "image/svg+xml");
        });
    }

    /// <summary>e.g. "Customer Portal roadmap 2026-11-04.xlsx", with characters Windows rejects replaced.</summary>
    public static string FileName(ProductRoadmap r, string extension, string kind = "roadmap")
    {
        var name = string.Concat(r.Product.Name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '-' : c)).Trim();
        return $"{name} {kind} {DateTime.Today:yyyy-MM-dd}.{extension}";
    }

    private static string TemplateCsv() => "﻿" + Csv.Write(new[]
    {
        RoadmapColumns.Importable,
        Row(("Type", "Goal"), ("Title", "Grow self-serve revenue"), ("Description", "Outcome we're aiming for this year")),
        Row(("Type", "Initiative"), ("Title", "Checkout redesign"), ("Parent", "Grow self-serve revenue"), ("Horizon", "Now"), ("Theme", "Growth")),
        Row(("Type", "Feature"), ("Title", "One-page checkout"), ("Parent", "Grow self-serve revenue › Checkout redesign"),
            ("Status", "Planned"), ("Start", "2026-01-05"), ("End", "2026-02-27"), ("Horizon", "Now"), ("Owner", "Product"),
            ("Theme", "Growth"), ("Tags", "ux; revenue"), ("Reach", "500"), ("Impact", "2"), ("Confidence", "80"), ("Effort", "2")),
        Row(("Title", "Build payment form"), ("Parent", "One-page checkout"), ("Status", "Idea"),
            ("Start", "2026-01-12"), ("End", "2026-02-06"), ("Owner", "Dev")),
    });

    /// <summary>A template row; unspecified columns are blank. (The last row leaves Type blank to show inference.)</summary>
    private static string[] Row(params (string Column, string Value)[] cells) =>
        RoadmapColumns.Importable.Select(c => cells.FirstOrDefault(x => x.Column == c).Value ?? "").ToArray();
}
