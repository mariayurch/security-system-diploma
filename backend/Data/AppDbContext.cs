using backend.Models;
using Microsoft.EntityFrameworkCore;

namespace backend.Data;

public class AppDbContext : DbContext
{
    public DbSet<SecurityEvent> SecurityEvents => Set<SecurityEvent>();

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SecurityEvent>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.DeviceId).HasMaxLength(100);
            entity.Property(e => e.Zone).HasMaxLength(100);
            entity.Property(e => e.Sensor).HasMaxLength(100);
            entity.Property(e => e.Event).HasMaxLength(100);

            entity.HasIndex(e => new { e.DeviceId, e.EventId }).IsUnique();
        });
    }
}