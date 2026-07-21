# Changelog

## 1.1.0

### Added
- `/starterchest preview <player>` command - rolls the configured loot and prints what would be
  given, without spawning a chest or touching the player's received-chest flag.
- The starter-chest chat message is now localized per-player
  (`assets/starterchest/lang/en.json`) instead of being hardcoded English, and now names the
  actual configured container (chest, trunk, or whatever a modded one calls itself) instead of
  assuming "chest".
- A public addon API (`StarterChestModSystem.RegisterLoadoutProvider`) lets other mods override
  what a specific player gets - e.g. varying the loadout by character class - without forking or
  duplicating this mod's placement/container logic. See the README's "Addons" section.

### Changed
- `RandomPickCount` now automatically caps itself to the real container's remaining slots (read
  from the placed container, so this works correctly for modded containers too) instead of
  rolling the full count and dropping/warning about overflow afterwards.
- Default `ContainerCode` is now `game:stationarybasket` (a small reed chest, 8 slots) instead of
  the 16-slot chest - a starter kit fits comfortably without a backpack. Default `RandomPickCount`
  bumped from 4 to 5 to match.

## 1.0.0

Initial release.

- One-time starter chest per player: guaranteed `FixedItems`, a weighted `RandomPool`, or both.
- Configurable container block and facing direction - the default chest, the trunk, or any other
  placeable container block, including modded ones.
- `/starterchest reset <player>` command for testing config changes without restarting the server.
