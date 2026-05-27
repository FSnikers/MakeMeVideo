using ChatGPTImageOrchestrator.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ChatGPTImageOrchestrator.Infrastructure.Persistence;

public class AppDbContext : DbContext
{

    public DbSet<GeneratorAccount> Accounts => Set<GeneratorAccount>();
    public DbSet<GenerationProject> Projects => Set<GenerationProject>();
    public DbSet<ImageTask> ImageTasks => Set<ImageTask>();

    public AppDbContext()
    {
    }

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseSqlite("Data Source=app.db");
        base.OnConfiguring(optionsBuilder);
    }
}