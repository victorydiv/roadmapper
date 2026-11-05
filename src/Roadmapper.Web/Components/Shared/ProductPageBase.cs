using Microsoft.AspNetCore.Components;
using Roadmapper.Core;
using Roadmapper.Data;

namespace Roadmapper.Web.Components.Shared;

/// <summary>Common state for the per-product pages: loading the roadmap and hosting the item editor.</summary>
public abstract class ProductPageBase : ComponentBase
{
    [Parameter] public int ProductId { get; set; }
    [Inject] protected RoadmapService Svc { get; set; } = default!;

    protected ProductRoadmap? Roadmap { get; private set; }
    protected bool Loaded { get; private set; }
    protected ItemDraft? Editing { get; set; }
    protected string? Error { get; set; }

    protected override async Task OnParametersSetAsync() => await ReloadAsync();

    protected async Task ReloadAsync()
    {
        Roadmap = await Svc.GetRoadmapAsync(ProductId);
        Loaded = true;
    }

    protected void OpenNew(ItemType type, int? parentId) => Editing = ItemDraft.New(ProductId, type, parentId);

    protected void OpenEdit(RoadmapItem item) => Editing = ItemDraft.From(item);

    protected virtual async Task OnEditorClosed(bool changed)
    {
        Editing = null;
        if (changed) await ReloadAsync();
    }

    /// <summary>Runs a mutation, shows any rule violation as a page error, then reloads.</summary>
    protected async Task RunAsync(Func<Task> action)
    {
        Error = null;
        try { await action(); }
        catch (InvalidOperationException ex) { Error = ex.Message; }
        await ReloadAsync();
    }
}
