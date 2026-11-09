using Microsoft.EntityFrameworkCore;
using Roadmapper.Data;
using Roadmapper.Data.Portfolio;
using Roadmapper.Web;
using Roadmapper.Web.Api;
using Roadmapper.Web.Components;
using Roadmapper.Web.Exports;

// `dotnet run` sets Development through launchSettings.json. Anything else is the packaged Roadmapper.exe,
// which may be started from a shortcut in another folder, so it looks for wwwroot and appsettings beside itself.
var packaged = !string.Equals(Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"), "Development", StringComparison.OrdinalIgnoreCase);
// --no-browser is our own switch. Remove it before ASP.NET reads the arguments, or it would take the next one as its value.
var openBrowser = !args.Contains("--no-browser", StringComparer.OrdinalIgnoreCase);
args = args.Where(a => !a.Equals("--no-browser", StringComparison.OrdinalIgnoreCase)).ToArray();
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = packaged ? AppContext.BaseDirectory : null,
});

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var dbPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(
    builder.Configuration["Database:Path"] ?? @"%LOCALAPPDATA%\Roadmapper\roadmapper.db"));
Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
builder.Services.AddDbContextFactory<RoadmapDbContext>(o => o.UseSqlite($"Data Source={dbPath}")
    // Roadmaps are small, so loading several collections in one query is fine; don't fill the console with this advice.
    .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.MultipleCollectionIncludeWarning)));
builder.Services.AddScoped<RoadmapService>();
builder.Services.AddScoped<PortfolioService>();
// The JSON API writes enums by name ("Tier1", "Active"), matching the database and .rmp files.
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));

// Unless an address was given (--urls or ASPNETCORE_URLS), listen on localhost only.
string? url = null;
if (string.IsNullOrEmpty(builder.Configuration["urls"]))
{
    switch (await Launcher.ChooseAddressAsync())
    {
        case { AlreadyRunning: true } running:
            // A second double-click: show the copy that's already running rather than starting another.
            if (openBrowser) Launcher.OpenBrowser(running.Url);
            Console.WriteLine($"Roadmapper is already running at {running.Url}.");
            return;
        case var free:
            url = free.Url;
            builder.WebHost.UseUrls(url);
            break;
    }
}

var app = builder.Build();

await DbInitializer.InitializeAsync(app.Services);
app.Logger.LogInformation("Using database {DbPath}", dbPath);

// Configure the HTTP request pipeline. The app only serves localhost over plain HTTP, so there's no HTTPS redirect or HSTS.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

app.UseAntiforgery();

app.MapStaticAssets();
app.MapGet(Launcher.ProbePath, () => Launcher.ProbeReply);
app.MapExportEndpoints();
app.MapPortfolioApi();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

if (packaged && url is not null)
{
    app.Lifetime.ApplicationStarted.Register(() =>
    {
        Launcher.ShowBanner(url, dbPath);
        if (openBrowser) Launcher.OpenBrowser(url);
    });
}

app.Run();
