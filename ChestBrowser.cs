using System;
using System.Collections.Generic;
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

        /// <summary>Accumulated delivery fee for the current session (charged on close).</summary>
        private static int _runningTotal;

        /// <summary>Whether the current session charges delivery fees.</summary>
        private static bool _chargeFees;

        /// <summary>The HUD message showing the running total (kept alive while browsing).</summary>
        private static HUDMessage _costMessage;

        internal static IMonitor Monitor;

        public static bool IsActive => _isActive;
        public static bool ChargeFees => _chargeFees;

        /// <summary>Apply Harmony patches for delivery fee interception.</summary>
        public static void Apply(Harmony harmony)
        {
            harmony.Patch(
                original: AccessTools.Method(typeof(Chest), nameof(Chest.grabItemFromChest)),
                prefix: new HarmonyMethod(typeof(ChestBrowser), nameof(GrabFromChest_Prefix))
            );
            harmony.Patch(
                original: AccessTools.Method(typeof(Chest), nameof(Chest.grabItemFromInventory)),
                prefix: new HarmonyMethod(typeof(ChestBrowser), nameof(GrabFromInventory_Prefix))
            );
        }

        /// <summary>Intercept chest→player transfers to accumulate delivery fees.</summary>
        private static bool GrabFromChest_Prefix(Item item, Farmer who)
        {
            if (!_isActive || !_chargeFees) return true;
            return TryAccumulateFee(item, who);
        }

        /// <summary>Intercept player→chest transfers to accumulate delivery fees.</summary>
        private static bool GrabFromInventory_Prefix(Item item, Farmer who)
        {
            if (!_isActive || !_chargeFees) return true;
            return TryAccumulateFee(item, who);
        }

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
            _runningTotal = 0;
            _costMessage = null;
            _chargeFees = ModEntry.Config.EnableProgressionGating
                && DeliveryState.Provider == DeliveryProvider.Shane;
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

        /// <summary>Called when the ItemGrabMenu closes to reset state.</summary>
        public static void OnMenuClosed()
        {
            // Charge accumulated delivery fees
            if (_runningTotal > 0)
            {
                Game1.player.Money -= _runningTotal;
                Game1.addHUDMessage(new HUDMessage($"Shane's fee: {_runningTotal}g") { noIcon = true });
                Monitor?.Log($"Charged delivery fee: {_runningTotal}g", LogLevel.Trace);
            }

            // Remove the running total HUD message
            if (_costMessage != null)
            {
                Game1.hudMessages.Remove(_costMessage);
                _costMessage = null;
            }

            _runningTotal = 0;
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
            var createSlots = HarmonyLib.AccessTools.Method(typeof(StardewValley.Objects.Chest), "createSlotsForCapacity");
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

            // Create ItemGrabMenu directly — don't use chest.ShowMenu() which goes through
            // the lid animation system and crashes for remote (non-current-location) chests.
            // Delivery fees are handled by Harmony prefixes on Chest.grabItemFromChest/grabItemFromInventory
            // so they persist even when the menu is recreated after each transfer.
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

        /// <summary>Calculate delivery fee: 1% of sell value * stack size. Free for unsellable items.</summary>
        private static int CalculateDeliveryCost(Item item)
        {
            int sellValue = item.sellToStorePrice();
            if (sellValue <= 0) return 0;
            return Math.Max(1, (int)(sellValue * 0.01 * item.Stack));
        }

        /// <summary>Add delivery fee to running total. Returns true if transfer should proceed, false to block.</summary>
        private static bool TryAccumulateFee(Item item, Farmer who)
        {
            int cost = CalculateDeliveryCost(item);
            if (cost <= 0) return true; // free transfer

            int availableGold = who.Money - _runningTotal;
            if (availableGold < cost)
            {
                Game1.addHUDMessage(new HUDMessage($"Not enough gold! This costs {cost}g.") { noIcon = true });
                Game1.playSound("cancel");
                return false;
            }

            _runningTotal += cost;
            UpdateCostHUD();
            return true;
        }

        /// <summary>Show or update the persistent HUD message with the running delivery total.</summary>
        private static void UpdateCostHUD()
        {
            // Remove old message
            if (_costMessage != null)
                Game1.hudMessages.Remove(_costMessage);

            _costMessage = new HUDMessage($"Delivery total: {_runningTotal}g") { noIcon = true, timeLeft = 99999f };
            Game1.addHUDMessage(_costMessage);
        }
    }
}
