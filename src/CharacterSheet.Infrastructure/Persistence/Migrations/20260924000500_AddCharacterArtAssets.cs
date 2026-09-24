using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CharacterSheet.Infrastructure.Persistence.Migrations;

[DbContext(typeof(CharacterSheetDbContext))]
[Migration("20260924000500_AddCharacterArtAssets")]
public partial class AddCharacterArtAssets : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "character_art_assets",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                CharacterId = table.Column<Guid>(type: "uuid", nullable: false),
                StorageKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                OriginalFileName = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                ContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                ByteLength = table.Column<long>(type: "bigint", nullable: false),
                IsPortrait = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_character_art_assets", x => x.Id);
                table.ForeignKey(
                    name: "FK_character_art_assets_character_sheet_roots_CharacterId",
                    column: x => x.CharacterId,
                    principalTable: "character_sheet_roots",
                    principalColumn: "CharacterId",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_character_art_assets_CharacterId_CreatedAt",
            table: "character_art_assets",
            columns: new[] { "CharacterId", "CreatedAt" });

        migrationBuilder.CreateIndex(
            name: "IX_character_art_assets_StorageKey",
            table: "character_art_assets",
            column: "StorageKey",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "UX_character_art_assets_Portrait",
            table: "character_art_assets",
            column: "CharacterId",
            unique: true,
            filter: "\"IsPortrait\" = TRUE");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "character_art_assets");
    }
}
