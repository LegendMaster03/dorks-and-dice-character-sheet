# Dorks & Dice Character Sheet

Character Sheet is the separately deployable, player-facing Dorks & Dice tool for building and managing rich digital character sheets.

The Dorks & Dice Site owns canonical Character identity, account ownership, Character lifecycle, and campaign-character associations. Character Sheet owns rich sheet state attached to the Site-generated `CharacterId`. Rules Core owns normalized rule definitions.

The current implementation provides the first real Character workflow: owner-only Tool Host Character authorization, EF Core/PostgreSQL persistence, canonical Site Character creation from `/new`, existing-basic-Character upgrades, archived read-only behavior, a Rules Core-backed initial builder, an explicit Embedded Module v2 frontend lifecycle, external database deployment, CI validation, and tests. Broader D&D character-building mechanics remain intentionally deferred.

See `docs/architecture.md` for the ownership, authorization, persistence, API, lifecycle, and routing contracts.

## Development

Frontend:

```text
cd src/CharacterSheet.Web/Client
npm install
npm run build
```

Character Sheet requires PostgreSQL for database-backed execution. Configure `ConnectionStrings__CharacterSheet` with a PostgreSQL connection string. `.env.example` contains a local-development shape only and no production credentials.

Full local validation requires a PostgreSQL server available to the test process through `ConnectionStrings__CharacterSheet`:

```text
(cd src/CharacterSheet.Web/Client && npm install --no-audit --no-fund && npm run build)
dotnet restore dorks-and-dice-character-sheet.slnx
dotnet test dorks-and-dice-character-sheet.slnx --configuration Release -p:BuildClient=false
ConnectionStrings__CharacterSheet='Host=postgres.example.invalid;Port=5432;Database=character_sheet;Username=character_sheet;Password=validation-only' docker compose -f docker-compose.yml config
docker build -t dorks-and-dice-character-sheet:test -f src/CharacterSheet.Web/Dockerfile .
```

CI starts a disposable PostgreSQL 18 server and runs the complete integration suite against isolated databases on that server. Container validation separately starts a disposable PostgreSQL 18 instance and verifies `/health`, `/ready`, and the frontend asset against the built Character Sheet image.

The standalone host serves `/`, `/new`, and `/characters/{characterId}` and mounts the same `/app.js` entry point used by the Site Tool Host. Standalone mode does not invent Site ownership. Without Tool Host authorization, Character data API requests are rejected.

## Production persistence

Character Sheet uses its own external PostgreSQL database. Production supplies `ConnectionStrings__CharacterSheet` through the server-side deployment environment file; the application does not contain production database credentials or a SQLite fallback.

The Character Sheet Compose definition owns only the application container. It does not define a PostgreSQL service, database filesystem mount, or named database volume. Normal application redeployment can recreate the Character Sheet container without recreating or deleting the external PostgreSQL database.
