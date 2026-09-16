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

            b.HasIndex("ParentAdvancementEntryId", "CharacterId");

            b.ToTable("character_advancement_entries");
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

        modelBuilder.Entity("CharacterSheet.Domain.Characters.CharacterFoundationalRuleSelection", b =>
        {
            b.HasOne("CharacterSheet.Domain.Characters.CharacterSheetRoot", null)
                .WithMany("FoundationalSelections")
                .HasForeignKey("CharacterId")
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired();
        });

        modelBuilder.Entity("CharacterSheet.Domain.Characters.CharacterSheetRoot", b =>
        {
            b.Navigation("AdvancementEntries");
            b.Navigation("FoundationalSelections");
        });
#pragma warning restore 612, 618
    }
}
