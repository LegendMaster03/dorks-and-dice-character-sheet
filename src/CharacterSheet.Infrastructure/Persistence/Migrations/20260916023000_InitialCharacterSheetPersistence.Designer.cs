using CharacterSheet.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CharacterSheet.Infrastructure.Persistence.Migrations;

[DbContext(typeof(CharacterSheetDbContext))]
[Migration("20260916023000_InitialCharacterSheetPersistence")]
partial class InitialCharacterSheetPersistence
{
    protected override void BuildTargetModel(ModelBuilder modelBuilder)
    {
#pragma warning disable 612, 618
        modelBuilder
            .HasAnnotation("ProductVersion", "10.0.10")
            .HasAnnotation("Relational:MaxIdentifierLength", 63);

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
#pragma warning restore 612, 618
    }
}
