# Roadmap

Nothing currently planned - suggestions welcome.

## Done (since v1.0.0)

- ~~Class-based starter loadouts~~ - `ClassLoadouts` config map, keyed by character class code
  (`"hunter"`, `"clockmaker"`, ...), each with its own `FixedItems`/`RandomPool`/
  `RandomPickCount`/`AllowDuplicatePicks`. Classes without an entry fall back to the top-level
  config. The player's class is read server-side from their entity at give-time.
- ~~Dry-run command~~ - `/starterchest preview <player>` rolls and prints what would be given,
  without spawning a chest.
- ~~Auto-fit picks to slots~~ - `RandomPickCount` now automatically caps itself to the real
  container's remaining slots (read from the placed container, so it works for modded containers
  too), instead of warning and dropping overflow.
- ~~Localization~~ - the starter-chest chat message now resolves per-player via
  `assets/starterchest/lang/en.json` instead of being hardcoded English.
