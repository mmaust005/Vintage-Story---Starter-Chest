using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace StarterChest
{
	public class StarterChestModSystem : ModSystem
	{
		const string ConfigFilename = "StarterChestConfig.json";
		const string ReceivedModDataKey = "starterchest:received";
		static readonly AssetLocation PackagedDefaultConfigLocation = new AssetLocation("starterchest", "config/defaultconfig.json");

		ICoreServerAPI sapi;
		StarterChestConfig config;
		readonly HashSet<string> warnedMissingCodes = new HashSet<string>();

		public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Server;

		public override void StartServerSide(ICoreServerAPI api)
		{
			sapi = api;
			LoadConfig();

			sapi.Event.PlayerNowPlaying += OnPlayerNowPlaying;

			sapi.ChatCommands.Create("starterchest")
				.WithDescription("Manage the Starter Chest mod")
				.RequiresPrivilege(Privilege.controlserver)
				.BeginSubCommand("reset")
					.WithDescription("Clears the given (online) player's starter-chest flag and immediately gives them a fresh one - handy for testing config changes without restarting the server.")
					.WithArgs(sapi.ChatCommands.Parsers.OnlinePlayer("player"))
					.HandleWith(OnResetCommand)
				.EndSubCommand()
				.BeginSubCommand("preview")
					.WithDescription("Rolls the configured loot and prints what would be given, without spawning a chest or touching the player's received-chest flag - for tuning weights without spamming test chests.")
					.WithArgs(sapi.ChatCommands.Parsers.OnlinePlayer("player"))
					.HandleWith(OnPreviewCommand)
				.EndSubCommand();
		}

		TextCommandResult OnResetCommand(TextCommandCallingArgs args)
		{
			var target = (IServerPlayer)args[0];

			target.SetModData(ReceivedModDataKey, true);

			if (GiveStarterChest(target))
			{
				return TextCommandResult.Success($"Reset and gave {target.PlayerName} a fresh starter chest.");
			}
			return TextCommandResult.Success($"Cleared {target.PlayerName}'s starter-chest flag, but no loot is configured (check FixedItems/RandomPool) so no chest was given.");
		}

		TextCommandResult OnPreviewCommand(TextCommandCallingArgs args)
		{
			var target = (IServerPlayer)args[0];

			Block containerBlock = ResolveContainerBlock();
			if (containerBlock == null)
			{
				return TextCommandResult.Error("Could not resolve a valid container block - check ContainerCode.");
			}

			string classCode = GetClassCode(target);
			EffectiveLoadout loadout = ResolveLoadout(classCode, out bool usedClassLoadout);

			int? maxSlots = EstimateSlotCount(containerBlock);
			List<ItemStack> stacks = BuildLootStacks(maxSlots ?? int.MaxValue, loadout);

			string loadoutDesc = usedClassLoadout ? $"'{classCode}' class loadout" : "default loadout";
			if (stacks.Count == 0)
			{
				return TextCommandResult.Success($"No loot configured in the {loadoutDesc} (check FixedItems/RandomPool) - nothing would be given.");
			}

			var sb = new StringBuilder();
			sb.AppendLine($"Would give {target.PlayerName} ({loadoutDesc}) a {containerBlock.Code} with {stacks.Count} stack(s):");
			foreach (ItemStack stack in stacks)
			{
				sb.AppendLine($"  {stack.Collectible.Code} x{stack.StackSize}");
			}
			if (maxSlots == null)
			{
				sb.Append("(Couldn't determine this container's slot count ahead of placement, so RandomPickCount wasn't capacity-capped for this preview - the real chest may hold fewer stacks.)");
			}

			return TextCommandResult.Success(sb.ToString());
		}

		// The character class the player picked at creation, e.g. "hunter", "clockmaker",
		// "commoner" - null if unset (shouldn't normally happen for a fully-created character).
		static string GetClassCode(IServerPlayer player) => player.Entity.WatchedAttributes.GetString("characterClass", null);

		void LoadConfig()
		{
			string configDir = sapi.GetOrCreateDataPath("ModConfig");
			string configPath = System.IO.Path.Combine(configDir, ConfigFilename);

			if (!System.IO.File.Exists(configPath))
			{
				// Seed the on-disk file with the packaged asset's raw bytes (not a re-serialized
				// object) so the formatting survives onto disk as-is. This file is never touched
				// again afterwards, so any edits/comments the user later adds stick around.
				IAsset seedAsset = sapi.Assets.TryGet(PackagedDefaultConfigLocation, true);
				if (seedAsset != null)
				{
					try
					{
						System.IO.File.WriteAllBytes(configPath, seedAsset.Data);
					}
					catch (Exception e)
					{
						sapi.Logger.Error("[StarterChest] Failed to write default config to '{0}': {1}", configPath, e.Message);
					}
				}
				else
				{
					sapi.Logger.Error("[StarterChest] Packaged default config '{0}' not found - no config was created on disk.", PackagedDefaultConfigLocation);
				}
			}

			try
			{
				config = sapi.LoadModConfig<StarterChestConfig>(ConfigFilename);
			}
			catch (Exception e)
			{
				sapi.Logger.Error("[StarterChest] Failed to parse '{0}': {1}. Using packaged defaults for this session - your file on disk was left untouched, fix it and restart.", configPath, e.Message);
				config = null;
			}

			if (config == null)
			{
				config = LoadPackagedDefaultConfig();
			}
		}

		StarterChestConfig LoadPackagedDefaultConfig()
		{
			IAsset asset = sapi.Assets.TryGet(PackagedDefaultConfigLocation, true);
			if (asset == null)
			{
				sapi.Logger.Error("[StarterChest] Packaged default config '{0}' not found - using an empty built-in fallback (no loot configured).", PackagedDefaultConfigLocation);
				return new StarterChestConfig();
			}

			try
			{
				return asset.ToObject<StarterChestConfig>();
			}
			catch (Exception e)
			{
				sapi.Logger.Error("[StarterChest] Failed to parse packaged default config '{0}': {1}. Using an empty built-in fallback (no loot configured).", PackagedDefaultConfigLocation, e.Message);
				return new StarterChestConfig();
			}
		}

		void OnPlayerNowPlaying(IServerPlayer byPlayer)
		{
			if (byPlayer.GetModData(ReceivedModDataKey, false))
			{
				return;
			}

			byPlayer.SetModData(ReceivedModDataKey, true);
			GiveStarterChest(byPlayer);
		}

		// Resolved set of FixedItems/RandomPool/RandomPickCount/AllowDuplicatePicks/RandomMode to
		// actually use for one chest - either a matching ClassLoadout, or the top-level config.
		class EffectiveLoadout
		{
			public bool RandomMode;
			public int RandomPickCount;
			public bool AllowDuplicatePicks;
			public List<LootEntry> FixedItems;
			public List<LootEntry> RandomPool;
		}

		EffectiveLoadout ResolveLoadout(string classCode, out bool usedClassLoadout)
		{
			if (!string.IsNullOrEmpty(classCode) && config.ClassLoadouts.TryGetValue(classCode, out ClassLoadout loadout))
			{
				usedClassLoadout = true;
				return new EffectiveLoadout
				{
					RandomMode = loadout.RandomMode,
					RandomPickCount = loadout.RandomPickCount,
					AllowDuplicatePicks = loadout.AllowDuplicatePicks,
					FixedItems = loadout.FixedItems,
					RandomPool = loadout.RandomPool,
				};
			}

			usedClassLoadout = false;
			return new EffectiveLoadout
			{
				RandomMode = config.RandomMode,
				RandomPickCount = config.RandomPickCount,
				AllowDuplicatePicks = config.AllowDuplicatePicks,
				FixedItems = config.FixedItems,
				RandomPool = config.RandomPool,
			};
		}

		List<ItemStack> BuildLootStacks(int maxSlots, EffectiveLoadout loadout)
		{
			var result = new List<ItemStack>();

			foreach (LootEntry entry in loadout.FixedItems)
			{
				if (result.Count >= maxSlots)
				{
					sapi.Logger.Warning("[StarterChest] FixedItems alone ({0} entries) exceed the container's {1} slot(s) - the rest were skipped.", loadout.FixedItems.Count, maxSlots);
					break;
				}

				ItemStack stack = ResolveStack(entry);
				if (stack != null) result.Add(stack);
			}

			if (loadout.RandomMode)
			{
				var pool = loadout.RandomPool.Where(e => ResolveCollectible(e) != null).ToList();
				var remaining = new List<LootEntry>(pool);

				// Auto-fit: never attempt more picks than the container has room left for, instead
				// of rolling the full RandomPickCount and dropping whatever doesn't fit afterwards.
				int picks = Math.Min(loadout.RandomPickCount, Math.Max(0, maxSlots - result.Count));
				for (int i = 0; i < picks && remaining.Count > 0; i++)
				{
					int totalWeight = remaining.Sum(e => Math.Max(0, e.Weight));
					if (totalWeight <= 0) break;

					int roll = sapi.World.Rand.Next(totalWeight);
					int cumulative = 0;
					LootEntry chosen = null;
					foreach (LootEntry entry in remaining)
					{
						cumulative += Math.Max(0, entry.Weight);
						if (roll < cumulative)
						{
							chosen = entry;
							break;
						}
					}
					if (chosen == null) break;

					ItemStack stack = ResolveStack(chosen);
					if (stack != null) result.Add(stack);

					if (!loadout.AllowDuplicatePicks) remaining.Remove(chosen);
				}
			}

			return result;
		}

		CollectibleObject ResolveCollectible(LootEntry entry)
		{
			var loc = new AssetLocation(entry.Code);
			CollectibleObject collectible = string.Equals(entry.Type, "block", StringComparison.OrdinalIgnoreCase)
				? (CollectibleObject)sapi.World.BlockAccessor.GetBlock(loc)
				: sapi.World.GetItem(loc);

			if (collectible == null || collectible.Id == 0)
			{
				if (warnedMissingCodes.Add(entry.Code))
				{
					sapi.Logger.Warning("[StarterChest] Configured loot entry '{0}' ({1}) was not found - is the mod that adds it installed? Skipping.", entry.Code, entry.Type);
				}
				return null;
			}

			return collectible;
		}

		ItemStack ResolveStack(LootEntry entry)
		{
			CollectibleObject collectible = ResolveCollectible(entry);
			if (collectible == null) return null;

			int min = Math.Max(1, Math.Min(entry.MinQuantity, entry.MaxQuantity));
			int max = Math.Max(min, Math.Max(entry.MinQuantity, entry.MaxQuantity));
			int qty = sapi.World.Rand.Next(min, max + 1);

			return new ItemStack(collectible, qty);
		}

		const string DefaultContainerCode = "game:chest";
		static readonly string[] Orientations = { "north", "east", "south", "west" };

		Block ResolveContainerBlock()
		{
			// Picked once per chest so a fixed ContainerOrientation applies consistently to both
			// the configured code and the fallback, instead of re-rolling for each.
			string orientation = PickOrientation();

			Block block = ResolveContainerBlockForBaseCode(config.ContainerCode, orientation);
			if (block != null)
			{
				return block;
			}

			sapi.Logger.Error("[StarterChest] Configured ContainerCode '{0}' is not a valid container block - falling back to the default chest ('{1}').", config.ContainerCode, DefaultContainerCode);
			return ResolveContainerBlockForBaseCode(DefaultContainerCode, orientation);
		}

		Block ResolveContainerBlockForBaseCode(string baseCode, string orientation)
		{
			Block block = sapi.World.BlockAccessor.GetBlock(new AssetLocation($"{baseCode}-{orientation}"));
			if (IsContainerBlock(block)) return block;

			// Not every container is direction-variant (some modded ones, or an already-complete
			// code like "somemodid:special-chest-north") - fall back to the bare code as-is.
			block = sapi.World.BlockAccessor.GetBlock(new AssetLocation(baseCode));
			if (IsContainerBlock(block)) return block;

			return null;
		}

		static bool IsContainerBlock(Block block) => block != null && block.Id != 0 && !string.IsNullOrEmpty(block.EntityClass);

		string PickOrientation()
		{
			if (!string.IsNullOrWhiteSpace(config.ContainerOrientation))
			{
				string requested = config.ContainerOrientation.Trim().ToLowerInvariant();
				if (Array.IndexOf(Orientations, requested) >= 0)
				{
					return requested;
				}
				sapi.Logger.Warning("[StarterChest] Configured ContainerOrientation '{0}' is not a valid direction (north/east/south/west) - picking a random direction instead.", config.ContainerOrientation);
			}

			return Orientations[sapi.World.Rand.Next(Orientations.Length)];
		}

		static float? OrientationToMeshAngle(string orientation)
		{
			switch (orientation)
			{
				case "north": return 0f;
				case "east": return GameMath.PI + GameMath.PIHALF;
				case "south": return GameMath.PI;
				case "west": return GameMath.PIHALF;
				default: return null;
			}
		}

		// Best-effort slot count read from the block's own JSON attributes, following the same
		// "quantitySlots"/"defaultType" convention the vanilla chest and trunk use. Only meant for
		// the preview command, which can't place a real block entity to ask directly - a modded
		// container that doesn't follow this convention just won't get capped in the preview.
		static int? EstimateSlotCount(Block block)
		{
			string type = block.Attributes?["defaultType"]?.AsString(null);
			if (string.IsNullOrEmpty(type)) return null;

			int count = block.Attributes["quantitySlots"][type].AsInt(-1);
			return count > 0 ? (int?)count : null;
		}

		bool GiveStarterChest(IServerPlayer player)
		{
			EffectiveLoadout loadout = ResolveLoadout(GetClassCode(player), out _);

			// Cheap pre-check so we don't place a chest at all when nothing could ever be given -
			// the real, capacity-aware loot list is only known once the container is placed below.
			bool anyPossibleLoot = loadout.FixedItems.Count > 0
				|| (loadout.RandomMode && loadout.RandomPool.Count > 0 && loadout.RandomPickCount > 0);
			if (!anyPossibleLoot) return false;

			Block containerBlock = ResolveContainerBlock();
			if (containerBlock == null)
			{
				sapi.Logger.Error("[StarterChest] Could not find the default chest block either - aborting starter chest placement.");
				return false;
			}

			BlockPos pos = FindChestPosition(player, containerBlock);

			sapi.World.BlockAccessor.SetBlock(containerBlock.Id, pos);

			// Runs block-level placement behaviors (e.g. the trunk's Multiblock behavior, which
			// registers its second block position) - SetBlock alone only sets the voxel, it doesn't
			// invoke placement hooks the way a normal player placement would.
			containerBlock.OnBlockPlaced(sapi.World, pos, null);

			var be = sapi.World.BlockAccessor.GetBlockEntity<BlockEntityGenericTypedContainer>(pos);
			if (be == null)
			{
				sapi.Logger.Error("[StarterChest] Container block entity did not spawn at {0} - aborting.", pos);
				return false;
			}

			be.OnBlockPlaced(null);

			// SetBlock places the correct block variant (chest-north/east/south/west, ...) but
			// bypasses DoPlaceBlock, which is what normally derives the entity's rendered MeshAngle
			// from the player's facing during a real placement. Without this, every chest renders
			// at the same default angle regardless of which variant was actually placed.
			if (containerBlock.Variant.TryGetValue("side", out string side))
			{
				float? meshAngle = OrientationToMeshAngle(side);
				if (meshAngle.HasValue)
				{
					be.MeshAngle = meshAngle.Value;
				}
			}

			// The real, live slot count - this is what makes the RandomPickCount auto-fit in
			// BuildLootStacks work correctly for any container, vanilla or modded, without needing
			// to know its schema in advance.
			List<ItemStack> stacks = BuildLootStacks(be.Inventory.Count, loadout);
			if (stacks.Count == 0)
			{
				// Everything configured turned out to be unresolvable (e.g. all missing-mod codes) -
				// remove the now-pointless empty container instead of leaving it sitting there.
				sapi.World.BlockAccessor.SetBlock(0, pos);
				return false;
			}

			FillInventory(be, stacks);
			be.MarkDirty(true, null);

			sapi.Logger.Notification("[StarterChest] Gave {0} a starter chest ({1}) at {2} ({3} item stack(s)).", player.PlayerName, containerBlock.Code, pos, stacks.Count);
			player.SendMessage(GlobalConstants.GeneralChatGroup, Lang.GetL(player.LanguageCode, "starterchest:chest-appeared"), EnumChatType.Notification, null);
			return true;
		}

		void FillInventory(BlockEntityGenericTypedContainer be, List<ItemStack> stacks)
		{
			InventoryBase inv = be.Inventory;

			for (int slot = 0; slot < stacks.Count; slot++)
			{
				inv[slot].Itemstack = stacks[slot];
				inv.MarkSlotDirty(slot);
			}
		}

		// The trunk visually/physically occupies a second cell next to the one it's actually placed
		// in (a "virtual" second cell handled via collision-mirroring, not a second SetBlock), sized
		// and positioned per orientation in its own asset (assets/survival/blocktypes/wood/chest-trunk.json,
		// the "Multiblock" behavior's propertiesByType). There's no public API to read that at
		// runtime, so it's hardcoded here as the one known wide container worth accounting for.
		static readonly Dictionary<string, Vec3i> TrunkSecondCellOffset = new Dictionary<string, Vec3i>
		{
			{ "north", new Vec3i(1, 0, 0) },
			{ "south", new Vec3i(-1, 0, 0) },
			{ "east", new Vec3i(0, 0, 1) },
			{ "west", new Vec3i(0, 0, -1) },
		};

		BlockPos FindChestPosition(IServerPlayer player, Block containerBlock)
		{
			BlockPos feet = player.Entity.Pos.AsBlockPos.Copy();
			BlockFacing facing = BlockFacing.HorizontalFromYaw(player.Entity.Pos.Yaw);

			BlockPos[] candidates =
			{
				feet.AddCopy(facing, 1),
				feet.AddCopy(facing, 1).Up(1),
				feet.AddCopy(facing, 2),
				feet.AddCopy(facing.Opposite, 1),
				feet.AddCopy(1, 0, 0),
				feet.AddCopy(-1, 0, 0),
				feet.AddCopy(0, 0, 1),
				feet.AddCopy(0, 0, -1),
			};

			foreach (BlockPos candidate in candidates)
			{
				if (IsSuitable(candidate, containerBlock)) return candidate;
			}

			// Nothing ideal nearby - just place it in front of the player, like a direct block placement would.
			return candidates[0];
		}

		bool IsSuitable(BlockPos pos, Block containerBlock)
		{
			if (!HasGroundSupport(pos)) return false;

			if (containerBlock.Class == "BlockGenericTypedContainerTrunk"
				&& containerBlock.Variant.TryGetValue("side", out string side)
				&& TrunkSecondCellOffset.TryGetValue(side, out Vec3i offset))
			{
				if (!HasGroundSupport(pos.AddCopy(offset.X, offset.Y, offset.Z))) return false;
			}

			return true;
		}

		bool HasGroundSupport(BlockPos pos)
		{
			IBlockAccessor accessor = sapi.World.BlockAccessor;
			Block here = accessor.GetBlock(pos);
			Block below = accessor.GetBlock(pos.DownCopy(1));

			// Replaceable (grass, shrubs, snow layers, ...) is fine to overwrite at the chest's own
			// position, but for the block below we need an actual solid top face - a shrub, for
			// example, is non-replaceable but still has no solid top, so it must not count as
			// "ground" or the chest ends up floating on top of it.
			return here.Replaceable >= 6000 && below.SideSolid.OnSide(BlockFacing.UP);
		}
	}
}
