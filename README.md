# Starter Chest

Gives every new player on a Vintage Story 1.22.3 server a one-time chest of starting supplies,
placed on the ground near them the first time they spawn - a configurable, mod-aware starter kit.

- The container defaults to a small reed chest (8 slots) but is configurable (the regular chest,
  trunk, or any other placeable container block, including modded ones), placed once per player
  and never refilled or respawned. It faces a random direction by default, or a fixed one if
  configured.
- Loot combines guaranteed `FixedItems` with a weighted `RandomPool`, auto-fit to however many
  slots the chosen container actually has - no manual tuning needed for modded containers.
- Each player is tracked individually (server-side player data), so leaving and rejoining, or
  dying and respawning, will not grant a second chest.
- Item/block codes can reference any installed mod, not just vanilla content.
- Server operators get `/starterchest reset` and `/starterchest preview` commands for testing
  config changes without restarting the server or spawning extra chests.

## Testing / resetting a player

Since each player only ever gets one chest, re-testing config changes normally means creating a
new character. Instead, server operators (`controlserver` privilege) can run:

```
/starterchest reset <playername>
```

This clears that (online) player's starter-chest flag and immediately gives them a fresh chest
with the current config - no restart or new character needed.

To check what a config change *would* give without spawning a chest at all:

```
/starterchest preview <playername>
```

This rolls the current config and prints the resulting item list to chat, without placing
anything or touching the player's received-chest flag - handy for tuning `Weight` values without
littering the world with test chests.

## Config

On first run the mod writes `ModConfig/StarterChestConfig.json` in your server/game data folder,
seeded from the default loot list packaged with the mod
(`assets/starterchest/config/defaultconfig.json`). Edit the `ModConfig` copy and restart the
server (or reload the world) to apply changes - the packaged file is only ever used to seed a
missing config, it's never read again afterwards. If you want to preconfigure a server before
anyone has joined, you can write `ModConfig/StarterChestConfig.json` yourself ahead of time,
matching the schema below, and the mod will use it as-is instead of the packaged default.

To reset back to the packaged defaults, delete `ModConfig/StarterChestConfig.json` and restart
the server/world - it will be recreated from the packaged file.

```json
{
  "ContainerCode": "game:stationarybasket",
  "ContainerOrientation": "",
  "RandomMode": true,
  "RandomPickCount": 5,
  "AllowDuplicatePicks": false,
  "FixedItems": [],
  "RandomPool": [
    { "Code": "game:firestarter", "Type": "item", "MinQuantity": 1, "MaxQuantity": 1, "Weight": 15 }
  ]
}
```

- **ContainerCode** - which block to place as the starter container, *without* an orientation
  suffix. Defaults to `"game:stationarybasket"` (the small reed chest, 8 slots) - other good
  options are `"game:chest"` (16 slots) or `"game:trunk"` (36 slots). Any valid placeable
  container block code works, including ones from other mods. Falls back to the default chest,
  with a logged error, if the code is invalid or not a container.
- **ContainerOrientation** - which way the container faces: `"north"`, `"east"`, `"south"`, or
  `"west"`. Leave it as `""` (default) to pick a random direction for each player - it's purely
  cosmetic and has no effect on slot count or any other behavior. An invalid value falls back to
  random, with a logged warning.
- **RandomMode** - when `true` (default), `RandomPickCount` entries are drawn
  from `RandomPool` and added on top of `FixedItems`. When `false`, only `FixedItems` are given.
- **RandomPickCount** - how many random entries to draw per player.
- **AllowDuplicatePicks** - if `true`, the same pool entry can be picked more than once for the
  same chest.
- **FixedItems** - always given, regardless of `RandomMode`. Use this for a guaranteed baseline
  kit (e.g. always give a torch), and `RandomPool` for the randomized bonus items. Example
  (always give a torch and 1-2 bread):
  ```json
  "FixedItems": [
    { "Code": "game:torch-basic-up", "Type": "block", "MinQuantity": 1, "MaxQuantity": 1 },
    { "Code": "game:bread-rye-perfect", "Type": "item", "MinQuantity": 1, "MaxQuantity": 2 }
  ]
  ```
- **RandomPool** - candidate entries for random picks, each with a relative `Weight` (higher =
  more likely). Example of adding a modded item to the pool:
  ```json
  { "Code": "somemodid:magic-gem", "Type": "item", "MinQuantity": 1, "MaxQuantity": 1, "Weight": 5 }
  ```
Each entry (`FixedItems` and `RandomPool`) supports:

| Field | Meaning |
|---|---|
| `Code` | Item/block code, e.g. `"game:bread-rye-perfect"`. No domain prefix defaults to `game`. Use `"othermodid:someitem"` for items from other mods. |
| `Type` | `"item"` or `"block"`. |
| `MinQuantity` / `MaxQuantity` | Stack size is randomized in this (inclusive) range. |
| `Weight` | Relative chance of being picked (`RandomPool` only). |

If a configured code belongs to a mod that isn't installed, that entry is skipped and a warning
is logged - it won't crash the chest or break other entries. The container has a limited number
of slots (8 for the default reed chest, 16 for a normal chest, 36 for a trunk, varies for modded
containers). `FixedItems`
are given first; `RandomPickCount` then automatically caps itself to whatever slots are left in
*that specific container* (read from the real container once placed, so this works correctly for
modded containers too, not just the vanilla chest/trunk) - so you won't get a log warning from
this in normal use. The only case still worth a warning is `FixedItems` alone exceeding the
container's slots, since those are meant to be guaranteed and can't be auto-capped without
breaking that guarantee.

