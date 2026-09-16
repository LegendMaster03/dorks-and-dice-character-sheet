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
#pragma warning restore 612, 618
    }
}
