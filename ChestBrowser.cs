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

        /// <summary>Whether the user cancelled via the X button (skip charging, restore items).</summary>
        private static bool _cancelled;

        /// <summary>The HUD message showing the running total (kept alive while browsing).</summary>
        private static HUDMessage _costMessage;

        /// <summary>Snapshot of player inventory at session open. Key: "QualifiedItemId:Quality", Value: (count, unitSellPrice).</summary>
        private static Dictionary<string, (int count, int unitPrice)> _inventorySnapshot = new();

        /// <summary>Deep copy of player inventory at session open (for cancel/restore).</summary>
        private static List<Item> _playerItemSnapshot;

        /// <summary>Deep copy of each chest's contents when first opened (for cancel/restore).</summary>
        private static Dictionary<Chest, List<Item>> _chestItemSnapshots = new();

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
            _cancelled = false;
            _costMessage = null;
            _lastDisplayedCost = 0;
            _chargeFees = ModEntry.Config.EnableProgressionGating
                && DeliveryState.Provider == DeliveryProvider.Shane;

            // Snapshot inventory before any transfers
            try
            {
                _inventorySnapshot = SnapshotInventory();
                Monitor?.Log("[DIAG] SnapshotInventory OK in Open()", LogLevel.Trace);
            }
            catch (Exception ex)
            {
                Monitor?.Log($"CRASH in Open/SnapshotInventory: {ex}", LogLevel.Error);
                _inventorySnapshot = new();
            }

            try
            {
                _playerItemSnapshot = DeepCopyItems(Game1.player.Items);
                Monitor?.Log("[DIAG] DeepCopyItems OK in Open()", LogLevel.Trace);
            }
            catch (Exception ex)
            {
                Monitor?.Log($"CRASH in Open/DeepCopyItems: {ex}", LogLevel.Error);
                _playerItemSnapshot = null;
            }
            _chestItemSnapshots.Clear();

            _diagRanOnce = false;
            Monitor?.Log($"[DIAG] Open(): chargeFees={_chargeFees}, provider={DeliveryState.Provider}, chests={_chests.Count}", LogLevel.Trace);
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

        /// <summary>Whether the first-tick diagnostic has fired.</summary>
        private static bool _diagRanOnce;

        /// <summary>Called every tick while browser is active to update the cost HUD.</summary>
        public static void OnUpdateTicked()
        {
            if (!_isActive || !_chargeFees) return;

            // DIAGNOSTIC v0.6.34: Run CalculateDiffCost ONCE to see if a single call crashes.
            if (_diagRanOnce) return;
            _diagRanOnce = true;

            try
            {
                Monitor?.Log("[DIAG] About to call CalculateDiffCost (one-shot)...", LogLevel.Trace);
                int cost = CalculateDiffCost(diag: true);
                Monitor?.Log($"[DIAG] CalculateDiffCost returned {cost} (one-shot OK)", LogLevel.Trace);
            }
            catch (Exception ex)
            {
                Monitor?.Log($"CRASH in one-shot CalculateDiffCost: {ex}", LogLevel.Error);
                _chargeFees = false;
            }
        }

        /// <summary>Called when the ItemGrabMenu closes to reset state.</summary>
        public static void OnMenuClosed()
        {
            // DIAGNOSTIC v0.6.33: BYPASS cost charging entirely
            if (_chargeFees)
                Monitor?.Log("[DIAG] OnMenuClosed: would charge fees (bypassed for crash test)", LogLevel.Trace);

            // Remove the running total HUD message
            if (_costMessage != null)
            {
                Game1.hudMessages.Remove(_costMessage);
                _costMessage = null;
            }

            _lastDisplayedCost = 0;
            _cancelled = false;
            _inventorySnapshot.Clear();
            _playerItemSnapshot = null;
            _chestItemSnapshots.Clear();
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

            // Snapshot chest contents on first open (for cancel/restore)
            if (!_chestItemSnapshots.ContainsKey(chest))
                _chestItemSnapshots[chest] = DeepCopyItems(chest.GetItemsForPlayer());

            Monitor?.Log($"Opening chest: {info.Label} ({info.LocationName}) [{_currentIndex + 1}/{_chests.Count}] items={chest.GetItemsForPlayer().Count}", LogLevel.Trace);

            ItemGrabMenu menu;
            try
            {
                menu = new ItemGrabMenu(
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
                Monitor?.Log("ItemGrabMenu created OK", LogLevel.Trace);
            }
            catch (Exception ex)
            {
                Monitor?.Log($"CRASH in ItemGrabMenu constructor: {ex}", LogLevel.Error);
                _isActive = false;
                return;
            }

            Game1.activeClickableMenu = menu;

            try
            {
                // Set up < > arrow buttons and label for mouse/touch cycling
                ItemGrabMenuPatches.SetupArrowButtons(menu, label);
                Monitor?.Log("SetupArrowButtons OK", LogLevel.Trace);
            }
            catch (Exception ex)
            {
                Monitor?.Log($"CRASH in SetupArrowButtons: {ex}", LogLevel.Error);
            }

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

        /// <summary>Cancel all item movement and restore everything to pre-session state.</summary>
        public static void CancelAndRestore()
        {
            _cancelled = true;

            // Restore player inventory
            if (_playerItemSnapshot != null)
            {
                var inv = Game1.player.Items;
                for (int i = 0; i < inv.Count && i < _playerItemSnapshot.Count; i++)
                    inv[i] = _playerItemSnapshot[i];
            }

            // Clear held/cursor item (it came from somewhere we're restoring)
            Game1.player.CursorSlotItem = null;
            if (Game1.activeClickableMenu is ItemGrabMenu menu)
                menu.heldItem = null;

            // Restore all touched chests
            foreach (var kvp in _chestItemSnapshots)
            {
                var items = kvp.Key.GetItemsForPlayer();
                var snapshot = kvp.Value;
                for (int i = 0; i < items.Count && i < snapshot.Count; i++)
                    items[i] = snapshot[i];
            }

            Game1.addHUDMessage(new HUDMessage("Delivery cancelled — items restored.") { noIcon = true });
            Monitor?.Log("Cancel: restored all items to pre-session state", LogLevel.Trace);
        }

        /// <summary>Deep copy a list of items (for cancel/restore snapshots).</summary>
        private static List<Item> DeepCopyItems(IList<Item> items)
        {
            var copy = new List<Item>(items.Count);
            foreach (var item in items)
            {
                if (item == null)
                {
                    copy.Add(null);
                }
                else
                {
                    var clone = item.getOne();
                    clone.Stack = item.Stack;
                    copy.Add(clone);
                }
            }
            return copy;
        }

        /// <summary>Build a map of the player's current inventory (including cursor/held item): key → (totalCount, unitSellPrice).</summary>
        private static Dictionary<string, (int count, int unitPrice)> SnapshotInventory(bool diag = false)
        {
            if (diag) Monitor?.Log("[DIAG-SI] entering SnapshotInventory", LogLevel.Trace);
            var map = new Dictionary<string, (int count, int unitPrice)>();
            if (diag) Monitor?.Log($"[DIAG-SI] player.Items.Count={Game1.player.Items.Count}", LogLevel.Trace);
            foreach (var item in Game1.player.Items)
            {
                if (item == null) continue;
                string key = $"{item.QualifiedItemId}:{item.Quality}";
                int price = item.sellToStorePrice();
                map.TryGetValue(key, out var existing);
                map[key] = (existing.count + item.Stack, price > 0 ? price : existing.unitPrice);
            }
            if (diag) Monitor?.Log($"[DIAG-SI] inventory loop done, {map.Count} keys", LogLevel.Trace);

            // Include held/cursor item
            if (diag) Monitor?.Log("[DIAG-SI] checking CursorSlotItem...", LogLevel.Trace);
            Item heldItem = Game1.player.CursorSlotItem;
            if (diag) Monitor?.Log($"[DIAG-SI] CursorSlotItem={(heldItem != null ? heldItem.DisplayName : "null")}", LogLevel.Trace);

            if (heldItem == null)
            {
                if (diag) Monitor?.Log("[DIAG-SI] checking activeClickableMenu for heldItem...", LogLevel.Trace);
                if (Game1.activeClickableMenu is ItemGrabMenu igm)
                {
                    if (diag) Monitor?.Log("[DIAG-SI] menu IS ItemGrabMenu, accessing heldItem...", LogLevel.Trace);
                    heldItem = igm.heldItem;
                    if (diag) Monitor?.Log($"[DIAG-SI] igm.heldItem={(heldItem != null ? heldItem.DisplayName : "null")}", LogLevel.Trace);
                }
            }

            if (heldItem != null)
            {
                if (diag) Monitor?.Log("[DIAG-SI] processing heldItem...", LogLevel.Trace);
                string key = $"{heldItem.QualifiedItemId}:{heldItem.Quality}";
                int price = heldItem.sellToStorePrice();
                map.TryGetValue(key, out var existing);
                map[key] = (existing.count + heldItem.Stack, price > 0 ? price : existing.unitPrice);
            }

            if (diag) Monitor?.Log($"[DIAG-SI] returning, {map.Count} keys total", LogLevel.Trace);
            return map;
        }

        /// <summary>Calculate delivery cost as 3% of the absolute net value change since session open.</summary>
        private static int CalculateDiffCost(bool diag = false)
        {
            if (diag) Monitor?.Log("[DIAG-CDC] calling SnapshotInventory...", LogLevel.Trace);
            var current = SnapshotInventory(diag);
            if (diag) Monitor?.Log("[DIAG-CDC] SnapshotInventory done, building HashSet...", LogLevel.Trace);
            var allKeys = new HashSet<string>(_inventorySnapshot.Keys);
            allKeys.UnionWith(current.Keys);
            if (diag) Monitor?.Log($"[DIAG-CDC] {allKeys.Count} keys, computing diff...", LogLevel.Trace);

            int totalCost = 0;
            foreach (string key in allKeys)
            {
                _inventorySnapshot.TryGetValue(key, out var was);
                current.TryGetValue(key, out var now);

                int diff = Math.Abs(now.count - was.count);
                if (diff <= 0) continue;

                int unitPrice = now.unitPrice > 0 ? now.unitPrice : was.unitPrice;
                if (unitPrice <= 0) continue;

                totalCost += Math.Max(1, (int)(unitPrice * diff * 0.03));
            }
            if (diag) Monitor?.Log($"[DIAG-CDC] done, totalCost={totalCost}", LogLevel.Trace);
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
