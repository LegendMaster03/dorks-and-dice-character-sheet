using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CharacterSheet.Infrastructure.Persistence.Migrations;

/// <summary>
/// Preserve the exact table decisions and Site-scoped approving actor in the
/// same immutable Origin receipt as the verified completed reconstruction.
/// Existing native-only events retain null DM-audit columns.
/// </summary>
[DbContext(typeof(CharacterSheetDbContext))]
[Migration("20261112170000_AddOriginDmRulingAudit")]
public partial class AddOriginDmRulingAudit : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "ApprovalActorUserId",
            table: "character_origin_event_receipts",
            type: "uuid",
            nullable: true);
        migrationBuilder.AddColumn<Guid>(
            name: "ApprovalCampaignId",
            table: "character_origin_event_receipts",
            type: "uuid",
            nullable: true);
        migrationBuilder.AddColumn<string>(
            name: "ApprovedDecisionsJson",
            table: "character_origin_event_receipts",
            type: "jsonb",
            nullable: true);
        migrationBuilder.AddCheckConstraint(
            name: "CK_origin_receipt_dm_audit_complete",
            table: "character_origin_event_receipts",
            sql: """
                ("ApprovalActorUserId" IS NULL AND "ApprovalCampaignId" IS NULL
                 AND "ApprovedDecisionsJson" IS NULL)
                OR
                ("ApprovalActorUserId" IS NOT NULL AND "ApprovalCampaignId" IS NOT NULL
                 AND "ApprovedDecisionsJson" IS NOT NULL)
                """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "CK_origin_receipt_dm_audit_complete",
            table: "character_origin_event_receipts");
        migrationBuilder.DropColumn(
            name: "ApprovedDecisionsJson",
            table: "character_origin_event_receipts");
        migrationBuilder.DropColumn(
            name: "ApprovalCampaignId",
            table: "character_origin_event_receipts");
        migrationBuilder.DropColumn(
            name: "ApprovalActorUserId",
            table: "character_origin_event_receipts");
    }
}
