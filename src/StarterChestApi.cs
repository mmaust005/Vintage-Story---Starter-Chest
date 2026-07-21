using System.Collections.Generic;
using Vintagestory.API.Server;

namespace StarterChest
{
	// Loot settings for one starter chest - same shape and meaning as the top-level
	// StarterChestConfig fields of the same name. Returned by a StarterChestLoadoutProvider to
	// override the top-level config for a given player.
	public class StarterChestLoadout
	{
		public bool RandomMode = true;
		public int RandomPickCount = 5;
		public bool AllowDuplicatePicks = false;
		public List<LootEntry> FixedItems = new List<LootEntry>();
		public List<LootEntry> RandomPool = new List<LootEntry>();
	}

	// Returned by a registered loadout provider. See StarterChestModSystem.RegisterLoadoutProvider.
	public class StarterChestLoadoutResult
	{
		// Loot settings to use instead of the top-level config. Required.
		public StarterChestLoadout Loadout;

		// Optional, already-localized label shown in the "A starter {DisplayName} chest has
		// appeared nearby!" message and in /starterchest preview output (e.g. "Hunter"). Leave
		// null/empty to use the generic, unlabeled message instead.
		public string DisplayName;
	}

	// Called once StarterChestModSystem is ready to resolve a loadout for a player. Return null to
	// fall back to the top-level config. See RegisterLoadoutProvider.
	public delegate StarterChestLoadoutResult StarterChestLoadoutProvider(IServerPlayer player);

	// Called while StarterChestModSystem decides whether it is safe to give a new player their
	// automatic chest yet. Return false to wait and check again shortly. See RegisterLoadoutProvider.
	public delegate bool StarterChestReadyCheck(IServerPlayer player);
}
