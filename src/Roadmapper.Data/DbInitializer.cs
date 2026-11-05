using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Roadmapper.Core;

namespace Roadmapper.Data;

public static class DbInitializer
{
    /// <summary>Applies pending migrations and, on a brand-new database, adds a sample product to explore.</summary>
    public static async Task InitializeAsync(IServiceProvider services)
    {
        var factory = services.GetRequiredService<IDbContextFactory<RoadmapDbContext>>();
        await using (var db = await factory.CreateDbContextAsync())
        {
            await db.Database.MigrateAsync();
            if (await db.Products.AnyAsync()) return;
        }

        await SeedSampleAsync(new RoadmapService(factory));
    }

    public static async Task SeedSampleAsync(RoadmapService svc)
    {
        var product = await svc.CreateProductAsync("Sample: Customer Portal",
            "A demo roadmap. Explore the views, then delete it from Settings.", ScoringModel.Rice);
        var pid = product.Id;
        await svc.AddThemeAsync(pid, "Growth", "#2f9e6e");
        await svc.AddThemeAsync(pid, "Platform", "#5b6ee1");
        await svc.AddTagAsync(pid, "customer-request");
        await svc.AddTagAsync(pid, "tech-debt");

        var roadmap = (await svc.GetRoadmapAsync(pid))!;
        int growth = roadmap.Product.Themes.Single(t => t.Name == "Growth").Id;
        int platform = roadmap.Product.Themes.Single(t => t.Name == "Platform").Id;

        var today = DateOnly.FromDateTime(DateTime.Today);
        var month = new DateOnly(today.Year, today.Month, 1);

        Task<int> Add(ItemType type, int? parent, string title, Action<ItemDraft>? configure = null)
        {
            var d = ItemDraft.New(pid, type, parent);
            d.Title = title;
            configure?.Invoke(d);
            return svc.SaveItemAsync(d);
        }

        Action<ItemDraft> Scheduled(int startMonth, int startDay, int days, ItemStatus status, int progress, string owner) => d =>
        {
            d.StartDate = month.AddMonths(startMonth).AddDays(startDay);
            d.EndDate = d.StartDate.Value.AddDays(days);
            d.Status = status;
            d.ProgressPct = progress;
            d.Owner = owner;
        };

        var goal = await Add(ItemType.Goal, null, "Increase self-serve adoption",
            d => d.Description = "Customers can do common tasks without contacting support.");

        var onboarding = await Add(ItemType.Initiative, goal, "Frictionless onboarding", d => { d.ThemeId = growth; d.Horizon = Horizon.Now; });
        var signup = await Add(ItemType.Feature, onboarding, "Self-service sign-up", d =>
        {
            d.ThemeId = growth; d.Horizon = Horizon.Now; d.Status = ItemStatus.InProgress; d.Owner = "Product";
            d.Score = new ItemScore { Reach = 800, Impact = 2, Confidence = 80, Effort = 2 };
        });
        await Add(ItemType.Task, signup, "Design sign-up flow", Scheduled(0, 0, 10, ItemStatus.Done, 100, "UX"));
        await Add(ItemType.Task, signup, "Build sign-up API", Scheduled(0, 8, 18, ItemStatus.InProgress, 40, "Dev"));
        var tour = await Add(ItemType.Feature, onboarding, "Guided product tour", d =>
        {
            d.ThemeId = growth; d.Horizon = Horizon.Next; d.Status = ItemStatus.Planned;
            d.Score = new ItemScore { Reach = 1200, Impact = 1, Confidence = 50, Effort = 3 };
        });
        await Add(ItemType.Task, tour, "Tour content & steps", Scheduled(1, 5, 14, ItemStatus.Planned, 0, "Product"));

        var billing = await Add(ItemType.Initiative, goal, "Billing self-service", d => { d.ThemeId = platform; d.Horizon = Horizon.Next; });
        var invoices = await Add(ItemType.Feature, billing, "Download invoices", d =>
        {
            d.ThemeId = platform; d.Horizon = Horizon.Next; d.Status = ItemStatus.Planned;
            d.Score = new ItemScore { Reach = 500, Impact = 1, Confidence = 100, Effort = 1 };
        });
        await Add(ItemType.Task, invoices, "Invoice PDF generation", Scheduled(1, 0, 20, ItemStatus.Planned, 0, "Dev"));
        var payments = await Add(ItemType.Feature, billing, "Update payment method", d =>
        {
            d.ThemeId = platform; d.Horizon = Horizon.Later; d.Status = ItemStatus.Idea;
            d.Score = new ItemScore { Reach = 300, Impact = 2, Confidence = 50, Effort = 4 };
        });
        await Add(ItemType.Task, payments, "Payment provider integration", Scheduled(2, 10, 25, ItemStatus.Idea, 0, "Dev"));

        await svc.AddDependencyAsync(signup, tour);
        await svc.AddDependencyAsync(invoices, payments);
    }
}
