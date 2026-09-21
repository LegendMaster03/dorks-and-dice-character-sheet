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

            b.Property<DateTimeOffset>("CreatedAt")
                .HasColumnType("timestamp with time zone");

            b.Property<string>("RuleConceptKey")
                .IsRequired()
                .HasMaxLength(300)
                .HasColumnType("character varying(300)");

            b.HasKey("Id");

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

        modelBuilder.Entity("CharacterSheet.Domain.Characters.CharacterFoundationalRuleSelection", b =>
        {
            b.HasOne("CharacterSheet.Domain.Characters.CharacterSheetRoot", null)
                .WithMany("FoundationalSelections")
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

        modelBuilder.Entity("CharacterSheet.Domain.Characters.CharacterSheetRoot", b =>
        {
            b.Navigation("AdvancementEntries");
            b.Navigation("BaseAbilityScoreInputs");
            b.Navigation("FoundationalSelections");
            b.Navigation("InventoryItemOccurrences");
            b.Navigation("Notes");
        });
#pragma warning restore 612, 618
    }
}
