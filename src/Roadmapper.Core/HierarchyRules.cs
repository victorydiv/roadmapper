namespace Roadmapper.Core;

public static class HierarchyRules
{
    public static ItemType? ParentTypeOf(ItemType type) => type == ItemType.Goal ? null : type - 1;

    public static ItemType? ChildTypeOf(ItemType type) => type == ItemType.Task ? null : type + 1;

    /// <summary>Goals must be top-level; every other type must sit directly under the level above it.</summary>
    public static bool IsValidParent(ItemType childType, ItemType? parentType) => ParentTypeOf(childType) == parentType;

    public static void Validate(ItemType childType, ItemType? parentType)
    {
        if (IsValidParent(childType, parentType)) return;

        var expected = ParentTypeOf(childType);
        throw new InvalidOperationException(expected is null
            ? $"A {childType} must be top-level and cannot have a parent."
            : $"A {childType} must belong to a {expected}.");
    }
}
