using CharacterSheet.Domain.Characters;
using Microsoft.EntityFrameworkCore;

namespace CharacterSheet.Infrastructure.Persistence;

public sealed class CharacterSheetDbContext(DbContextOptions<CharacterSheetDbContext> options)
    : DbContext(options)
{
    public DbSet<CharacterSheetRoot> CharacterSheets => Set<CharacterSheetRoot>();
    public DbSet<CharacterFoundationalRuleSelection> FoundationalRuleSelections => Set<CharacterFoundationalRuleSelection>();
    public DbSet<CharacterAdvancementEntry> CharacterAdvancementEntries => Set<CharacterAdvancementEntry>();
    public DbSet<CharacterBaseAbilityScoreInput> BaseAbilityScoreInputs => Set<CharacterBaseAbilityScoreInput>();
    public DbSet<CharacterInventoryItemOccurrence> InventoryItemOccurrences => Set<CharacterInventoryItemOccurrence>();
    public DbSet<CharacterNote> CharacterNotes => Set<CharacterNote>();
    public DbSet<CharacterConditionOccurrence> CharacterConditions => Set<CharacterConditionOccurrence>();
    public DbSet<CharacterRulesInputState> CharacterRulesInputs => Set<CharacterRulesInputState>();
    public DbSet<CharacterHitPointGainState> CharacterHitPointGains => Set<CharacterHitPointGainState>();
    public DbSet<CharacterProfileState> CharacterProfiles => Set<CharacterProfileState>();
    public DbSet<CharacterCurrencyBalance> CharacterCurrencyBalances => Set<CharacterCurrencyBalance>();
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
        root.Property(character => character.CurrentHitPoints);
        root.Property(character => character.DeathSaveSuccesses)
            .HasDefaultValue(0)
            .IsRequired();
        root.Property(character => character.DeathSaveFailures)
            .HasDefaultValue(0)
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

        var baseAbilityScoreInput = modelBuilder.Entity<CharacterBaseAbilityScoreInput>();
        baseAbilityScoreInput.ToTable("character_base_ability_score_inputs");
        baseAbilityScoreInput.HasKey(value => value.Id);
        baseAbilityScoreInput.Property(value => value.Id)
            .ValueGeneratedNever();
        baseAbilityScoreInput.Property(value => value.CharacterId)
            .ValueGeneratedNever()
            .IsRequired();
        baseAbilityScoreInput.Property(value => value.AbilityKey)
            .HasMaxLength(CharacterAbilityKey.MaxKeyLength)
            .IsRequired();
        baseAbilityScoreInput.Property(value => value.Score)
            .IsRequired();
        baseAbilityScoreInput.Property(value => value.CreatedAt)
            .IsRequired();
        baseAbilityScoreInput.Property(value => value.UpdatedAt)
            .IsRequired();
        baseAbilityScoreInput.HasIndex(value => new { value.CharacterId, value.AbilityKey })
            .IsUnique();
        root.HasMany(value => value.BaseAbilityScoreInputs)
            .WithOne()
            .HasForeignKey(value => value.CharacterId)
            .OnDelete(DeleteBehavior.Cascade);

        var advancement = modelBuilder.Entity<CharacterAdvancementEntry>();
        advancement.ToTable("character_advancement_entries");
        advancement.HasKey(value => value.Id);
        advancement.HasAlternateKey(value => new { value.Id, value.CharacterId });
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
        advancement.Property(value => value.Level);
        advancement.Property(value => value.CreatedAt)
            .IsRequired();
        advancement.Property(value => value.UpdatedAt)
            .IsRequired();
        advancement.HasIndex(value => new { value.CharacterId, value.Ordinal });
        advancement.HasIndex(value => new { value.ParentAdvancementEntryId, value.CharacterId });
        advancement.HasIndex(value => new { value.CharacterId, value.ParentAdvancementEntryId })
            .HasDatabaseName("UX_character_advancement_entries_SubclassPerClass")
            .IsUnique()
            .HasFilter("\"Kind\" = 'Subclass' AND \"ParentAdvancementEntryId\" IS NOT NULL");
        advancement.HasIndex(value => value.CharacterId)
            .HasDatabaseName("UX_character_advancement_entries_StartingClass")
            .IsUnique()
            .HasFilter("\"Kind\" = 'Class' AND \"Ordinal\" = 0 AND \"ParentAdvancementEntryId\" IS NULL");
        root.HasMany(value => value.AdvancementEntries)
            .WithOne()
            .HasForeignKey(value => value.CharacterId)
            .OnDelete(DeleteBehavior.Cascade);
        advancement.HasOne<CharacterAdvancementEntry>()
            .WithMany()
            .HasForeignKey(value => new { value.ParentAdvancementEntryId, value.CharacterId })
            .HasPrincipalKey(value => new { value.Id, value.CharacterId })
            .OnDelete(DeleteBehavior.NoAction);

        var inventoryItemOccurrence = modelBuilder.Entity<CharacterInventoryItemOccurrence>();
        inventoryItemOccurrence.ToTable("character_inventory_item_occurrences");
        inventoryItemOccurrence.HasKey(value => value.Id);
        inventoryItemOccurrence.Property(value => value.Id)
            .ValueGeneratedNever();
        inventoryItemOccurrence.Property(value => value.CharacterId)
            .ValueGeneratedNever()
            .IsRequired();
        inventoryItemOccurrence.Property(value => value.RuleConceptKey)
            .HasMaxLength(CharacterRuleReference.MaxConceptKeyLength)
            .IsRequired();
        inventoryItemOccurrence.Property(value => value.Quantity)
            .HasDefaultValue(1)
            .IsRequired();
        inventoryItemOccurrence.Property(value => value.IsCarried)
            .HasDefaultValue(true)
            .IsRequired();
        inventoryItemOccurrence.Property(value => value.IsEquipped)
            .HasDefaultValue(false)
            .IsRequired();
        inventoryItemOccurrence.Property(value => value.IsAttuned)
            .HasDefaultValue(false)
            .IsRequired();
        inventoryItemOccurrence.Property(value => value.ContainerOccurrenceId);
        inventoryItemOccurrence.Property(value => value.CreatedAt)
            .IsRequired();
        inventoryItemOccurrence.Property(value => value.UpdatedAt)
            .IsRequired();
        inventoryItemOccurrence.HasIndex(value => new { value.CharacterId, value.RuleConceptKey });
        inventoryItemOccurrence.HasIndex(value => new { value.CharacterId, value.ContainerOccurrenceId });
        root.HasMany(value => value.InventoryItemOccurrences)
            .WithOne()
            .HasForeignKey(value => value.CharacterId)
            .OnDelete(DeleteBehavior.Cascade);

        var characterNote = modelBuilder.Entity<CharacterNote>();
        characterNote.ToTable("character_notes");
        characterNote.HasKey(value => value.Id);
        characterNote.Property(value => value.Id)
            .ValueGeneratedNever();
        characterNote.Property(value => value.CharacterId)
            .ValueGeneratedNever()
            .IsRequired();
        characterNote.Property(value => value.Content)
            .HasColumnType("text")
            .IsRequired();
        characterNote.Property(value => value.CreatedAt)
            .IsRequired();
        characterNote.Property(value => value.UpdatedAt)
            .IsRequired();
        characterNote.HasIndex(value => value.CharacterId);
        root.HasMany(value => value.Notes)
            .WithOne()
            .HasForeignKey(value => value.CharacterId)
            .OnDelete(DeleteBehavior.Cascade);

        var characterCondition = modelBuilder.Entity<CharacterConditionOccurrence>();
        characterCondition.ToTable("character_conditions");
        characterCondition.HasKey(value => value.Id);
        characterCondition.Property(value => value.Id)
            .ValueGeneratedNever();
        characterCondition.Property(value => value.CharacterId)
            .ValueGeneratedNever()
            .IsRequired();
        characterCondition.Property(value => value.RuleConceptKey)
            .HasMaxLength(CharacterRuleReference.MaxConceptKeyLength);
        characterCondition.Property(value => value.CustomName)
            .HasMaxLength(CharacterConditionOccurrence.MaxCustomNameLength);
        characterCondition.Property(value => value.Level);
        characterCondition.Property(value => value.CounterCurrent);
        characterCondition.Property(value => value.CounterMaximum);
        characterCondition.Property(value => value.Duration)
            .HasMaxLength(CharacterConditionOccurrence.MaxDurationLength);
        characterCondition.Property(value => value.Notes)
            .HasMaxLength(CharacterConditionOccurrence.MaxNotesLength);
        characterCondition.Property(value => value.CreatedAt)
            .IsRequired();
        characterCondition.Property(value => value.UpdatedAt)
            .IsRequired();
        characterCondition.HasIndex(value => value.CharacterId);
        characterCondition.HasIndex(value => new { value.CharacterId, value.RuleConceptKey });
        root.HasMany(value => value.Conditions)
            .WithOne()
            .HasForeignKey(value => value.CharacterId)
            .OnDelete(DeleteBehavior.Cascade);

        var rulesInput = modelBuilder.Entity<CharacterRulesInputState>();
        rulesInput.ToTable("character_rules_inputs");
        rulesInput.HasKey(value => value.Id);
        rulesInput.Property(value => value.Id).ValueGeneratedNever();
        rulesInput.Property(value => value.CharacterId).ValueGeneratedNever().IsRequired();
        rulesInput.Property(value => value.Kind)
            .HasConversion<string>()
            .HasMaxLength(64)
            .IsRequired();
        rulesInput.Property(value => value.Key)
            .HasMaxLength(CharacterRulesInputKey.MaxLength)
            .IsRequired();
        rulesInput.Property(value => value.IntegerValue);
        rulesInput.Property(value => value.BooleanValue);
        rulesInput.Property(value => value.TextValue)
            .HasMaxLength(CharacterRulesInputKey.MaxTextValueLength);
        rulesInput.Property(value => value.CreatedAt).IsRequired();
        rulesInput.Property(value => value.UpdatedAt).IsRequired();
        rulesInput.HasIndex(value => new { value.CharacterId, value.Kind, value.Key }).IsUnique();
        root.HasMany(value => value.RulesInputs)
            .WithOne()
            .HasForeignKey(value => value.CharacterId)
            .OnDelete(DeleteBehavior.Cascade);

        var hitPointGain = modelBuilder.Entity<CharacterHitPointGainState>();
        hitPointGain.ToTable("character_hit_point_gains");
        hitPointGain.HasKey(value => value.Id);
        hitPointGain.Property(value => value.Id).ValueGeneratedNever();
        hitPointGain.Property(value => value.CharacterId).ValueGeneratedNever().IsRequired();
        hitPointGain.Property(value => value.AdvancementOccurrenceId).ValueGeneratedNever().IsRequired();
        hitPointGain.Property(value => value.ClassLevel).IsRequired();
        hitPointGain.Property(value => value.HitDieValue).IsRequired();
        hitPointGain.Property(value => value.CreatedAt).IsRequired();
        hitPointGain.Property(value => value.UpdatedAt).IsRequired();
        hitPointGain.HasIndex(value => new
        {
            value.CharacterId,
            value.AdvancementOccurrenceId,
            value.ClassLevel
        }).IsUnique();
        root.HasMany(value => value.HitPointGains)
            .WithOne()
            .HasForeignKey(value => value.CharacterId)
            .OnDelete(DeleteBehavior.Cascade);

        var currency = modelBuilder.Entity<CharacterCurrencyBalance>();
        currency.ToTable("character_currency_balances");
        currency.HasKey(value => value.Id);
        currency.Property(value => value.Id).ValueGeneratedNever();
        currency.Property(value => value.CharacterId).ValueGeneratedNever().IsRequired();
        currency.Property(value => value.CurrencyKey)
            .HasMaxLength(CharacterCurrencyBalance.MaxKeyLength)
            .IsRequired();
        currency.Property(value => value.Amount).IsRequired();
        currency.Property(value => value.CreatedAt).IsRequired();
        currency.Property(value => value.UpdatedAt).IsRequired();
        currency.HasIndex(value => new { value.CharacterId, value.CurrencyKey }).IsUnique();
        root.HasMany(value => value.CurrencyBalances)
            .WithOne()
            .HasForeignKey(value => value.CharacterId)
            .OnDelete(DeleteBehavior.Cascade);

        var profile = modelBuilder.Entity<CharacterProfileState>();
        profile.ToTable("character_profiles");
        profile.HasKey(value => value.CharacterId);
        profile.Property(value => value.CharacterId)
            .ValueGeneratedNever();
        profile.Property(value => value.Alignment)
            .HasMaxLength(CharacterProfileState.MaxShortTextLength);
        profile.Property(value => value.Deity)
            .HasMaxLength(CharacterProfileState.MaxShortTextLength);
        profile.Property(value => value.Age)
            .HasMaxLength(CharacterProfileState.MaxShortTextLength);
        profile.Property(value => value.Height)
            .HasMaxLength(CharacterProfileState.MaxShortTextLength);
        profile.Property(value => value.Weight)
            .HasMaxLength(CharacterProfileState.MaxShortTextLength);
        profile.Property(value => value.Appearance)
            .HasMaxLength(CharacterProfileState.MaxDescriptionLength);
        profile.Property(value => value.PersonalityTraits)
            .HasMaxLength(CharacterProfileState.MaxDescriptionLength);
        profile.Property(value => value.Ideals)
            .HasMaxLength(CharacterProfileState.MaxDescriptionLength);
        profile.Property(value => value.Bonds)
            .HasMaxLength(CharacterProfileState.MaxDescriptionLength);
        profile.Property(value => value.Flaws)
            .HasMaxLength(CharacterProfileState.MaxDescriptionLength);
        profile.Property(value => value.Backstory)
            .HasMaxLength(CharacterProfileState.MaxLongTextLength);
        profile.Property(value => value.AlliesAndOrganizations)
            .HasMaxLength(CharacterProfileState.MaxLongTextLength);
        profile.Property(value => value.Symbol)
            .HasMaxLength(CharacterProfileState.MaxDescriptionLength);
        profile.Property(value => value.CreatedAt)
            .IsRequired();
        profile.Property(value => value.UpdatedAt)
            .IsRequired();
        root.HasOne(value => value.Profile)
            .WithOne()
            .HasForeignKey<CharacterProfileState>(value => value.CharacterId)
            .OnDelete(DeleteBehavior.Cascade);

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
