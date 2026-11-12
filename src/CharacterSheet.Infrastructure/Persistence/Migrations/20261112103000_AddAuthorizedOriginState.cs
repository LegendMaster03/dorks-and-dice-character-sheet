using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CharacterSheet.Infrastructure.Persistence.Migrations;

/// <summary>
/// Separate current authorized Origin from untrusted draft selections.
/// Immutable receipts enforce one event ID and one event per revision.
/// </summary>
[DbContext(typeof(CharacterSheetDbContext))]
[Migration("20261112103000_AddAuthorizedOriginState")]
public partial class AddAuthorizedOriginState : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "character_authorized_origins",
            columns: table => new
            {
                CharacterId = table.Column<Guid>(type: "uuid", nullable: false),
                RulesetId = table.Column<string>(type: "character varying(160)",
                    maxLength: 160, nullable: false),
                Fingerprint = table.Column<string>(type: "character varying(200)",
                    maxLength: 200, nullable: false),
                ProfileJson = table.Column<string>(type: "jsonb", nullable: false),
                Revision = table.Column<long>(type: "bigint", nullable: false),
                SourceDraftRevision = table.Column<long>(type: "bigint", nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(
                    type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(
                    type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_character_authorized_origins", x => x.CharacterId);
                table.ForeignKey(
                    name: "FK_character_authorized_origins_character_sheet_roots_CharacterId",
                    column: x => x.CharacterId,
                    principalTable: "character_sheet_roots",
                    principalColumn: "CharacterId",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "character_origin_event_receipts",
            columns: table => new
            {
                CharacterId = table.Column<Guid>(type: "uuid", nullable: false),
                EventId = table.Column<string>(type: "character varying(240)",
                    maxLength: 240, nullable: false),
                CommittedRevision = table.Column<long>(type: "bigint", nullable: false),
                MethodRuleId = table.Column<string>(type: "character varying(180)",
                    maxLength: 180, nullable: false),
                MethodId = table.Column<string>(type: "character varying(180)",
                    maxLength: 180, nullable: false),
                AuthorizationKind = table.Column<string>(type: "character varying(32)",
                    maxLength: 32, nullable: false),
                AuthorizationEvidenceId = table.Column<string>(type: "character varying(320)",
                    maxLength: 320, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(
                    type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_character_origin_event_receipts",
                    x => new { x.CharacterId, x.EventId });
                table.ForeignKey(
                    name: "FK_character_origin_event_receipts_character_authorized_origins_CharacterId",
                    column: x => x.CharacterId,
                    principalTable: "character_authorized_origins",
                    principalColumn: "CharacterId",
                    onDelete: ReferentialAction.Cascade);
            });
        migrationBuilder.CreateIndex(
            name: "UX_character_origin_event_receipts_Revision",
            table: "character_origin_event_receipts",
            columns: new[] { "CharacterId", "CommittedRevision" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "character_origin_event_receipts");
        migrationBuilder.DropTable(name: "character_authorized_origins");
    }
}
