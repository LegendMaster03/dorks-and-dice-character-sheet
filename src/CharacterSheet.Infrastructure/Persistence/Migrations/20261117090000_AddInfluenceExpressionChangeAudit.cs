using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CharacterSheet.Infrastructure.Persistence.Migrations;

/// <summary>
/// Preserve the exact verified before/after expression evidence for new influence
/// events. Historical receipts have no inferred expression history.
/// </summary>
[DbContext(typeof(CharacterSheetDbContext))]
[Migration("20261117090000_AddInfluenceExpressionChangeAudit")]
public partial class AddInfluenceExpressionChangeAudit : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "ExpressionChangeJson",
            table: "character_supernatural_event_receipts",
            type: "jsonb",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DO $guard$
            BEGIN
                IF EXISTS (
                    SELECT 1 FROM character_supernatural_event_receipts
                    WHERE "ExpressionChangeJson" IS NOT NULL
                ) THEN
                    RAISE EXCEPTION
                        'Cannot discard durable influence expression-change history';
                END IF;
            END
            $guard$;
            """);
        migrationBuilder.DropColumn(
            name: "ExpressionChangeJson",
            table: "character_supernatural_event_receipts");
    }
}
