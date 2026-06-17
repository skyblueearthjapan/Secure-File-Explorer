using Microsoft.EntityFrameworkCore;

namespace SecureFileExplorer.Server.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<CatalogNode> Nodes => Set<CatalogNode>();
    public DbSet<AccessLogEntity> AccessLogs => Set<AccessLogEntity>();
    public DbSet<MailMessageEntity> MailOutbox => Set<MailMessageEntity>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<CatalogNode>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired();
            e.Property(x => x.FullPath).IsRequired();
            e.HasIndex(x => x.ParentId);
            e.HasIndex(x => x.FullPath).IsUnique();
            e.HasIndex(x => x.Name);
        });

        b.Entity<AccessLogEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.TimestampUtc);
            e.HasIndex(x => x.UserName);
            e.HasIndex(x => x.Action);
        });

        b.Entity<MailMessageEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.RelatedUser);
        });
    }
}