### How weighting works

`Weight` has no fixed scale (not 1-100, not percentages) - it's only ever compared to the other
`Weight` values currently in the pool. For any single draw, an entry's chance is:

```
entry's Weight / (sum of Weight across every entry still eligible for that draw)
```

For example, the packaged default pool has these weights:

| Entry | Weight | Chance on the first draw |
|---|---|---|
| `flint` | 25 | 25 / 130 ≈ 19% |
| `stick` | 25 | 25 / 130 ≈ 19% |
| `bread-rye-perfect` | 20 | 20 / 130 ≈ 15% |
| `cheese-cheddar-1slice` | 20 | 20 / 130 ≈ 15% |
| `firestarter` | 15 | 15 / 130 ≈ 12% |
| `rope` | 15 | 15 / 130 ≈ 12% |
| `knife-generic-flint` | 10 | 10 / 130 ≈ 8% |

(130 is the sum of all seven weights.) Doubling an entry's `Weight` roughly doubles its odds
relative to the others; it does not need to "fit" any total - add, remove, or reweight entries
freely and the percentages simply rebalance.

`RandomPickCount` draws happen one at a time, and **`AllowDuplicatePicks` changes what "still
eligible" means for the draws after the first**:

- `false` (default) - each entry can only be won once. After it's picked, it's removed from the
  pool for the rest of that chest, so the denominator shrinks and everyone else's odds go up
  slightly for the remaining draws. If `RandomPickCount` is greater than or equal to the number
  of entries in `RandomPool`, every entry just gets picked once (the pool runs out early, which is
  the situation `AllowDuplicatePicks` doesn't apply to).
- `true` - every draw is independent and re-rolls against the full pool with its original
  weights, so the same high-weight entry can be won multiple times (each with its own randomized
  quantity), and low-weight entries can end up skipped entirely by chance.

`Weight` is only used for `RandomPool`; it's ignored on `FixedItems` since those are always given.

## Building

The game (1.22.3) targets .NET 10, which needs the .NET 10 SDK to compile against
`VintagestoryAPI.dll`/`VSSurvivalMod.dll`. If your machine's default `dotnet` is older, install a
.NET 10 SDK side by side (e.g. under `%USERPROFILE%\dotnet-sdk10`) - it won't affect the system
default unless you put it on PATH. Build with that SDK explicitly:

```
& "$env:USERPROFILE\dotnet-sdk10\dotnet.exe" build
```

This also copies `StarterChest.dll` and `modinfo.json` into
`%APPDATA%\VintagestoryData\Mods\StarterChest` automatically (see the `DeployMod` target in
`StarterChest.csproj`), so a restart of the game/server picks up the change.

If you'd rather always use this SDK for this project, add a `global.json` pinning it, or add
your side-by-side SDK folder to PATH (before the existing `dotnet`) for this shell.

By default, `StarterChest.csproj` looks for the game/data folders under `%APPDATA%`. Set the
`VINTAGE_STORY` / `VINTAGE_STORY_DATA` environment variables to override either path.

### Packaging a release

To build a clean zip for the Mod DB or a GitHub Release - just the runtime files (dll, modinfo,
icon, assets, this README), no source or dev files:

```
& "$env:USERPROFILE\dotnet-sdk10\dotnet.exe" build -c Release -t:PackMod
```

This writes `release/StarterChest.zip` (gitignored, rebuilt fresh each time).

## Addons

Other mods can override what a specific player gets - for example, varying the loadout by
character class - without forking or reimplementing any of this mod's placement/container logic.
`StarterChestModSystem` exposes:

```csharp
public void RegisterLoadoutProvider(StarterChestLoadoutProvider provider, StarterChestReadyCheck readyCheck = null)
```

- `provider` is called once this mod is ready to resolve a loadout for a player, and returns a
  `StarterChestLoadoutResult` (a `StarterChestLoadout` - same shape as the top-level config - plus
  an optional, already-localized `DisplayName` shown in the "A starter {DisplayName} chest has
  appeared nearby!" message and in `/starterchest preview` output). Return `null` to fall back to
  the top-level config for that player.
- `readyCheck` is optional. If given, it's polled briefly (bounded by a timeout) before giving a
  new player's automatic chest, so the provider can wait for whatever it needs - e.g. character
  creation to finish - before being asked to resolve anything. Only one provider can be registered
  at a time.

Call it from your addon's `StartServerSide`, once the base mod is loaded:

```csharp
sapi.ModLoader.GetModSystem<StarterChestModSystem>()?.RegisterLoadoutProvider(MyProvider, MyReadyCheck);
```

The official class-based-loadout addon, [Starter Chest: Class Loadouts](https://github.com/mmaust005/VS-StarterChestClasses),
is built entirely on this API and is a good reference implementation.

## Changelog

See [CHANGELOG.md](CHANGELOG.md).

## License

Source code is [MIT licensed](LICENSE). The mod icon and other artwork are All Rights Reserved -
see [`assets/NOTICE.md`](assets/NOTICE.md).
