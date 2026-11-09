using Roadmapper.Core;
using Roadmapper.Core.Portfolio;
using Roadmapper.Data.Portfolio;

namespace Roadmapper.Web.Api;

/// <summary>
/// Read-only JSON over the portfolio, for Power BI, Excel (Get Data › From Web) and scripts. Every endpoint is a GET
/// that changes nothing, so an unauthenticated localhost app can serve it safely. The UI doesn't use it; Razor pages
/// call <see cref="PortfolioService"/> directly.
/// </summary>
public static class PortfolioApi
{
    public static void MapPortfolioApi(this WebApplication app)
    {
        var api = app.MapGroup("/api/portfolio");

        // ?status=Active&category=WMS&tier=Tier1 (or 1). Unknown values are ignored rather than rejected.
        api.MapGet("/applications", async (PortfolioService svc, string? status, string? category, string? tier) =>
        {
            IEnumerable<Application> apps = await svc.GetApplicationsAsync();
            if (Parse<LifecycleStatus>(status) is { } s) apps = apps.Where(a => a.Status == s);
            if (Parse<AppCategory>(category) is { } c) apps = apps.Where(a => a.Category == c);
            if (ParseTier(tier) is { } t) apps = apps.Where(a => a.Criticality == t);
            return apps.Select(ApplicationDto.From).ToList();
        });

        api.MapGet("/applications/{id:int}", async (int id, PortfolioService svc) =>
            await svc.GetApplicationAsync(id) is { } a
                ? Results.Ok(ApplicationDetailDto.From(a, await svc.GetLinkedItemsAsync(id)))
                : Results.NotFound());

        api.MapGet("/vendors", async (PortfolioService svc) =>
            (await svc.GetVendorsAsync()).Select(v => VendorDto.From(v, svc.Today)).ToList());

        api.MapGet("/vendors/{id:int}", async (int id, PortfolioService svc) =>
            await svc.GetVendorAsync(id) is { } v ? Results.Ok(VendorDetailDto.From(v, svc.Today)) : Results.NotFound());

        // Contracts whose notice deadline is within N days (default: the portfolio's renewal warning setting).
        api.MapGet("/contracts", async (PortfolioService svc, int? renewingWithinDays) =>
            (await svc.GetRenewalsAsync(renewingWithinDays)).Select(r => new RenewalDto(ContractDto.From(r.Contract), r.VendorName, r.Applications)).ToList());

        api.MapGet("/contacts", async (PortfolioService svc) =>
            (await svc.GetContactsAsync()).Select(ContactDto.From).ToList());
    }

    private static T? Parse<T>(string? value) where T : struct, Enum =>
        Enum.TryParse<T>(value, ignoreCase: true, out var v) && Enum.IsDefined(v) ? v : null;

    private static Criticality? ParseTier(string? value) =>
        int.TryParse(value, out var n) && Enum.IsDefined((Criticality)n) ? (Criticality)n : Parse<Criticality>(value);
}

public record Ref(int Id, string Name)
{
    public static Ref? Of(Contact? c) => c is null ? null : new(c.Id, c.Name);
    public static Ref? Of(Vendor? v) => v is null ? null : new(v.Id, v.Name);
    public static Ref? Of(Product? p) => p is null ? null : new(p.Id, p.Name);
}

public record ContractDto(int Id, int VendorId, string Name, string? ContractNumber, DateOnly? StartDate, DateOnly? EndDate,
    DateOnly? RenewalDate, int? NoticePeriodDays, DateOnly? NoticeDeadline, bool AutoRenews, decimal? AnnualValue)
{
    public static ContractDto? FromOrNull(Contract? c) => c is null ? null : From(c);

    public static ContractDto From(Contract c) => new(c.Id, c.VendorId, c.Name, c.ContractNumber, c.StartDate, c.EndDate,
        c.RenewalDate, c.NoticePeriodDays, c.NoticeDeadline, c.AutoRenews, c.AnnualValue);
}

