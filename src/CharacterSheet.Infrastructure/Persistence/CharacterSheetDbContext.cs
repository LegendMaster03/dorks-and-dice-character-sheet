using CharacterSheet.Domain.Characters;
using Microsoft.EntityFrameworkCore;

namespace CharacterSheet.Infrastructure.Persistence;

public sealed class CharacterSheetDbContext(DbContextOptions<CharacterSheetDbContext> options)
    : DbContext(options)
{
    public DbSet<CharacterSheetRoot> CharacterSheets => Set<CharacterSheetRoot>();
    public DbSet<CharacterFoundationalRuleSelection> FoundationalRuleSelections => Set<CharacterFoundationalRuleSelection>();
    public DbSet<CharacterAdvancementEntry> CharacterAdvancementEntries => Set<CharacterAdvancementEntry>();
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

        var foundationalSelection = modelBuilder.Entity<CharacterFoundationalRuleSelection>();
        foundationalSelection.ToTable("character_foundational_rule_selections");
        foundationalSelection.HasKey(value => value.Id);
        foundationalSelection.Property(value => value.Id)
            .ValueGeneratedNever();
        foundationalSelection.Property(value => value.CharacterId)
            .ValueGeneratedNever()
            .IsRequired();
        foundationalSelection.Property(value => value.Category)
            .HasConversion<string>()
            .HasMaxLength(64)
            .IsRequired();
        foundationalSelection.Property(value => value.RuleConceptKey)
            .HasMaxLength(CharacterRuleReference.MaxConceptKeyLength)
            .IsRequired();
        foundationalSelection.Property(value => value.CreatedAt)
            .IsRequired();
        foundationalSelection.Property(value => value.UpdatedAt)
            .IsRequired();
        foundationalSelection.HasIndex(value => new { value.CharacterId, value.Category })
            .IsUnique();
        root.HasMany(value => value.FoundationalSelections)
            .WithOne()
            .HasForeignKey(value => value.CharacterId)
            .OnDelete(DeleteBehavior.Cascade);

        var advancement = modelBuilder.Entity<CharacterAdvancementEntry>();
        advancement.ToTable("character_advancement_entries");
        advancement.HasKey(value => value.Id);
        advancement.Property(value => value.Id)
            .ValueGeneratedNever();
        advancement.Property(value => value.CharacterId)
            .ValueGeneratedNever()
            .IsRequired();
        advancement.Property(value => value.Kind)
            .HasConversion<string>()
            .HasMaxLength(64)
            .IsRequired();
        advancement.Property(value => value.RuleConceptKey)
            .HasMaxLength(CharacterRuleReference.MaxConceptKeyLength)
            .IsRequired();
        advancement.Property(value => value.Ordinal);
        advancement.Property(value => value.ParentAdvancementEntryId);
        advancement.Property(value => value.CreatedAt)
            .IsRequired();
        advancement.Property(value => value.UpdatedAt)
            .IsRequired();
        advancement.HasIndex(value => new { value.CharacterId, value.Ordinal });
        advancement.HasIndex(value => value.ParentAdvancementEntryId);
        root.HasMany(value => value.AdvancementEntries)
            .WithOne()
            .HasForeignKey(value => value.CharacterId)
            .OnDelete(DeleteBehavior.Cascade);
        advancement.HasOne<CharacterAdvancementEntry>()
            .WithMany()
            .HasForeignKey(value => value.ParentAdvancementEntryId)
            .OnDelete(DeleteBehavior.SetNull);

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
