using CharacterSheet.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

#nullable disable

namespace CharacterSheet.Infrastructure.Persistence.Migrations;

[DbContext(typeof(CharacterSheetDbContext))]
partial class CharacterSheetDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder)
    {
#pragma warning disable 612, 618
        modelBuilder
            .HasAnnotation("ProductVersion", "10.0.10")
            .HasAnnotation("Relational:MaxIdentifierLength", 63);

        modelBuilder.Entity("CharacterSheet.Domain.Characters.CharacterAdvancementEntry", b =>
        {
            b.Property<Guid>("Id")
                .HasColumnType("uuid");

            b.Property<Guid>("CharacterId")
                .HasColumnType("uuid");

            b.Property<DateTimeOffset>("CreatedAt")
                .HasColumnType("timestamp with time zone");

            b.Property<string>("Kind")
                .IsRequired()
                .HasMaxLength(64)
                .HasColumnType("character varying(64)");

            b.Property<int?>("Level")
                .HasColumnType("integer");

            b.Property<int?>("Ordinal")
                .HasColumnType("integer");

            b.Property<Guid?>("ParentAdvancementEntryId")
                .HasColumnType("uuid");

            b.Property<string>("RuleConceptKey")
                .IsRequired()
                .HasMaxLength(300)
                .HasColumnType("character varying(300)");

            b.Property<DateTimeOffset>("UpdatedAt")
                .HasColumnType("timestamp with time zone");

            b.HasKey("Id");

            b.HasAlternateKey("Id", "CharacterId");

            b.HasIndex("CharacterId")
                .IsUnique()
                .HasDatabaseName("UX_character_advancement_entries_StartingClass")
                .HasFilter("\"Kind\" = 'Class' AND \"Ordinal\" = 0 AND \"ParentAdvancementEntryId\" IS NULL");

            b.HasIndex("CharacterId", "Ordinal");

            b.HasIndex("CharacterId", "ParentAdvancementEntryId")
                .IsUnique()
                .HasDatabaseName("UX_character_advancement_entries_SubclassPerClass")
                .HasFilter("\"Kind\" = 'Subclass' AND \"ParentAdvancementEntryId\" IS NOT NULL");

            b.HasIndex("ParentAdvancementEntryId", "CharacterId");

            b.ToTable("character_advancement_entries");
        });

        modelBuilder.Entity("CharacterSheet.Domain.Characters.CharacterBaseAbilityScoreInput", b =>
        {
            b.Property<Guid>("Id")
                .HasColumnType("uuid");

            b.Property<string>("AbilityKey")
                .IsRequired()
                .HasMaxLength(64)
                .HasColumnType("character varying(64)");

            b.Property<Guid>("CharacterId")
                .HasColumnType("uuid");

            b.Property<DateTimeOffset>("CreatedAt")
                .HasColumnType("timestamp with time zone");

            b.Property<int>("Score")
                .HasColumnType("integer");

            b.Property<DateTimeOffset>("UpdatedAt")
                .HasColumnType("timestamp with time zone");

            b.HasKey("Id");

            b.HasIndex("CharacterId", "AbilityKey")
                .IsUnique();

            b.ToTable("character_base_ability_score_inputs");
        });

        modelBuilder.Entity("CharacterSheet.Domain.Characters.CharacterInventoryItemOccurrence", b =>
        {
            b.Property<Guid>("Id")
                .HasColumnType("uuid");

            b.Property<Guid>("CharacterId")
                .HasColumnType("uuid");

            b.Property<Guid?>("ContainerOccurrenceId")
                .HasColumnType("uuid");

            b.Property<DateTimeOffset>("CreatedAt")
                .HasColumnType("timestamp with time zone");

            b.Property<bool>("IsAttuned")
                .ValueGeneratedOnAdd()
                .HasColumnType("boolean")
                .HasDefaultValue(false);

            b.Property<bool>("IsCarried")
                .ValueGeneratedOnAdd()
                .HasColumnType("boolean")
                .HasDefaultValue(true);

            b.Property<bool>("IsEquipped")
                .ValueGeneratedOnAdd()
                .HasColumnType("boolean")
                .HasDefaultValue(false);

            b.Property<int>("Quantity")
                .ValueGeneratedOnAdd()
                .HasColumnType("integer")
                .HasDefaultValue(1);

            b.Property<string>("RuleConceptKey")
                .IsRequired()
                .HasMaxLength(300)
                .HasColumnType("character varying(300)");

            b.Property<DateTimeOffset>("UpdatedAt")
                .HasColumnType("timestamp with time zone");

            b.HasKey("Id");

            b.HasIndex("CharacterId", "ContainerOccurrenceId");

            b.HasIndex("CharacterId", "RuleConceptKey");

            b.ToTable("character_inventory_item_occurrences");
        });

        modelBuilder.Entity("CharacterSheet.Domain.Characters.CharacterNote", b =>
        {
            b.Property<Guid>("Id")
                .HasColumnType("uuid");

            b.Property<Guid>("CharacterId")
                .HasColumnType("uuid");

            b.Property<string>("Content")
                .IsRequired()
                .HasColumnType("text");

            b.Property<DateTimeOffset>("CreatedAt")
                .HasColumnType("timestamp with time zone");

            b.Property<DateTimeOffset>("UpdatedAt")
                .HasColumnType("timestamp with time zone");

            b.HasKey("Id");

            b.HasIndex("CharacterId");

            b.ToTable("character_notes");
        });

        modelBuilder.Entity("CharacterSheet.Domain.Characters.CharacterConditionOccurrence", b =>
        {
            b.Property<Guid>("Id")
                .HasColumnType("uuid");

            b.Property<Guid>("CharacterId")
                .HasColumnType("uuid");

            b.Property<int?>("CounterCurrent")
                .HasColumnType("integer");

            b.Property<int?>("CounterMaximum")
                .HasColumnType("integer");

            b.Property<DateTimeOffset>("CreatedAt")
                .HasColumnType("timestamp with time zone");

            b.Property<string>("CustomName")
                .HasMaxLength(120)
                .HasColumnType("character varying(120)");

            b.Property<string>("Duration")
                .HasMaxLength(160)
                .HasColumnType("character varying(160)");

            b.Property<int?>("Level")
                .HasColumnType("integer");

            b.Property<string>("Notes")
                .HasMaxLength(2000)
                .HasColumnType("character varying(2000)");

            b.Property<string>("RuleConceptKey")
                .HasMaxLength(300)
                .HasColumnType("character varying(300)");

            b.Property<DateTimeOffset>("UpdatedAt")
                .HasColumnType("timestamp with time zone");

            b.HasKey("Id");

            b.HasIndex("CharacterId");

            b.HasIndex("CharacterId", "RuleConceptKey");

            b.ToTable("character_conditions");
        });

        modelBuilder.Entity("CharacterSheet.Domain.Characters.CharacterCurrencyBalance", b =>
        {
            b.Property<Guid>("Id")
                .HasColumnType("uuid");

            b.Property<long>("Amount")
                .HasColumnType("bigint");

            b.Property<Guid>("CharacterId")
                .HasColumnType("uuid");

            b.Property<DateTimeOffset>("CreatedAt")
                .HasColumnType("timestamp with time zone");

            b.Property<string>("CurrencyKey")
                .IsRequired()
                .HasMaxLength(160)
                .HasColumnType("character varying(160)");

            b.Property<DateTimeOffset>("UpdatedAt")
                .HasColumnType("timestamp with time zone");

            b.HasKey("Id");

            b.HasIndex("CharacterId", "CurrencyKey")
                .IsUnique();

            b.ToTable("character_currency_balances");
        });

        modelBuilder.Entity("CharacterSheet.Domain.Characters.CharacterFoundationalRuleSelection", b =>
        {
            b.Property<Guid>("Id")
                .HasColumnType("uuid");

            b.Property<string>("Category")
                .IsRequired()
                .HasMaxLength(64)
                .HasColumnType("character varying(64)");

            b.Property<Guid>("CharacterId")
                .HasColumnType("uuid");

            b.Property<DateTimeOffset>("CreatedAt")
                .HasColumnType("timestamp with time zone");

            b.Property<string>("RuleConceptKey")
                .IsRequired()
                .HasMaxLength(300)
                .HasColumnType("character varying(300)");

            b.Property<DateTimeOffset>("UpdatedAt")
                .HasColumnType("timestamp with time zone");

            b.HasKey("Id");

            b.HasIndex("CharacterId", "Category")
                .IsUnique();

            b.ToTable("character_foundational_rule_selections");
        });

        modelBuilder.Entity("CharacterSheet.Domain.Characters.CharacterHitPointGainState", b =>
        {
            b.Property<Guid>("Id")
                .HasColumnType("uuid");

            b.Property<Guid>("AdvancementOccurrenceId")
                .HasColumnType("uuid");

            b.Property<int>("ClassLevel")
                .HasColumnType("integer");

            b.Property<Guid>("CharacterId")
                .HasColumnType("uuid");

            b.Property<DateTimeOffset>("CreatedAt")
                .HasColumnType("timestamp with time zone");

            b.Property<int>("HitDieValue")
                .HasColumnType("integer");

            b.Property<DateTimeOffset>("UpdatedAt")
                .HasColumnType("timestamp with time zone");

            b.HasKey("Id");

            b.HasIndex("CharacterId", "AdvancementOccurrenceId", "ClassLevel")
                .IsUnique();

            b.ToTable("character_hit_point_gains");
        });

        modelBuilder.Entity("CharacterSheet.Domain.Characters.CharacterProfileState", b =>
        {
            b.Property<Guid>("CharacterId")
                .HasColumnType("uuid");

            b.Property<string>("Age")
                .HasMaxLength(300)
                .HasColumnType("character varying(300)");

            b.Property<string>("Alignment")
                .HasMaxLength(300)
                .HasColumnType("character varying(300)");

            b.Property<string>("AlliesAndOrganizations")
                .HasMaxLength(20000)
                .HasColumnType("character varying(20000)");

            b.Property<string>("Appearance")
                .HasMaxLength(4000)
                .HasColumnType("character varying(4000)");

            b.Property<string>("Backstory")
                .HasMaxLength(20000)
                .HasColumnType("character varying(20000)");

            b.Property<string>("Bonds")
                .HasMaxLength(4000)
                .HasColumnType("character varying(4000)");

            b.Property<DateTimeOffset>("CreatedAt")
                .HasColumnType("timestamp with time zone");

            b.Property<string>("Deity")
                .HasMaxLength(300)
                .HasColumnType("character varying(300)");

            b.Property<string>("Flaws")
                .HasMaxLength(4000)
                .HasColumnType("character varying(4000)");

            b.Property<string>("Height")
                .HasMaxLength(300)
                .HasColumnType("character varying(300)");

            b.Property<string>("Ideals")
                .HasMaxLength(4000)
                .HasColumnType("character varying(4000)");

            b.Property<string>("PersonalityTraits")
                .HasMaxLength(4000)
                .HasColumnType("character varying(4000)");

            b.Property<string>("Symbol")
                .HasMaxLength(4000)
                .HasColumnType("character varying(4000)");

            b.Property<DateTimeOffset>("UpdatedAt")
                .HasColumnType("timestamp with time zone");

            b.Property<string>("Weight")
                .HasMaxLength(300)
                .HasColumnType("character varying(300)");

            b.HasKey("CharacterId");

            b.ToTable("character_profiles");
        });

        modelBuilder.Entity("CharacterSheet.Domain.Characters.CharacterRulesInputState", b =>
        {
            b.Property<Guid>("Id")
                .HasColumnType("uuid");

            b.Property<bool?>("BooleanValue")
                .HasColumnType("boolean");

            b.Property<Guid>("CharacterId")
                .HasColumnType("uuid");

            b.Property<DateTimeOffset>("CreatedAt")
                .HasColumnType("timestamp with time zone");

            b.Property<int?>("IntegerValue")
                .HasColumnType("integer");

            b.Property<string>("Key")
                .IsRequired()
                .HasMaxLength(300)
                .HasColumnType("character varying(300)");

            b.Property<string>("Kind")
                .IsRequired()
                .HasMaxLength(64)
                .HasColumnType("character varying(64)");

            b.Property<string>("TextValue")
                .HasMaxLength(2000)
                .HasColumnType("character varying(2000)");

            b.Property<DateTimeOffset>("UpdatedAt")
                .HasColumnType("timestamp with time zone");

            b.HasKey("Id");

            b.HasIndex("CharacterId", "Kind", "Key")
                .IsUnique();

            b.ToTable("character_rules_inputs");
        });

        modelBuilder.Entity("CharacterSheet.Domain.Characters.CharacterSheetRoot", b =>
        {
            b.Property<Guid>("CharacterId")
                .HasColumnType("uuid");

            b.Property<string>("BuilderStatus")
                .IsRequired()
                .HasMaxLength(64)
                .HasColumnType("character varying(64)");

            b.Property<DateTimeOffset>("CreatedAt")
                .HasColumnType("timestamp with time zone");

            b.Property<int?>("CurrentHitPoints")
                .HasColumnType("integer");

            b.Property<int>("DeathSaveFailures")
                .ValueGeneratedOnAdd()
                .HasColumnType("integer")
                .HasDefaultValue(0);

            b.Property<int>("DeathSaveSuccesses")
                .ValueGeneratedOnAdd()
                .HasColumnType("integer")
                .HasDefaultValue(0);

            b.Property<int>("SchemaVersion")
                .HasColumnType("integer");

            b.Property<DateTimeOffset>("UpdatedAt")
                .HasColumnType("timestamp with time zone");

            b.HasKey("CharacterId");
            b.ToTable("character_sheet_roots");
        });

        modelBuilder.Entity("CharacterSheet.Infrastructure.Persistence.ProcessedLifecycleEvent", b =>
        {
            b.Property<Guid>("EventId")
                .HasColumnType("uuid");

            b.Property<string>("EventType")
                .IsRequired()
                .HasMaxLength(80)
                .HasColumnType("character varying(80)");

            b.Property<DateTimeOffset>("ProcessedAt")
                .HasColumnType("timestamp with time zone");

            b.Property<Guid>("SubjectId")
                .HasColumnType("uuid");

            b.HasKey("EventId");
            b.ToTable("processed_lifecycle_events");
        });

        modelBuilder.Entity("CharacterSheet.Domain.Characters.CharacterAdvancementEntry", b =>
        {
            b.HasOne("CharacterSheet.Domain.Characters.CharacterSheetRoot", null)
                .WithMany("AdvancementEntries")
                .HasForeignKey("CharacterId")
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired();

            b.HasOne("CharacterSheet.Domain.Characters.CharacterAdvancementEntry", null)
                .WithMany()
                .HasForeignKey("ParentAdvancementEntryId", "CharacterId")
                .HasPrincipalKey("Id", "CharacterId")
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity("CharacterSheet.Domain.Characters.CharacterBaseAbilityScoreInput", b =>
        {
            b.HasOne("CharacterSheet.Domain.Characters.CharacterSheetRoot", null)
                .WithMany("BaseAbilityScoreInputs")
                .HasForeignKey("CharacterId")
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired();
        });

        modelBuilder.Entity("CharacterSheet.Domain.Characters.CharacterConditionOccurrence", b =>
        {
            b.HasOne("CharacterSheet.Domain.Characters.CharacterSheetRoot", null)
                .WithMany("Conditions")
                .HasForeignKey("CharacterId")
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired();
        });

        modelBuilder.Entity("CharacterSheet.Domain.Characters.CharacterCurrencyBalance", b =>
        {
            b.HasOne("CharacterSheet.Domain.Characters.CharacterSheetRoot", null)
                .WithMany("CurrencyBalances")
                .HasForeignKey("CharacterId")
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired();
        });

        modelBuilder.Entity("CharacterSheet.Domain.Characters.CharacterFoundationalRuleSelection", b =>
        {
            b.HasOne("CharacterSheet.Domain.Characters.CharacterSheetRoot", null)
                .WithMany("FoundationalSelections")
                .HasForeignKey("CharacterId")
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired();
        });

        modelBuilder.Entity("CharacterSheet.Domain.Characters.CharacterHitPointGainState", b =>
        {
            b.HasOne("CharacterSheet.Domain.Characters.CharacterSheetRoot", null)
                .WithMany("HitPointGains")
                .HasForeignKey("CharacterId")
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired();
        });

        modelBuilder.Entity("CharacterSheet.Domain.Characters.CharacterInventoryItemOccurrence", b =>
        {
            b.HasOne("CharacterSheet.Domain.Characters.CharacterSheetRoot", null)
                .WithMany("InventoryItemOccurrences")
                .HasForeignKey("CharacterId")
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired();
        });

        modelBuilder.Entity("CharacterSheet.Domain.Characters.CharacterNote", b =>
        {
            b.HasOne("CharacterSheet.Domain.Characters.CharacterSheetRoot", null)
                .WithMany("Notes")
                .HasForeignKey("CharacterId")
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired();
        });

        modelBuilder.Entity("CharacterSheet.Domain.Characters.CharacterProfileState", b =>
        {
            b.HasOne("CharacterSheet.Domain.Characters.CharacterSheetRoot", null)
                .WithOne("Profile")
                .HasForeignKey("CharacterSheet.Domain.Characters.CharacterProfileState", "CharacterId")
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired();
        });

        modelBuilder.Entity("CharacterSheet.Domain.Characters.CharacterRulesInputState", b =>
        {
            b.HasOne("CharacterSheet.Domain.Characters.CharacterSheetRoot", null)
                .WithMany("RulesInputs")
                .HasForeignKey("CharacterId")
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired();
        });

        modelBuilder.Entity("CharacterSheet.Domain.Characters.CharacterSheetRoot", b =>
        {
            b.Navigation("AdvancementEntries");
            b.Navigation("BaseAbilityScoreInputs");
            b.Navigation("Conditions");
            b.Navigation("CurrencyBalances");
            b.Navigation("FoundationalSelections");
            b.Navigation("HitPointGains");
            b.Navigation("InventoryItemOccurrences");
            b.Navigation("Notes");
            b.Navigation("Profile");
            b.Navigation("RulesInputs");
        });
#pragma warning restore 612, 618
    }
}
