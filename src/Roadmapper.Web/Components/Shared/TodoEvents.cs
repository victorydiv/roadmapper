namespace Roadmapper.Web.Components.Shared;

/// <summary>
/// Tells this browser tab's to-do badge and reminder panels that a to-do changed, so they reload. One per circuit (scoped).
/// </summary>
public sealed class TodoEvents
{
    public event Action? Changed;
    public void Notify() => Changed?.Invoke();
}
