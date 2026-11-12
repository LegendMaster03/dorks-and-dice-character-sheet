using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CharacterSheet.Infrastructure.Persistence.Migrations;

/// <summary>
/// A revision-bound Core projection is cached with the authoritative Origin.
/// The consumer must compare its fingerprint to current native content before
/// using its supported scalar grants; an old projection grants nothing.
/// </summary>
[DbContext(typeof(CharacterSheetDbContext))]
[Migration("20261112180000_AddCommittedOriginProjection")]
public partial class AddCommittedOriginProjection : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "EvaluatedProjectionJson",
            table: "character_authorized_origins",
            type: "jsonb",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "EvaluatedProjectionJson",
            table: "character_authorized_origins");
    }
}
