using CharacterSheet.Domain.Characters;
using Microsoft.EntityFrameworkCore;

namespace CharacterSheet.Infrastructure.Persistence;

public sealed class CharacterSheetDbContext(DbContextOptions<CharacterSheetDbContext> options)
    : DbContext(options)
{
    public DbSet<CharacterSheetRoot> CharacterSheets => Set<CharacterSheetRoot>();
    public DbSet<ProcessedLifecycleEvent> ProcessedLifecycleEvents => Set<ProcessedLifecycleEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var root = modelBuilder.Entity<CharacterSheetRoot>();
        root.ToTable("character_sheet_roots");
        root.HasKey(character => character.CharacterId);
        root.Property(character => character.CharacterId)
            .ValueGeneratedNever();
        root.Property(character => character.SchemaVersion)
            .IsRequired();
        root.Property(character => character.BuilderStatus)
            .HasConversion<string>()
            .HasMaxLength(64)
            .IsRequired();
        root.Property(character => character.CreatedAt)
            .IsRequired();
        root.Property(character => character.UpdatedAt)
            .IsRequired();

        var processedLifecycleEvent = modelBuilder.Entity<ProcessedLifecycleEvent>();
        processedLifecycleEvent.ToTable("processed_lifecycle_events");
        processedLifecycleEvent.HasKey(item => item.EventId);
        processedLifecycleEvent.Property(item => item.EventId)
            .ValueGeneratedNever();
        processedLifecycleEvent.Property(item => item.EventType)
            .HasMaxLength(80)
            .IsRequired();
        processedLifecycleEvent.Property(item => item.SubjectId)
            .IsRequired();
        processedLifecycleEvent.Property(item => item.ProcessedAt)
            .IsRequired();
    }
}
