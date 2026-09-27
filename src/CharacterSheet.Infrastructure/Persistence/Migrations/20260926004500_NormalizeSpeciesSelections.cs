using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CharacterSheet.Infrastructure.Persistence.Migrations;

[DbContext(typeof(CharacterSheetDbContext))]
[Migration("20260926004500_NormalizeSpeciesSelections")]
public partial class NormalizeSpeciesSelections : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DELETE FROM character_foundational_rule_selections legacy
            USING character_foundational_rule_selections canonical
            WHERE legacy."CharacterId" = canonical."CharacterId"
              AND legacy."Category" = 'RaceSpecies'
              AND canonical."Category" = 'Species';

            UPDATE character_foundational_rule_selections
            SET "Category" = 'Species'
            WHERE "Category" = 'RaceSpecies';
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DELETE FROM character_foundational_rule_selections
            WHERE "Category" = 'Subspecies';

            UPDATE character_foundational_rule_selections
            SET "Category" = 'RaceSpecies'
            WHERE "Category" = 'Species';
            """);
    }
}
