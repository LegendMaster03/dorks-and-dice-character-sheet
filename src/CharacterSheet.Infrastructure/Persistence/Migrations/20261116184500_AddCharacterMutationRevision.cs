using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CharacterSheet.Infrastructure.Persistence.Migrations;

/// <summary>
/// Add an independent character-owned monotonic mutation revision.
/// Old rows begin at revision zero; every later aggregate mutation advances
/// the token regardless of UpdatedAt timestamp ordering.
/// </summary>
[DbContext(typeof(CharacterSheetDbContext))]
[Migration("20261116184500_AddCharacterMutationRevision")]
public partial class AddCharacterMutationRevision : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<long>(
            name: "MutationRevision",
            table: "character_sheet_roots",
            type: "bigint",
            nullable: false,
            defaultValue: 0L);

        migrationBuilder.AddCheckConstraint(
            name: "CK_character_mutation_revision_nonnegative",
            table: "character_sheet_roots",
            sql: "\"MutationRevision\" >= 0");

        // Atomic database-side advancement is required because Character
        // Sheet intentionally allows independent Feat occurrences to be
        // appended concurrently, sometimes from roots read at the same old
        // revision. The trigger resolves that race without a blanket EF CAS
        // on unrelated aggregate writes. Every root UPDATE serializes at
        // PostgreSQL's row lock and increments from the *database* value.
        migrationBuilder.Sql("""
            CREATE FUNCTION character_sheet_bump_mutation_revision()
            RETURNS trigger
            LANGUAGE plpgsql
            AS $character_mutation$
            BEGIN
                IF OLD."MutationRevision" >= 9223372036854775807 THEN
                    RAISE EXCEPTION 'Character mutation revision overflow';
                END IF;
                NEW."MutationRevision" := OLD."MutationRevision" + 1;
                NEW."UpdatedAt" := GREATEST(OLD."UpdatedAt", NEW."UpdatedAt");
                RETURN NEW;
            END;
            $character_mutation$;

            CREATE TRIGGER TR_character_sheet_root_mutation_revision
            BEFORE UPDATE ON character_sheet_roots
            FOR EACH ROW
            EXECUTE FUNCTION character_sheet_bump_mutation_revision();
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP TRIGGER IF EXISTS TR_character_sheet_root_mutation_revision
                ON character_sheet_roots;
            DROP FUNCTION IF EXISTS character_sheet_bump_mutation_revision();
            """);

        migrationBuilder.DropCheckConstraint(
            name: "CK_character_mutation_revision_nonnegative",
            table: "character_sheet_roots");

        migrationBuilder.DropColumn(
            name: "MutationRevision",
            table: "character_sheet_roots");
    }
}
