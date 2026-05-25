using MakeMeVideo.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace MakeMeVideo.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<GeneratorAccount> Accounts => Set<GeneratorAccount>();
    public DbSet<GenerationProject> Projects => Set<GenerationProject>();
    public DbSet<ImageTaskItem> ImageTasks => Set<ImageTaskItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<GenerationProject>().HasIndex(x => x.Name).IsUnique();
        modelBuilder.Entity<ImageTaskItem>().HasIndex(x => new { x.ProjectId, x.FileName }).IsUnique();
    }
}
