# Changelog

## 1.2.0

### Added
- Default class loadouts shipped for all 6 vanilla classes (`commoner`, `hunter`, `malefactor`,
  `clockmaker`, `blackguard`, `tailor`), each with thematically appropriate gear.

### Changed
- Class loadouts moved from the inline `ClassLoadouts` config block (added in 1.1.0) to one file
  per class under `ModConfig/StarterChestClasses/` (e.g. `hunter.json`), seeded on first run.
  Easier for mod authors/communities to add support for a new class - just drop in one file - and
  avoids the main config growing unwieldy as more class mods get installed.

## 1.1.0

### Added
- `ClassLoadouts` config option - give specific character classes (e.g. `"hunter"`,
  `"clockmaker"`) their own loadout, with the same shape as the top-level config. Classes without
  an entry fall back to the top-level `FixedItems`/`RandomPool` as before.
- `/starterchest preview <player>` command - rolls the configured loot and prints what would be
  given, without spawning a chest or touching the player's received-chest flag.
- The starter-chest chat message is now localized per-player (`assets/starterchest/lang/en.json`)
  instead of being hardcoded English.

### Changed
- `RandomPickCount` now automatically caps itself to the real container's remaining slots (read
  from the placed container, so this works correctly for modded containers too) instead of
  rolling the full count and dropping/warning about overflow afterwards.

## 1.0.0

Initial release.

- One-time starter chest per player: guaranteed `FixedItems`, a weighted `RandomPool`, or both.
- Configurable container block and facing direction - the default chest, the trunk, or any other
  placeable container block, including modded ones.
- `/starterchest reset <player>` command for testing config changes without restarting the server.
