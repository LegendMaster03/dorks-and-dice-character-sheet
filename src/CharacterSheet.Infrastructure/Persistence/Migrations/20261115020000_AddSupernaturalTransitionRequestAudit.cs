using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CharacterSheet.Infrastructure.Persistence.Migrations;

/// <summary>
/// Persist the original ordered, source-verified manifestation steps with each
/// new immutable receipt. Existing receipts are left null; inventing earlier
/// steps from the final ledger would falsify the historical audit.
/// </summary>
[DbContext(typeof(CharacterSheetDbContext))]
[Migration("20261115020000_AddSupernaturalTransitionRequestAudit")]
public partial class AddSupernaturalTransitionRequestAudit : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "TransitionRequestJson",
            table: "character_supernatural_event_receipts",
            type: "jsonb",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Once an audited transition has committed, dropping this column would
        // silently erase the only durable record of its ordered, source-verified
        // actions. Allow rollback on empty/legacy-only databases, but require an
        // explicit data-preserving migration plan for real audited histories.
        migrationBuilder.Sql(
            """
            DO $guard$
            BEGIN
                IF EXISTS (
                    SELECT 1 FROM character_supernatural_event_receipts
                    WHERE "TransitionRequestJson" IS NOT NULL
                ) THEN
                    RAISE EXCEPTION
                        'Cannot downgrade supernatural transition audit: verified event steps would be lost';
                END IF;
            END
            $guard$;
            """);

        migrationBuilder.DropColumn(
            name: "TransitionRequestJson",
            table: "character_supernatural_event_receipts");
    }
}
