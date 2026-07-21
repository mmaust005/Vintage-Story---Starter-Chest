using System.Collections.Generic;

namespace StarterChest
{
	public class LootEntry
	{
		/// <summary>
		/// Item or block code, e.g. "game:bread-rye-perfect", "flint" (defaults to the "game" domain
		/// if no domain is given), or "othermodid:someitem" for items/blocks added by other mods.
		/// </summary>
		public string Code = "game:flint";

		/// <summary>"item" or "block".</summary>
		public string Type = "item";

		public int MinQuantity = 1;
		public int MaxQuantity = 1;

		/// <summary>Relative chance of being picked when RandomMode is on. Ignored for FixedItems.</summary>
		public int Weight = 100;
	}

	/// <summary>
	/// A per-class override of FixedItems/RandomPool/RandomPickCount/AllowDuplicatePicks - same
	/// shape and meaning as the top-level StarterChestConfig fields of the same name, just scoped
	/// to players of one character class instead of everyone.
	/// </summary>
	public class ClassLoadout
	{
		public bool RandomMode = true;
		public int RandomPickCount = 4;
		public bool AllowDuplicatePicks = false;
		public List<LootEntry> FixedItems = new List<LootEntry>();
		public List<LootEntry> RandomPool = new List<LootEntry>();
	}

	public class StarterChestConfig
	{
		/// <summary>
		/// Which block to place as the starter container, without an orientation suffix, e.g.
		/// "game:chest" (16 slots) or "game:trunk" (36 slots). Any valid placeable container block
		/// code works, including ones from other mods. Falls back to the default chest if the code
		/// is invalid or not a container. See ContainerOrientation for which way it faces.
		/// </summary>
		public string ContainerCode = "game:chest";

		/// <summary>
		/// Which direction the container faces: "north", "east", "south", or "west". Leave empty
		/// (the default) to pick a random direction for each player - purely cosmetic, doesn't
		/// affect slot count or any other behavior.
		/// </summary>
		public string ContainerOrientation = "";

		/// <summary>
		/// When true (the default), RandomPickCount entries are randomly drawn from RandomPool
		/// and added on top of FixedItems.
		/// When false, only FixedItems are given.
		/// </summary>
		public bool RandomMode = true;

		/// <summary>How many entries to draw from RandomPool when RandomMode is true.</summary>
		public int RandomPickCount = 4;

		/// <summary>If true, the same pool entry can be picked more than once.</summary>
		public bool AllowDuplicatePicks = false;

		/// <summary>Items always placed in the chest, regardless of RandomMode.</summary>
		public List<LootEntry> FixedItems = new List<LootEntry>();

		/// <summary>
		/// Candidate items for random picks. Only used when RandomMode is true. Left empty here on
		/// purpose - the real shipped default lives in assets/starterchest/config/defaultconfig.json
		/// and is only used when no user config file exists yet; this bare object is just the
		/// last-resort fallback if that packaged asset can't be found or parsed.
		/// </summary>
		public List<LootEntry> RandomPool = new List<LootEntry>();
	}
}
