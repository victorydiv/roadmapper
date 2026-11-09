namespace Roadmapper.Core.Portfolio;

// Detached, editable copies of portfolio records, so the UI never binds to tracked entities (same idea as ItemDraft).
// Validate() returns a user-facing message or null; ApplyTo() copies cleaned values onto an entity.

internal static class DraftText
{
    public static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

public class ContactDraft
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Title { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public int? VendorId { get; set; }
    public string? Department { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;

    public bool IsNew => Id == 0;

    public static ContactDraft From(Contact c) => new()
    {
        Id = c.Id, Name = c.Name, Title = c.Title, Email = c.Email, Phone = c.Phone, VendorId = c.VendorId,
        Department = c.Department, Notes = c.Notes, IsActive = c.IsActive,
    };

    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Name)) return "Name is required.";
        if (Name.Trim().Length > PortfolioRules.MaxNameLength) return $"Name can be at most {PortfolioRules.MaxNameLength} characters.";
        if (DraftText.Clean(Email) is { } e && (!e.Contains('@') || e.Contains(' '))) return "That email address doesn't look right.";
        return null;
    }

    public void ApplyTo(Contact c)
    {
        c.Name = Name.Trim();
        c.Title = DraftText.Clean(Title);
        c.Email = DraftText.Clean(Email);
        c.Phone = DraftText.Clean(Phone);
        c.VendorId = VendorId;
        // Department describes internal staff only.
        c.Department = VendorId is null ? DraftText.Clean(Department) : null;
        c.Notes = DraftText.Clean(Notes);
        c.IsActive = IsActive;
    }
}

public class VendorDraft
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Website { get; set; }
    public int? AccountManagerId { get; set; }
    public int? TechnicalContactId { get; set; }
    public string? SupportPortalUrl { get; set; }
    public string? SupportPhone { get; set; }
    public string? Notes { get; set; }

    public bool IsNew => Id == 0;

    public static VendorDraft From(Vendor v) => new()
    {
        Id = v.Id, Name = v.Name, Website = v.Website, AccountManagerId = v.AccountManagerId,
        TechnicalContactId = v.TechnicalContactId, SupportPortalUrl = v.SupportPortalUrl, SupportPhone = v.SupportPhone, Notes = v.Notes,
    };

    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Name)) return "Vendor name is required.";
        if (Name.Trim().Length > PortfolioRules.MaxNameLength) return $"Name can be at most {PortfolioRules.MaxNameLength} characters.";
        return null;
    }

    public void ApplyTo(Vendor v)
    {
        v.Name = Name.Trim();
        v.Website = DraftText.Clean(Website);
        v.AccountManagerId = AccountManagerId;
        v.TechnicalContactId = TechnicalContactId;
        v.SupportPortalUrl = DraftText.Clean(SupportPortalUrl);
        v.SupportPhone = DraftText.Clean(SupportPhone);
        v.Notes = DraftText.Clean(Notes);
    }
}

public class ContractDraft
{
    public int Id { get; set; }
    public int VendorId { get; set; }
    public string Name { get; set; } = "";
    public string? ContractNumber { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public DateOnly? RenewalDate { get; set; }
    public int? NoticePeriodDays { get; set; }
    public bool AutoRenews { get; set; }
    public decimal? AnnualValue { get; set; }
    public string? Notes { get; set; }

    public bool IsNew => Id == 0;

    public static ContractDraft New(int vendorId) => new() { VendorId = vendorId };

    public static ContractDraft From(Contract c) => new()
    {
        Id = c.Id, VendorId = c.VendorId, Name = c.Name, ContractNumber = c.ContractNumber, StartDate = c.StartDate,
        EndDate = c.EndDate, RenewalDate = c.RenewalDate, NoticePeriodDays = c.NoticePeriodDays, AutoRenews = c.AutoRenews,
        AnnualValue = c.AnnualValue, Notes = c.Notes,
    };

    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Name)) return "Contract name is required.";
        if (StartDate is { } s && EndDate is { } e && e < s) return "End date can't be before the start date.";
        if (NoticePeriodDays is < 0) return "Notice period can't be negative.";
        if (AnnualValue is < 0) return "Annual value can't be negative.";
        return null;
    }

    public void ApplyTo(Contract c)
    {
        c.Name = Name.Trim();
        c.ContractNumber = DraftText.Clean(ContractNumber);
        c.StartDate = StartDate;
        c.EndDate = EndDate;
        c.RenewalDate = RenewalDate;
        c.NoticePeriodDays = NoticePeriodDays;
        c.AutoRenews = AutoRenews;
        c.AnnualValue = AnnualValue;
        c.Notes = DraftText.Clean(Notes);
    }
}

