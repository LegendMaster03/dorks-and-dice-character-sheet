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
#pragma warning restore 612, 618
    }
}
