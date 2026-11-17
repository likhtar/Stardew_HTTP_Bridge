# AGENTS.md

Distilled knowledge from the Stardew Valley streaming setup for future AI sessions.
User: Ukrainian streamer `likh_tar`. Speaks Ukrainian — reply in Ukrainian.

## Projects

| Path | What it is |
|---|---|
| `/home/likhtar/Stardew_HTTP_Bridge` | **Main mod (this repo).** SMAPI mod: local HTTP server that executes Twitch redeem actions in-game. |
| `/home/likhtar/KeyLayoutFix` | SMAPI mod: fixes movement when the game runs with a non-Latin (Ukrainian) keyboard layout. |
| `/home/likhtar/Downloads/CommandsForTwitchChatMod` | Reference mod (Miihau, compiled DLL only). Good patterns for warp / reverse controls / NPC dialogue / curated safe-location list. |

## Stardew_HTTP_Bridge architecture

- `HttpListener` on `http://127.0.0.1:8080/stardew/` (+ `localhost:8080`). POST body = `CommandPayload` JSON (case-insensitive):
  `Action`, `Target`, `Id`, `Amount`, `Duration`, `X`, `Y`.
- HTTP handler only enqueues into a `ConcurrentQueue`; actions run on the game thread in `OnUpdateTicked` (guards `Context.IsWorldReady`). Never run game code on the HTTP thread.
- Supported actions (switch in `ExecuteCommand`): `heal`, `spawn_item`, `drink`/`eat`, `cash`, `crash`, `health`, `energy`, `noise`/`sound`, `bomb`, `warp`, `hat`, `shuffle`, `delete`, `speed`, `slug`, `freeze`, `reverse`, `npc`, `fish`, `monster`/`spawn_monster`.
- HUD messages are in Ukrainian. Code style: `#nullable enable`, no code comments unless asked.

### Critical design decisions (learned the hard way)