public class ApplicationDraft
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public AppCategory Category { get; set; } = AppCategory.Other;
    public int? VendorId { get; set; }
    public int? ContractId { get; set; }
    public int? ProductId { get; set; }
    public int? BusinessOwnerId { get; set; }
    public int? TechnicalOwnerId { get; set; }
    public Criticality Criticality { get; set; } = Criticality.Tier2;
    public LifecycleStatus Status { get; set; } = LifecycleStatus.Active;
    public Hosting Hosting { get; set; } = Hosting.SaaS;
    public decimal? AnnualCost { get; set; }
    public int? BusinessFit { get; set; }
    public int? TechnicalFit { get; set; }
    public DateOnly? GoLiveDate { get; set; }
    public DateOnly? RetireDate { get; set; }
    public string? Notes { get; set; }

    public bool IsNew => Id == 0;

    public static ApplicationDraft From(Application a) => new()
    {
        Id = a.Id, Name = a.Name, Description = a.Description, Category = a.Category, VendorId = a.VendorId,
        ContractId = a.ContractId, ProductId = a.ProductId, BusinessOwnerId = a.BusinessOwnerId, TechnicalOwnerId = a.TechnicalOwnerId,
        Criticality = a.Criticality, Status = a.Status, Hosting = a.Hosting, AnnualCost = a.AnnualCost,
        BusinessFit = a.BusinessFit, TechnicalFit = a.TechnicalFit, GoLiveDate = a.GoLiveDate, RetireDate = a.RetireDate, Notes = a.Notes,
    };

    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Name)) return "Application name is required.";
        if (Name.Trim().Length > PortfolioRules.MaxNameLength) return $"Name can be at most {PortfolioRules.MaxNameLength} characters.";
        if (BusinessFit is < 1 or > 5 || TechnicalFit is < 1 or > 5) return "Fit scores go from 1 to 5.";
        if (AnnualCost is < 0) return "Annual cost can't be negative.";
        if (GoLiveDate is { } g && RetireDate is { } r && r < g) return "Retire date can't be before the go-live date.";
        if (ContractId is not null && VendorId is null) return "Choose the vendor before choosing a contract.";
        return null;
    }

    public void ApplyTo(Application a)
    {
        a.Name = Name.Trim();
        a.Description = DraftText.Clean(Description);
        a.Category = Category;
        a.VendorId = VendorId;
        a.ContractId = VendorId is null ? null : ContractId;
        a.ProductId = ProductId;
        a.BusinessOwnerId = BusinessOwnerId;
        a.TechnicalOwnerId = TechnicalOwnerId;
        a.Criticality = Criticality;
        a.Status = Status;
        a.Hosting = Hosting;
        a.AnnualCost = AnnualCost;
        a.BusinessFit = BusinessFit;
        a.TechnicalFit = TechnicalFit;
        a.GoLiveDate = GoLiveDate;
        a.RetireDate = RetireDate;
        a.Notes = DraftText.Clean(Notes);
    }
}

public class IntegrationDraft
{
    public int Id { get; set; }
    /// <summary>Optional: when blank the service names it "Source → Target".</summary>
    public string? Name { get; set; }
    public int? SourceApplicationId { get; set; }
    public int? TargetApplicationId { get; set; }
    public int? ViaApplicationId { get; set; }
    public IntegrationType Type { get; set; } = IntegrationType.API;
    public FlowDirection Direction { get; set; } = FlowDirection.OneWay;
    public IntegrationFrequency Frequency { get; set; } = IntegrationFrequency.RealTime;
    public string? MessageFormat { get; set; }
    public string BusinessPurpose { get; set; } = "";
    public Criticality Criticality { get; set; } = Criticality.Tier2;
    public int? OwnerId { get; set; }
    public LifecycleStatus Status { get; set; } = LifecycleStatus.Active;
    public DateOnly? LastReviewDate { get; set; }
    public int ReviewIntervalMonths { get; set; } = 12;
    public string? Notes { get; set; }

