# Stardew HTTP Bridge

SMAPI mod for Stardew Valley: a local HTTP bridge for spawning items and invoking in-game features.

Send a POST request to `http://127.0.0.1:8080/stardew/` with a `CommandPayload` JSON body. Fields (case-insensitive): `Action`, `Target`, `Id`, `Amount`, `Duration`, `X`, `Y`. The HTTP handler only queues requests; actions execute on the game thread.

Supported actions: `heal`, `spawn_item`, `drink`/`eat`, `cash`, `crash`, `health`, `energy`, `noise`/`sound`, `bomb`, `warp`, `hat`, `shuffle`, `delete`, `speed`, `slug`, `freeze`, `reverse`, `npc`, `fish`, `monster`/`spawn_monster`.

Build with `dotnet build -c Release` (net6.0, `Pathoschild.Stardew.ModBuildConfig` 4.1.1).
