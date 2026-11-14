using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CharacterSheet.Infrastructure.Persistence.Migrations;

/// <summary>
/// Stores incomplete Origin creation choices without activating any grants.
/// Does not remove or alter existing character inventory or advancement state.
/// </summary>
[DbContext(typeof(CharacterSheetDbContext))]
[Migration("20261112090000_AddOriginDraftState")]
public partial class AddOriginDraftState : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "character_origin_drafts",
            columns: table => new
            {
                CharacterId = table.Column<Guid>(type: "uuid", nullable: false),
                RulesetId = table.Column<string>(
                    type: "character varying(160)", maxLength: 160, nullable: false),
                Fingerprint = table.Column<string>(
                    type: "character varying(200)", maxLength: 200, nullable: true),
                ProposalJson = table.Column<string>(
                    type: "jsonb", nullable: false),
                Revision = table.Column<long>(type: "bigint", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(
                    type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(
                    type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_character_origin_drafts", x => x.CharacterId);
                table.ForeignKey(
                    name: "FK_character_origin_drafts_character_sheet_roots_CharacterId",
                    column: x => x.CharacterId,
                    principalTable: "character_sheet_roots",
                    principalColumn: "CharacterId",
                    onDelete: ReferentialAction.Cascade);
            });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "character_origin_drafts");
    }
}