- **Warp** (`TeleportPlayer`): NEVER use `GameLocation.getRandomTile()` — it can land the player in water/walls/off-map (unwinnable). Always warp to `location.GetData().DefaultArrivalTile` (the game's safe arrival point from `Data/Locations`). For random warp, pick only from the curated `SafeWarpLocations` list (Farm, Town, Beach, Saloon, SeedShop, …; mines/desert/caves excluded). Unknown location name → log warning, skip warp (do NOT silently fall back to Farm).
- **Reverse controls**: do NOT flip `xVelocity/yVelocity` (janky, only works mid-motion). Instead swap `Game1.options.moveUpButton[0]` ↔ `moveDownButton[0]`, `moveLeftButton[0]` ↔ `moveRightButton[0]`; save originals, restore after `duration * 60` ticks in `OnUpdateTicked`. Guard with `_reverseActive`.
- **NPC dialogue**: pick from `Utility.getAllCharacters()` filtered by `c.IsVillager` (any location, not just current). Show via `npc.CurrentDialogue.Push(new Dialogue(npc, "en", text)); Game1.drawDialogue(npc);` — not `new DialogueBox(...)` directly.

## KeyLayoutFix (keyboard layout bug)

- Symptom: character can't move (WASD) when the OS layout is Ukrainian/Cyrillic.
- Root cause: SDL2 emits layout-dependent `keysym` (physical W → 1094 `ч`); MonoGame `KeyboardUtil.ToXna` only maps ASCII → `Keys.None` → SDV sees no key down.
- Fix: `SDL_AddEventWatch` rewrites `keysym.Sym` (byte offset **20**, scancode at offset **16**, confirmed from MonoGame's `Sdl.Keyboard.Event`) to the physical QWERTY ASCII for SDL scancodes 4–39 (`4..29`→`'a'..'z'`, `30..38`→`'1'..'9'`, `39`→`'0'`), before MonoGame's `SdlRunLoop` reads `_event.Key.Keysym.Sym`. Text input (`SDL_TEXTINPUT`) untouched, so Ukrainian chat still works.
- Do NOT patch `KeyboardUtil.ToXna` with Harmony if avoidable: Harmony's MonoMod `mm-exhelper.so` needs an executable stack, and this machine's glibc 2.44 **refuses execstack dlopen** (`cannot enable executable stack as shared object requires: Invalid argument`). This affects processes launched from the shell (seccomp/NoNewPrivs are 0; plain mprotect works). In the real game process Harmony works (ConvenientInventory patches fine) — the game's own runtime/apphost avoids it. Verified end-to-end with a net6 self-contained harness pushing synthetic SDL events through the game's `libSDL2-2.0.so.0`.

## Build & deploy

- Both mods: `dotnet build -c Release` (net6.0, `Pathoschild.Stardew.ModBuildConfig` 4.1.1). ModBuildConfig auto-copies to the game Mods folder and makes a release zip. No `GamePath` needed — it auto-detects via `~/.steam/steam` → `~/.local/share/Steam`.
- Game dir: `/home/likhtar/.local/share/Steam/steamapps/common/Stardew Valley/` (`~/.steam/steam` is a symlink to it — same inode).
- Installed mods live in `…/Mods/`: ContentPatcher 2.8.0, `[CP] Ukrainizacija v1.6.15`, UKRUTILITIES 1.6.0, Stardew_HTTP_Bridge, KeyLayoutFix, ConvenientInventory, LookupAnything, NPCMapLocations, etc.
- Game: SDV 1.6.15 (build 24356), SMAPI 4.5.2, native Linux, MonoGame.Framework 3.8.0.1641.
- SDK on system: only .NET 10 (`/usr/share/dotnet`). Game bundles its own .NET 6 runtime (self-contained). To run a net6 console harness locally: `dotnet publish -c Release -r linux-x64 --self-contained true` then run the published binary.

## Firebot integration

- Profile JSON: `/home/likhtar/.config/Firebot/v5/profiles/Main/channel-rewards.json` (live-edited; **Firebot must be restarted** to reload).
- Tags: `стардік_команди` = `fb2643bf-2347-47f1-a5e7-dd1332ac370c` (11 rewards converted from `firebot:chat` → `firebot:http-request` POST `http://127.0.0.1:8080/stardew/` with per-reward JSON), `стардік_ліхтармод` = `7b99d23f-0581-49f7-bef5-9f403d32b669` (finished ones, same pattern). Tag→id mapping: `sort-tags.json`.
- Reward→action mapping: +500G→`cash 500`, −1000G→`crash 1000`, Телепорт на ферму→`warp Farm`, діалог NPC→`npc`, випадкова локація→`warp` (no target), Бомба→`bomb`, Заморозка 5с→`freeze 5`, Обернути керування→`reverse 10`, HP+енергія→`health 15`+`energy 15`, Сповільнити→`slug 10`, Прискорити→`speed 10`. Error chat effect: «НЕ РОБЕ».
- Rewrite script/backup: `/tmp/opencode/convert_rewards.py`, `/tmp/opencode/channel-rewards.json.bak`.

## Debugging toolkit

- `~/.dotnet/tools/ilspycmd` — decompile any game/mod DLL: `ilspycmd -t "Type.Name" <dll>` or `ilspycmd -p <dll> -o <outdir>`. Decompiled artifacts in `/tmp/opencode/` (e.g. `game1.cs`, `sdl.cs`, `twitchchat_mod_decompiled/`).
- SMAPI log: `/home/likhtar/.config/StardewValley/ErrorLogs/SMAPI-latest.txt`.
- MCP debugger: `debug-mcp` server configured in `~/.config/opencode/opencode.jsonc`; launch via `dotnet tool exec debug-mcp -y` (no `dnx` in this SDK). opencode must be restarted to pick up config changes.
- libSDL2 symbols confirmed on the game's `libSDL2-2.0.so.0`: `SDL_GetKeyFromScancode`, `SDL_GetScancodeFromKey`, `SDL_Init`, `SDL_AddEventWatch`, `SDL_GetKeyFromScancode`. SDL version 2.30.4.
- `SDL_GetScancodeFromKey` returns 0 for Cyrillic keysyms — build reverse maps yourself by enumerating scancodes 0..N with `SDL_GetKeyFromScancode`.

## Environment quirks

- X11 has 2 layout groups: Group1 = `us`, Group2 = Ukrainian. `setxkbmap -query` shows only the active group.
- `dlopen` of any exec-stack `.so` fails in shell-spawned processes on this machine (glibc 2.44) — see KeyLayoutFix section. Workarounds: run through the game process, or use net6 self-contained publishes (same result) — the game process is the only place Harmony is known to work.
- Locale is `uk_UA`; the system SDL builds a Cyrillic keymap from locale when there's no video backend — treat this as normal.
- ffmpeg n9.0 (FFmpeg 63) regression: `setpts` alone drops every 2nd frame (output timebase = frame duration, fractional PTS truncate → collisions). ALWAYS chain a `fps` filter after `setpts` (e.g. `setpts=PTS/SPEED,fps=<orig_fps*SPEED>`). Was fine in n8.1.2. Found via `sticker_circle.sh` (4.96s → stuck at 3.04s instead of 2.99s).
