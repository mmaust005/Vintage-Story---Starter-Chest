# Starter Chest

Gives every new player on a Vintage Story 1.22.3 server a one-time chest of starting supplies,
placed on the ground near them the first time they spawn - like a Minecraft "starter kit"
datapack, but configurable and mod-aware.

- The chest is a normal vanilla chest, placed once per player and never refilled or respawned.
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
  "RandomMode": true,
  "RandomPickCount": 4,
  "AllowDuplicatePicks": false,
  "FixedItems": [],
  "RandomPool": [
    { "Code": "game:firestarter", "Type": "item", "MinQuantity": 1, "MaxQuantity": 1, "Weight": 15 }
  ]
}
```

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
is logged - it won't crash the chest or break other entries. The chest has a limited number of
slots (16 for a normal chest); if `FixedItems` + your random picks add up to more than that, the
extras are dropped and a warning is logged, so keep pick counts reasonable.

## Building

The game (1.22.3) targets .NET 10, which needs the .NET 10 SDK to compile against
`VintagestoryAPI.dll`/`VSSurvivalMod.dll`. This machine's default `dotnet` is still 9.0.300, so a
separate .NET 10 SDK was installed side by side at `C:\Users\Mitch\dotnet-sdk10` (not on PATH,
doesn't affect the system default). Build with that SDK explicitly:

```
C:\Users\Mitch\dotnet-sdk10\dotnet.exe build
```

This also copies `StarterChest.dll` and `modinfo.json` into
`%APPDATA%\VintagestoryData\Mods\StarterChest` automatically (see the `DeployMod` target in
`StarterChest.csproj`), so a restart of the game/server picks up the change.

If you'd rather always use this SDK for this project, add a `global.json` pinning it, or add
`C:\Users\Mitch\dotnet-sdk10` to PATH (before the existing dotnet) for this shell.
