using System;
using System.Collections.Generic;
using System.Linq;
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
		}

		void LoadConfig()
		{
			string configDir = sapi.GetOrCreateDataPath("ModConfig");
			string configPath = System.IO.Path.Combine(configDir, ConfigFilename);

			if (!System.IO.File.Exists(configPath))
			{
				// Seed the on-disk file with the packaged asset's raw bytes (not a re-serialized
				// object) so the human-readable comments/examples in it survive onto disk. This file
				// is never touched again afterwards, so any edits/comments the user makes stick.
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

			List<ItemStack> stacks = BuildLootStacks();
			if (stacks.Count == 0)
			{
				return;
			}

			GiveStarterChest(byPlayer, stacks);
		}

		List<ItemStack> BuildLootStacks()
		{
			var result = new List<ItemStack>();

			foreach (LootEntry entry in config.FixedItems)
			{
				ItemStack stack = ResolveStack(entry);
				if (stack != null) result.Add(stack);
			}

			if (config.RandomMode)
			{
				var pool = config.RandomPool.Where(e => ResolveCollectible(e) != null).ToList();
				var remaining = new List<LootEntry>(pool);

				int picks = config.RandomPickCount;
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

					if (!config.AllowDuplicatePicks) remaining.Remove(chosen);
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

		const string DefaultContainerCode = "game:chest-north";

		Block ResolveContainerBlock()
		{
			Block block = sapi.World.BlockAccessor.GetBlock(new AssetLocation(config.ContainerCode));
			if (block != null && block.Id != 0 && !string.IsNullOrEmpty(block.EntityClass))
			{
				return block;
			}

			sapi.Logger.Error("[StarterChest] Configured ContainerCode '{0}' is not a valid container block - falling back to the default chest ('{1}').", config.ContainerCode, DefaultContainerCode);
			return sapi.World.BlockAccessor.GetBlock(new AssetLocation(DefaultContainerCode));
		}

		void GiveStarterChest(IServerPlayer player, List<ItemStack> stacks)
		{
			Block containerBlock = ResolveContainerBlock();
			if (containerBlock == null)
			{
				sapi.Logger.Error("[StarterChest] Could not find the default chest block either - aborting starter chest placement.");
				return;
			}

			BlockPos pos = FindChestPosition(player);

			sapi.World.BlockAccessor.SetBlock(containerBlock.Id, pos);

			// Runs block-level placement behaviors (e.g. the trunk's Multiblock behavior, which
			// registers its second block position) - SetBlock alone only sets the voxel, it doesn't
			// invoke placement hooks the way a normal player placement would.
			containerBlock.OnBlockPlaced(sapi.World, pos, null);

			var be = sapi.World.BlockAccessor.GetBlockEntity<BlockEntityGenericTypedContainer>(pos);
			if (be == null)
			{
				sapi.Logger.Error("[StarterChest] Container block entity did not spawn at {0} - aborting.", pos);
				return;
			}

			be.OnBlockPlaced(null);
			FillInventory(be, stacks);
			be.MarkDirty(true, null);

			sapi.Logger.Notification("[StarterChest] Gave {0} a starter chest ({1}) at {2} ({3} item stack(s)).", player.PlayerName, containerBlock.Code, pos, stacks.Count);
			player.SendMessage(GlobalConstants.GeneralChatGroup, "A starter chest has appeared nearby!", EnumChatType.Notification, null);
		}

		void FillInventory(BlockEntityGenericTypedContainer be, List<ItemStack> stacks)
		{
			InventoryBase inv = be.Inventory;
			int slot = 0;

			foreach (ItemStack stack in stacks)
			{
				if (slot >= inv.Count)
				{
					sapi.Logger.Warning("[StarterChest] The starter chest only has {0} slots - {1} configured loot entrie(s) did not fit and were skipped. Reduce FixedItems/RandomPickCount to avoid this.", inv.Count, stacks.Count - slot);
					break;
				}

				inv[slot].Itemstack = stack;
				inv.MarkSlotDirty(slot);
				slot++;
			}
		}

		BlockPos FindChestPosition(IServerPlayer player)
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
				if (IsSuitable(candidate)) return candidate;
			}

			// Nothing ideal nearby - just place it in front of the player, like Minecraft's /setblock does.
			return candidates[0];
		}

		bool IsSuitable(BlockPos pos)
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
