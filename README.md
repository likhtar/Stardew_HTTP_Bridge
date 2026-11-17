# Stardew HTTP Bridge

SMAPI mod for Stardew Valley: a local HTTP bridge for spawning items and invoking in-game features.

Send a POST request to `http://127.0.0.1:8080/stardew/` with a `CommandPayload` JSON body. The HTTP handler only queues requests; actions execute on the game thread.

| Field | Meaning |
|---|---|
| `Action` | Action ID (case-insensitive) |
| `Target` | Warp destination |
| `Id` | Item / sound / monster / hat / fish ID |
| `Amount` | Count or magnitude |
| `Duration` | Seconds for timed effects |
| `X`, `Y` | Optional warp tile coordinates |

```json
{ "Action": "spawn_item", "Id": "(O)74", "Amount": 1 }
```

## Action IDs

| `Action` | Effect | Parameters |
|---|---|---|
| `heal` | Restore HP and stamina to max | — |
| `spawn_item` | Add item to inventory | `Id` = `(O)<id>` or bare numeric ID, `random` picks from the pool below; `Amount` (default `1`) |
| `drink`, `eat` | Consume an item | `Id` = qualified item ID (default `(O)346`, Coffee) |
| `cash` | Add gold | `Amount` (default `500`) |
| `crash` | Remove gold | `Amount` (default `1000`) |
| `health` | Change HP | `Amount` (default `15`) |
| `energy` | Change stamina | `Amount` (default `15`) |
| `noise`, `sound` | Play a sound cue | `Id` = cue name (default: random from the list below) |
| `bomb` | Place bombs near the player | `Id` = `cherry`/`regular`/`mega` or `286`/`287`/`288` (also `random`); `Amount` (default `1`) |
| `warp` | Teleport the player | `Target` = location name, `random`, or empty; `X`/`Y` optional (default: safe arrival tile) |
| `hat` | Equip a hat | `Id` = hat index (default: random `0..89`) |
| `shuffle` | Shuffle the inventory | — |
| `delete` | Delete one random inventory item | — |
| `speed` | Speed buff | `Amount` = bonus (default `3`), `Duration` seconds (default `10`) |
| `slug` | Slowness buff | `Amount` = slowdown (default `3`), `Duration` seconds (default `10`) |
| `freeze` | Lock player movement | `Duration` seconds (default `5`) |
| `reverse` | Invert movement controls | `Duration` seconds (default `10`) |
| `npc` | Show a random villager dialogue | — |
| `fish` | Open the bobber minigame | `Id` = fish item ID (default `(O)142`) |
| `monster`, `spawn_monster` | Spawn monsters | `Id` = monster type (default `random`/`adaptive`), `Amount` (default `1`) |
| `glitch`, `fake_crash` | Screen glitch + NPC scream | `Duration` seconds (default `3`) |

## Buff IDs

| Buff ID | Action | Effect |
|---|---|---|
| `firebot_speed` | `speed` | `BuffEffects.Speed = Amount`, duration `Duration * 1000` ms |
| `firebot_slug` | `slug` | `BuffEffects.Speed = -Amount`, duration `Duration * 1000` ms |

`freeze`, `reverse` and `glitch`/`fake_crash` are timed actions, not SMAPI buffs — their ID is the action name itself.

## Item IDs for `spawn_item`

With `Id: "random"` (or no `Id`), an item is picked from this weighted pool:

| Item ID | Weight | Count range |
|---|---|---|
| `330` | 10 | 1–5 |
| `382` | 10 | 1–5 |
| `378` | 8 | 1–4 |
| `380` | 8 | 1–3 |
| `390` | 6 | 5–10 |
| `388` | 6 | 5–10 |
| `80` | 6 | 1–3 |
| `771` | 6 | 3–6 |
| `535` | 5 | 1 |
| `536` | 5 | 1 |
| `537` | 4 | 1 |
| `749` | 3 | 1 |
| `60` | 2 | 1 |
| `62` | 2 | 1 |
| `64` | 2 | 1 |
| `66` | 2 | 1 |
| `68` | 2 | 1 |
| `70` | 1 | 1 |
| `72` | 5 | 1 |
| `788` | 4 | 1 |
| `337` | 4 | 1 |
| `74` | 2 | 1 |

## Bomb IDs for `bomb`

| `Id` | Qualified item ID |
|---|---|
| `cherry`, `286` | `(O)286` |
| `regular`, `287` | `(O)287` (default) |
| `mega`, `288` | `(O)288` |
| `random` | random of the three |

## Monster IDs for `monster` / `spawn_monster`

`bat`, `ghost`, `skeleton`, `serpent`, `shadow` / `shadowbrute`, `crab` / `rockcrab`, `mummy`, `fly`, `dust` / `dustspirit`; anything else spawns a green slime. `random` / `adaptive` picks a type unlocked by your mine progress.

## Sound IDs for `noise` / `sound`

With no `Id`, one of these cues is chosen at random:

```
Duck, cluck, rooster, cat, cow, pig, owl, crow,
ghost, parrot, slime, goat, dinosaur, mummyGasp,
thunder, gasp, stardrop, achievement, explosion, fireball,
wand, stairsdown, secret1, reward, yoba, portal, healSound,
levelUp, questcomplete, newArtifact, crystal, fairy, powerup,
hammer, coin, swordswipe, dirtyHit, discoverMineral,
axe, hoe, scissors, trashcan, openChest, shipment,
boulderBreak, clubhit, crit, slash, cast, pullItemFromWater,
dwop, boop, pluck, glug, sip, eating, gulp, grunt,
clack, purchase, sell, bubbles, furnace, woodWhack
```

## Warp locations for `warp`

`AnimalShop`, `ArchaeologyHouse`, `Backwoods`, `Beach`, `Blacksmith`, `BusStop`, `CommunityCenter`, `ElliottHouse`, `Farm`, `FishShop`, `Forest`, `HaleyHouse`, `Hospital`, `JojaMart`, `LeahHouse`, `ManorHouse`, `Mountain`, `Saloon`, `SamHouse`, `ScienceHouse`, `SeedShop`, `Sunroom`, `Tent`, `Town`, `Tunnel`, `WizardHouse`. Random warp only uses locations that are unlocked.

## Build

`dotnet build -c Release` (net6.0, `Pathoschild.Stardew.ModBuildConfig` 4.1.1).
