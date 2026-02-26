using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;
using StardewValley.Objects;
using StardewDeliveryService.Patches;

namespace StardewDeliveryService
{
    /// <summary>Manages remote chest browsing state — opening chests, cycling with LB/RB or keyboard.</summary>
    internal static class ChestBrowser
    {
        private static List<ChestInfo> _chests;
        private static int _currentIndex;
        private static bool _isActive;

        /// <summary>Whether the current session charges delivery fees.</summary>
        private static bool _chargeFees;

        /// <summary>The HUD message showing the running total (kept alive while browsing).</summary>
        private static HUDMessage _costMessage;

        /// <summary>Snapshot of player inventory at session open. Key: "QualifiedItemId:Quality", Value: (count, unitSellPrice).</summary>
        private static Dictionary<string, (int count, int unitPrice)> _inventorySnapshot = new();

        /// <summary>Last calculated pending cost (cached to avoid HUD spam).</summary>
        private static int _lastDisplayedCost;

        internal static IMonitor Monitor;

        public static bool IsActive => _isActive;
        public static bool ChargeFees => _chargeFees;

        /// <summary>Open the chest browser starting at the first chest.</summary>
        public static void Open()
        {
            // Gate: require progression unlock when gating is enabled
            if (ModEntry.Config.EnableProgressionGating && !DeliveryState.IsUnlocked)
            {
                Game1.addHUDMessage(new HUDMessage("Complete a Community Center bundle to unlock deliveries.") { noIcon = true });
                return;
            }

            _chests = ChestScanner.GetAllChests();
            if (_chests.Count == 0)
            {
                Game1.addHUDMessage(new HUDMessage("No chests found") { noIcon = true });
                return;
            }

            _currentIndex = 0;
            _isActive = true;
            _costMessage = null;
            _lastDisplayedCost = 0;
            _chargeFees = ModEntry.Config.EnableProgressionGating
                && DeliveryState.Provider == DeliveryProvider.Shane;

            // Snapshot inventory before any transfers
            _inventorySnapshot = SnapshotInventory();

            OpenCurrentChest();
        }

        /// <summary>Cycle to the next chest (RT / Tab).</summary>
        /// <param name="snapToComponentID">Component ID to snap cursor to after opening. -1 = no snap.</param>
        public static void CycleNext(int snapToComponentID = -1)
        {
            if (_chests == null || _chests.Count == 0) return;
            _currentIndex = (_currentIndex + 1) % _chests.Count;
            OpenCurrentChest(snapToComponentID);
        }

        /// <summary>Cycle to the previous chest (LT / Ctrl+Tab).</summary>
        /// <param name="snapToComponentID">Component ID to snap cursor to after opening. -1 = no snap.</param>
        public static void CyclePrev(int snapToComponentID = -1)
        {
            if (_chests == null || _chests.Count == 0) return;
            _currentIndex = (_currentIndex - 1 + _chests.Count) % _chests.Count;
            OpenCurrentChest(snapToComponentID);
        }

        /// <summary>Called every tick while browser is active to update the cost HUD.</summary>
        public static void OnUpdateTicked()
        {
            if (!_isActive || !_chargeFees) return;

            int cost = CalculateDiffCost();
            if (cost != _lastDisplayedCost)
            {
                _lastDisplayedCost = cost;
                UpdateCostHUD(cost);
            }
        }

        /// <summary>Called when the ItemGrabMenu closes to reset state.</summary>
        public static void OnMenuClosed()
        {
            if (_chargeFees)
            {
                int totalCost = CalculateDiffCost();
                if (totalCost > 0)
                {
                    int charge = Math.Min(totalCost, Game1.player.Money);
                    Game1.player.Money -= charge;
                    Game1.addHUDMessage(new HUDMessage($"Shane's fee: {charge}g") { noIcon = true });
                    Monitor?.Log($"Charged delivery fee: {charge}g (calculated: {totalCost}g)", LogLevel.Trace);
                }
            }

            // Remove the running total HUD message
            if (_costMessage != null)
            {
                Game1.hudMessages.Remove(_costMessage);
                _costMessage = null;
            }

            _lastDisplayedCost = 0;
            _inventorySnapshot.Clear();
            _isActive = false;
            _chests = null;
            ItemGrabMenuPatches.ClearArrowButtons();
        }

