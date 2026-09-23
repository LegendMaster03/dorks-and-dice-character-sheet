using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CharacterSheet.Infrastructure.Persistence.Migrations;

[DbContext(typeof(CharacterSheetDbContext))]
[Migration("20260923023000_AddCharacterProfileState")]
public partial class AddCharacterProfileState : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "character_profiles",
            columns: table => new
            {
                CharacterId = table.Column<Guid>(type: "uuid", nullable: false),
                Alignment = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                Deity = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                Age = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                Height = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                Weight = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                Appearance = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                PersonalityTraits = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                Ideals = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                Bonds = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                Flaws = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                Backstory = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: true),
                AlliesAndOrganizations = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: true),
                Symbol = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_character_profiles", x => x.CharacterId);
                table.ForeignKey(
                    name: "FK_character_profiles_character_sheet_roots_CharacterId",
                    column: x => x.CharacterId,
                    principalTable: "character_sheet_roots",
                    principalColumn: "CharacterId",
                    onDelete: ReferentialAction.Cascade);
            });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "character_profiles");
    }
}
