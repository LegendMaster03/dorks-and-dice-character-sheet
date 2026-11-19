using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CharacterSheet.Infrastructure.Persistence.Migrations;

/// <summary>
/// Persist one character-owned Origin free-cast ledger with immutable receipts.
/// The foreign-key chain requires an already committed authoritative Origin.
/// Character-level row locks and revision predicates are enforced by its store.
/// </summary>
[DbContext(typeof(CharacterSheetDbContext))]
[Migration("20261116120000_AddInnateSpellLedger")]
public partial class AddInnateSpellLedger : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "character_innate_spell_ledgers",
            columns: table => new
            {
                CharacterId = table.Column<Guid>(type: "uuid", nullable: false),
                RulesetId = table.Column<string>(type: "character varying(160)",
                    maxLength: 160, nullable: false),
                Fingerprint = table.Column<string>(type: "character varying(200)",
                    maxLength: 200, nullable: false),
                OriginProfileId = table.Column<string>(type: "character varying(240)",
                    maxLength: 240, nullable: false),
                ProposalId = table.Column<string>(type: "character varying(240)",
                    maxLength: 240, nullable: false),
                OriginRevision = table.Column<long>(type: "bigint", nullable: false),
                LedgerRevision = table.Column<long>(type: "bigint", nullable: false),
                LedgerJson = table.Column<string>(type: "jsonb", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(
                    type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(
                    type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_character_innate_spell_ledgers", x => x.CharacterId);
                table.ForeignKey(
                    name: "FK_character_innate_spell_ledgers_character_authorized_origins_CharacterId",
                    column: x => x.CharacterId,
                    principalTable: "character_authorized_origins",
                    principalColumn: "CharacterId",
                    onDelete: ReferentialAction.Cascade);
                table.CheckConstraint("CK_innate_ledger_revision_nonnegative",
                    "\"LedgerRevision\" >= 0 AND \"OriginRevision\" >= 1");
            });

        migrationBuilder.CreateTable(
            name: "character_innate_spell_ledger_receipts",
            columns: table => new
            {
                CharacterId = table.Column<Guid>(type: "uuid", nullable: false),
                EventId = table.Column<string>(type: "character varying(240)",
                    maxLength: 240, nullable: false),
                CommittedRevision = table.Column<long>(type: "bigint", nullable: false),
                OriginRevision = table.Column<long>(type: "bigint", nullable: false),
                ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                AuthorizationEvidenceId = table.Column<string>(
                    type: "character varying(320)", maxLength: 320, nullable: false),
                Operation = table.Column<string>(type: "character varying(64)",
                    maxLength: 64, nullable: false),
                AppliedDecisionIdsJson = table.Column<string>(type: "jsonb",
                    nullable: true),
                RecordedAt = table.Column<DateTimeOffset>(
                    type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_character_innate_spell_ledger_receipts",
                    x => new { x.CharacterId, x.EventId });
                table.ForeignKey(
                    name: "FK_character_innate_spell_ledger_receipts_character_innate_spell_ledgers_CharacterId",
                    column: x => x.CharacterId,
                    principalTable: "character_innate_spell_ledgers",
                    principalColumn: "CharacterId",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "UX_character_innate_spell_ledger_receipts_Revision",
            table: "character_innate_spell_ledger_receipts",
            columns: new[] { "CharacterId", "CommittedRevision" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "character_innate_spell_ledger_receipts");
        migrationBuilder.DropTable(name: "character_innate_spell_ledgers");
    }
}
