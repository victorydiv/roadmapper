# Roadmapper

A product roadmap app for product managers. You plan work as Goals › Initiatives › Features › Tasks and view it as a tree, a timeline (Gantt), Now / Next / Later, Kanban and a priority matrix. You can score items with RICE, WSJF or MoSCoW, save baselines and see how dates slip, and export to Excel, PDF and images. Roadmaps are shared as `.rmp` files.

It runs on your own PC, in your browser. There's no account or sign-in, and your data stays on your machine.

## Getting started

1. **Download** `Roadmapper-<version>-win-x64.zip` from the [Releases](https://github.com/victorydiv/roadmapper/releases) page, or ask whoever sent you Roadmapper for it.
2. **Unzip** it to a folder of your own, for example `Documents\Roadmapper`. Don't run it from inside the zip.
3. **Double-click `Roadmapper.exe`.** Your browser opens Roadmapper at `http://localhost:5210`. A small console window stays open while the app runs; closing it stops Roadmapper.
4. **Explore the sample product**, then create your own with **+ New product**. Click **User guide** at the top of any page for help.

**"Windows protected your PC"?** The app isn't code-signed, so Windows SmartScreen warns about it the first time. Click **More info → Run anyway**. If your organization blocks the app entirely, ask IT to allow it.

**Sharing a roadmap.** Open the product, go to **Import / Export → Roadmapper file (.rmp)**, and send the file. The other person opens it with **Open .rmp file…** on their Products page, and it becomes a new product in their copy of the app.

**Your data** is saved in `%LOCALAPPDATA%\Roadmapper\roadmapper.db`, not in the app folder. So when a new version comes out, replace the app folder with the new one and your roadmaps carry over. To back up a single product, export it as a `.rmp`. To back up everything, close Roadmapper and copy that `.db` file.

Double-clicking `Roadmapper.exe` while it's already running just reopens it in your browser. If port 5210 is used by another program, Roadmapper picks a free port; the console window shows the address.

Requirements: Windows 10 or 11 (64-bit) and a modern browser. Nothing else needs to be installed.

## For developers

- .NET 10, Blazor Server, EF Core + SQLite. The solution is `Roadmapper.slnx`.
- Run: `dotnet run --project src/Roadmapper.Web --launch-profile http` from this folder.
- Test: `dotnet test`.
- Package: `./publish.ps1` runs the tests and writes `artifacts/Roadmapper-<version>-win-x64.zip`. Raise `<Version>` in `src/Roadmapper.Web/Roadmapper.Web.csproj` for each release.
- How everything works, including the rules the code enforces: [docs/USER_GUIDE.md](docs/USER_GUIDE.md), section 10.
