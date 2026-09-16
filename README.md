# Dorks & Dice Character Sheet

Character Sheet is the separately deployable, player-facing Dorks & Dice tool for building and managing rich digital character sheets.

The Dorks & Dice Site owns canonical Character identity, account ownership, Character lifecycle, and campaign-character associations. Character Sheet owns rich sheet state attached to the Site-generated `CharacterId`. Rules Core owns normalized rule definitions.

The current implementation provides the first real Character workflow: owner-only Tool Host Character authorization, EF Core/SQLite root persistence, canonical Site Character creation from `/new`, existing-basic-Character upgrades, archived read-only behavior, an explicit Embedded Module v2 frontend lifecycle, Docker persistence, CI validation, and tests. D&D character-building mechanics are intentionally deferred.

See `docs/architecture.md` for the ownership, authorization, persistence, API, lifecycle, and routing contracts.

## Development

Frontend:

```text
cd src/CharacterSheet.Web/Client
npm install
npm run build
```

Full validation:

```text
(cd src/CharacterSheet.Web/Client && npm install --no-audit --no-fund && npm run build)
dotnet restore dorks-and-dice-character-sheet.slnx
dotnet test dorks-and-dice-character-sheet.slnx --configuration Release -p:BuildClient=false
docker compose -f docker-compose.yml config
docker build -t dorks-and-dice-character-sheet:test -f src/CharacterSheet.Web/Dockerfile .
```

The standalone host serves `/`, `/new`, and `/characters/{characterId}` and mounts the same `/app.js` entry point used by the Site Tool Host. Standalone mode does not invent Site ownership. Without Tool Host authorization, Character data API requests are rejected.

Production Compose stores SQLite under the named `dorks-and-dice-character-sheet-data` volume rather than in the Git checkout. For an explicit standalone database, configure `ConnectionStrings__CharacterSheet`.
