using backend.Models;
using Microsoft.EntityFrameworkCore;

namespace backend.Data;

public class AppDbContext : DbContext
{
    public DbSet<SecurityEvent> SecurityEvents => Set<SecurityEvent>();
    public DbSet<Incident> Incidents => Set<Incident>();
    public DbSet<IncidentEventLink> IncidentEventLinks => Set<IncidentEventLink>();

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
            entity.Property(e => e.SensorId).HasMaxLength(100);
            entity.Property(e => e.Sensor).HasMaxLength(100);
            entity.Property(e => e.Event).HasMaxLength(100);
            entity.Property(e => e.BootId).HasMaxLength(100);
            
            entity.HasIndex(e => new { e.DeviceId, e.BootId, e.EventId }).IsUnique();
        });

        modelBuilder.Entity<Incident>(entity =>
        {
            entity.HasKey(i => i.Id);

            entity.Property(i => i.Zone).HasMaxLength(100);
            entity.Property(i => i.Explanation).HasMaxLength(1000);
        });

        modelBuilder.Entity<IncidentEventLink>(entity =>
        {
            entity.HasKey(x => new { x.IncidentId, x.SecurityEventId });

            entity.HasOne(x => x.Incident)
                .WithMany(i => i.IncidentEventLinks)
                .HasForeignKey(x => x.IncidentId);

            entity.HasOne(x => x.SecurityEvent)
                .WithMany(e => e.IncidentEventLinks)
                .HasForeignKey(x => x.SecurityEventId);
        });
    }
}