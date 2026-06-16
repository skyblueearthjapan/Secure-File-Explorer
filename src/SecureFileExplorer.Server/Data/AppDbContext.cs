using Microsoft.EntityFrameworkCore;

namespace SecureFileExplorer.Server.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<FolderEntity> Folders => Set<FolderEntity>();
    public DbSet<FileEntity> Files => Set<FileEntity>();
    public DbSet<AccessLogEntity> AccessLogs => Set<AccessLogEntity>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<FolderEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired();
            e.Property(x => x.FullPath).IsRequired();
            e.HasIndex(x => x.ParentId);
            e.HasIndex(x => x.FullPath).IsUnique();
            e.HasOne(x => x.Parent)
                .WithMany(x => x.Children)
                .HasForeignKey(x => x.ParentId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<FileEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired();
            e.Property(x => x.FullPath).IsRequired();
            e.HasIndex(x => x.FolderId);
            e.HasIndex(x => x.Name);
            e.HasIndex(x => x.FullPath).IsUnique();
            e.HasOne(x => x.Folder)
                .WithMany(x => x.Files)
                .HasForeignKey(x => x.FolderId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<AccessLogEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.TimestampUtc);
            e.HasIndex(x => x.UserName);
            e.HasIndex(x => x.Action);
        });
    }
}
