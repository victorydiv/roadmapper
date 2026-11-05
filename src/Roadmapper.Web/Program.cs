using Microsoft.EntityFrameworkCore;
using Roadmapper.Data;
using Roadmapper.Web.Components;
using Roadmapper.Web.Exports;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var dbPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(
    builder.Configuration["Database:Path"] ?? @"%LOCALAPPDATA%\Roadmapper\roadmapper.db"));
Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
builder.Services.AddDbContextFactory<RoadmapDbContext>(o => o.UseSqlite($"Data Source={dbPath}"));
builder.Services.AddScoped<RoadmapService>();

var app = builder.Build();

await DbInitializer.InitializeAsync(app.Services);
app.Logger.LogInformation("Using database {DbPath}", dbPath);

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapExportEndpoints();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
