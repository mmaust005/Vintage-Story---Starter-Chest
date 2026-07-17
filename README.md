# Starter Chest

Gives every new player on a Vintage Story 1.22.3 server a one-time chest of starting supplies,
placed on the ground near them the first time they spawn - like a Minecraft "starter kit"
datapack, but configurable and mod-aware.

- The container defaults to a normal vanilla chest but is configurable (chest, trunk, or any
  other placeable container block, including modded ones), placed once per player and never
  refilled or respawned.
- Each player is tracked individually (server-side player data), so leaving and rejoining, or
  dying and respawning, will not grant a second chest.
- Item/block codes can reference any installed mod, not just vanilla content.

## Config

On first run the mod writes `ModConfig/StarterChestConfig.json` in your server/game data folder,
seeded from the default loot list packaged with the mod
(`assets/starterchest/config/defaultconfig.json`). Edit the `ModConfig` copy and restart the
server (or reload the world) to apply changes - the packaged file is only ever used to seed a
missing config, it's never read again afterwards. If you want to preconfigure a server before
anyone has joined, you can write `ModConfig/StarterChestConfig.json` yourself ahead of time,
matching the schema below, and the mod will use it as-is instead of the packaged default.

```json
{
  "ContainerCode": "game:chest-north",
  "RandomMode": true,
  "RandomPickCount": 4,
  "AllowDuplicatePicks": false,
  "FixedItems": [],
  "RandomPool": [
    { "Code": "game:firestarter", "Type": "item", "MinQuantity": 1, "MaxQuantity": 1, "Weight": 15 }
  ]
}
```

- **ContainerCode** - which block to place as the starter container, e.g. `"game:chest-north"`
  (16 slots, default) or `"game:trunk-north"` (36 slots). Any valid placeable container block
  code works, including ones from other mods. Falls back to the default chest, with a logged
  error, if the code is invalid or not a container.
- **RandomMode** - when `true` (default, Minecraft-style), `RandomPickCount` entries are drawn
  from `RandomPool` and added on top of `FixedItems`. When `false`, only `FixedItems` are given.
- **RandomPickCount** - how many random entries to draw per player.
- **AllowDuplicatePicks** - if `true`, the same pool entry can be picked more than once for the
  same chest.
- **FixedItems** - always given, regardless of `RandomMode`. Use this for a guaranteed baseline
  kit (e.g. always give a torch), and `RandomPool` for the randomized bonus items.
- **RandomPool** - candidate entries for random picks, each with a relative `Weight` (higher =
  more likely).

Each entry (`FixedItems` and `RandomPool`) supports:

| Field | Meaning |
|---|---|
| `Code` | Item/block code, e.g. `"game:bread-rye-perfect"`. No domain prefix defaults to `game`. Use `"othermodid:someitem"` for items from other mods. |
| `Type` | `"item"` or `"block"`. |
| `MinQuantity` / `MaxQuantity` | Stack size is randomized in this (inclusive) range. |
| `Weight` | Relative chance of being picked (`RandomPool` only). |

If a configured code belongs to a mod that isn't installed, that entry is skipped and a warning
is logged - it won't crash the chest or break other entries. The container has a limited number
of slots (16 for the default chest, 36 for a trunk, varies for modded containers); if
`FixedItems` + your random picks add up to more than that, the extras are dropped and a warning
is logged, so keep pick counts reasonable.

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
