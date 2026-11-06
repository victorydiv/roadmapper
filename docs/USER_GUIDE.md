# Roadmapper User Guide

Roadmapper is a product roadmap app. You use it to plan your work as a hierarchy, schedule it on a timeline, prioritize it with a scoring model, and track progress against saved baselines. You can also export reports and exchange data with Excel.

This guide is for two audiences:

- **People using the app.** Sections 1–9 explain each screen and how to get common jobs done.
- **AI assistants and developers.** Section 10 gives exact rules, the data model, and where each behavior lives in the code. The rules there are written so they can be relied on when answering questions or changing the app.

---

## Contents

1. [Getting started](#1-getting-started)
2. [Core concepts](#2-core-concepts)
3. [The Tree tab: building the roadmap](#3-the-tree-tab-building-the-roadmap)
4. [Editing an item](#4-editing-an-item)
5. [Views: Dashboard, Timeline, Now/Next/Later, Kanban, Priorities](#5-views)
6. [Task templates](#6-task-templates)
7. [Snapshots and baselines](#7-snapshots-and-baselines)
8. [Import and export](#8-import-and-export)
9. [Settings](#9-settings)
10. [Reference for AI assistants and developers](#10-reference-for-ai-assistants-and-developers)
11. [FAQ and troubleshooting](#11-faq-and-troubleshooting)

---

## 1. Getting started

### Running the app

Roadmapper runs on your own PC and opens in your browser. It's a single-user app with no sign-in.

1. Unzip `Roadmapper-<version>-win-x64.zip` to a folder of your own (don't run it from inside the zip).
2. Double-click **Roadmapper.exe**. Your browser opens at **http://localhost:5210**.
3. A console window stays open while the app runs. Close it to stop Roadmapper.

Nothing else needs to be installed. Notes:

- **"Windows protected your PC":** the app isn't code-signed. Click **More info → Run anyway**.
- **Already running?** Double-clicking `Roadmapper.exe` again just reopens it in your browser.
- **Port in use:** if another program has port 5210, Roadmapper uses a free port and shows the address in its console window.
- **Updating:** replace the app folder with the new version. Your data is kept separately (see below).
- **Help:** click **User guide** at the top right of any page to read this guide in the app.

**Developers** run it from the repository root (the folder containing `Roadmapper.slnx`, not `src`) with `dotnet run --project src/Roadmapper.Web --launch-profile http`, and package it with `./publish.ps1` (see [Working on the code](#working-on-the-code)).

### Where your data lives

Everything is stored in one SQLite file:

```
%LOCALAPPDATA%\Roadmapper\roadmapper.db
```

You can change the location with the `Database:Path` setting in `appsettings.json` (environment variables such as `%LOCALAPPDATA%` are expanded). The database is created and upgraded automatically when the app starts.

On a brand-new database, Roadmapper creates a demo product, **Sample: Customer Portal**, for you to explore. Delete it from its Settings tab when you're done.

**Backups:** the easiest way to back up one product is to export a [Roadmapper file (.rmp)](#roadmapper-files-rmp), which holds the complete product. To back up everything at once, stop the app and copy `roadmapper.db`.

### Moving around

- The **Products** page (home) lists every product. Each card shows its scoring model, how many goals and items it has, and how many are done.
- **+ New product** asks for a name, an optional description, and a prioritization model (RICE, WSJF or MoSCoW; you can change it later).
- **Open .rmp file…** opens a [Roadmapper file](#roadmapper-files-rmp) as a new product.
- Opening a product shows its tabs: **Dashboard · Tree · Timeline · Now / Next / Later · Kanban · Priorities · Snapshots · Import / Export · Settings**.
- Clicking any item title, bar or card anywhere in the app opens the [item editor](#4-editing-an-item).

---

## 2. Core concepts

### Products

A product is a separate roadmap with its own items, themes, tags, task templates, snapshots and change history. Nothing is shared between products.

### The four-level hierarchy

| Level | Meaning | Must sit under |
|---|---|---|
| **Goal** | An outcome you're aiming for | Nothing (always top level) |
| **Initiative** | A body of work toward a goal | A Goal |
| **Feature** | A deliverable piece of an initiative | An Initiative |
| **Task** | A unit of work to deliver a feature | A Feature |

Each item sits exactly one level below its parent; you can't skip a level. An item's type is fixed once created, but you can move it to a different parent of the right type.

### Item fields

| Field | Notes |
|---|---|
| Title | Required. |
| Description | Free text. |
| Status | Idea, Planned, In progress, Done, Cancelled. Setting **Done** also sets progress to 100%. |
| Progress % | 0–100. |
| Start / End | Dates; End can't be before Start. Either can be blank. |
| Horizon | Now, Next, Later, or unassigned. A loose time bucket for the Now/Next/Later board. |
| Owner | Free text (a person or team). |
| Theme | One per item; colors the item's bars and cards. |
| Tags | Any number, from the product's tag list. |
| Scoring inputs | Depend on the product's model (see below). |
| Manual rank | Optional whole number ≥ 1 that pins the item ahead of computed scores (1 = top). |

### Roll-up: how parents get their dates and progress

A parent's dates and progress are calculated from its children automatically:

- **Start** = the earliest child start; **End** = the latest child end.
- **Progress** = the average of the children's progress, rounded.
- **Cancelled** children are ignored.
- If none of the children have dates, the parent keeps its own dates.

Changes cascade upward: Task → Feature → Initiative → Goal. While a parent rolls up, its Start, End and Progress fields are read-only in the editor and shown as *(rolled up)*.

To set a parent's values yourself, tick **"Set dates and progress manually instead of rolling them up from children"** in its editor.

### Prioritization (scoring)

Each product uses one scoring model, chosen in Settings:

| Model | Inputs | Score |
|---|---|---|
| **RICE** | Reach, Impact (0.25–3), Confidence % (0–100), Effort (person-months) | (Reach × Impact × Confidence/100) ÷ Effort |
| **WSJF** | Business value, Time criticality, Risk reduction, Job size | (BV + TC + RR) ÷ Job size |
| **MoSCoW** | Must / Should / Could / Won't; optional Effort (only used to place it on the matrix) | Must = 4, Should = 3, Could = 2, Won't = 1 |

- An item with missing inputs (or an Effort/Job size of 0) has no score and shows **–**.
- **Ranking order:** items with a manual rank come first (by rank), then by score from highest to lowest, then unscored items.
- **Switching models** recalculates every score. Inputs for the other models are kept, so you can switch back without losing them.

### Dependencies and "blocked"

A dependency says **A blocks B**: B shouldn't start until A is finished.

- An item is **blocked** while any item blocking it is still open (not Done or Cancelled). Blocked items show a red **blocked** badge.
- A **schedule overlap** (⚠) means B starts on or before the day A ends.
- An item can't depend on itself, the same dependency can't be added twice, and a dependency that would create a loop (A → B → A) is refused.
- Dependencies can link items at any level, including across branches of the tree.

### Overdue

An item is **overdue** when it's still open and its End date is before today.

### Themes and tags

- **Themes** are product-wide categories with a color, such as "Growth" or "Platform". An item can have one theme. Its bars and cards use the theme color; without a theme they use the color of the item's level.
- **Tags** are lightweight labels; an item can have many.
- Both are managed in [Settings](#9-settings).

### Change history

Roadmapper records every change to items, scores, dependencies and product settings: what changed, the old and new values, and when. It shows on the Dashboard (**Recent activity**) and in each item's editor (**History**). History is kept even after an item is deleted.

---

## 3. The Tree tab: building the roadmap

The Tree is where you build and organize the roadmap. Each row shows the item's type, title, status, progress, dates, horizon, owner, theme and tags, and score (with a manual rank in brackets). Rolled-up progress and dates are shown in a muted style.

**Toolbar**

| Control | What it does |
|---|---|
| **+ Goal** | Creates a new top-level goal. |
| **Manual order / By priority** | Orders siblings by your manual order or by [ranking order](#prioritization-scoring). The ↑/↓ buttons only appear in manual order. |
| **Expand all / Collapse all** | Opens or closes every branch. ▶/▼ toggles one branch. |
| **Select features** | Shows checkboxes on Feature rows so you can [apply a template to several features](#apply-a-template-to-several-features-at-once) at once. |

**Row actions** (on the right of each row)

- **+ Initiative / + Feature**: adds a child of the next level down.
- **+ Tasks ▾** (Features only; right-clicking a Feature row opens the same menu):
  - **Add task** opens a new task under this feature.
  - **Add from template…** opens the [template picker](#apply-a-template-to-a-feature).
  - **Save tasks as template…** turns the feature's tasks into a new template. It's disabled if the feature has no tasks.
- **↑ / ↓** moves the item among its siblings.
- **Edit** opens the item editor.

After you apply a template, a banner says how many tasks were added and offers **Undo**.

---

## 4. Editing an item

The editor opens as a pop-up. Its sections are:

- **Title** and **Description**.
- **Type** (new items only) and **Parent**. The parent list only offers items of the level directly above.
- **Status & schedule:** status, horizon, progress, start, end and owner. For a parent, this section also has the manual roll-up checkbox (see [Roll-up](#roll-up-how-parents-get-their-dates-and-progress)). Progress is locked at 100% while the status is Done.
- **Priority:** the inputs for the product's scoring model, a live score preview, and **Manual rank**.
- **Classification:** theme and tags.
- **Tasks** (Features only): **+ Add task**, **Add from template…** and **Save tasks as template…**. It also lists each template batch added to this feature with an **Undo** button. If any task in a batch was edited since it was added, Undo asks you to confirm first.
- **Created from template X on date** (Tasks made from a template). This is a reference only; editing or deleting the template never changes the task.
- **Dependencies:** the items this one is *blocked by* and the items it *blocks*, with schedule-overlap warnings. Add one with **This is blocked by / This blocks**, choose the other item, then **Add**.
- **History:** **Show change history** lists this item's changes.
- **Delete:** asks you to confirm and tells you how many child items will be deleted with it. Deleting an item also deletes everything below it, along with its dependencies.

---

## 5. Views

### Dashboard

A one-page health check:

- **Overall progress:** the average of the active (non-cancelled) goals' progress.
- **Tiles:** total items, in progress, due in the next 30 days, overdue, and blocked.
- **Progress by goal:** each goal's progress bar, dates, items done, and overdue count.
- **Status by level:** how many Goals, Initiatives, Features and Tasks are in each status. Toggle between bars and a table.
- **Upcoming deadlines:** open items due in the next 30, 60 or 90 days.
- **Blocked items** and **Recent activity**.

### Timeline

A Gantt chart of the whole roadmap.

- **Months / Quarters** zooms the chart.
- **Show down to** limits the depth (for example, Features to hide tasks).
- **Compare to** overlays a [snapshot](#7-snapshots-and-baselines). Gray "ghost" bars show the baseline dates, and a **+Nd / −Nd** label shows how far each end date has moved.
- **What's on the chart:**
  - Parent bars are thin, item bars are thick, and the lighter part of a bar shows progress.
  - Arrows show dependencies; red arrows mark a schedule overlap.
  - A vertical line marks today.
  - An item with only a start or only an end date is drawn as a one-day bar. Items with no dates have no bar.
- **Exports:**
  - **PDF** opens the report with this Gantt, using the current depth and baseline.
  - **SVG** downloads the chart as a vector image.
  - **PNG** downloads it as a picture.

### Now / Next / Later

A board with columns **Now**, **Next**, **Later** and **Unassigned**. Drag a card to change its horizon.

- Each column is sorted by [ranking order](#prioritization-scoring).
- Filter by level; it shows Features by default. Tick **Include done / cancelled** to see finished work.

### Kanban

A board with one column per status. Drag a card to change its status; dropping on **Done** sets progress to 100%.

- Filter by level and horizon.
- The Cancelled column is hidden unless you tick **Show Cancelled column**.

### Priorities

An **impact-vs-effort matrix** for one level at a time, with a ranked table beside it.

- **The four quadrants:**
  - **Quick wins:** high value, low effort.
  - **Big bets:** high value, high effort.
  - **Fill-ins:** low value, low effort.
  - **Time sinks:** low value, high effort.
- The split lines sit at the median value and median effort of the plotted items, so the quadrants adapt to your own scale.
- **What gets plotted** depends on the scoring model:
  - RICE: Reach × Impact × Confidence against Effort.
  - WSJF: cost of delay against Job size.
  - MoSCoW: priority against Effort.
- Items with incomplete inputs aren't plotted; the table marks them "needs inputs".
- The top three items are labeled on the chart. Hover a dot for details, or click it to edit.

---

## 6. Task templates

A task template is a reusable, ordered list of tasks, such as the standard steps for an EDI migration. You apply it to a Feature to create all the tasks at once.

### Managing templates

Go to **Settings → Task templates**.

- **+ New template**, **Edit**, **Duplicate** (creates "Name (copy)") and **Delete…**.
- Deleting a template asks you to confirm and never removes tasks already created from it.
- Template names must be unique within the product, ignoring capitals.
- Each template has a name, an optional description, and at least one task line.
- Templates belong to one product. To reuse one elsewhere, export the product to Excel and import the file into the other product.

**Each task line has:**

| Field | Meaning |
|---|---|
| Title | Required. |
| Owner | Copied to the task. |
| **Gap** | Extra days to wait before this task starts. Blank = 0; a negative number overlaps the previous task. |
| **Duration** | How many days the task lasts, counting its start day (so 1 = a one-day task). Blank = no end date. |
| ▾ Description and tags | Copied to the task. |

To reorder lines, drag them by the ⠿ handle or use ↑/↓. ✕ removes a line.

Every new product comes with a starter template, **EDI Migration Standard**: Build BRD → Review EDI documents → Develop EDI docs → Test EDI docs → TR/CAB → Implementation. It has no durations; add your own under Edit.

### How task dates are calculated

Tasks are scheduled **one after another** from the Feature's start date:

1. The first task starts on the **Feature's start date + its gap**.
2. Each later task starts **the day after the previous task ends, + its gap**.
3. A task ends on **start + duration − 1**.
4. If a task has no duration, it has no end date and the next task starts **on the same day**.
5. If the Feature has **no start date**, all the tasks are created without dates.

Example: a Feature starting Monday, March 2:

| Task | Gap | Duration | Dates |
|---|---|---|---|
| Kickoff | – | 5 | Mar 2–6 |
| Build | 2 | 3 | Mar 9–11 |
| Review | – | – | Mar 12 (no end) |
| Sign-off | – | 1 | Mar 12 |
| Launch | −1 | 2 | Mar 12–13 |

### Apply a template to a Feature

Open **+ Tasks ▾ → Add from template…** on the Tree, or **Add from template…** in the Feature's editor.

1. **Choose a template** on the left. The preview shows every task with its calculated dates, owner and tags.
2. **Untick** any task you don't want. A task whose title already exists under that Feature is marked **already exists** and starts unticked.
3. **Adjust the dates** if needed. The tasks after an edited one move to follow it, and dates you typed yourself are kept. **↺** puts a task back on its calculated dates.
4. **Check the roll-up warning.** If adding the tasks will change the Feature's rolled-up dates or progress, a yellow warning shows the before and after values. To stop that, tick the Feature's manual roll-up checkbox before applying.
5. Click **Add N tasks**.

**What the new tasks get:**

- Status **Idea**, 0% progress, and the line's title, description, owner and tags.
- They're added after any existing tasks.
- Each task remembers the template's name and when it was applied. This is for reference only; editing the template later never changes them.

**Undo:** all tasks from one apply action form a **batch**.

- Undo it from the banner on the Tree, which appears right after you apply, or later from the Feature editor's Tasks section.
- Undo removes every task in the batch. In the Feature editor, if any of the tasks were edited since they were added, you're asked to confirm first.

### Apply a template to several features at once

On the Tree, click **Select features**, tick the Features, then click **Apply template to selected (N)**. The picker shows a separate preview for each Feature, scheduled from that Feature's own start date. Everything is created in one batch, so one Undo removes it all.

### Save a Feature's tasks as a template

**+ Tasks ▾ → Save tasks as template…** (or the same button in the Feature editor) opens the template editor prefilled from that Feature's tasks.

- Lines keep the tasks' order, title, description, owner and tags.
- If the Feature has a start date, Roadmapper works out each line's **duration** and **gap** from the task dates. Applying the template to a Feature with the same start date then reproduces the original schedule.
- Give the template a name and save.

---

## 7. Snapshots and baselines

A **snapshot** saves every item's dates, status, progress, horizon, owner and score as they are now. Use one to record an agreed plan, then compare against it later.

On the **Snapshots** tab:

- **Take a snapshot:** give it a name (the default is "Baseline YYYY-MM-DD") and an optional note.
- **Compare:** shows *Now vs. "snapshot"*:
  - Counts of items **slipped** (end date later), **pulled in** (earlier), **completed since**, **added** and **removed**.
  - A table of each item's changes, such as "End +5d", status changes, progress changes, horizon moves, renames and dates added or cleared.
  - **Only show differences** hides unchanged items.
- **View on timeline** opens the Timeline with that snapshot as the baseline overlay.
- **✕** deletes a snapshot, after you confirm. It doesn't affect the roadmap.

Snapshots match items by their internal id, so renaming or moving an item still compares correctly.

---

## 8. Import and export

Find these under **Import / Export**.

### Roadmapper files (.rmp)

A `.rmp` file holds **one complete product**, so it works as a backup and for sharing with anyone who has Roadmapper. It contains:

- The product's name, description and scoring model.
- Its themes and tags.
- Every item with all its fields and scoring inputs.
- Dependencies, task templates and snapshots.
- The full change history.

**To save one:** go to Import / Export → **Roadmapper file (.rmp)**. The file is named like `LoadProof roadmap 2026-11-06.rmp`.

**To open one:** go to the **Products** page → **Open .rmp file…** and choose the file. Roadmapper checks the file, then:

- It always creates a **new product** and never changes or replaces an existing one. To restore a backup over a product, open the file, check the new product, then delete the old one.
- If a product with the same name already exists, the new one is called "Name (imported)", then "Name (imported 2)", and so on. Rename it in Settings.
- Every link between items comes across: parents, dependencies, snapshot comparisons (including items deleted before the export), history, and template batches. Undo works on the copy's batches without affecting the original product.
- Opening the file isn't itself recorded as edits in the history.
- If the file is damaged or from a newer version of Roadmapper, nothing is created and you'll see the reason (for example, "This file was made by a newer version of Roadmapper").

A `.rmp` is plain JSON, so it can be opened in any text editor. See [the file format](#roadmapper-file-format) for details.

### Export

| Format | Contents |
|---|---|
| **Roadmapper (.rmp)** | The complete product, as described above. |
| **Excel (.xlsx)** | A **Roadmap** sheet with every item and field, a **Dependencies** sheet, and a **Templates** sheet with the product's task templates. You can edit it and import it back. |
| **CSV** | The same columns as the Roadmap sheet. |
| **PDF report** | **Gantt pages:** the whole timeline fitted to landscape page(s), with a color key, dependency arrows and blocked markers. **Summary line:** overall progress, item count, in progress, overdue, blocked, and the scoring model. **Item table:** every item, with blocked items marked. **Dependency table:** each dependency and its effect (Blocked, Cleared or Successor closed). Opens in a new browser tab, where you can save or print it. |
| **SVG / PNG** | The Timeline as an image. Use the buttons on the Timeline tab. |

**Spreadsheet columns** (the same on export and import):

`Type, Title, Parent, Description, Status, Progress, Start, End, Horizon, Owner, Theme, Tags, Reach, Impact, Confidence, Effort, Business value, Time criticality, Risk reduction, Job size, MoSCoW, Manual rank, From template, Template applied, Score, Id`

- **Parent** is the path of the item's ancestors joined with " › ", for example `Grow revenue › Checkout redesign`.
- **Tags** are separated by `;`.
- **Score** and **Id** are for information only and are ignored on import.

### Import

Import is a three-step wizard:

1. **Choose file.** A `.csv` or `.xlsx` with a header row; for Excel files, the first sheet is used. **Download template** gives you a sample CSV.
2. **Match columns.**
   - Each column in the file is matched to a field automatically. Common alternative names are recognized too, such as *Due date* → End, *Assignee* → Owner, *Labels* → Tags and *% complete* → Progress.
   - Change any match, or set it to *ignore*. A Title column is required, and each field can only come from one column.
3. **Review & import.**
   - Each row shows ✓ (ready), ⚠ (will import with warnings) or ✕ (has errors and will be skipped), with the reason.
   - Tick **Only show rows with problems** to focus on the rows that need attention, then click **Import N items**.

**Import rules**

- Import only **adds** items; it never changes or deletes existing ones. Importing the same file twice creates duplicates, and you'll see a warning when a path already exists.
- **Parents** can be other rows in the file or items already in the product. Use the full path, or just the title if it's unique.
- **Type** can be left blank. It's then worked out from the parent; a row with no parent becomes a Goal.
- Missing **themes and tags** are created by name.
- **Values accepted:**
  - Status: Idea, Planned, In progress, Done, Cancelled.
  - Horizon: Now, Next, Later.
  - Progress: 0–100.
  - MoSCoW: Must, Should, Could, Won't.
  - Manual rank: a whole number of 1 or more.
- A row whose parent has errors is skipped too.
- If the Excel file has a **Templates** sheet, its templates are added as well. Any whose name already exists in the product are skipped.

<a id="what-import-restores"></a>**What spreadsheet import doesn't restore:** dependencies (the Dependencies sheet is for reading only), snapshots and change history. For a complete copy, use a [.rmp file](#roadmapper-files-rmp).

---

## 9. Settings

- **Product:** name, description and prioritization model. Changing the model recalculates scores; inputs for other models are kept.
- **Themes:** add one with a name and color, or remove one with ✕. Removing a theme clears it from its items without deleting them.
- **Tags:** add or remove. Tag names must be unique in the product; removing a tag takes it off every item.
- **Task templates:** see [section 6](#managing-templates).
- **Danger zone:** **Delete product…** asks you to type the product's name, then permanently deletes the product and its whole roadmap, along with its themes, tags, templates, snapshots and history.

---

## 10. Reference for AI assistants and developers

### Architecture

- **Stack:** .NET 10, Blazor Server (global interactive server rendering), EF Core 10 with SQLite.
- **No authentication;** single user.
- **Schema changes:** migrations apply automatically on startup (`DbInitializer`).

| Project | Responsibility |
|---|---|
| `src/Roadmapper.Core` | Domain model and pure rules, with no database access: `Entities.cs`, `Enums.cs`, `HierarchyRules`, `RollupService`, `Scoring`, `PriorityMatrix`, `DependencyRules`, `ProductRoadmap` (a read-only in-memory view), `Snapshots`, `TaskTemplates` (`TemplateRules`, `StarterTemplates`), `TimelineLayout`, `RoadmapColors`. `Exchange/` holds the CSV, column, import-planning and template-sheet code. |
| `src/Roadmapper.Data` | `RoadmapDbContext`, migrations, and `RoadmapService` (all reads and writes; a short-lived DbContext per call). `RoadmapService.Templates.cs` holds the template operations. `AuditTrail` produces the change history. `Exchange/` holds the XLSX/CSV export, the table reader, and the PDF report (`RoadmapPdf`, `GanttPdf`). |
| `src/Roadmapper.Web` | Blazor pages (`Components/Pages/Product*.razor`) and shared components (`ItemEditor`, `TemplateEditor`, `TemplatePicker`, `DragBoard`, …). `Exports/ExportEndpoints.cs` provides the file downloads. `Launcher.cs` handles start-up for the packaged exe (port choice, opening the browser). `UserGuide.cs` renders this guide (embedded from `docs/USER_GUIDE.md`) for `/guide`. The assembly, and so the packaged exe, is named `Roadmapper`. |
| `tests/Roadmapper.Tests` | xUnit tests, including the Task Templates acceptance checks (`TaskTemplateTests.cs`). |

### Data model

- `Product`: owns everything below; deleting it cascades.
- `RoadmapItem`: **one table for all four levels**.
  - Linked by `ParentId` and `ItemType` (Goal=0, Initiative=1, Feature=2, Task=3).
  - Fields: `Title, Description, Status, ProgressPct, StartDate, EndDate (DateOnly), Horizon, Owner, ThemeId, SortOrder, ManualRank, IsRollupOverridden, CreatedAt, UpdatedAt`.
  - Template reference fields: `SourceTemplateName, SourceTemplateAppliedAt, TemplateBatchId`.
  - Deleting an item cascades to its children, score, tag links and dependencies.
- `ItemScore`: 1:1 with an item. Holds the inputs for all three models plus `ComputedScore`.
- `Dependency`: `PredecessorId` blocks `SuccessorId`. The only type is `Blocks`.
- `Theme` (with a color) and `Tag`: belong to a product. Items link to tags through a many-to-many join table.
- `AuditEntry`: change history. It deliberately has no foreign key to the item, so history survives deletion.
- `Snapshot`: name, note and time, plus `PayloadJson`, a list of `SnapshotItem` records (id, parent, type, title, status, progress, dates, horizon, owner, score).
- `TaskTemplate` (unique, case-insensitive name per product) has ordered `TaskTemplateLine`s: `SortOrder, Title, Description, Owner, StartOffsetDays` (shown in the UI as **Gap**), `DurationDays`, and Tags.

### Invariants (rules the code enforces)

1. **Hierarchy:** a child's type is always its parent's type + 1; Goals have no parent (`HierarchyRules.Validate`). An item's type can't change after it's created.
2. **Validation:** a title is required; End ≥ Start; progress is 0–100; manual rank is ≥ 1. Violations surface as an `InvalidOperationException` with a message for the user.
3. **Done ⇒ 100% progress**, whether set in the editor, on the Kanban, or on import.
4. **Every item change runs `RecalculateAsync`:** a bottom-up roll-up (`RollupService.Apply`), then a fresh score for every item.
5. **Roll-up:** ignores Cancelled children; skips parents with `IsRollupOverridden`; leaves dates alone when no child has them.
6. **Dependencies:** no self-links, no duplicates, no loops (`DependencyRules.WouldCreateCycle`); both items must be in the same product.
7. **Blocked** = at least one predecessor is not Done or Cancelled. **Schedule conflict** = successor start ≤ predecessor end.
8. **Template application** happens in one save. Each task is created as Idea at 0% and gets one shared `TemplateBatchId` (a GUID); `UndoTemplateBatchAsync` deletes by that id.
9. **Template scheduling** (`TemplateRules.Schedule`) works through the ticked tasks in order. The anchor starts as the Feature start. For each task, start = anchor + gap; end = start + duration − 1 when there is a duration; the next anchor is `end + 1`, or `start` when there is no end. Tasks with `DatesEdited` keep their dates and still move the anchor on. With no Feature start, every task is undated.
10. **Ranking order** (`Scoring.InRankOrder`): manual rank first (ascending), then computed score (descending), then unscored, then `SortOrder`.
11. **Priority matrix** quadrants split at the medians; points exactly on a split line count as high value and low effort.

### HTTP endpoints (file downloads)

| Route | Returns |
|---|---|
| `GET /export/template.csv` | Sample import CSV |
| `GET /export/{productId}/roadmap.rmp` | Roadmapper file (whole product as JSON) |
| `GET /export/{productId}/roadmap.csv` | CSV export |
| `GET /export/{productId}/roadmap.xlsx` | Excel export (Roadmap, Dependencies, Templates) |
| `GET /export/{productId}/roadmap.pdf?depth={Goal\|Initiative\|Feature\|Task}&baseline={snapshotId}` | PDF report, shown in the browser (`inline`) |
| `GET /export/{productId}/timeline.svg?scale=quarter&depth=…&baseline=…&download=true` | Timeline SVG |

### Roadmapper file format

A `.rmp` is UTF-8 JSON defined by `Core/Exchange/RoadmapFile.cs`. Export and import live in `Data/RoadmapService.Files.cs` (`ExportFileAsync` / `ImportFileAsync`).

- **Formatting:** property names are camelCase, enums are strings (for example `"InProgress"`), dates are `yyyy-MM-dd`, and empty values are left out.
- **Top level:** `format` (always `"roadmapper"`), `version` (currently `1`), `exportedAt`, `product` {name, description, scoringModel}, `themes`, `tags`, `items`, `dependencies`, `templates`, `snapshots`, `history`.
- **References:**
  - `items`, `themes` and `tags` have an `id` that only applies inside the file.
  - Items refer to each other by `parentId`; dependencies use `predecessorId`/`successorId`; items and template lines use `themeId` and `tagIds`.
  - Snapshot rows and history entries also use item ids. History entries for items that no longer exist have no `itemId`.
- **Items:** `type`, `title`, `status`, `progressPct`, `startDate`, `endDate`, `horizon`, `owner`, `sortOrder`, `manualRank`, `isRollupOverridden`, `score` (scoring inputs only; the score itself is recalculated), the template reference fields, and `createdAt`/`updatedAt`.
- **Template lines:** `title`, `description`, `owner`, `gapDays`, `durationDays`, `tagIds`.
- **Import:**
  - Validates the whole file first and creates nothing if any check fails. It checks the format and version, hierarchy rules, missing references, dependency loops, duplicate ids, tag names or template names, empty templates, and dates where End is before Start.
  - Then creates a new product in one transaction with change history recording turned off, assigning new ids and new template-batch ids, then recalculates roll-ups and scores.
- **Version rule:** files with a higher `version` than the app supports are refused. If the format changes, bump `CurrentVersion` and keep reading older versions.

### Page routes

`/` (products), `/guide` (this guide), `/products/{id}` (Tree), `/products/{id}/dashboard`, `/timeline` (accepts `?baseline={snapshotId}`), `/board`, `/kanban`, `/priorities`, `/snapshots`, `/exchange`, `/settings`.

### Colors (shared by the UI and every export)

| Item | Color |
|---|---|
| Goal | `#7c4dcc` |
| Initiative | `#3f5bd9` |
| Feature | `#128a8a` |
| Task | `#6b7280` |
| Baseline and dependency lines | `#9aa3b2` |
| Schedule conflict | `#c8372d` |
| Blocked | `#d03b3b` |

An item with a theme uses the theme's color instead of its level's color (`RoadmapColors.ColorOf`).

### Working on the code

- **Build and test:** run `dotnet test` from the repo root.
- **Migrations:** `dotnet ef migrations add <Name> --project src/Roadmapper.Data --startup-project src/Roadmapper.Web` (`dotnet-ef` is a local tool; run `dotnet tool restore` first).
- **Packaging:** `./publish.ps1` runs the tests and writes `artifacts/Roadmapper-<version>-win-x64.zip`: a self-contained, single-file `Roadmapper.exe` (no .NET install needed) plus `wwwroot` and `appsettings.json`. Raise `<Version>` in `Roadmapper.Web.csproj` for each release.
  - The packaged app (any environment other than Development) uses its own folder as the content root, listens only on `http://localhost` (5210, or a free port if taken), and opens the browser once started (`--no-browser` turns that off). Passing `--urls` or setting `ASPNETCORE_URLS` skips the port logic.
  - A second launch calls `GET /_roadmapper` on port 5210. If Roadmapper answers, the new copy opens the browser and exits.
  - There is no HTTPS redirect or HSTS, since the app is only served on localhost.
- **User guide in the app:** `docs/USER_GUIDE.md` is embedded at build time, so edits appear in the app after a rebuild. Links between sections use GitHub-style heading ids, which work on GitHub and in the app. Rendering uses Markdig (BSD-2-Clause).
- **NuGet:** `nuget.config` adds nuget.org because the machine has no global source.
- **Licensing:** **PDFsharp-MigraDoc is pinned to 6.2.4 (MIT).** Version 6.5 and later is AGPL-3.0, so don't upgrade it without a license review. Excel files are produced with ClosedXML.
- **No JavaScript frameworks.** Drag-and-drop uses native HTML5 events; the PNG export is the only JS interop.

### Glossary

| Term | Meaning |
|---|---|
| Baseline | A snapshot used for comparison. |
| Batch | All tasks created by one "apply template" action. |
| Blocked | Has an unfinished predecessor. |
| Gap | Days of wait before a template task starts, after the previous task (or the Feature start). |
| Horizon | Now / Next / Later bucket. |
| Open | Status not Done or Cancelled. |
| Overdue | Open, with an End date in the past. |
| Roll-up | A parent's dates and progress calculated from its children. |
| Slip | Change in end date versus the baseline (+ = later). |

---

## 11. FAQ and troubleshooting

**I can't edit a Feature's dates or progress.**
They're rolled up from its tasks. Tick "Set dates and progress manually…" in the editor.

**My parent's dates didn't change when I added a task.**
Either the new task has no dates, or the parent is set to manual roll-up.

**An item shows "–" for its score.**
One of the inputs for the scoring model is missing, or Effort or Job size is 0.

**Why won't it let me add a dependency?**
It would create a loop, it already exists, or it links an item to itself. The message says which.

**Template tasks came in undated.**
The Feature had no start date. Set one, then undo and re-apply. Or set the task dates by hand.

**All my template tasks start on the same day.**
The template's lines have no durations. Add durations under Settings → Task templates → Edit.

**Importing created duplicates.**
Spreadsheet import only ever adds items. Delete the extras, or open a .rmp backup you made before the import.

**How do I share a roadmap with someone?**
Export a Roadmapper file (.rmp) from Import / Export and send it. They open it with **Open .rmp file…** on their Products page.

**Nothing happens when I double-click Roadmapper.exe, or the console closes straight away.**
Make sure you unzipped the whole folder (the exe needs the `wwwroot` folder beside it). If the console window shows an error, send a screenshot of it to whoever gave you the app.

**`dotnet run` says the project path doesn't exist.**
Run it from the repository root, not from `src`.

**The app won't build because a file is locked.**
A copy of the app is still running. Stop it (Ctrl+C in its terminal) and try again.
