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
			try
			{
				config = sapi.LoadModConfig<StarterChestConfig>(ConfigFilename);
			}
			catch (Exception e)
			{
				sapi.Logger.Error("[StarterChest] Failed to parse {0}, falling back to defaults: {1}", ConfigFilename, e.Message);
				config = null;
			}

			if (config == null)
			{
				config = LoadPackagedDefaultConfig();
			}

			// Re-store so the file on disk always reflects the full, current schema (fills in any
			// fields missing from an older/hand-edited config with their code defaults).
			sapi.StoreModConfig(config, ConfigFilename);
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

		void GiveStarterChest(IServerPlayer player, List<ItemStack> stacks)
		{
			Block chestBlock = sapi.World.BlockAccessor.GetBlock(new AssetLocation("game:chest-north"));
			if (chestBlock == null)
			{
				sapi.Logger.Error("[StarterChest] Could not find the vanilla 'game:chest-north' block - aborting starter chest placement.");
				return;
			}

			BlockPos pos = FindChestPosition(player);

			sapi.World.BlockAccessor.SetBlock(chestBlock.Id, pos);

			var be = sapi.World.BlockAccessor.GetBlockEntity<BlockEntityGenericTypedContainer>(pos);
			if (be == null)
			{
				sapi.Logger.Error("[StarterChest] Chest block entity did not spawn at {0} - aborting.", pos);
				return;
			}

			be.OnBlockPlaced(null);
			FillInventory(be, stacks);
			be.MarkDirty(true, null);

			sapi.Logger.Notification("[StarterChest] Gave {0} a starter chest at {1} ({2} item stack(s)).", player.PlayerName, pos, stacks.Count);
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

			return here.Replaceable >= 6000 && below.Replaceable < 6000;
		}
	}
}