public record ApplicationDto(int Id, string Name, string? Description, AppCategory Category, Criticality Criticality,
    LifecycleStatus Status, Hosting Hosting, Ref? Vendor, ContractDto? Contract, DateOnly? RenewalDate, DateOnly? NoticeDeadline,
    Ref? BusinessOwner, Ref? TechnicalOwner, Ref? Roadmap, decimal? AnnualCost, int? BusinessFit, int? TechnicalFit,
    TimeQuadrant? TimeQuadrant, DateOnly? GoLiveDate, DateOnly? RetireDate, int LinkedItemCount)
{
    public static ApplicationDto From(Application a) => new(a.Id, a.Name, a.Description, a.Category, a.Criticality, a.Status, a.Hosting,
        Ref.Of(a.Vendor), ContractDto.FromOrNull(a.Contract), a.RenewalDate, a.NoticeDeadline, Ref.Of(a.BusinessOwner), Ref.Of(a.TechnicalOwner),
        Ref.Of(a.Product), a.AnnualCost, a.BusinessFit, a.TechnicalFit, a.TimeQuadrant, a.GoLiveDate, a.RetireDate, a.Initiatives.Count);
}

public record SupportContactDto(Ref Contact, SupportRole Role, string? Note);

public record ApplicationDetailDto(ApplicationDto Application, string? Notes, IReadOnlyList<SupportContactDto> SupportContacts,
    IReadOnlyList<LinkedItem> LinkedItems)
{
    public static ApplicationDetailDto From(Application a, IReadOnlyList<LinkedItem> items) =>
        new(ApplicationDto.From(a) with { LinkedItemCount = items.Count }, a.Notes,
            a.SupportContacts.Select(s => new SupportContactDto(Ref.Of(s.Contact)!, s.Role, s.Note)).ToList(), items);
}

public record VendorDto(int Id, string Name, string? Website, Ref? AccountManager, Ref? TechnicalContact,
    DateOnly? ContractStart, DateOnly? ContractEnd, ContractDto? NextRenewal, decimal? AnnualContractValue, IReadOnlyList<Ref> Applications)
{
    public static VendorDto From(Vendor v, DateOnly today) => new(v.Id, v.Name, v.Website, Ref.Of(v.AccountManager), Ref.Of(v.TechnicalContact),
        PortfolioRules.ContractStart(v.Contracts), PortfolioRules.ContractEnd(v.Contracts),
        ContractDto.FromOrNull(PortfolioRules.NextRenewal(v.Contracts, today)),
        v.Contracts.Any(c => c.AnnualValue is not null) ? v.Contracts.Sum(c => c.AnnualValue ?? 0) : null,
        v.Applications.OrderBy(a => a.Name).Select(a => new Ref(a.Id, a.Name)).ToList());
}

public record EscalationDto(int Level, Ref Contact, string? Title, string? Email, string? Phone, string? Note);

public record VendorDetailDto(VendorDto Vendor, string? SupportPortalUrl, string? SupportPhone, string? Notes,
    IReadOnlyList<ContractDto> Contracts, IReadOnlyList<EscalationDto> EscalationPath, IReadOnlyList<ContactDto> Staff)
{
    public static VendorDetailDto From(Vendor v, DateOnly today) => new(VendorDto.From(v, today), v.SupportPortalUrl, v.SupportPhone, v.Notes,
        v.Contracts.Select(ContractDto.From).ToList(),
        v.EscalationContacts.OrderBy(x => x.Level)
            .Select(x => new EscalationDto(x.Level, Ref.Of(x.Contact)!, x.Contact?.Title, x.Contact?.Email, x.Contact?.Phone, x.Note)).ToList(),
        v.Staff.Select(ContactDto.From).ToList());
}

public record ContactDto(int Id, string Name, string? Title, string? Email, string? Phone, Ref? Vendor, string? Department, bool IsActive)
{
    public static ContactDto From(Contact c) => new(c.Id, c.Name, c.Title, c.Email, c.Phone, Ref.Of(c.Vendor), c.Department, c.IsActive);
}

public record RenewalDto(ContractDto Contract, string VendorName, IReadOnlyList<string> Applications);
