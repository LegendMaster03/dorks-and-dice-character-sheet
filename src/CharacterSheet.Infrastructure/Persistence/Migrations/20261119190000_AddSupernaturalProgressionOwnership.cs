using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CharacterSheet.Infrastructure.Persistence.Migrations;

/// <summary>
/// Additive, backward-compatible ownership of supernatural progression evidence
/// and scoped influence decisions. Manifestation ledgers remain independent.
/// </summary>
[DbContext(typeof(CharacterSheetDbContext))]
[Migration("20261119190000_AddSupernaturalProgressionOwnership")]
public partial class AddSupernaturalProgressionOwnership : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "ProgressionJournalJson",
            table: "character_supernatural_states",
            type: "jsonb",
            nullable: false,
            defaultValue: "{}");

        migrationBuilder.AddColumn<string>(
            name: "InfluenceOverlapSelectionsJson",
            table: "character_supernatural_states",
            type: "jsonb",
            nullable: false,
            defaultValue: "[]");

        migrationBuilder.AddColumn<string>(
            name: "ProgressionJournalSnapshotJson",
            table: "character_supernatural_event_receipts",
            type: "jsonb",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "OverlapSelectionsSnapshotJson",
            table: "character_supernatural_event_receipts",
            type: "jsonb",
            nullable: true);

        // Historical rows have no independent progression journal. Preserve
        // their existing manifestation history without inventing earned events.
        migrationBuilder.Sql("""
            UPDATE character_supernatural_states
            SET "ProgressionJournalJson" = jsonb_build_object(
                'characterId', "CharacterId"::text,
                'revision', 0,
                'processedEventIds', jsonb_build_array(),
                'consumedEligibilityEvidenceIds', jsonb_build_array())
            WHERE "ProgressionJournalJson" = '{}'::jsonb;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "ProgressionJournalSnapshotJson",
            table: "character_supernatural_event_receipts");
        migrationBuilder.DropColumn(
            name: "OverlapSelectionsSnapshotJson",
            table: "character_supernatural_event_receipts");
        migrationBuilder.DropColumn(
            name: "ProgressionJournalJson",
            table: "character_supernatural_states");
        migrationBuilder.DropColumn(
            name: "InfluenceOverlapSelectionsJson",
            table: "character_supernatural_states");
    }
}
