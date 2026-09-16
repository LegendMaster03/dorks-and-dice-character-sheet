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
        modelBuilder.HasAnnotation("ProductVersion", "10.0.0");

        modelBuilder.Entity("CharacterSheet.Domain.Characters.CharacterAdvancementEntry", b =>
        {
            b.Property<Guid>("Id")
                .HasColumnType("TEXT");

            b.Property<Guid>("CharacterId")
                .HasColumnType("TEXT");

            b.Property<DateTimeOffset>("CreatedAt")
                .HasColumnType("TEXT");

            b.Property<string>("Kind")
                .IsRequired()
                .HasMaxLength(64)
                .HasColumnType("TEXT");

            b.Property<int?>("Ordinal")
                .HasColumnType("INTEGER");

            b.Property<Guid?>("ParentAdvancementEntryId")
                .HasColumnType("TEXT");

            b.Property<string>("RuleConceptKey")
                .IsRequired()
                .HasMaxLength(300)
                .HasColumnType("TEXT");

            b.Property<DateTimeOffset>("UpdatedAt")
                .HasColumnType("TEXT");

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
                .HasColumnType("TEXT");

            b.Property<string>("Category")
                .IsRequired()
                .HasMaxLength(64)
                .HasColumnType("TEXT");

            b.Property<Guid>("CharacterId")
                .HasColumnType("TEXT");

            b.Property<DateTimeOffset>("CreatedAt")
                .HasColumnType("TEXT");

            b.Property<string>("RuleConceptKey")
                .IsRequired()
                .HasMaxLength(300)
                .HasColumnType("TEXT");

            b.Property<DateTimeOffset>("UpdatedAt")
                .HasColumnType("TEXT");

            b.HasKey("Id");

            b.HasIndex("CharacterId", "Category")
                .IsUnique();

            b.ToTable("character_foundational_rule_selections");
        });

        modelBuilder.Entity("CharacterSheet.Domain.Characters.CharacterSheetRoot", b =>
        {
            b.Property<Guid>("CharacterId")
                .HasColumnType("TEXT");

            b.Property<string>("BuilderStatus")
                .IsRequired()
                .HasMaxLength(64)
                .HasColumnType("TEXT");

            b.Property<DateTimeOffset>("CreatedAt")
                .HasColumnType("TEXT");

            b.Property<int>("SchemaVersion")
                .HasColumnType("INTEGER");

            b.Property<DateTimeOffset>("UpdatedAt")
                .HasColumnType("TEXT");

            b.HasKey("CharacterId");
            b.ToTable("character_sheet_roots");
        });

        modelBuilder.Entity("CharacterSheet.Infrastructure.Persistence.ProcessedLifecycleEvent", b =>
        {
            b.Property<Guid>("EventId")
                .HasColumnType("TEXT");

            b.Property<string>("EventType")
                .IsRequired()
                .HasMaxLength(80)
                .HasColumnType("TEXT");

            b.Property<DateTimeOffset>("ProcessedAt")
                .HasColumnType("TEXT");

            b.Property<Guid>("SubjectId")
                .HasColumnType("TEXT");

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
                .OnDelete(DeleteBehavior.Restrict);
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
