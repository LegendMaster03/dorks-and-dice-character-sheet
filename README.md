# Dorks & Dice Character Sheet

Character Sheet is the separately deployable, player-facing Dorks & Dice tool for building and managing rich digital character sheets.

The Dorks & Dice Site owns canonical character identity, account ownership, character lifecycle, and campaign-character associations. Character Sheet owns rich sheet state attached to the Site-generated `CharacterId`. Rules Core owns normalized rule definitions.

This repository currently contains the production-capable application foundation only: .NET 10 projects, ASP.NET Core host, TypeScript/Vite Embedded Module frontend, Tool Host authentication adapter, domain/persistence boundaries, standalone development shell, Docker build, CI validation, and tests. It intentionally does not contain the full character builder or rules content.

See `docs/architecture.md` for the ownership, lifecycle, routing, authentication, Rules Core, and future persistence boundaries.

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
docker build -t dorks-and-dice-character-sheet:test -f src/CharacterSheet.Web/Dockerfile .
```

The standalone host serves `/`, `/new`, and `/characters/{characterId}` and mounts the same `/app.js` entry point used by the Site Tool Host.
