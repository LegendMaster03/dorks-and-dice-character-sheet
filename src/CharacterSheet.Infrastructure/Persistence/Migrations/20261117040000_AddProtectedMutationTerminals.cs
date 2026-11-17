using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CharacterSheet.Infrastructure.Persistence.Migrations;

/// <summary>
/// Durable R3 operation tombstones and commit markers. No existing protected
/// mutation is reclassified by this migration, and the Site protocol remains
/// inactive until both services have negotiated a protected admission.
/// </summary>
[DbContext(typeof(CharacterSheetDbContext))]
[Migration("20261117040000_AddProtectedMutationTerminals")]
public sealed class AddProtectedMutationTerminals : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "character_protected_mutation_terminals",
            columns: table => new
            {
                OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                CharacterId = table.Column<Guid>(type: "uuid", nullable: false),
                OperationKind = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                CommandDigest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                AdmissionDigest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                Status = table.Column<int>(type: "integer", nullable: false),
                ReceiptEventId = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: true),
                RecordedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
                table.PrimaryKey("PK_character_protected_mutation_terminals", x => x.OperationId));
        migrationBuilder.CreateIndex(
            name: "IX_character_protected_mutation_terminals_CharacterId_Status",
            table: "character_protected_mutation_terminals",
            columns: new[] { "CharacterId", "Status" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Do not silently discard durable cancel tombstones or commit
        // identities, even when changing application versions.
        migrationBuilder.Sql(
            """
            DO $guard$
            BEGIN
                IF EXISTS (SELECT 1 FROM character_protected_mutation_terminals) THEN
                    RAISE EXCEPTION
                        'Cannot downgrade R3 terminal journal while operation outcomes exist';
                END IF;
            END
            $guard$;
            """);
        migrationBuilder.DropTable(name: "character_protected_mutation_terminals");
    }
}
