namespace Roadmapper.Core;

/// <summary>Hierarchy levels, ordered top to bottom. A child is always exactly one level below its parent.</summary>
public enum ItemType { Goal = 0, Initiative = 1, Feature = 2, Task = 3 }

public enum ItemStatus { Idea, Planned, InProgress, Done, Cancelled }

public enum Horizon { Now, Next, Later }

public enum ScoringModel { Rice, Wsjf, Moscow }

public enum MoscowPriority { WontHave = 1, CouldHave = 2, ShouldHave = 3, MustHave = 4 }

public enum DependencyType { Blocks }

public enum AuditAction { Created, Updated, Deleted }

/// <summary>Impact-vs-effort quadrants: high value is good, high effort is bad.</summary>
public enum Quadrant { QuickWin, BigBet, FillIn, TimeSink }
