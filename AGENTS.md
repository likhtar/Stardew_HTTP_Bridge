# AGENTS.md

Public notes for `Stardew_HTTP_Bridge`: a local HTTP bridge for Stardew Valley that spawns items and invokes in-game features. Reply in Ukrainian.

## Architecture

- `HttpListener` on `http://127.0.0.1:8080/stardew/` (+ `localhost:8080`). POST body = `CommandPayload` JSON (case-insensitive):
  `Action`, `Target`, `Id`, `Amount`, `Duration`, `X`, `Y`.
- HTTP handler only enqueues into a `ConcurrentQueue`; actions run on the game thread in `OnUpdateTicked` (guards `Context.IsWorldReady`). Never run game code on the HTTP thread.
- Supported actions (switch in `ExecuteCommand`): `heal`, `spawn_item`, `drink`/`eat`, `cash`, `crash`, `health`, `energy`, `noise`/`sound`, `bomb`, `warp`, `hat`, `shuffle`, `delete`, `speed`, `slug`, `freeze`, `reverse`, `npc`, `fish`, `monster`/`spawn_monster`.
- HUD messages are in Ukrainian. Code style: `#nullable enable`, no code comments unless asked.

### Critical design decisions (learned the hard way)

- **Warp** (`TeleportPlayer`): NEVER use `GameLocation.getRandomTile()` — it can land the player in water/walls/off-map (unwinnable). Always warp to `location.GetData().DefaultArrivalTile` (the game's safe arrival point from `Data/Locations`). For random warp, pick only from the curated `SafeWarpLocations` list (Farm, Town, Beach, Saloon, SeedShop, …; mines/desert/caves excluded). Unknown location name → log warning, skip warp (do NOT silently fall back to Farm).
- **Reverse controls**: do NOT flip `xVelocity/yVelocity` (janky, only works mid-motion). Instead swap `Game1.options.moveUpButton[0]` ↔ `moveDownButton[0]`, `moveLeftButton[0]` ↔ `moveRightButton[0]`; save originals, restore after `duration * 60` ticks in `OnUpdateTicked`. Guard with `_reverseActive`.
- **NPC dialogue**: pick from `Utility.getAllCharacters()` filtered by `c.IsVillager` (any location, not just current). Show via `npc.CurrentDialogue.Push(new Dialogue(npc, "en", text)); Game1.drawDialogue(npc);` — not `new DialogueBox(...)` directly.

## Build

`dotnet build -c Release` (net6.0, `Pathoschild.Stardew.ModBuildConfig` 4.1.1). ModBuildConfig auto-copies the mod to the game's Mods folder and generates a release zip. No `GamePath` needed — it auto-detects the Steam install.
