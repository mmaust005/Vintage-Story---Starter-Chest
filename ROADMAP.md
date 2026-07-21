# Roadmap

- **Class-based starter loadouts** - still being designed. Not generic themed "kits" (fisher vs
  miner) - the starter chest should reflect the character class the player picked at creation,
  so a Hunter and a Clockmaker get different, class-appropriate loadouts instead of the same
  random pool. Needs figuring out how to read the player's chosen class server-side and how that
  maps onto config (e.g. a loadout per class, falling back to the existing FixedItems/RandomPool
  behavior for classes without one configured).

## Done (since v1.0.0)

- ~~Dry-run command~~ - `/starterchest preview <player>` rolls and prints what would be given,
  without spawning a chest.
- ~~Auto-fit picks to slots~~ - `RandomPickCount` now automatically caps itself to the real
  container's remaining slots (read from the placed container, so it works for modded containers
  too), instead of warning and dropping overflow.
- ~~Localization~~ - the starter-chest chat message now resolves per-player via
  `assets/starterchest/lang/en.json` instead of being hardcoded English.
