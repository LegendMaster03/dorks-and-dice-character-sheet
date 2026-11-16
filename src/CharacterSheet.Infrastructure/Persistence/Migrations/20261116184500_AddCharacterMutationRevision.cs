using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CharacterSheet.Infrastructure.Persistence.Migrations;

/// <summary>
/// Add an independent character-owned monotonic mutation revision.
/// Old rows begin at revision zero; every later aggregate mutation advances
/// the token regardless of UpdatedAt timestamp ordering.
/// </summary>
[DbContext(typeof(CharacterSheetDbContext))]
[Migration("20261116184500_AddCharacterMutationRevision")]
public partial class AddCharacterMutationRevision : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<long>(
            name: "MutationRevision",
            table: "character_sheet_roots",
            type: "bigint",
            nullable: false,
            defaultValue: 0L);

        migrationBuilder.AddCheckConstraint(
            name: "CK_character_mutation_revision_nonnegative",
            table: "character_sheet_roots",
            sql: "\"MutationRevision\" >= 0");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "CK_character_mutation_revision_nonnegative",
            table: "character_sheet_roots");

        migrationBuilder.DropColumn(
            name: "MutationRevision",
            table: "character_sheet_roots");
    }
}
