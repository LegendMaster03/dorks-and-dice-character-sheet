using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CharacterSheet.Infrastructure.Persistence.Migrations;

/// <summary>
/// Additive character-owned supernatural state and immutable transition events.
/// Ordinary characters require no rows and existing Origins are unchanged.
/// </summary>
[DbContext(typeof(CharacterSheetDbContext))]
[Migration("20261115010000_AddCharacterSupernaturalState")]
public partial class AddCharacterSupernaturalState : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "character_supernatural_states",
            columns: table => new
            {
                CharacterId = table.Column<Guid>(type: "uuid", nullable: false),
                ContractVersion = table.Column<int>(type: "integer", nullable: false),
                RulesetId = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                Fingerprint = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                AlternateSheetsJson = table.Column<string>(type: "jsonb", nullable: false),
                PersistentInfluencesJson = table.Column<string>(type: "jsonb", nullable: false),
                LedgerJson = table.Column<string>(type: "jsonb", nullable: false),
                LedgerRevision = table.Column<long>(type: "bigint", nullable: false),
                DependencyFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                Revision = table.Column<long>(type: "bigint", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_character_supernatural_states", x => x.CharacterId);
                table.ForeignKey("FK_character_supernatural_states_character_sheet_roots_CharacterId",
                    x => x.CharacterId, "character_sheet_roots", "CharacterId",
                    onDelete: ReferentialAction.Cascade);
                table.CheckConstraint("CK_character_supernatural_states_Revision",
                    "\"Revision\" >= 1 AND \"LedgerRevision\" >= 0 AND \"ContractVersion\" = 1");
            });

        migrationBuilder.CreateTable(
            name: "character_supernatural_event_receipts",
            columns: table => new
            {
                CharacterId = table.Column<Guid>(type: "uuid", nullable: false),
                EventId = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                CommittedRevision = table.Column<long>(type: "bigint", nullable: false),
                LedgerRevision = table.Column<long>(type: "bigint", nullable: false),
                Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                AuthorizationEvidenceId = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_character_supernatural_event_receipts", x => new { x.CharacterId, x.EventId });
                table.ForeignKey("FK_character_supernatural_event_receipts_character_supernatural_states_CharacterId",
                    x => x.CharacterId, "character_supernatural_states", "CharacterId",
                    onDelete: ReferentialAction.Cascade);
            });
        migrationBuilder.CreateIndex(
            name: "UX_character_supernatural_event_receipts_Revision",
            table: "character_supernatural_event_receipts",
            columns: new[] { "CharacterId", "CommittedRevision" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "character_supernatural_event_receipts");
        migrationBuilder.DropTable(name: "character_supernatural_states");
    }
}
