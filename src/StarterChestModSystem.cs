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

		StarterChestLoadoutProvider loadoutProvider;
		StarterChestReadyCheck readyCheck;

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

		// Lets another mod supply a per-player loadout override instead of the top-level
		// FixedItems/RandomPool/RandomPickCount/AllowDuplicatePicks, e.g. to vary loot by
		// character class. Only one provider at a time; a later call replaces an earlier one.
		// readyCheck is optional - polled (every ~250ms, up to a ~15s timeout) before giving a new
		// player's automatic chest, so the provider can wait for something (e.g. character
		// creation finishing) before being asked to resolve a loadout. Call from an addon's
		// StartServerSide:
		//   sapi.ModLoader.GetModSystem<StarterChestModSystem>()?.RegisterLoadoutProvider(MyProvider, MyReadyCheck);
		public void RegisterLoadoutProvider(StarterChestLoadoutProvider provider, StarterChestReadyCheck readyCheck = null)
		{
			loadoutProvider = provider;
			this.readyCheck = readyCheck;
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

			StarterChestLoadout loadout = ResolveLoadout(target, out string displayName);

			int? maxSlots = EstimateSlotCount(containerBlock);
			List<ItemStack> stacks = BuildLootStacks(maxSlots ?? int.MaxValue, loadout);

			string loadoutDesc = string.IsNullOrEmpty(displayName) ? "default loadout" : $"'{displayName}' loadout";
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

		void LoadConfig()
		{
			string configDir = sapi.GetOrCreateDataPath("ModConfig");
			string configPath = System.IO.Path.Combine(configDir, ConfigFilename);

			if (!System.IO.File.Exists(configPath))
			{
				// Seed with the packaged asset's raw bytes, not a re-serialized object, so
				// formatting survives as-is. Never touched again after this.
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

			TryGiveWhenReady(byPlayer, 0);
		}

		const int ReadyPollMs = 250;
		const int ReadyTimeoutMs = 15000;

		// No registered readyCheck: give immediately on PlayerNowPlaying. With one, poll it first
		// (bounded by a timeout) so the provider can wait for something before resolving a loadout.
		void TryGiveWhenReady(IServerPlayer player, int elapsedMs)
		{
			// Mid-character-creation is legitimately "Connected", not yet "Playing" - only bail
			// out on a genuine disconnect.
			if (player.ConnectionState == EnumClientState.Offline)
			{
				return;
			}

			if (readyCheck == null || readyCheck(player) || elapsedMs >= ReadyTimeoutMs)
			{
				player.SetModData(ReceivedModDataKey, true);
				GiveStarterChest(player);
				return;
			}

			sapi.World.RegisterCallback(_ => TryGiveWhenReady(player, elapsedMs + ReadyPollMs), ReadyPollMs);
		}

		// Uses the registered loadout provider if present, else the top-level config. displayName
		// is whatever the provider supplied, or null for the top-level config.
		StarterChestLoadout ResolveLoadout(IServerPlayer player, out string displayName)
		{
			if (loadoutProvider != null)
			{
				StarterChestLoadoutResult result = loadoutProvider(player);
				if (result?.Loadout != null)
				{
					displayName = result.DisplayName;
					return result.Loadout;
				}
			}

			displayName = null;
			return new StarterChestLoadout
			{
				RandomMode = config.RandomMode,
				RandomPickCount = config.RandomPickCount,
				AllowDuplicatePicks = config.AllowDuplicatePicks,
				FixedItems = config.FixedItems,
				RandomPool = config.RandomPool,
			};
		}

		List<ItemStack> BuildLootStacks(int maxSlots, StarterChestLoadout loadout)
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

				// Auto-fit: cap picks to remaining room instead of rolling the full
				// RandomPickCount and dropping whatever doesn't fit afterwards.
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
			// Picked once per chest so a fixed ContainerOrientation applies to both the
			// configured code and the fallback, instead of re-rolling for each.
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

			// Not every container is direction-variant - fall back to the bare code as-is.
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

		// Best-effort slot count from the block's own "quantitySlots"/"defaultType" attributes.
		// Preview-only - a modded container that doesn't follow this convention just won't be
		// capacity-capped in the preview.
		static int? EstimateSlotCount(Block block)
		{
			string type = block.Attributes?["defaultType"]?.AsString(null);
			if (string.IsNullOrEmpty(type)) return null;

			int count = block.Attributes["quantitySlots"][type].AsInt(-1);
			return count > 0 ? (int?)count : null;
		}

		bool GiveStarterChest(IServerPlayer player)
		{
			StarterChestLoadout loadout = ResolveLoadout(player, out string displayName);

			// Skip placement entirely when nothing could ever be given - the real,
			// capacity-aware loot list is only known once the container is placed below.
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

			// Runs block-level placement behaviors (e.g. multiblock second-position setup) that
			// SetBlock alone skips.
			containerBlock.OnBlockPlaced(sapi.World, pos, null);

			var be = sapi.World.BlockAccessor.GetBlockEntity<BlockEntityGenericTypedContainer>(pos);
			if (be == null)
			{
				sapi.Logger.Error("[StarterChest] Container block entity did not spawn at {0} - aborting.", pos);
				return false;
			}

			be.OnBlockPlaced(null);

			// SetBlock bypasses DoPlaceBlock, which normally derives the entity's rendered
			// MeshAngle from player facing - set it explicitly from the placed variant.
			if (containerBlock.Variant.TryGetValue("side", out string side))
			{
				float? meshAngle = OrientationToMeshAngle(side);
				if (meshAngle.HasValue)
				{
					be.MeshAngle = meshAngle.Value;
				}
			}

			// Real, live slot count - makes RandomPickCount auto-fit work for any container.
			List<ItemStack> stacks = BuildLootStacks(be.Inventory.Count, loadout);
			if (stacks.Count == 0)
			{
				// Everything configured was unresolvable - remove the pointless empty container.
				sapi.World.BlockAccessor.SetBlock(0, pos);
				return false;
			}

			FillInventory(be, stacks);
			be.MarkDirty(true, null);

			sapi.Logger.Notification("[StarterChest] Gave {0} a starter chest ({1}) at {2} ({3} item stack(s)).", player.PlayerName, containerBlock.Code, pos, stacks.Count);
			player.SendMessage(GlobalConstants.GeneralChatGroup, BuildAppearedMessage(player, containerBlock, pos, displayName), EnumChatType.Notification, null);
			return true;
		}

		// Tries Lang.GetL first, falling back to a hardcoded English string if the key didn't
		// resolve (GetL returns the raw key on a miss) - mod-added lang entries aren't always
		// guaranteed to be loaded server-side.
		string BuildAppearedMessage(IServerPlayer player, Block containerBlock, BlockPos pos, string displayName)
		{
			// Actual block display name, lowercased for mid-sentence reading, so the message
			// matches the configured ContainerCode instead of assuming "chest".
			string containerName = containerBlock.GetPlacedBlockName(sapi.World, pos).ToLowerInvariant();

			if (!string.IsNullOrEmpty(displayName))
			{
				string message = ResolveWithFallback(player.LanguageCode, "starterchest:chest-appeared-class", null, displayName, containerName);
				return message ?? $"A starter {displayName} {containerName} has appeared nearby!";
			}

			return ResolveWithFallback(player.LanguageCode, "starterchest:chest-appeared", null, containerName)
				?? $"A starter {containerName} has appeared nearby!";
		}

		// Returns null (or fallback) instead of the raw key when a translation is missing.
		static string ResolveWithFallback(string langCode, string key, string fallback, params object[] args)
		{
			string resolved = Lang.GetL(langCode, key, args);
			return resolved == key ? fallback : resolved;
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

		// The trunk's second cell is a virtual, collision-mirrored cell, not a second SetBlock -
		// no public API exposes it, so hardcode the one known wide container worth checking.
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

			// Nothing ideal nearby - place in front of the player, like a direct placement would.
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

			// Replaceable at the container's own cell is fine; the cell below needs an actual
			// solid top face, or the container ends up floating.
			return here.Replaceable >= 6000 && below.SideSolid.OnSide(BlockFacing.UP);
		}
	}
}
