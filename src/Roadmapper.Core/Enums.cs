namespace Roadmapper.Core;

/// <summary>Hierarchy levels, ordered top to bottom. A child is always exactly one level below its parent.</summary>
public enum ItemType { Goal = 0, Initiative = 1, Feature = 2, Task = 3 }

public enum ItemStatus { Idea, Planned, InProgress, Done, Cancelled }

public enum Horizon { Now, Next, Later }

public enum ScoringModel { Rice, Wsjf, Moscow }

public enum MoscowPriority { WontHave = 1, CouldHave = 2, ShouldHave = 3, MustHave = 4 }

public enum DependencyType { Blocks }

/// <summary>The four WSJF inputs a scoring rubric can calculate.</summary>
public enum WsjfFactor { BusinessValue, TimeCriticality, RiskReduction, JobSize }

/// <summary>A set of WSJF factors; used to mark which factors were entered by hand.</summary>
[Flags]
public enum WsjfFactors { None = 0, BusinessValue = 1, TimeCriticality = 2, RiskReduction = 4, JobSize = 8 }

/// <summary>Base criteria give a factor its score; adjusters move it up or down the scale.</summary>
public enum CriterionRole { Base, Adjuster }

/// <summary>How a criterion is answered: a number matched to bands, a pick-list, or the item's start date.</summary>
public enum CriterionKind { NumberBands, Choice, StartDate }

public enum AuditAction { Created, Updated, Deleted }

/// <summary>Impact-vs-effort quadrants: high value is good, high effort is bad.</summary>
public enum Quadrant { QuickWin, BigBet, FillIn, TimeSink }
