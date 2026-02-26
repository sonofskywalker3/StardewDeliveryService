using System.Collections.Generic;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;
using StardewDeliveryService.Patches;

namespace StardewDeliveryService
{
    /// <summary>Manages remote chest browsing state — opening chests, cycling with LB/RB or keyboard.</summary>
    internal static class ChestBrowser
    {
        private static List<ChestInfo> _chests;
        private static int _currentIndex;
        private static bool _isActive;

        internal static IMonitor Monitor;

        public static bool IsActive => _isActive;

        /// <summary>Open the chest browser starting at the first chest.</summary>
        public static void Open()
        {
            _chests = ChestScanner.GetAllChests();
            if (_chests.Count == 0)
            {
                Game1.addHUDMessage(new HUDMessage("No chests found") { noIcon = true });
                return;
            }

            _currentIndex = 0;
            _isActive = true;
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
            // the lid animation system and crashes for remote (non-current-location) chests
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
    }
}
