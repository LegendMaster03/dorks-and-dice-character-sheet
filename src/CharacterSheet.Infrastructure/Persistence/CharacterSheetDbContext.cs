using CharacterSheet.Domain.Characters;
using Microsoft.EntityFrameworkCore;

namespace CharacterSheet.Infrastructure.Persistence;

/// <summary>
/// Maps the Character-owned aggregate to PostgreSQL while keeping Site identities and Rules Core rule
/// definitions outside the local schema.
/// </summary>
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
    public DbSet<CharacterOriginDraftState> CharacterOriginDrafts => Set<CharacterOriginDraftState>();
    public DbSet<CharacterAuthorizedOriginState> AuthorizedOrigins => Set<CharacterAuthorizedOriginState>();
    public DbSet<CharacterOriginEventReceipt> OriginEventReceipts => Set<CharacterOriginEventReceipt>();
    public DbSet<CharacterCurrencyBalance> CharacterCurrencyBalances => Set<CharacterCurrencyBalance>();
    public DbSet<CharacterArtAsset> CharacterArtAssets => Set<CharacterArtAsset>();
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
        root.Property(character => character.AdvancementProgress);
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
        // Enforce at most one Subclass per parent Class, even if two requests
        // race past application-layer validation.
        advancement.HasIndex(value => new { value.CharacterId, value.ParentAdvancementEntryId })
            .HasDatabaseName("UX_character_advancement_entries_SubclassPerClass")
            .IsUnique()
            .HasFilter("\"Kind\" = 'Subclass' AND \"ParentAdvancementEntryId\" IS NOT NULL");
        // A Character has exactly one starting-Class slot (ordinal zero);
        // later multiclass occurrences receive different ordinals.
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
            .HasMaxLength(CharacterRuleReference.MaxConceptKeyLength);
        inventoryItemOccurrence.Property(value => value.CustomName)
            .HasMaxLength(CharacterInventoryItemOccurrence.MaxCustomNameLength);
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
        // An inventory occurrence is either Rules Core-backed or custom,
        // never both and never neither.
        inventoryItemOccurrence.ToTable(table => table.HasCheckConstraint(
            "CK_character_inventory_item_occurrences_Identity",
            "(\"RuleConceptKey\" IS NOT NULL) <> (\"CustomName\" IS NOT NULL)"));
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
        // Character-owned rules inputs are unique per kind and key, so
        // changing a choice updates its slot instead of creating duplicates.
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

        // The Origin proposal is deliberately a draft, never active mechanics.
        var originDraft = modelBuilder.Entity<CharacterOriginDraftState>();
        originDraft.ToTable("character_origin_drafts");
        originDraft.HasKey(value => value.CharacterId);
        originDraft.Property(value => value.CharacterId).ValueGeneratedNever();
        originDraft.Property(value => value.RulesetId)
            .HasMaxLength(CharacterOriginDraftState.MaxRulesetIdLength).IsRequired();
        originDraft.Property(value => value.Fingerprint)
            .HasMaxLength(CharacterOriginDraftState.MaxFingerprintLength);
        originDraft.Property(value => value.ProposalJson)
            .HasColumnType("jsonb").IsRequired();
        originDraft.Property(value => value.Revision).IsRequired();
        originDraft.Property(value => value.CreatedAt).IsRequired();
        originDraft.Property(value => value.UpdatedAt).IsRequired();
        originDraft.HasOne<CharacterSheetRoot>().WithOne()
            .HasForeignKey<CharacterOriginDraftState>(value => value.CharacterId)
            .OnDelete(DeleteBehavior.Cascade);

        // Only a trusted application evaluator may publish this state; keeping
        // it in separate tables prevents draft JSON from becoming live grants.
        var authorizedOrigin = modelBuilder.Entity<CharacterAuthorizedOriginState>();
        authorizedOrigin.ToTable("character_authorized_origins");
        authorizedOrigin.HasKey(value => value.CharacterId);
        authorizedOrigin.Property(value => value.CharacterId).ValueGeneratedNever();
        authorizedOrigin.Property(value => value.RulesetId)
            .HasMaxLength(CharacterOriginDraftState.MaxRulesetIdLength).IsRequired();
        authorizedOrigin.Property(value => value.Fingerprint)
            .HasMaxLength(CharacterOriginDraftState.MaxFingerprintLength).IsRequired();
        authorizedOrigin.Property(value => value.ProfileJson)
            .HasColumnType("jsonb").IsRequired();
        authorizedOrigin.Property(value => value.Revision).IsRequired();
        authorizedOrigin.Property(value => value.SourceDraftRevision);
        authorizedOrigin.Property(value => value.CreatedAt).IsRequired();
        authorizedOrigin.Property(value => value.UpdatedAt).IsRequired();
        authorizedOrigin.HasOne<CharacterSheetRoot>().WithOne()
            .HasForeignKey<CharacterAuthorizedOriginState>(value => value.CharacterId)
            .OnDelete(DeleteBehavior.Cascade);

        var originEvent = modelBuilder.Entity<CharacterOriginEventReceipt>();
        originEvent.ToTable("character_origin_event_receipts");
        originEvent.HasKey(value => new { value.CharacterId, value.EventId });
        originEvent.Property(value => value.CharacterId).ValueGeneratedNever();
        originEvent.Property(value => value.EventId).HasMaxLength(240).IsRequired();
        originEvent.Property(value => value.CommittedRevision).IsRequired();
        originEvent.Property(value => value.MethodRuleId).HasMaxLength(180).IsRequired();
        originEvent.Property(value => value.MethodId).HasMaxLength(180).IsRequired();
        originEvent.Property(value => value.AuthorizationKind).HasMaxLength(32).IsRequired();
        originEvent.Property(value => value.AuthorizationEvidenceId).HasMaxLength(320).IsRequired();
        originEvent.Property(value => value.CreatedAt).IsRequired();
        originEvent.HasIndex(value => new { value.CharacterId, value.CommittedRevision })
            .HasDatabaseName("UX_character_origin_event_receipts_Revision").IsUnique();
        originEvent.HasOne<CharacterAuthorizedOriginState>().WithMany()
            .HasForeignKey(value => value.CharacterId).OnDelete(DeleteBehavior.Cascade);

        var art = modelBuilder.Entity<CharacterArtAsset>();
        art.ToTable("character_art_assets");
        art.HasKey(value => value.Id);
        art.Property(value => value.Id).ValueGeneratedNever();
        art.Property(value => value.CharacterId).ValueGeneratedNever().IsRequired();
        art.Property(value => value.StorageKey).HasMaxLength(CharacterArtAsset.MaxStorageKeyLength).IsRequired();
        art.Property(value => value.OriginalFileName).HasMaxLength(CharacterArtAsset.MaxOriginalFileNameLength).IsRequired();
        art.Property(value => value.ContentType).HasMaxLength(CharacterArtAsset.MaxContentTypeLength).IsRequired();
        art.Property(value => value.ByteLength).IsRequired();
        art.Property(value => value.IsPortrait).HasDefaultValue(false).IsRequired();
        art.Property(value => value.CreatedAt).IsRequired();
        art.Property(value => value.UpdatedAt).IsRequired();
        art.HasIndex(value => value.StorageKey).IsUnique();
        art.HasIndex(value => new { value.CharacterId, value.CreatedAt });
        art.HasIndex(value => value.CharacterId)
            .HasDatabaseName("UX_character_art_assets_Portrait")
            .IsUnique()
            .HasFilter("\"IsPortrait\" = TRUE");
        art.HasOne<CharacterSheetRoot>()
            .WithMany()
            .HasForeignKey(value => value.CharacterId)
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
