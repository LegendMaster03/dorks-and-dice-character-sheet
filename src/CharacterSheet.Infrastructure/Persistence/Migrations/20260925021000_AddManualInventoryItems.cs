using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CharacterSheet.Infrastructure.Persistence.Migrations;

[DbContext(typeof(CharacterSheetDbContext))]
[Migration("20260925021000_AddManualInventoryItems")]
public partial class AddManualInventoryItems : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<string>(
            name: "RuleConceptKey",
            table: "character_inventory_item_occurrences",
            type: "character varying(300)",
            maxLength: 300,
            nullable: true,
            oldClrType: typeof(string),
            oldType: "character varying(300)",
            oldMaxLength: 300);

        migrationBuilder.AddColumn<string>(
            name: "CustomName",
            table: "character_inventory_item_occurrences",
            type: "character varying(200)",
            maxLength: 200,
            nullable: true);

        migrationBuilder.AddCheckConstraint(
            name: "CK_character_inventory_item_occurrences_Identity",
            table: "character_inventory_item_occurrences",
            sql: "(\"RuleConceptKey\" IS NOT NULL) <> (\"CustomName\" IS NOT NULL)");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "CK_character_inventory_item_occurrences_Identity",
            table: "character_inventory_item_occurrences");

        migrationBuilder.Sql(
            "DELETE FROM character_inventory_item_occurrences WHERE \"RuleConceptKey\" IS NULL;");

        migrationBuilder.DropColumn(
            name: "CustomName",
            table: "character_inventory_item_occurrences");

        migrationBuilder.AlterColumn<string>(
            name: "RuleConceptKey",
            table: "character_inventory_item_occurrences",
            type: "character varying(300)",
            maxLength: 300,
            nullable: false,
            defaultValue: "",
            oldClrType: typeof(string),
            oldType: "character varying(300)",
            oldMaxLength: 300,
            oldNullable: true);
    }
}
