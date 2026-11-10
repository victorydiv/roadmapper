# Roadmapper User Guide

Roadmapper is a product roadmap app. You use it to plan your work as a hierarchy, schedule it on a timeline, prioritize it with a scoring model, and track progress against saved baselines. You can also export reports and exchange data with Excel.

Above the roadmaps, the **application portfolio** records the systems you run, their vendors and contracts, the trading partners you exchange data with, the integrations between them, the business processes that depend on them, the value roadmap work delivers and the decisions behind it, so you can see what a change will touch before you make it.

This guide is for two audiences:

- **People using the app.** Sections 1–9 explain the roadmap screens and how to get common jobs done. Section 10 covers the application portfolio, including **Capabilities** (comparing applications feature by feature). Section 13 introduces the three scoring models for anyone who hasn't used them before.
- **AI assistants and developers.** Section 11 gives exact rules, the data model, and where each behavior lives in the code. The rules there are written so they can be relied on when answering questions or changing the app.

---

## Contents

1. [Getting started](#1-getting-started)
2. [Core concepts](#2-core-concepts)
3. [The Tree tab: building the roadmap](#3-the-tree-tab-building-the-roadmap)
4. [Editing an item](#4-editing-an-item)
5. [Views: Dashboard, Timeline, Now/Next/Later, Kanban, Priorities, Scoring](#5-views)
6. [Task templates](#6-task-templates)
7. [Snapshots and baselines](#7-snapshots-and-baselines)
8. [Import and export](#8-import-and-export)
9. [Settings](#9-settings)
10. [Application portfolio: applications, partners, integrations, processes, impact, benefits, decisions](#10-application-portfolio)
11. [Reference for AI assistants and developers](#11-reference-for-ai-assistants-and-developers)
12. [FAQ and troubleshooting](#12-faq-and-troubleshooting)
13. [Scoring models explained: RICE, WSJF, MoSCoW](#13-scoring-models-explained)

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

**Backups:** the easiest way to back up one product is to export a [Roadmapper file (.rmp)](#roadmapper-files-rmp), which holds the complete product. Back up the application portfolio as a [portfolio file (.rpf)](#portfolio-settings-backup-and-export) from Portfolio › Settings. To back up everything at once, stop the app and copy `roadmapper.db`.

### Moving around

- The **Products** page (home) lists every product. Each card shows its scoring model, how many goals and items it has, and how many are done.
- **+ New product** asks for a name, an optional description, and a prioritization model (RICE, WSJF or MoSCoW; you can change it later).
- **Open .rmp file…** opens a [Roadmapper file](#roadmapper-files-rmp) as a new product.
- Opening a product shows its tabs: **Dashboard · Tree · Timeline · Now / Next / Later · Kanban · Priorities · Scoring · Snapshots · Import / Export · Settings**.
- Clicking any item title, bar or card anywhere in the app opens the [item editor](#4-editing-an-item).
- **Portfolio** at the top of every page opens the [application portfolio](#10-application-portfolio).

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
| Jira key, Link | Optional. A Jira issue key such as `WMS-123`, or any web address. See [Linking roadmap items to Jira](#linking-roadmap-items-to-jira). |
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
| **RICE** ([what it is](#rice-explained)) | Reach, Impact (0.25–3), Confidence % (0–100), Effort (person-months) | (Reach × Impact × Confidence/100) ÷ Effort |
| **WSJF** ([what it is](#wsjf-explained)) | Business value, Time criticality, Risk reduction, Job size | (BV + TC + RR) ÷ Job size |
| **MoSCoW** ([what it is](#moscow-explained)) | Must / Should / Could / Won't; optional Effort (only used to place it on the matrix) | Must = 4, Should = 3, Could = 2, Won't = 1 |

New to these models? [Section 13](#13-scoring-models-explained) explains each one and [how to choose](#choosing-a-model).

- An item with missing inputs (or an Effort/Job size of 0) has no score and shows **–**.
- **Ranking order:** items with a manual rank come first (by rank), then by score from highest to lowest, then unscored items.
- **Switching models** recalculates every score. Inputs for the other models are kept, so you can switch back without losing them.
- **WSJF scoring rubric:** under WSJF, the four inputs can be worked out from a few answers per item instead of typed. See [Scoring](#scoring-wsjf-rubric).

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
- **Priority:** the inputs for the product's scoring model, a live score preview, and **Manual rank**. Under WSJF with a [scoring rubric](#scoring-wsjf-rubric), each factor shows its criteria to answer, the calculated value and how it was worked out, plus **Enter by hand** / **Use rubric** to switch between typing the value and calculating it.
- **Links:** the item's Jira key and/or a web link, with **Open ↗**.
- **Classification:** theme and tags.
- **Tasks** (Features only): **+ Add task**, **Add from template…** and **Save tasks as template…**. It also lists each template batch added to this feature with an **Undo** button. If any task in a batch was edited since it was added, Undo asks you to confirm first.
- **Created from template X on date** (Tasks made from a template). This is a reference only; editing or deleting the template never changes the task.
- **Dependencies:** the items this one is *blocked by* and the items it *blocks*, with schedule-overlap warnings. Add one with **This is blocked by / This blocks**, choose the other item, then **Add**.
- **Applications** (existing items): the portfolio applications this item changes, with the kind of impact (Enhances, Implements, Replaces, Retires, Integrates, Upgrades). Linked items show on the application's page, in impact analysis and on the portfolio dashboard.
- **Benefits** (existing items): what the item is expected to deliver and how much is realized, with **+ Add benefit**. See [Benefits](#benefits).
- **Documents** (existing items): the item's SOW, BRD and other documents, with **+ Upload** and **+ Link**. See [Documents](#documents-sharepoint).
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

### Scoring (WSJF rubric)

A **scoring rubric** works out the four WSJF inputs (Business value, Time criticality, Risk reduction and Job size) from a few answers about each item, instead of you typing the numbers. Each product has its own rubric, set up under **Settings → WSJF scoring rubric**. It only calculates while the product's model is WSJF.

**Criteria.** A rubric is a list of criteria. Each one belongs to one factor and is answered in one of three ways:

| Answered by | You enter | Example |
|---|---|---|
| **Number with bands** | A number. Each band applies from its "from" value up to the next band; numbers below the first band use the first band. | Order lines: from 0 → 1, from 1,000 → 3, from 5,000 → 5, from 15,000 → 8, from 30,000 → 13 |
| **Pick-list** | One of the labelled choices. | Version: 4.2 → 8, 6.0.8 → 5 |
| **From the start date** | Nothing. The number of whole months from this month to the item's start month picks the band (0 or less means it has started or starts this month). Items without a start date aren't scored by it. | Start: from 0 → 13, from 4 → 8, from 10 → 5, from 16 → 3 |

Each criterion either **sets the score** (its values are scores on the scale) or is an **adjuster** (its values are steps, such as +1 to move the score one value up the scale).

**The scale** is the list of allowed scores, by default **1, 2, 3, 5, 8, 13, 20**. Change it in Settings; it can't change while a criterion uses a score that isn't on the new scale.

**How a factor is worked out:**

1. Take the answered criteria that set the score. With one, use its score. With several, average them and round to the nearest value on the scale (halfway rounds up).
2. Add up the answered adjusters' steps and move that many places along the scale, stopping at either end.
3. If no criterion that sets the score is answered, the factor isn't calculated and keeps whatever value it already has.

**Worked example** (Chervon NA, from a site-upgrade plan): 25,065 order lines → **8**; started Aug 2026 → **13**; on version 4.2 → **8**; 3 integrations → 5, then 116 named users (an adjuster: from 61 → +1 step) moves it up one step → **8**. WSJF = (8 + 13 + 8) ÷ 8 = **3.6**.

**Typed values win.** Each factor on each item is either calculated or **entered by hand**:

- Values typed before a rubric existed, values typed for a factor the rubric doesn't cover, and WSJF values from a spreadsheet import all count as entered by hand. The rubric never overwrites them, so adding a rubric changes no existing scores.
- Switch a factor with **Use rubric** / **Enter by hand** in the item editor, or with ✎ on the Scoring tab. Switching to "by hand" starts from the calculated value.

**The Scoring tab** lists every item at one level (Initiatives or Features by default), with one column per criterion, each factor's score, the WSJF score and the rank.

- Answers save as soon as you change them; each one is an ordinary item edit, so roll-ups, scores and history update.
- Hover a factor's score to see how it was worked out. **By rank** sorts by ranking order; **Tree order** follows the Tree.
- **Recalculate** re-scores with today's date. Start-date criteria change as months pass; the app also re-scores every rubric product when it starts.
- With no rubric yet, **Start from example** adds a simple one (a pick-list each for Business value, Risk reduction and Job size, and the start-date bands above for Time criticality) to edit.

**History:** answer changes appear in the item's history as "Rubric: *criterion name*", next to the factor changes they caused. Re-scoring because time passed isn't recorded.

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

`Type, Title, Parent, Description, Status, Progress, Start, End, Horizon, Owner, Theme, Tags, Jira key, Link, Reach, Impact, Confidence, Effort, Business value, Time criticality, Risk reduction, Job size, MoSCoW, Manual rank, From template, Template applied, Score, Id`

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
- **WSJF scoring rubric:** the scale, and the criteria grouped by factor, with **+ New criterion**, **Edit**, ↑/↓ and **Delete…** (deleting a criterion deletes every answer to it). **Start from example** appears while the rubric is empty. See [Scoring](#scoring-wsjf-rubric).
- **Danger zone:** **Delete product…** asks you to type the product's name, then permanently deletes the product and its whole roadmap, along with its themes, tags, templates, snapshots and history.

---

## 10. Application portfolio

The portfolio sits above your roadmaps. It records the systems you own and run (applications), the companies behind them (vendors and contracts), the customers and carriers you exchange data with (trading partners), how data moves between all of them (integrations), the business processes that depend on them, what roadmap work is expected to deliver (benefits), why things were decided (the decision log), and where the paperwork is (documents kept in SharePoint). It links back into your roadmaps instead of copying them, so status, dates and progress always come from the roadmap.

Open it with **Portfolio** at the top of any page; **Capabilities**, next to it, compares applications feature by feature (see [Capabilities](#capabilities-comparing-applications)). Its tabs are **Dashboard · Applications · Trading partners · Integrations · Map · Processes · Impact analysis · Benefits · Decisions · Documents · Vendors & contracts · Contacts · Settings**.

On a brand-new database, Roadmapper adds a made-up 3PL sample portfolio (a WMS, TMS, ERP, EDI provider, customer portal and more) linked to the sample roadmap. In an existing portfolio, the Integrations, Processes, Benefits and Decisions pages offer **Add sample …** buttons while they're empty and the sample applications are present.

### Where to start

A good order for entering your own portfolio:

1. **Contacts and vendors:** the people (yours and the vendors') and each vendor's contracts with renewal dates and notice periods.
2. **Applications:** each system, its vendor and contract, owners, tier and fit scores.
3. **Trading partners:** your customers and carriers, with their account codes and EDI identifiers.
4. **Integrations:** every flow between applications and partners, with a business purpose, tier and any known risks.
5. **Business processes:** the work the operation does, which applications each step uses and which integrations it relies on.
6. **Benefits** on your roadmap initiatives, and **decisions** as you make them.

You don't need everything before it's useful: the integration map and impact analysis work as soon as applications and integrations are in.

### Dashboard

The first page of the portfolio, for a one-page status update.

- **Tiles:** application health (how many are Green, Amber, Red), annual run cost (the sum of applications' annual cost), live integrations and how many are high risk, value realized per year against expected, renewals due, and open pain points.
- **Application health:** the least healthy applications first, each with its score and main issue. Hover a health badge to see everything that took points off. Below it: how many applications fall in each TIME quadrant.
- **Upcoming renewals:** contracts whose notice deadline falls within the renewal warning window (120 days unless changed in [Settings](#portfolio-settings-backup-and-export)), including deadlines already missed.
- **Integrations needing attention:** live integrations at high or medium risk, worst first, with overdue reviews flagged.
- **Value by category:** expected and realized dollars per year for each benefit category.
- **Strategic roadmap work:** roadmap items linked to an application or carrying benefits, in progress first, with progress from the roadmap.
- **Recent decisions.**
- **Export to Excel** downloads the whole portfolio (one sheet per kind of record); **Print** prints the page.

### Applications

**Applications** lists every system with filters, tiles for cost and renewals, and a TIME view. Click one to open its page.

| Field | Notes |
|---|---|
| Name | Required and unique. |
| Category | WMS, TMS, YMS (yard management), ERP, EDI, Customer portal, Reporting, Integration platform, Finance, Other. Customers and carriers are [trading partners](#trading-partners), not applications. |
| Tier | How critical the system is to operations. **Tier 1:** operations stop without it (you can't receive, ship or bill). **Tier 2:** important, but there's a workaround for a while. **Tier 3:** inconvenient if it's down. Tier raises the weight of problems in [health](#application-health) and the severity of [impact analysis](#impact-analysis). |
| Status | Planned, Active, Retiring, Retired, Evaluating. Retiring means you're moving off it; Retired applications aren't health-rated. **Evaluating** is for candidates you're comparing but don't run (e.g. a YMS you're considering): they can have vendors, contacts, documents, decisions and feature ratings, but are left out of run cost, health and the dashboard. |
| Hosting | SaaS, Hosted, On-premises. |
| Vendor and contract | The contract must be one of the vendor's. The application's renewal and notice dates come from the contract. |
| Own roadmap | The product whose roadmap is this application's. One application per roadmap. |
| Business and technical owner | People from Contacts. |
| Annual cost | This application's share per year (one contract can cover several applications). Accepts cents. |
| Business fit, technical fit | 1–5 each. Together they place the application in a **TIME** quadrant: **Invest** (both 3+), **Migrate** (business 3+, technical below 3), **Tolerate** (technical 3+, business below 3), **Eliminate** (both below 3). |
| Go-live, retire date, description, notes | Free. |

An application's page also shows its **health**, support contacts (with roles such as L1 support or key user), its **integrations**, a **dependencies** list and map, the **business processes** that use it, the **roadmap items** linked to it (from any roadmap, with the kind of impact: Enhances, Implements, Replaces, Retires, Integrates, Upgrades), its **benefits**, its **decisions** and its history. **Impact analysis** opens the [impact report](#impact-analysis) for it.

You can't delete an application that integrations still use; set it to Retired instead, or delete the integrations first.

#### Application health

Each application that isn't retired gets a score from 0 to 100. It starts at 100 and loses points for:

| Problem | Points |
|---|---|
| Part of a high-risk integration | 25 |
| Otherwise, part of an integration with an open risk scoring 6+ | 10 |
| Integration reviews overdue | 5 each, at most 15 |
| Contract notice deadline missed (it will renew as is) | 20 |
| Contract notice deadline within the renewal warning window | 10 |
| Retiring but still in live integrations | 15 |
| TIME: Eliminate / Migrate / Tolerate | 20 / 15 / 5 |

Tier 1 applications lose 50% more points and Tier 3 applications 25% fewer. **Green** is 80 and up, **Amber** 60–79, **Red** below 60.

### Vendors, contracts and contacts

- **Vendors & contracts:** each vendor has contracts (number, start, end, renewal date, notice period in days, auto-renews, annual value), its people, an account manager and technical contact, support phone and portal, and an ordered **escalation path** (level 1 is called first). The **notice deadline** is the renewal date minus the notice period: the last day to cancel or renegotiate. It shows amber when it's within the renewal warning window and red once missed.
- **Contacts:** every person, internal or at a vendor or trading partner. When you delete someone, the editor first lists every role they hold (owner of, account manager for, escalation level, risk owner…), so it's never a surprise. Clear **Active** when someone leaves to keep their history.

### Trading partners

Customers, carriers, suppliers and other outside companies that exchange data with your applications. You track who they are and how to reach them, not their systems.

- **Type:** Customer, Carrier, Supplier, Other. **Status:** Onboarding (being set up and tested), Active, Inactive.
- **Importance:** **Key** partners are flagged everywhere and make impact analysis High severity when their flows are affected.
- **Identifiers:** account/customer code (your ID for them in the WMS or ERP), EDI qualifier and ID (ISA, shown as `01:123456789`), and GS ID if different.
- **Relationship owner** and the partner's own people (EDI coordinator, IT contact).

A partner's page shows its integrations, the applications its flows pass through (dependencies and map), decisions and history. A partner with integrations can't be deleted; set it to Inactive instead.

### Integrations

One integration is one flow of data, from a **source** to a **target**, optionally through a middle hop (**via**: an EDI provider, VAN or integration platform such as Cleo or Boomi). Each end is one of your applications or a trading partner; at least one end must be an application, and the via is always an application.

| Field | Notes |
|---|---|
| Source, via, target | Choose from applications and partners. A blank name becomes "Source → Target". |
| Type | API, SFTP, EDI, Manual, Batch file. |
| Direction | **One-way** (source → target) or **Bidirectional** (data also comes back, e.g. orders in and ship confirmations out on one connection). |
| Frequency | Real-time, Near real-time, Hourly, Daily, Weekly, On demand. |
| Message format | E.g. `X12 940`, `JSON`, `CSV`. |
| Business purpose | Required: what the flow is for. |
| Tier | Business criticality of the flow, same scale as applications. |
| Owner, status | Status: Planned, Active, Retiring, Retired (Evaluating is for applications only). Retired flows are ignored by the map, dependencies, health and impact analysis. |
| Last review, review interval | When it was last reviewed and how often it should be (months). **Mark reviewed** on the integration's page sets today. A live integration that was never reviewed, or whose interval has passed, is **review overdue**. |

**Risks:** each integration has a risk register. A risk has a description, **likelihood** and **impact** (1–5 each), a **score** (likelihood × impact, 1–25), mitigation, owner and status (Open, Mitigated, Accepted, Closed). Open and Accepted risks count; Mitigated and Closed don't.

**Risk level** of an integration:

- **High:** an open or accepted risk scores 15+, or 8+ on a Tier 1 integration.
- **Medium:** a risk scores 6+, or the review is overdue.
- **Low:** otherwise. Retired integrations are always Low.

**Tip:** for a customer's two-way EDI flow, enter the **customer as the source** (their orders start the flow). The map draws flows in the direction you record them, so customers then sit on the left, feeding your systems.

### The integration map and dependencies

**Map** draws applications as boxes (the colored stripe on the left is the tier) and trading partners as rounded pills (a purple dot marks key partners). Integrations are arrows colored by type, dashed when manual or planned and faded when retiring, with a dot on each colored by its risk level. Click a box, pill or line to open it.

- **Whole portfolio:** columns run Trading partners · EDI & integration · Portals · Warehouse & transport · ERP & finance · Reporting & other. Filter by type or tier, show retired flows, or hide trading partners.
- **Focus on** one application or partner, with a **depth** of 1–5 steps, to see what feeds it and what it feeds. The same view appears on application and partner pages as **Dependencies**.

How dependencies are worked out:

- **Each integration is one step.** A flow routed through a via counts as source → via, via → target and also source → target.
- **Bidirectional** flows count both ways.
- **Partners are end points.** A walk reaches a partner but never continues through it, so one customer isn't a "dependency" of another just because both send to your WMS.
- **A partner's data stays on its own flows.** A flow with a partner at one end carries only that partner's data, so once the walk has followed one partner's flow it won't continue along another partner's. Flows between your own applications carry everyone's data and are always followed. That's why Boomi's map shows only the customers Boomi carries, not Cleo's.
- **Layout:** each integration is drawn source → via → target as you recorded it, starting with the focus's own flows. A routed path stays in a straight line; a direct flow between the same two systems arcs around it.

### Business processes

**Processes** is a tree of what the operation does, e.g. **Order to Cash** with steps **Order intake**, **Pick, pack and ship** and **Customer invoicing**. Each process has a domain (Inbound, Inventory, Outbound, Transportation, Billing, Customer service, Reporting, Other), an owner and a description. A process can't sit under itself or one of its own steps, and a process with steps can't be deleted until they're moved or deleted.

On a process's page:

- **Applications used:** what it uses each for, and which is the **primary** system. This is how a change to an application reaches the process in impact analysis.
- **Integrations relied on:** flows the process stops or degrades without. Integrations touching the process's applications are listed first.
- **Pain points:** what slows the process down or causes errors, with severity (Low to Critical), the application where it hurts, and status (Open, Addressed, Won't fix). Link each to the roadmap item that fixes it, or choose **Create an initiative**: pick a roadmap and a goal, give it a title, and the pain point's editor creates the initiative (as an Idea, with the pain point as its description) and links it.
- **KPIs:** name, unit, direction (higher or lower is better), baseline, target and current value. The bar shows how far current has moved from baseline toward target; a KPI is **on target** when current reaches the target in the right direction.

The Processes list flags steps with no applications or integrations: impact analysis can't reach them.

### Impact analysis

Choose an application or trading partner that's about to change (an upgrade, an outage, a replacement, a new partner mapping) and a depth. The report lists everything it touches:

- **Integrations:** **direct** ones (the subject is the source, target or via) and **indirect** ones (they carry the subject's data further downstream, up to the depth), with type, tier and risk. The dependency rules above apply.
- **Trading partners** and **applications** at the other ends, key partners first, with their owners.
- **Business processes** that use the application or rely on an affected integration, plus the processes above them ("a sub-process is affected"), with open pain points.
- **Roadmap items** linked to the subject or to an application it exchanges data with directly. **In flight** (Planned or In progress) items come first: they could collide with the change.
- **Vendors to coordinate with:** of the subject and of the applications it exchanges data with directly, with their first escalation contact, support phone and next notice deadline.
- **Past decisions** about the subject, its direct integrations, its vendor or contract, or the affected processes.
- **Open risks** on the affected integrations and **open pain points** on the affected processes.

**Severity:**

- **High:** the subject is a Tier 1 application or a key partner, a directly affected integration is Tier 1 or already high risk, or any key partner's flows are affected.
- **Medium:** any Tier 1–2 integration, process or in-flight roadmap item is affected.
- **Low:** otherwise.

**Copy as text** copies the report as plain text for an email or ticket; **Print** prints it.

### Benefits

A benefit is value a roadmap item (usually an initiative) is expected to deliver. Add one with **+ New benefit** on the Benefits page, or **+ Add benefit** in a roadmap item's editor.

| Field | Notes |
|---|---|
| Delivered by | The roadmap item. Deleting the item deletes its benefits. |
| Category | Cost savings, Labor savings, Revenue impact, Adoption, Risk reduction, Other. |
| Unit | US dollars, Hours, Percent, Count. |
| Application | Where the benefit lands, for context (shown on that application's page). |
| Baseline | The starting level, for benefits measured as a level (adoption %, error rate, a run-rate cost). Leave empty for savings counted from zero. |
| Expected | Per year once fully realized, or the target level when there's a baseline. |
| Expected by | When it should be fully realized. |
| Measured by KPI | Optional process KPI. Its current value is used until the benefit has its own measurements, and its baseline when the benefit has none. |

**Measurements** (in the benefit's editor) record what the benefit is running at on a date: an annual figure for savings (e.g. "running at 520 hours a year") or the current level. One reading per day; the **latest** one is the actual.

How the numbers work:

- **Gain:** with no baseline, the actual itself. With a baseline, how far the actual has moved from the baseline toward the expected value. A baseline above the expected value means lower is better: a drop counts as gain.
- **Realized %** = gain ÷ (expected − baseline).
- **Dollars:** US dollars as they are; Hours × the **labor rate** (set on the Benefits page or in Settings); percentages and counts have no dollar value. Until a labor rate is set, hour-based benefits are left out of dollar totals and the page says so. A setback counts as $0 realized, not negative.
- **Status:** **Not measured** (nothing yet), **Realizing**, **At risk** (the expected-by date has passed and less than 50% is realized), **Realized** (100% or more).

The Benefits page groups benefits by roadmap item with totals; the product's Dashboard tab has a Benefits card; the item editor lists the item's benefits.

**Example: retiring a system to save run cost.** Attach the benefit to the "Retire Synapse" goal, category Cost savings, unit US dollars, application Synapse. Enter Synapse's current yearly cost as the **baseline** and its expected yearly cost after the last customer moves as **expected**. Each time a customer moves or the invoice changes, record the new yearly cost as a measurement. If the replacement costs more than the old system, don't record a negative saving: record what the move buys you (for example a Risk reduction benefit, percent of customers moved, baseline 0, expected 100), put each system's annual cost on its application, and record the trade-off in the [decision log](#decision-log).

### Decision log

**Decisions** records the calls that shape the portfolio so the reasoning isn't lost.

- **Fields:** title, decided on, status (**Proposed**, **Decided**, **Superseded**, **Reversed**), decided by, context, the decision, the outcome (fill it in later), and **alternatives considered** (option, pros, cons, why not chosen).
- **Superseded by:** when status is Superseded, choose the later decision that replaced it. The later decision's page lists what it replaced. Chains can't loop.
- **Linked to:** link a decision to any number of applications, integrations, vendors, contracts, processes, roadmap goals or initiatives, and trading partners. It then shows in the **Decisions** section of each of those pages and in impact analysis. **+ Record decision** on any of those pages creates a decision already linked to it.
- Filter the log by status, by what decisions are linked to, or to those **missing an outcome**. Deleting a linked record removes the link but keeps the decision; prefer **Reversed** to deleting a decision.

### Documents (SharePoint)

Roadmaps, contracts, invoices, SOWs and BRDs stay in SharePoint. Roadmapper keeps a list of them, organized by product and attached to the records they belong to, and can upload new ones. It doesn't sign in to SharePoint. Instead, each type of document has a folder on your PC that **OneDrive syncs** to a SharePoint folder: Roadmapper saves the file there and OneDrive uploads it.

**Folders per product.** Each product has its own folder for each type (**Roadmap, Contract, Invoice, SOW, BRD, Other**), set on that product's **Settings › Document folders**. There's also a set of **shared** folders in Portfolio › Settings, for documents that don't belong to one product (a vendor's master agreement, say) and for any product that hasn't set a folder of its own for that type. An upload goes to the product's own folder for its type; if the product has none, to the shared one; if neither is set, the upload asks you to set one up.

**Setting up a folder:**

1. In SharePoint, open the folder (for example *Synapse › Roadmaps*) and choose **Add shortcut to My files** (or **Sync**). After a moment it appears on your PC, e.g. `C:\Users\you\OneDrive - Company\Team - Synapse Roadmaps`.
2. On the product's Settings tab, under **Document folders**, enter that path as the type's **Folder on this PC**. It must already exist: Roadmapper never creates it, so a typo can't collect files that never reach SharePoint.
3. Paste the same folder's **SharePoint address**: the browser's address bar, or **Copy link** (a link with `/:f:/r/` in it). Roadmapper turns either into the folder's plain address and uses it to open the files. A "people in your organization" sharing link (`/:f:/s/…`) hides the folder's path and is refused; copy the address bar instead.
4. **Save folders.** Each row shows **✓ Found**, **⚠ Not found**, **Uses shared** (blank here, but a shared folder is set) or **Not set**.

**Which product a document belongs to.** The upload and link dialogs have a **Product** picker, filled in from where you started:

- From a product's **Dashboard**: that product.
- From a **roadmap item** (the item editor's Documents section): the item's product. This can't be changed; an item's documents always belong to its product.
- From an **application**: the application's own roadmap, if it has one.
- From a **vendor** or **contract**, or the Documents tab: you choose (or leave it shared), since one vendor can serve several products.

The dialog shows exactly which folder the file will be saved to, and says when that's the shared folder.

**Adding documents.** The **Documents** tab lists every document, with search and filters by product, type and what it's attached to. A **Documents** section on each product's Dashboard (its documents), each application, vendor (with its contracts) and roadmap item shows the documents for that record.

- **+ Upload:** pick the type, product, an optional date (an invoice date, a signing date), what it's attached to, and the file. The title defaults to the file name. The file is saved under its own name (characters SharePoint doesn't allow are replaced with `_`). If a file with that name is already in the folder, the upload stops unless you tick **Replace a file with the same name**; SharePoint keeps the old one as a previous version. Files up to 250 MB.
- **+ Link:** for documents already in SharePoint. Open the document there, copy its address, and paste it with a title.
- **Open:** a document's title opens it in SharePoint (Office files open in the browser). **local copy** opens the synced copy on this PC, which works offline. Without a SharePoint address for its folder, the title opens the local copy.
- **Edit** changes the title, type, product, date, notes or what it's attached to. It doesn't move or rename the file. **Remove** takes it off Roadmapper's lists; the file stays in SharePoint. Deleting a record that has documents leaves them unattached; deleting a product removes its folder settings and makes its documents shared.

Uploads appear in SharePoint once OneDrive has synced them (usually seconds). If OneDrive is paused or signed out, files wait in the folder and upload later.

### Capabilities: comparing applications

**Capabilities** (at the top of every page) compares applications feature by feature, one **application category** at a time: for example every YMS you run or are considering, rated on dock scheduling, yard visibility, gate security and so on. The first page shows each category with how many features and applications it has and how much is rated; open a category to work on it. It has four tabs: Comparison matrix, Heat map, Cost and Features & applications.

**Features & applications** (set up first):

- **Features:** the capabilities this category is compared on, in the order they're shown. Add one by typing its name; **Edit** renames it or adds a description (what counts as having it); **↑ ↓** reorder; **Delete** removes it and its ratings.
- **Compared applications:** every application of the category is included automatically. **Add** brings in applications from other categories that cover some of the same ground (a WMS with yard features, a telematics tool); **Remove** takes an added one out again (its ratings are kept for if it comes back). Candidates you're only considering: add them as applications with status **Evaluating** first.
- **Show retired applications** includes retired ones, which are otherwise hidden.

Each cell has **two ratings**, set together by clicking it on the comparison matrix (or a feature row in an application's **Features** section):

| Rating | Values | Used by |
|---|---|---|
| Has it? | ✓ Yes, ◐ Partial, ✗ No (or not rated) | The comparison matrix |
| Strength | None, Low, Medium, High (or not rated) | The heat map |
| Note | Free text, e.g. "through an add-on module" | Hover text, Excel comments |

**No** means no strength: choosing it sets strength to None, and a higher strength can't be saved with it.

**Comparison matrix:** features down the side, applications across (the category's own first, then added ones, each alphabetically; Evaluating and added ones are labeled). Each cell shows ✓ / ◐ / ✗ shaded by strength, with a dot when there's a note. **Have it** counts the applications with a Yes (+ partials). **Gap**: no application has the feature fully. **Overlap**: two or more do. The **Coverage** row shows each application's Yes count out of all features and how much of it is rated. **Only gaps** filters to the gaps.

**Heat map:** applications down the side and features along the bottom, colored by strength (None grey, Low yellow, Medium orange, High red; not rated is white and hatched), with a legend. **Features down the side** swaps them. Hover a cell for its rating and note. **Download SVG** saves a picture you can insert straight into PowerPoint; **Print** prints the page.

**Export to Excel** writes a workbook with a **Comparison** sheet (Yes/Partial/No, coverage, gaps and overlaps) and a **Heat map** sheet (colored strengths), with notes as cell comments. Once anything is priced it adds a **Cost** sheet: every price costed for the selected site profile, and each application's monthly, one-time and first-year totals.

Each application's page has a **Features & cost** section listing its ratings and its prices in every comparison it's part of, with links to the matrix, heat map and cost.

#### Cost of adoption

Solutions are adopted one site (or customer) at a time, so the **Cost** tab answers *"what would this cost a site like ours, per month?"*. Vendors price differently (per user, per dock door, per site, per transaction), so you enter each price **the way it's quoted**, and describe a typical site once; Roadmapper works out comparable figures.

**Site profiles** describe a typical site: e.g. "Small DC: 15 users, 12 dock doors, 2,000 transactions a month". Sizes: users, licenses, dock doors, trailers / assets, transactions per month (leave one blank if you don't know it). **Add site profile** creates one; the first becomes the **default** (used when you open the comparison; change it in **Edit profile**). Pick a profile in **Costed for** to see every number for that size of site. **Try your own site…** lets a site type its own sizes and see the result without saving anything (**Save as a site profile** keeps it).

**Prices**: each application has a card on the Cost tab; **Add price** adds one line of its quote, and clicking a line edits or deletes it:

| Field | Meaning |
|---|---|
| Name | e.g. Subscription, Implementation, Hardware, Training |
| Priced | Flat, Per site, Per user, Per license, Per dock door, Per trailer / asset, Per transaction, or per another unit you name (e.g. kiosk) |
| Price, How often | The unit price; Monthly, Annual or One-time. Per-transaction prices are monthly (× transactions per month). |
| Minimum | Optional: the least the line costs per period (e.g. $500 a month) |
| How many | Optional: a fixed quantity from the quote, used instead of the site profile's (required for "another unit") |
| Source, As of, Note | Quote, List price or Estimate; the date of the price; anything else (term, discounts) |

**What's worked out**, for the selected site: **Monthly** (monthly prices plus annual ones ÷ 12), **One-time** and **First year** (12 months + one-time), plus monthly cost **per user** and **per dock door**. Flat and per-site prices count once. The table on the Cost tab ranks the applications cheapest first, with how many features each has; the cheapest fully costed one is marked **Lowest monthly**.

If a price is per something the site profile doesn't size (e.g. per dock door, with no dock doors in the profile), it can't be costed: the application is marked **Incomplete**, its totals show "≥" (they leave that price out), and the hover text says which size is missing. An application with no prices shows **Not priced**; neither ever counts as $0.

The **comparison matrix** shows **Monthly**, **One-time** and **First year** rows under Coverage once anything is priced, with the cheapest highlighted and a **Cost for** picker to switch site profile.

### Portfolio settings, backup and export

**Settings:**

- **Labor rate ($ per hour):** values labor-hour benefits.
- **Renewal warning (days):** how far ahead notice deadlines count as upcoming (default 120). Affects the dashboard, renewal lists and health.
- **Integration review interval (months):** the starting interval for new integrations (default 12).
- **Jira site:** e.g. `https://yourcompany.atlassian.net`. Roadmap items with a [Jira key](#linking-roadmap-items-to-jira) link to `{site}/browse/{key}`.
- **Document folders:** the shared folders, used for documents without a product and by products with no folder of their own for that type. Each product's folders are on its Settings tab. See [Documents](#documents-sharepoint).

**Back up the portfolio** downloads a **portfolio file (.rpf)**: every application, vendor, contract, contact, trading partner, integration, risk, process, pain point, KPI, benefit, measurement, decision, capability feature and rating, the document list (not the files, which stay in SharePoint), the settings including every product's and the shared document folders, and the change history. Roadmaps aren't in it; back those up as [.rmp files](#roadmapper-files-rmp). Links into roadmaps are saved by roadmap name and item path, and reconnect when a matching roadmap exists.

**Import a portfolio file:**

- **Restore** (only into an empty portfolio): brings everything back, including settings and history. Use it to move to a new computer: open your .rmp files first, then restore the .rpf.
- **Merge:** adds to what's here. A record that already exists is skipped and listed, and what belongs to it (a vendor's contracts, an integration's risks, a process's pain points) comes with it only when the record is new. References point at the existing record, so integrations in the file connect to applications you already have. Settings and history aren't changed.
- **Matching rule for Merge:** applications, vendors, trading partners and processes by name; contacts by name and company; integrations by source, target, type and name; benefits by roadmap item and description; decisions by title and date; documents by SharePoint address or saved file path; capability features by category and name (ratings by feature and application); site profiles by category and name; prices are added only for an application with no prices yet in that comparison.
- Roadmap links with no matching roadmap or item are listed after the import instead of failing it. Import the missing roadmaps' .rmp files, then merge again, or link them by hand.
- The import runs in one transaction: if anything fails, nothing is added.

**Export to Excel** (Dashboard) writes one sheet each for Applications (with health), Vendors, Contracts, Trading partners, Integrations, Risks, Processes, Pain points, KPIs, Benefits, Decisions, Documents, Features (every rating in every category) and Contacts.

**Read-only API:** everything is also available as JSON under `/api/portfolio/` (for Power BI or Excel's **Get Data › From Web**). See [Portfolio API](#portfolio-api).

### Linking roadmap items to Jira

Roadmapper isn't a replacement for Jira; Jira stays the system of record for stories and sprints. To jump across, give a roadmap item a **Jira key** (e.g. `WMS-123`; it's upper-cased) or a **Link** (any `https://` address) in the editor's **Links** section. With the Jira site set in Portfolio › Settings, the key becomes a link; a Link, when set, is used instead. The Tree shows a small **↗ WMS-123** next to linked items. Jira key and Link are also columns in spreadsheet export and import, and travel in .rmp files.

---

## 11. Reference for AI assistants and developers

### Architecture

- **Stack:** .NET 10, Blazor Server (global interactive server rendering), EF Core 10 with SQLite.
- **No authentication;** single user.
- **Schema changes:** migrations apply automatically on startup (`DbInitializer`).

| Project | Responsibility |
|---|---|
| `src/Roadmapper.Core` | Domain model and pure rules, with no database access: `Entities.cs`, `Enums.cs`, `HierarchyRules`, `RollupService`, `Scoring`, `PriorityMatrix`, `DependencyRules`, `ProductRoadmap` (a read-only in-memory view), `Snapshots`, `TaskTemplates` (`TemplateRules`, `StarterTemplates`), `ScoringRubric` (`RubricCalculator`, `RubricScale`, `RubricRules`, `StarterRubric`), `TimelineLayout`, `RoadmapColors`. `Exchange/` holds the CSV, column, import-planning and template-sheet code. |
| `src/Roadmapper.Data` | `RoadmapDbContext`, migrations, and `RoadmapService` (all reads and writes; a short-lived DbContext per call). `RoadmapService.Templates.cs` holds the template operations and `RoadmapService.Rubric.cs` the scoring rubric. `AuditTrail` produces the change history. `Exchange/` holds the XLSX/CSV export, the table reader, and the PDF report (`RoadmapPdf`, `GanttPdf`). |
| `src/Roadmapper.Web` | Blazor pages (`Components/Pages/Product*.razor`) and shared components (`ItemEditor`, `TemplateEditor`, `TemplatePicker`, `DragBoard`, …). `Exports/ExportEndpoints.cs` provides the file downloads. `Launcher.cs` handles start-up for the packaged exe (port choice, opening the browser). `UserGuide.cs` renders this guide (embedded from `docs/USER_GUIDE.md`) for `/guide`. The assembly, and so the packaged exe, is named `Roadmapper`. |
| `src/Roadmapper.Core/Portfolio` | The portfolio's entities, enums, drafts (`Validate`/`ApplyTo`) and pure rules: `PortfolioRules` (notice deadlines, TIME, risk level, process tree, KPI progress), `PortfolioText` (labels), `PortfolioGraph` (dependency walks), `MapLayout`, `ImpactAnalyzer`, `BenefitMath`, `PortfolioHealth`, `Capabilities.cs` (`CapabilityMatrix`, `CapabilityRules`), and `PortfolioDocuments.cs` (`DocumentLibrary`: SharePoint addresses and file names). `Core/Exchange/PortfolioFile.cs` is the .rpf format; `Core/ExternalLinks.cs` the Jira link rules. |
| `src/Roadmapper.Data/Portfolio` | `PortfolioModel` (EF mapping, applied by `RoadmapDbContext`), `PortfolioAuditTrail` (portfolio history, captured in the same `SaveChangesAsync` override), and the partial `PortfolioService` (one file per area: Applications, Vendors, Contacts, Partners, Integrations, Processes (with impact), Benefits, Decisions, Documents, Capabilities, Dashboard, Files (.rpf), Sample). `PortfolioExport` writes the portfolio workbook and `CapabilityExport` a category's comparison workbook. |
| `src/Roadmapper.Web` (portfolio) | `Components/Pages/Portfolio/*.razor` and `Components/Shared/Portfolio/*` (editors, badges, `PortfolioHeader` tabs, `PortfolioUi` formatting). `Api/PortfolioApi.cs` is the read-only JSON API; `Exports/IntegrationMapSvg.cs` draws the map. |
| `tests/Roadmapper.Tests` | xUnit tests, including the Task Templates acceptance checks (`TaskTemplateTests.cs`) and the portfolio tests (`PortfolioServiceTests`, `IntegrationServiceTests`, `TradingPartnerTests`, `PortfolioGraphTests`, `ImpactAnalyzerTests`, `ProcessServiceTests`, `BenefitTests`, `DashboardAndDecisionTests`, `PortfolioFileTests`, `DocumentTests`, `CapabilityTests`, `CapabilityCostTests`). |

### Data model

- `Product`: owns everything below; deleting it cascades.
- `RoadmapItem`: **one table for all four levels**.
  - Linked by `ParentId` and `ItemType` (Goal=0, Initiative=1, Feature=2, Task=3).
  - Fields: `Title, Description, Status, ProgressPct, StartDate, EndDate (DateOnly), Horizon, Owner, JiraKey, ExternalUrl, ThemeId, SortOrder, ManualRank, IsRollupOverridden, CreatedAt, UpdatedAt`.
  - Template reference fields: `SourceTemplateName, SourceTemplateAppliedAt, TemplateBatchId`.
  - Deleting an item cascades to its children, score, tag links and dependencies.
- `ItemScore`: 1:1 with an item. Holds the inputs for all three models plus `ComputedScore`, and `ManualFactors` (a `[Flags] WsjfFactors` stored as an int: BusinessValue 1, TimeCriticality 2, RiskReduction 4, JobSize 8) marking WSJF factors typed by hand.
- `Product.WsjfScale`: the rubric's scale as text (`"1, 2, 3, 5"`); null means the default 1, 2, 3, 5, 8, 13, 20.
- `ScoringCriterion` (table `ScoringCriteria`): `ProductId, Factor (WsjfFactor), Role (Base | Adjuster), Kind (NumberBands | Choice | StartDate), Name, Help, SortOrder`, with ordered `ScoringCriterionOption`s: `Label, From (bands), Value` (a score for Base, steps for Adjuster).
- `ItemRubricAnswer` (key `ItemId, CriterionId`): `Number` (number bands) or `OptionId` (pick-list). Start-date criteria have no stored answer. Deleting the item, the criterion or the chosen option deletes the answer.
- `Dependency`: `PredecessorId` blocks `SuccessorId`. The only type is `Blocks`.
- `Theme` (with a color) and `Tag`: belong to a product. Items link to tags through a many-to-many join table.
- `AuditEntry`: change history. It deliberately has no foreign key to the item, so history survives deletion.
- `Snapshot`: name, note and time, plus `PayloadJson`, a list of `SnapshotItem` records (id, parent, type, title, status, progress, dates, horizon, owner, score).
- `TaskTemplate` (unique, case-insensitive name per product) has ordered `TaskTemplateLine`s: `SortOrder, Title, Description, Owner, StartOffsetDays` (shown in the UI as **Gap**), `DurationDays`, and Tags.

### Portfolio data model

All portfolio tables live in the same database; enums are stored as text; names marked unique compare case-insensitively (`NOCASE`). Every table has `CreatedAt`/`UpdatedAt` unless noted.

- `Contact`: `Name, Title, Email, Phone, Department, Notes, IsActive`; `VendorId` or `TradingPartnerId` (cascade with the company), or neither for internal staff.
- `Vendor` (unique name): `Website, AccountManagerId, TechnicalContactId, SupportPortalUrl, SupportPhone, Notes`. `Contract` (cascade): `Name, ContractNumber, StartDate, EndDate, RenewalDate, NoticePeriodDays, AutoRenews, AnnualValue (decimal), Notes`; *NoticeDeadline = RenewalDate − NoticePeriodDays*. `VendorEscalationContact` (key `VendorId, ContactId`): `Level, Note`.
- `Application` (unique name): `Category, Criticality (Tier1–3), Status (Planned, Active, Retiring, Retired, Evaluating), Hosting, VendorId, ContractId (must be the vendor's), ProductId (unique when set: its own roadmap), BusinessOwnerId, TechnicalOwnerId, AnnualCost, BusinessFit, TechnicalFit (1–5), GoLiveDate, RetireDate, Description, Notes`. `ApplicationSupportContact` (key `ApplicationId, ContactId, Role`). `ApplicationInitiative` (key `ApplicationId, RoadmapItemId`, cascade both ways): `ImpactType`.
- `TradingPartner` (unique name): `Type, Status (Onboarding, Active, Inactive), Importance (Key, Standard), AccountCode, EdiQualifier (2), EdiId (35), GsId (35), RelationshipOwnerId, Notes`.
- `Integration`: `Name`; `SourceApplicationId` xor `SourcePartnerId`; `TargetApplicationId` xor `TargetPartnerId` (CHECK constraints); `ViaApplicationId`; all endpoint FKs are Restrict. `Type, Direction, Frequency, MessageFormat, BusinessPurpose (required), Criticality, OwnerId, Status, LastReviewDate, ReviewIntervalMonths, Notes`. `IntegrationRisk` (cascade): `Description, Likelihood, Impact (1–5, CHECK), Mitigation, Status, OwnerId, RaisedOn`.
- `BusinessProcess` (unique name): `Description, Domain, ProcessOwnerId, ParentProcessId (Restrict), SortOrder`. `ProcessApplication` (key `ProcessId, ApplicationId`): `Usage, IsPrimary`. `ProcessIntegration` (key `ProcessId, IntegrationId`). `ProcessPainPoint`: `Description, Severity, ApplicationId (SetNull), AddressedByItemId (SetNull), Status`. `ProcessKpi`: `Name, Unit, Direction, Baseline, Target, Current, MeasuredOn`.
- `Benefit`: `RoadmapItemId (cascade), ApplicationId (SetNull), KpiId (SetNull), Category, Description, Unit, ExpectedValue, ExpectedBy, Baseline, Notes`. `BenefitMeasurement` (unique `BenefitId, MeasuredOn`): `Value, Source, Note`.
- `Decision`: `Title, DecidedOn, Status, Context, DecisionText (column "Decision"), Outcome, DecidedBy, SupersededById (SetNull)`. `DecisionAlternative`: `Option, Pros, Cons, WhyRejected, SortOrder`. `DecisionLink`: exactly one of `ApplicationId, IntegrationId, VendorId, ContractId, ProcessId, RoadmapItemId, TradingPartnerId` (CHECK `CK_DecisionLinks_OneTarget`); each cascades with its target.
- `PortfolioDocument` (table `Documents`): `Title, Category (Roadmap, Contract, Invoice, Sow, Brd, Other), FileName, LocalPath, Url, SizeBytes, DocumentDate, Notes`; `ProductId` (the product it belongs to, null = shared, SetNull); at most one of `ApplicationId, VendorId, ContractId, RoadmapItemId` (CHECK `CK_Documents_OneTarget`), each SetNull. An uploaded file has `LocalPath` (and `Url` when its folder's address is set); a link has only `Url`.
- `CapabilityFeature` (table `CapabilityFeatures`, unique `Category, Name`): `Category, Name, Description, SortOrder`. `ComparisonMember` (key `Category, ApplicationId`; cascade with the application): an application added to another category's comparison. `FeatureAssessment` (key `FeatureId, ApplicationId`; cascade both ways): `Availability (Yes, Partial, No)?, Strength (None, Low, Medium, High)?, Note, UpdatedAt`. `PricingLine` (table `PricingLines`; cascade with the application): `Category, ApplicationId, Label, Basis (Flat, Site, User, License, DockDoor, Asset, Transaction, Other), Unit, Quantity?, UnitPrice, Frequency (Monthly, Annual, OneTime), Minimum?, Source (Quote, ListPrice, Estimate), AsOf?, Note, SortOrder`. `SiteProfile` (table `SiteProfiles`, unique `Category, Name`): `Category, Name, IsDefault, Users?, Licenses?, DockDoors?, Assets?, TransactionsPerMonth?, Notes`.
- `DocumentFolder`: `Id, ProductId (null = shared; cascade), Category, LocalPath, SharePointUrl`; unique `(ProductId, Category)` and unique `Category` where `ProductId` is null.
- `PortfolioSettings` (one row, Id 1): `LaborRate, RenewalWarningDays (120), DefaultReviewIntervalMonths (12), JiraBaseUrl`.
- `PortfolioAuditEntry`: `EntityType, EntityId, EntityName (snapshot), Action, Field, OldValue, NewValue, ChangedAt`; no foreign keys. Contracts and escalation are filed under the vendor; support contacts and initiative links under the application; risks under the integration; process links, pain points and KPIs under the process; measurements under the benefit; links under the decision.

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
12. **WSJF rubric** (`Core/ScoringRubric.cs`, `RubricCalculator`): a factor = the average of its answered Base criteria, snapped to the nearest scale value (halfway goes up), then moved by the sum of answered Adjuster steps, clamped to the scale. No answered Base criterion means the factor isn't calculated. Number bands and start-date bands pick the highest `From` ≤ the value, or the lowest band below that. Start-date value = `(start.Year − today.Year) × 12 + start.Month − today.Month`.
13. **The rubric writes into the ordinary WSJF fields**, so ranking, the matrix, snapshots and every export are unchanged. It runs only when the model is WSJF, never on a factor flagged in `ManualFactors`, inside `RecalculateAsync` (after roll-up, so parents use their rolled-up start) and inside `SaveItemAsync` before the save (so factor changes are audited). On save, a factor with a value that the rubric can't calculate (no Base criterion) is flagged as manual. Spreadsheet import flags every WSJF value it sets; the migration flagged every existing value.
14. **Rubric validation** (`RubricRules.Validate`): a name; at least one option (max 20); choices need unique labels; bands need unique `From` values; Base values must be on the scale; Adjuster values are whole steps from −6 to +6. The scale (`RubricScale.TryParse`) is 2–15 increasing positive numbers.
15. **Changing a criterion's kind** deletes its answers; removing an option deletes the answers that picked it. `RecalculateAllAsync` runs at startup and doesn't touch `UpdatedAt` or the history.

### Portfolio rules

16. **Integration ends:** each end is an application or a partner (CHECK); at least one is an application; source ≠ target; via ≠ either end; no duplicate (source, target, type, name). Applications and partners still used by integrations can't be deleted.
17. **Risk level** (`PortfolioRules.RiskLevel`): retired → Low; top active risk score ≥ 15, or ≥ 8 on Tier 1 → High; ≥ 6 or review overdue → Medium; else Low. Review overdue = Active or Retiring and (never reviewed or `LastReviewDate + ReviewIntervalMonths < today`).
18. **Dependency walk** (`PortfolioGraph`): retired integrations excluded; one step per integration (a via adds source→via and via→target as well as source→target); bidirectional both ways; partner nodes other than the start are end points; breadth-first over (node, carrying partner) states, where following a partner's integration sets the carried partner and an integration of a different partner is never followed. Each node is reported at its shortest distance with the integrations that reach it.
19. **Focus map layout** (`MapLayout.FocusColumns`): members come from the walk; placement lays out whole integrations in recorded direction (source, via, target), starting from the focus's own integrations (routed first, then by id), breadth-first, first placement wins; anything unplaced goes downstream by distance, else upstream.
20. **Impact** (`ImpactAnalyzer`): integrations = touching the subject (direct) ∪ the downstream walk's integration ids (indirect, at their distance); nodes = the other ends; processes = using the subject application or linking an affected integration, plus ancestors as "sub-process affected"; roadmap items and vendors = subject + applications at distance 1; decisions = linked to the subject, a direct integration, the subject's vendor or contract, or an affected (non-roll-up) process. Severity rules as in [Impact analysis](#impact-analysis).
21. **Health** (`PortfolioHealth.Assess`): as in [Application health](#application-health); points × 1.5 for Tier 1, × 0.75 for Tier 3, rounded half away from zero; score floored at 0.
22. **Benefits** (`BenefitMath`): actual = latest measurement, else the KPI's current; baseline = own, else the KPI's; gain and realization as in [Benefits](#benefits); realized $ = max(0, gain) in dollars; at risk below 50% after `ExpectedBy`. Measurements can't be in the future.
23. **Processes:** no parent loops (`PortfolioRules.ParentError`); a process with sub-processes can't be deleted. KPI drafts reject a target on the wrong side of the baseline.
24. **Decisions:** `SupersededById` only with status Superseded; no loops; deleting a decision clears "superseded by" on those it replaced.
25. **Documents** (`DocumentLibrary`, `PortfolioService.Documents.cs`): the upload folder is `DocumentLibrary.Resolve`: the product's folder of that type with a local path, else the shared one. A document attached to a roadmap item always gets the item's `ProductId`. A folder's local path must be fully qualified and exist (never created). SharePoint addresses: https only; `/:x:/r/` sharing links lose the prefix and query; `.aspx` views use the `id` (or `RootFolder`) parameter; other `/:x:/?/` sharing links are refused; segments are unescaped and re-escaped. File URL = folder URL + `/` + escaped name, plus `?web=1` for Office files. File names: folder part dropped, `"*:<>?|#%` and control characters → `_`, trimmed, leading `~$` removed, ≤ 200 characters keeping the extension. Upload writes `~$roadmapper-{guid}.tmp` in the folder (OneDrive ignores `~$` files) and then renames it; an existing name is refused unless replacing, and replacing a listed file updates that row. Removing a document never deletes the file.
26. **Capabilities** (`CapabilityMatrix.Build`, `CapabilityRules`, `PortfolioService.Capabilities.cs`): columns = applications of the category plus `ComparisonMember`s (Retired left out unless asked), the category's own first, then by name; rows = the category's features by `SortOrder`. Feature coverage counts Yes/Partial/No/not rated; gap = no Yes; overlap = 2+ Yes; strongest = applications at the highest strength above None. A rating needs the application in the comparison; No forces strength None (higher is refused); an empty rating (no values, blank note) is deleted. Feature names are unique per category (case-insensitive); deleting a feature deletes its ratings; removing a member keeps its ratings. `PortfolioRules.CountsInPortfolio` (not Retired or Evaluating) decides which applications count in the dashboard, run cost and health; integrations can't be Evaluating.
27. **Cost of adoption** (`CostEstimate.For`, `CapabilityCost.cs`): each `PricingLine` of an application in the comparison is costed for one site: quantity = the line's own `Quantity`, else the site profile's size for its basis (Flat and Site = 1; Other has none, so needs its own); charge = max(quantity × unit price, minimum). Monthly = Σ monthly charges + Σ annual ÷ 12; one-time = Σ one-time; first year = 12 × monthly + one-time. A line whose quantity is unknown is left out and makes the estimate incomplete (its size is listed in `Missing`); an application with no lines is not priced. `Cheapest` = the fully costed applications with the lowest monthly cost. The site is the chosen profile, else the category's default (the first profile saved, or one marked default; deleting the default promotes the next by name), else "One site" with every size unknown. A price needs the application in the comparison; per-transaction prices must be monthly. Profile names are unique per category (case-insensitive).

### HTTP endpoints (file downloads)

| Route | Returns |
|---|---|
| `GET /export/template.csv` | Sample import CSV |
| `GET /export/{productId}/roadmap.rmp` | Roadmapper file (whole product as JSON) |
| `GET /export/{productId}/roadmap.csv` | CSV export |
| `GET /export/{productId}/roadmap.xlsx` | Excel export (Roadmap, Dependencies, Templates) |
| `GET /export/{productId}/roadmap.pdf?depth={Goal\|Initiative\|Feature\|Task}&baseline={snapshotId}` | PDF report, shown in the browser (`inline`) |
| `GET /export/{productId}/timeline.svg?scale=quarter&depth=…&baseline=…&download=true` | Timeline SVG |
| `GET /export/portfolio.xlsx` | Portfolio workbook (14 sheets) |
| `GET /export/capabilities/{category}.xlsx?retired=true&profile={id}` | A category's comparison workbook (Comparison and Heat map sheets, plus Cost once anything is priced, costed for the site profile or the default) |
| `GET /export/capabilities/{category}/heatmap.svg?transpose=true&retired=true&download=true` | A category's heat map as SVG |
| `GET /documents/{id}/file` | A document's saved copy on this PC (PDFs, images and text inline; other types download); 404 when it isn't there |
| `GET /export/portfolio.rpf` | Portfolio file (whole portfolio as JSON) |

### Portfolio API

Read-only JSON under `/api/portfolio` (camelCase, enums as strings); nothing changes data, so it's safe on localhost. Unknown filter values are ignored.

| Route | Returns |
|---|---|
| `/dashboard` | Health counts and per-application health, run cost, TIME counts, renewals, high/medium-risk integrations, benefit totals and value by category, strategic items, recent decisions |
| `/applications?status=&category=&tier=` · `/applications/{id}` | Applications; detail adds support contacts and linked roadmap items |
| `/applications/{id}/dependencies?depth=` · `/partners/{id}/dependencies?depth=` | Upstream and downstream (depth 1–5, default 2) |
| `/applications/{id}/impact?depth=&format=text` · `/partners/{id}/impact?…` | Impact report (JSON, or the plain-text report with `format=text`) |
| `/vendors` · `/vendors/{id}` · `/contracts?renewingWithinDays=` · `/contacts` | Vendors (detail with contracts and escalation), upcoming renewals, people |
| `/partners?type=&status=` · `/partners/{id}` | Trading partners; detail adds people and integrations |
| `/integrations?risk=&type=&applicationId=` · `/integrations/{id}` | Integrations with risk level; detail adds risks |
| `/processes?domain=&applicationId=` · `/processes/{id}` | Processes in tree order; detail adds pain points, KPIs, integrations |
| `/benefits?productId=&itemId=&applicationId=&status=` | Labor rate, totals and benefits with actual, realization, status and dollars |
| `/decisions?entityType=&entityId=&status=` · `/decisions/{id}` | Decisions with alternatives and links |
| `/capabilities` · `/capabilities/{category}?retired=true&profile={id}` | Every category's feature/application counts, % rated and gaps; one category's features with coverage and ratings, its applications with coverage and `cost` (priced, complete, monthly, oneTime, firstYear, missing, lines), and the `site` it's costed for |
| `/capabilities/{category}/profiles` | A category's site profiles |
| `/documents?productId=&entityType=&entityId=&category=` | Documents with their SharePoint address, file, date, product and what they're attached to (`entityType`: Application, Vendor (includes its contracts'), Contract, RoadmapItem) |
| `/map.svg?focus=&partner=&depth=&type=&tier=&retired=&partners=false&download=true` | The integration map as SVG |

### Roadmapper file format

A `.rmp` is UTF-8 JSON defined by `Core/Exchange/RoadmapFile.cs`. Export and import live in `Data/RoadmapService.Files.cs` (`ExportFileAsync` / `ImportFileAsync`).

- **Formatting:** property names are camelCase, enums are strings (for example `"InProgress"`), dates are `yyyy-MM-dd`, and empty values are left out.
- **Top level:** `format` (always `"roadmapper"`), `version` (currently `1`), `exportedAt`, `product` {name, description, scoringModel, wsjfScale}, `themes`, `tags`, `items`, `dependencies`, `templates`, `rubric`, `snapshots`, `history`.
- **References:**
  - `items`, `themes` and `tags` have an `id` that only applies inside the file.
  - Items refer to each other by `parentId`; dependencies use `predecessorId`/`successorId`; items and template lines use `themeId` and `tagIds`.
  - Snapshot rows and history entries also use item ids. History entries for items that no longer exist have no `itemId`.
- **Items:** `type`, `title`, `status`, `progressPct`, `startDate`, `endDate`, `horizon`, `owner`, `sortOrder`, `manualRank`, `isRollupOverridden`, `score` (scoring inputs only; the score itself is recalculated), the template reference fields, and `createdAt`/`updatedAt`.
- **Template lines:** `title`, `description`, `owner`, `gapDays`, `durationDays`, `tagIds`.
- **Rubric:** `rubric` lists criteria {`id`, `factor`, `role`, `kind`, `name`, `help`, `options` [{`id`, `label`, `from`, `value`}]}. Items carry `rubricAnswers` [{`criterionId`, `number` or `optionId`}] and `manualFactors` (e.g. `"BusinessValue, JobSize"`). These fields are optional: in files without `manualFactors`, every WSJF value present counts as typed by hand. Import checks the scale, the criteria rules, and that every answer matches a criterion and one of its options.
- **Import:**
  - Validates the whole file first and creates nothing if any check fails. It checks the format and version, hierarchy rules, missing references, dependency loops, duplicate ids, tag names or template names, empty templates, and dates where End is before Start.
  - Then creates a new product in one transaction with change history recording turned off, assigning new ids and new template-batch ids, then recalculates roll-ups and scores.
- **Version rule:** files with a higher `version` than the app supports are refused. If the format changes, bump `CurrentVersion` and keep reading older versions.

### Portfolio file format

A `.rpf` is UTF-8 JSON defined by `Core/Exchange/PortfolioFile.cs`; export and import are in `Data/Portfolio/PortfolioService.Files.cs` (`ExportPortfolioAsync` / `ImportPortfolioAsync`). Same JSON conventions as `.rmp`.

- **Top level:** `format` (`"roadmapper-portfolio"`), `version` (`1`), `exportedAt`, `appVersion`, `settings`, `contacts`, `vendors` (with `contracts`, `escalation`), `partners`, `applications` (with `support`, `initiatives`), `integrations` (with `risks`), `processes` (with `applications`, `integrationIds`, `painPoints`, `kpis`), `benefits` (with `measurements`), `decisions` (with `alternatives`, `links`), `documents`, `features` (with `ratings` by application id), `comparisonMembers`, `pricingLines`, `siteProfiles`, `history`. `settings.documentFolders` lists `{ category, localPath, sharePointUrl, product }` (`product`: a product name, or absent for shared). Missing lists are treated as empty.
- **References:** records have file-local `id`s; references use them (`vendorId`, `contractId`, `sourceApplicationId`, `kpiId`, …). Roadmap references are `{ "product": name, "path": "Goal › Initiative" }` (`application.roadmap` is just the product name); decision links are `{ kind, id }` or `{ kind: "RoadmapItem", item }`. A document has `product` (a product name; absent = shared) and `attachedTo` with `targetId` (application, vendor, contract) or `item`. Folders and documents whose product doesn't match are listed as unmatched; the document is kept as shared.
- **Validation** (before anything is written): format and version, unique ids per kind, names present, every id reference present in the file, exactly one source and target per integration. A `.rmp` is recognized and refused with a pointer to the Products page.
- **Import:** one transaction, staged saves with old→new id maps. Restore requires `IsEmptyAsync()`, imports settings and history (history re-pointed to new ids; unmapped ids become 0) with audit capture off. Merge keeps audit on, keeps settings, skips existing records by the matching rule in [Portfolio settings, backup and export](#portfolio-settings-backup-and-export) and maps references to them; children of skipped records are skipped, except that contracts and KPIs of existing parents are matched by name for references. Unmatched roadmap references are dropped and listed (`PortfolioImportResult.Unmatched`); a benefit whose item doesn't match is skipped.

### Page routes

Capabilities: `/capabilities`, `/capabilities/{category}?view=matrix|heatmap|cost|features` (category as its enum name, e.g. `YMS`).

Portfolio: `/portfolio` (dashboard), `/portfolio/applications[/{id}]`, `/portfolio/partners[/{id}]`, `/portfolio/integrations[/{id}]`, `/portfolio/map?focus=&partner=&depth=`, `/portfolio/processes[/{id}]`, `/portfolio/impact?app=|partner=&depth=`, `/portfolio/benefits?item=&new=true&product=`, `/portfolio/decisions[/{id}]`, `/portfolio/documents?product={id}&attach={key}&add=upload|link` (key: `a`/`v`/`c`/`r` + id), `/portfolio/vendors[/{id}]`, `/portfolio/contacts`, `/portfolio/settings`.

Roadmaps: `/` (products), `/guide` (this guide), `/products/{id}` (Tree), `/products/{id}/scoring` (WSJF rubric grid), `/products/{id}/dashboard`, `/timeline` (accepts `?baseline={snapshotId}`), `/board`, `/kanban`, `/priorities`, `/snapshots`, `/exchange`, `/settings`.

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
| Adjuster | A rubric criterion that moves a WSJF factor up or down the scale instead of setting it. |
| Baseline | A snapshot used for comparison. |
| Batch | All tasks created by one "apply template" action. |
| Blocked | Has an unfinished predecessor. |
| Gap | Days of wait before a template task starts, after the previous task (or the Feature start). |
| Health | An application's 0–100 score and Green/Amber/Red rating. |
| Key partner | A trading partner whose flows make impact analysis High severity. |
| Notice deadline | A contract's renewal date minus its notice period: the last day to cancel or renegotiate. |
| Realized % | How much of a benefit's expected gain has been achieved. |
| Tier | Business criticality: Tier 1 = operations stop without it. |
| TIME | Tolerate / Invest / Migrate / Eliminate, from business × technical fit. |
| Via | The middle hop of an integration (EDI provider, VAN, integration platform). |
| Horizon | Now / Next / Later bucket. |
| Open | Status not Done or Cancelled. |
| Overdue | Open, with an End date in the past. |
| Roll-up | A parent's dates and progress calculated from its children. |
| Rubric | A product's criteria for working out WSJF factors from answers. |
| Slip | Change in end date versus the baseline (+ = later). |

---

## 12. FAQ and troubleshooting

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

**Time criticality changed by itself.**
It comes from a start-date criterion in the WSJF scoring rubric, which counts months from today, so it moves as time passes (the app re-scores when it starts, or use **Recalculate** on the Scoring tab). To fix the value, click **Enter by hand** on that factor in the item editor.

**I set up a rubric but my scores didn't change.**
Values typed before the rubric existed are kept as "entered by hand". Switch a factor to **Use rubric** in the item editor (or click ✎ on the Scoring tab) once it's answered.

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

**A customer doesn't show on another customer's dependency map, even though both send to the same WMS. Is that right?**
Yes. Each partner's flows carry only that partner's data, and a dependency walk never passes from one partner's flows to another's. Flows between your own applications are always followed.

**Why is a two-way customer flow drawn with the customer on the right?**
The map draws each integration in the direction it was recorded. Edit the integration and make the customer the source.

**My labor-hour benefits don't add to the dollar totals.**
Set a labor rate on the Benefits page or in Portfolio › Settings.

**I can't delete an application (or trading partner).**
Integrations still use it. Set it to Retired (Inactive for a partner) to keep the history, or delete those integrations first.

**After restoring a portfolio file, some roadmap links are missing.**
The import lists links whose roadmap or item path didn't match. Open those roadmaps' .rmp files first (titles must match), then import the .rpf again with Merge, or link the items by hand.

**A Jira key shows but isn't clickable.**
Set the Jira site in Portfolio › Settings.

**A product's upload went to the shared folder.**
That product has no folder of its own for that type. Set one on the product's Settings › Document folders; documents already uploaded stay where they are.

**A solution I'm evaluating shows in the YMS comparison but not on the portfolio dashboard.**
That's intended: applications with status Evaluating are left out of run cost, health and the dashboard. Set it to Planned or Active once you've chosen it.

**An application I rated has disappeared from a comparison.**
It was added from another category and then removed, or it was retired. Add it again (its ratings come back), or tick **Show retired applications**.

**A solution's cost says "Incomplete" or shows "≥".**
One of its prices is per something the selected site profile doesn't size, e.g. per dock door when the profile has no dock doors. Hover it to see which; fill that size in with **Edit profile** (or **Try your own site…**), or give the price its own **How many**.

**I uploaded a document but it isn't in SharePoint yet.**
OneDrive uploads it. Check the OneDrive icon in the taskbar: it may be paused, signed out, or still syncing. The file is safe in the folder meanwhile, and **local copy** opens it.

**Upload says the folder "isn't on this PC".**
The shortcut was removed or OneDrive stopped syncing it. Add the shortcut again in SharePoint (**Add shortcut to My files**), then check the path in the product's Settings › Document folders (or Portfolio › Settings for shared folders).

**Opening a document says it can't be found.**
The file was moved or renamed in SharePoint after it was added. For a link, edit it and paste the new address. For an uploaded file, remove it and add it again as a link, or upload it again.

---

## 13. Scoring models explained

All three models answer the same question: **with more good ideas than time, what should we do first?** They make the reasoning behind an order visible, so a team can argue about the inputs ("is this really high impact?") instead of about gut feelings. A score is a conversation starter, not a verdict. If the top of the list looks wrong, that usually means an input is wrong or something important isn't being measured, so fix that, or use **Manual rank** for the exceptions.

Some habits apply to every model:

- **Score relatively.** Compare items with each other, not against an absolute ideal. Scoring a batch side by side (the [Priorities](#priorities) and [Scoring](#scoring-wsjf-rubric) tabs help) gives more consistent numbers than scoring one item at a time.
- **Score at one level.** Compare Initiatives with Initiatives, or Features with Features. A Task and a Goal can't be sensibly compared.
- **Revisit.** Scores go stale as you learn more and as deadlines approach. Re-score when something changes.

### RICE explained

**Origin:** created by the product team at Intercom to compare product ideas consistently.

**The thinking:** an idea is worth more the more people it helps, the more it helps each of them, and the surer you are of both, and worth less the more it costs to build. RICE multiplies the benefits and divides by the cost, so the result is roughly *value delivered per unit of effort*.

| Input | Question it answers | How to estimate |
|---|---|---|
| **Reach** | How many people or events will this affect in a set period? | A real count over a fixed period, e.g. customers per quarter or orders per month. Use the same period for every item. |
| **Impact** | How much will it move the needle for each one? | A fixed scale: 3 = massive, 2 = high, 1 = medium, 0.5 = low, 0.25 = minimal. |
| **Confidence** | How sure are we about Reach and Impact? | A percentage: 100% = backed by data, 80% = some evidence, 50% = a hunch. Below 50% usually means "go research first". |
| **Effort** | What will it cost? | Person-months across everyone involved (product, design, engineering, testing). |

**Example:** a feature reaching 500 customers a quarter, with high impact (2), 80% confidence and 2 person-months of effort scores (500 × 2 × 0.8) ÷ 2 = **400**.

**Good for:** product and feature ideas where you can count users or transactions. Confidence explicitly penalises wishful thinking.
**Watch out for:** Reach dominates the score because it's a raw count, so mixing items measured over different periods or units makes the comparison meaningless. Scores have no fixed range; only the order matters.

### WSJF explained

**Origin:** Weighted Shortest Job First comes from Don Reinertsen's work on product development flow and is the prioritization method used in SAFe (the Scaled Agile Framework).

**The thinking:** every week a valuable item waits, you lose something: revenue not earned, a deadline approaching, a risk left open. This loss is the **cost of delay**. If two jobs have the same cost of delay, do the shorter one first, because it starts paying back sooner and the longer one waits less in total. WSJF divides cost of delay by job size, so items that are *both* urgent-and-valuable *and* small rise to the top.

**Cost of delay** is the sum of three factors:

| Factor | Question it answers |
|---|---|
| **Business value** | How much do customers or the business gain? Revenue, savings, user satisfaction. |
| **Time criticality** | How quickly does that value fade? Is there a fixed date, a contract, a season, a competitor? Something with a hard deadline next month scores high; something that would be just as valuable if delivered next year scores low. |
| **Risk reduction / opportunity enablement** | Does it lower a risk (security, compliance, an unsupported version) or unlock other work (a platform others build on)? |

**Job size** is the relative effort or duration, used as a stand-in for how long the work blocks the team.

**Scoring:** all four are relative numbers on a scale with growing gaps, usually 1, 2, 3, 5, 8, 13, 20. The gaps are deliberate: it's easy to say one thing is "about twice as big" as another, and hard to tell a 6 from a 7. A common technique is to score one factor at a time across every item: find the smallest item in that column and give it a 1, then score the rest relative to it.

**Example:** an upgrade with Business value 8, Time criticality 13, Risk reduction 8 and Job size 8 scores (8 + 13 + 8) ÷ 8 = **3.6**. An equally urgent job half the size would score twice as high.

**Good for:** larger pieces of work (Initiatives, Features) competing for the same team, especially when deadlines and risk matter, not just customer value. In Roadmapper, a [scoring rubric](#scoring-wsjf-rubric) can work out the factors from facts about each item, so different people score the same way.
**Watch out for:** large, important items can sink because Job size divides the score. If one keeps losing, consider splitting it into smaller pieces that each deliver value.

### MoSCoW explained

**Origin:** created by Dai Clegg at Oracle and popularised by the DSDM agile method for deciding what goes into a fixed-date release. The letters stand for **Must, Should, Could, Won't**; the o's are only there to make it pronounceable.

**The thinking:** when time or budget is fixed, the real question isn't "how valuable is this?" but "what can we leave out and still succeed?" MoSCoW sorts items into four buckets for a specific release or time frame:

| Category | Meaning | A useful test |
|---|---|---|
| **Must have** | Without it the release fails or isn't worth delivering: legal, safety or a core need. | "What happens if this isn't there?" If the answer is "we can't go live", it's a Must. If there's a workaround, it isn't. |
| **Should have** | Important and painful to leave out, but there's a workaround. | Would we delay the release for it? If not, it's a Should. |
| **Could have** | Nice to have; small impact if missing. | The first things dropped when time runs short. |
| **Won't have (this time)** | Agreed to be out of scope for now. | Recording these is valuable: it sets expectations and stops them coming back every week. |

**Good for:** agreeing scope with stakeholders for a deadline-driven release, and for quick, plain-language prioritization that everyone understands.
**Watch out for:** everything drifting into Must. A common rule of thumb is to keep Musts to no more than about 60% of the effort, so the Shoulds and Coulds leave room for surprises. MoSCoW doesn't order items within a bucket: every Must gets the same score, so use **Manual rank** to order them. The optional Effort only places items on the [priority matrix](#priorities).

### Choosing a model

| If your situation is… | Try |
|---|---|
| Many product ideas, and you can count the users or transactions each one affects | **RICE** |
| Larger pieces of work competing for one team, where deadlines, risk and dependencies matter as much as value | **WSJF** |
| A fixed date or budget, and the question is what's in and what's out | **MoSCoW** |
| A short list that a group needs to agree on quickly | **MoSCoW**, then rank within each bucket |

You can switch a product's model at any time in [Settings](#9-settings); the inputs for the other models are kept, so it's cheap to try one and switch back.
