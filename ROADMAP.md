# Roadmap

Ideas for after v1.0.0, not yet started.

- **Loot "kits"** - let `RandomPool` optionally group items into named bundles (e.g. "fisher kit"
  vs "miner kit") and pick one bundle rather than N loose weighted items. Top priority: tying kits
  to the character class the player picks at character creation, so new players get a
  class-appropriate starter kit instead of fully random loot.
- **Dry-run command** - `.starterchest preview <player>` that rolls and prints what would be
  given, without spawning a chest, for tuning loot weights without spamming test chests.
- **Auto-fit picks to slots** - instead of just warning when loot overflows the container slots,
  optionally auto-cap `RandomPickCount` to what actually fits.
- **Localization** - the "A starter chest has appeared nearby!" chat message is hardcoded English
  in `StarterChestModSystem.cs`; move it to a lang file so it can be translated like vanilla
  strings.
