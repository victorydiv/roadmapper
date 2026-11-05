using Microsoft.EntityFrameworkCore;
using Roadmapper.Core;

namespace Roadmapper.Data;

public class RoadmapDbContext(DbContextOptions<RoadmapDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();
    public DbSet<RoadmapItem> Items => Set<RoadmapItem>();
    public DbSet<ItemScore> ItemScores => Set<ItemScore>();
    public DbSet<Theme> Themes => Set<Theme>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<Dependency> Dependencies => Set<Dependency>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
    public DbSet<Snapshot> Snapshots => Set<Snapshot>();

    /// <summary>Turned off while re-deriving rollups and scores, so history only shows deliberate edits.</summary>
    public bool AuditEnabled { get; set; } = true;

    /// <summary>Writes field-level audit rows alongside every async save. (All app writes are async.)</summary>
    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        if (!AuditEnabled) return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);

        ChangeTracker.DetectChanges();
        var pending = AuditTrail.Capture(ChangeTracker);
        var result = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        if (pending.Count > 0)
        {
            AuditEntries.AddRange(await AuditTrail.ResolveAsync(this, pending, cancellationToken));
            await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }
        return result;
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        // Enums are stored as text so the SQLite file stays readable in any DB browser.
        b.Entity<Product>(e =>
        {
            e.Property(p => p.Name).IsRequired().HasMaxLength(200);
            e.Property(p => p.ScoringModel).HasConversion<string>().HasMaxLength(20);
        });

        b.Entity<RoadmapItem>(e =>
        {
            e.ToTable("Items");
            e.Property(i => i.Title).IsRequired().HasMaxLength(300);
            e.Property(i => i.Owner).HasMaxLength(100);
            e.Property(i => i.ItemType).HasConversion<string>().HasMaxLength(20);
            e.Property(i => i.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(i => i.Horizon).HasConversion<string>().HasMaxLength(20);

            e.HasOne(i => i.Product).WithMany(p => p.Items).HasForeignKey(i => i.ProductId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(i => i.Parent).WithMany(p => p.Children).HasForeignKey(i => i.ParentId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(i => i.Theme).WithMany().HasForeignKey(i => i.ThemeId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(i => i.Score).WithOne().HasForeignKey<ItemScore>(s => s.ItemId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(i => i.Tags).WithMany(t => t.Items).UsingEntity("ItemTags");
            e.HasIndex(i => new { i.ProductId, i.ParentId, i.SortOrder });
        });

        b.Entity<ItemScore>(e =>
        {
            e.HasKey(s => s.ItemId);
            e.Property(s => s.Moscow).HasConversion<string>().HasMaxLength(20);
        });

        b.Entity<Theme>(e =>
        {
            e.Property(t => t.Name).IsRequired().HasMaxLength(100);
            e.Property(t => t.Color).IsRequired().HasMaxLength(20);
            e.HasOne<Product>().WithMany(p => p.Themes).HasForeignKey(t => t.ProductId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Tag>(e =>
        {
            e.Property(t => t.Name).IsRequired().HasMaxLength(100);
            e.HasOne<Product>().WithMany(p => p.Tags).HasForeignKey(t => t.ProductId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(t => new { t.ProductId, t.Name }).IsUnique();
        });

        b.Entity<Dependency>(e =>
        {
            e.Property(d => d.Type).HasConversion<string>().HasMaxLength(20);
            e.HasOne(d => d.Predecessor).WithMany(i => i.Successors).HasForeignKey(d => d.PredecessorId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(d => d.Successor).WithMany(i => i.Predecessors).HasForeignKey(d => d.SuccessorId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(d => new { d.PredecessorId, d.SuccessorId }).IsUnique();
        });

        b.Entity<AuditEntry>(e =>
        {
            e.Property(a => a.Action).HasConversion<string>().HasMaxLength(20);
            e.Property(a => a.ItemTitle).HasMaxLength(300);
            e.Property(a => a.Field).HasMaxLength(100);
            e.HasOne<Product>().WithMany().HasForeignKey(a => a.ProductId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(a => new { a.ProductId, a.ChangedAt });
            e.HasIndex(a => a.ItemId);
        });

        b.Entity<Snapshot>(e =>
        {
            e.Property(s => s.Name).IsRequired().HasMaxLength(200);
            e.Property(s => s.Note).HasMaxLength(1000);
            e.HasOne<Product>().WithMany().HasForeignKey(s => s.ProductId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(s => new { s.ProductId, s.TakenAt });
        });
    }
}