        private static void OpenCurrentChest(int snapToComponentID = -1)
        {
            var info = _chests[_currentIndex];
            var chest = info.Chest;
            string label = $"{info.Label} ({info.LocationName}) [{_currentIndex + 1}/{_chests.Count}]";

            // Ensure inventory slots exist — remote chests that were never opened locally
            // may have 0 slots (createSlotsForCapacity only runs in the constructor).
            // Use reflection since the method isn't in the PC reference DLL.
            var createSlots = HarmonyLib.AccessTools.Method(typeof(Chest), "createSlotsForCapacity");
            if (createSlots != null)
                createSlots.Invoke(chest, new object[] { false });
            else
            {
                // PC fallback: manually pad to capacity
                var items = chest.GetItemsForPlayer();
                int capacity = chest.GetActualCapacity();
                while (items.Count < capacity)
                    items.Add(null);
            }

            Monitor?.Log($"Opening chest: {info.Label} ({info.LocationName}) [{_currentIndex + 1}/{_chests.Count}] items={chest.GetItemsForPlayer().Count}", LogLevel.Trace);

            var menu = new ItemGrabMenu(
                chest.GetItemsForPlayer(),
                reverseGrab: false,
                showReceivingMenu: true,
                InventoryMenu.highlightAllItems,
                chest.grabItemFromInventory,
                null,
                chest.grabItemFromChest,
                snapToBottom: false,
                canBeExitedWithKey: true,
                playRightClickSound: true,
                allowRightClick: true,
                showOrganizeButton: true,
                source: 1,
                context: chest
            );
            Game1.activeClickableMenu = menu;

            // Set up < > arrow buttons and label for mouse/touch cycling
            ItemGrabMenuPatches.SetupArrowButtons(menu, label);

            // Snap cursor to the requested component (e.g. same slot the user was on)
            if (snapToComponentID >= 0)
            {
                var comp = menu.getComponentWithID(snapToComponentID);
                if (comp != null)
                {
                    menu.currentlySnappedComponent = comp;
                    menu.snapCursorToCurrentSnappedComponent();
                }
            }
        }

        /// <summary>Build a map of the player's current inventory: key → (totalCount, unitSellPrice).</summary>
        private static Dictionary<string, (int count, int unitPrice)> SnapshotInventory()
        {
            var map = new Dictionary<string, (int count, int unitPrice)>();
            foreach (var item in Game1.player.Items)
            {
                if (item == null) continue;
                string key = $"{item.QualifiedItemId}:{item.Quality}";
                int price = item.sellToStorePrice();
                map.TryGetValue(key, out var existing);
                map[key] = (existing.count + item.Stack, price > 0 ? price : existing.unitPrice);
            }
            return map;
        }

        /// <summary>Calculate delivery cost as 3% of the absolute net value change since session open.</summary>
        private static int CalculateDiffCost()
        {
            var current = SnapshotInventory();
            var allKeys = new HashSet<string>(_inventorySnapshot.Keys);
            allKeys.UnionWith(current.Keys);

            int totalCost = 0;
            foreach (string key in allKeys)
            {
                _inventorySnapshot.TryGetValue(key, out var was);
                current.TryGetValue(key, out var now);

                int diff = Math.Abs(now.count - was.count);
                if (diff <= 0) continue;

                // Use whichever unit price we have (current or snapshot)
                int unitPrice = now.unitPrice > 0 ? now.unitPrice : was.unitPrice;
                if (unitPrice <= 0) continue; // unsellable items are free

                totalCost += Math.Max(1, (int)(unitPrice * 0.03)) * diff;
            }
            return totalCost;
        }

        /// <summary>Show or update the persistent HUD message with the delivery total.</summary>
        private static void UpdateCostHUD(int cost)
        {
            if (_costMessage != null)
                Game1.hudMessages.Remove(_costMessage);

            if (cost <= 0)
            {
                _costMessage = null;
                return;
            }

            _costMessage = new HUDMessage($"Delivery total: {cost}g") { noIcon = true, timeLeft = 99999f };
            Game1.addHUDMessage(_costMessage);
        }
    }
}
