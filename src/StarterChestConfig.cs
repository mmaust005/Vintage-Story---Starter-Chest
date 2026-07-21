using System.Collections.Generic;

namespace StarterChest
{
	public class LootEntry
	{
		// Item or block code, e.g. "game:bread-rye-perfect" or "othermodid:someitem". No domain
		// prefix defaults to "game".
		public string Code = "game:flint";

		// "item" or "block".
		public string Type = "item";

		public int MinQuantity = 1;
		public int MaxQuantity = 1;

		// Relative chance of being picked when RandomMode is on. Ignored for FixedItems.
		public int Weight = 100;
	}

	public class StarterChestConfig
	{
		// Container block to place, without an orientation suffix, e.g. "game:chest" or
		// "game:trunk". Falls back to the default chest if invalid or not a container.
		public string ContainerCode = "game:chest";

		// "north", "east", "south", or "west". Empty picks a random direction per player.
		public string ContainerOrientation = "";

		// True: RandomPickCount entries drawn from RandomPool, added on top of FixedItems.
		// False: only FixedItems given.
		public bool RandomMode = true;

		// How many entries to draw from RandomPool when RandomMode is true.
		public int RandomPickCount = 5;

		// True: the same pool entry can be picked more than once.
		public bool AllowDuplicatePicks = false;

		// Items always placed, regardless of RandomMode.
		public List<LootEntry> FixedItems = new List<LootEntry>();

		// Candidate items for random picks. Empty here on purpose - the shipped default lives in
		// assets/starterchest/config/defaultconfig.json; this is only the last-resort fallback if
		// that packaged asset can't be found or parsed.
		public List<LootEntry> RandomPool = new List<LootEntry>();
	}
}