    public bool IsNew => Id == 0;

    public static IntegrationDraft From(Integration i) => new()
    {
        Id = i.Id, Name = i.Name, SourceApplicationId = i.SourceApplicationId, TargetApplicationId = i.TargetApplicationId,
        ViaApplicationId = i.ViaApplicationId, Type = i.Type, Direction = i.Direction, Frequency = i.Frequency,
        MessageFormat = i.MessageFormat, BusinessPurpose = i.BusinessPurpose, Criticality = i.Criticality, OwnerId = i.OwnerId,
        Status = i.Status, LastReviewDate = i.LastReviewDate, ReviewIntervalMonths = i.ReviewIntervalMonths, Notes = i.Notes,
    };

    public string? Validate()
    {
        if (SourceApplicationId is null || TargetApplicationId is null) return "Choose both the source and the target application.";
        if (SourceApplicationId == TargetApplicationId) return "Source and target must be different applications.";
        if (ViaApplicationId is { } via && (via == SourceApplicationId || via == TargetApplicationId))
            return "The “via” application must be different from the source and target.";
        if (string.IsNullOrWhiteSpace(BusinessPurpose)) return "Describe the business purpose: what the flow is for.";
        if (Name?.Trim().Length > PortfolioRules.MaxNameLength) return $"Name can be at most {PortfolioRules.MaxNameLength} characters.";
        if (ReviewIntervalMonths is < 1 or > 60) return "Review interval must be between 1 and 60 months.";
        return null;
    }

    public void ApplyTo(Integration i, string name)
    {
        i.Name = name;
        i.SourceApplicationId = SourceApplicationId!.Value;
        i.TargetApplicationId = TargetApplicationId!.Value;
        i.ViaApplicationId = ViaApplicationId;
        i.Type = Type;
        i.Direction = Direction;
        i.Frequency = Frequency;
        i.MessageFormat = DraftText.Clean(MessageFormat);
        i.BusinessPurpose = BusinessPurpose.Trim();
        i.Criticality = Criticality;
        i.OwnerId = OwnerId;
        i.Status = Status;
        i.LastReviewDate = LastReviewDate;
        i.ReviewIntervalMonths = ReviewIntervalMonths;
        i.Notes = DraftText.Clean(Notes);
    }
}

public class RiskDraft
{
    public int Id { get; set; }
    public int IntegrationId { get; set; }
    public string Description { get; set; } = "";
    public int Likelihood { get; set; } = 3;
    public int Impact { get; set; } = 3;
    public string? Mitigation { get; set; }
    public RiskStatus Status { get; set; } = RiskStatus.Open;
    public int? OwnerId { get; set; }
    public DateOnly? RaisedOn { get; set; }

    public bool IsNew => Id == 0;
    public int Score => Likelihood * Impact;

    public static RiskDraft New(int integrationId, DateOnly today) => new() { IntegrationId = integrationId, RaisedOn = today };

    public static RiskDraft From(IntegrationRisk r) => new()
    {
        Id = r.Id, IntegrationId = r.IntegrationId, Description = r.Description, Likelihood = r.Likelihood, Impact = r.Impact,
        Mitigation = r.Mitigation, Status = r.Status, OwnerId = r.OwnerId, RaisedOn = r.RaisedOn,
    };

    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Description)) return "Describe the risk.";
        if (Description.Trim().Length > 1000) return "Description can be at most 1000 characters.";
        if (Likelihood is < 1 or > 5 || Impact is < 1 or > 5) return "Likelihood and impact go from 1 to 5.";
        return null;
    }

    public void ApplyTo(IntegrationRisk r)
    {
        r.Description = Description.Trim();
        r.Likelihood = Likelihood;
        r.Impact = Impact;
        r.Mitigation = DraftText.Clean(Mitigation);
        r.Status = Status;
        r.OwnerId = OwnerId;
        r.RaisedOn = RaisedOn;
    }
}
