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

        // The whole application portfolio as a workbook, one sheet per kind of record.
        app.MapGet("/export/portfolio.xlsx", async (Roadmapper.Data.Portfolio.PortfolioService svc) =>
            Results.File(await Roadmapper.Data.Portfolio.PortfolioExport.ToXlsxAsync(svc),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"portfolio-{svc.Today:yyyy-MM-dd}.xlsx"));

        // The whole portfolio as a portfolio file (.rpf), for backups and moving to another computer.
        app.MapGet("/export/portfolio.rpf", async (Roadmapper.Data.Portfolio.PortfolioService svc) =>
            Results.File((await svc.ExportPortfolioAsync(typeof(ExportEndpoints).Assembly.GetName().Version?.ToString(3))).ToBytes(),
                PortfolioFile.ContentType, $"portfolio-{svc.Today:yyyy-MM-dd}{PortfolioFile.Extension}"));

        // A document's copy on this PC (the synced folder), for when SharePoint's address isn't set or you're offline.
        // PDFs, images and text open in the browser; anything else downloads.
        app.MapGet("/documents/{id:int}/file", async (int id, Roadmapper.Data.Portfolio.PortfolioService svc) =>
        {
            if (await svc.GetDocumentFileAsync(id) is not { } path) return Results.NotFound("That file isn't on this PC any more. Open it from SharePoint instead.");
            if (!new Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider().TryGetContentType(path, out var type)) type = "application/octet-stream";
            var inline = type.StartsWith("image/") || type is "application/pdf" or "text/plain";
            return Results.File(path, type, inline ? null : Path.GetFileName(path), enableRangeProcessing: true);
        });

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

        // The whole product as a Roadmapper file, for backups and sharing; opened from the Products page.
        export.MapGet("/roadmap.rmp", async (int productId, RoadmapService svc) =>
            await svc.ExportFileAsync(productId) is { } file
                ? Results.File(file.ToBytes(), RoadmapFile.ContentType, FileName(file.Product.Name, "rmp"))
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
    public static string FileName(ProductRoadmap r, string extension, string kind = "roadmap") => FileName(r.Product.Name, extension, kind);

    public static string FileName(string productName, string extension, string kind = "roadmap")
    {
        var name = string.Concat(productName.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '-' : c)).Trim();
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
