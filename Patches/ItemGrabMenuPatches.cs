using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using HarmonyLib;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;

namespace StardewDeliveryService.Patches
{
    /// <summary>Patches ItemGrabMenu for chest cycling — LT/RT, Tab, and arrow buttons.</summary>
    internal static class ItemGrabMenuPatches
    {
        private static ModConfig Config;
        private static IMonitor Monitor;

        private const int PrevButtonID = 899;
        private const int NextButtonID = 900;

        private static ClickableTextureComponent _prevButton;
        private static ClickableTextureComponent _nextButton;
        private static string _chestLabel;

        internal static void Init(ModConfig config, IMonitor monitor)
        {
            Config = config;
            Monitor = monitor;
        }

        internal static void Apply(Harmony harmony)
        {
            harmony.Patch(
                original: AccessTools.Method(typeof(ItemGrabMenu), nameof(ItemGrabMenu.receiveGamePadButton)),
                prefix: new HarmonyMethod(typeof(ItemGrabMenuPatches), nameof(ReceiveGamePadButton_Prefix))
            );

            harmony.Patch(
                original: AccessTools.Method(typeof(ItemGrabMenu), nameof(ItemGrabMenu.receiveKeyPress)),
                prefix: new HarmonyMethod(typeof(ItemGrabMenuPatches), nameof(ReceiveKeyPress_Prefix))
            );

            harmony.Patch(
                original: AccessTools.Method(typeof(ItemGrabMenu), nameof(ItemGrabMenu.receiveLeftClick)),
                prefix: new HarmonyMethod(typeof(ItemGrabMenuPatches), nameof(ReceiveLeftClick_Prefix))
            );

            harmony.Patch(
                original: AccessTools.Method(typeof(ItemGrabMenu), nameof(ItemGrabMenu.draw), new[] { typeof(SpriteBatch) }),
                prefix: new HarmonyMethod(typeof(ItemGrabMenuPatches), nameof(Draw_Prefix)),
                postfix: new HarmonyMethod(typeof(ItemGrabMenuPatches), nameof(Draw_Postfix))
            );

            harmony.Patch(
                original: AccessTools.Method(typeof(ItemGrabMenu), nameof(ItemGrabMenu.performHoverAction)),
                postfix: new HarmonyMethod(typeof(ItemGrabMenuPatches), nameof(PerformHoverAction_Postfix))
            );

            // Draw arrows during InventoryMenu.draw — this places them in the z-order
            // BEFORE ItemGrabMenu draws tooltips, so arrows appear behind tooltips naturally.
            harmony.Patch(
                original: AccessTools.Method(typeof(InventoryMenu), nameof(InventoryMenu.draw), new[] { typeof(SpriteBatch) }),
                postfix: new HarmonyMethod(typeof(ItemGrabMenuPatches), nameof(InventoryMenu_Draw_Postfix))
            );
        }

        /// <summary>Create arrow buttons and label when the chest browser opens an ItemGrabMenu.</summary>
        internal static void SetupArrowButtons(ItemGrabMenu menu, string label)
        {
            _chestLabel = label;

            bool isAndroid = Constants.TargetPlatform == GamePlatform.Android;

            if (isAndroid)
                SetupAndroidArrows(menu);
            else
                SetupPCArrows(menu);

            InjectArrowButtons(menu);
        }

        private static void SetupPCArrows(ItemGrabMenu menu)
        {
            // Flanking above the menu on PC
            int arrowY = menu.yPositionOnScreen - 56;

            _prevButton = new ClickableTextureComponent(
                new Rectangle(menu.xPositionOnScreen - 80, arrowY, 64, 64),
                Game1.mouseCursors,
                new Rectangle(352, 495, 12, 11),
                4f
            )
            {
                myID = PrevButtonID,
                rightNeighborID = NextButtonID,
                leftNeighborID = -7777,
                upNeighborID = -7777,
                downNeighborID = -99998,
                hoverText = "Previous chest"
            };

            _nextButton = new ClickableTextureComponent(
                new Rectangle(menu.xPositionOnScreen + menu.width + 16, arrowY, 64, 64),
                Game1.mouseCursors,
                new Rectangle(365, 495, 12, 11),
                4f
            )
            {
                myID = NextButtonID,
                leftNeighborID = PrevButtonID,
                rightNeighborID = -7777,
                upNeighborID = -7777,
                downNeighborID = -99998,
                hoverText = "Next chest"
            };
        }

        private static void SetupAndroidArrows(ItemGrabMenu menu)
        {
            // Sidebar: below fillStacksButton (the "add to stacks" button)
            var anchor = menu.fillStacksButton;
            if (anchor == null)
            {
                // Fallback: try organizeButton on ItemsToGrabMenu via reflection
                var field = AccessTools.Field(typeof(InventoryMenu), "organizeButton");
                if (field != null && menu.ItemsToGrabMenu != null)
                    anchor = field.GetValue(menu.ItemsToGrabMenu) as ClickableTextureComponent;
            }
            if (anchor == null)
            {
                Monitor?.Log("No anchor button found for Android arrow placement", LogLevel.Warn);
                return;
            }

            int arrowY = anchor.bounds.Y + anchor.bounds.Height + 46;
            int startX = anchor.bounds.X + (anchor.bounds.Width - 64) / 2 - 37;

            _prevButton = new ClickableTextureComponent(
                new Rectangle(startX, arrowY, 64, 64),
                Game1.mouseCursors,
                new Rectangle(352, 495, 12, 11),
                4f
            )
            {
                myID = PrevButtonID,
                rightNeighborID = NextButtonID,
                leftNeighborID = -7777,
                upNeighborID = anchor.myID,
                downNeighborID = -99998,
                hoverText = "Previous chest"
            };

            _nextButton = new ClickableTextureComponent(
                new Rectangle(startX + 64 + 23, arrowY, 64, 64),
                Game1.mouseCursors,
                new Rectangle(365, 495, 12, 11),
                4f
            )
            {
                myID = NextButtonID,
                leftNeighborID = PrevButtonID,
                rightNeighborID = -7777,
                upNeighborID = anchor.myID,
                downNeighborID = -99998,
                hoverText = "Next chest"
            };
        }

        /// <summary>Re-inject arrow buttons and wire neighbors after populateClickableComponentList rebuilds the list.</summary>
        internal static void InjectArrowButtons(ItemGrabMenu menu)
        {
            if (_prevButton == null || _nextButton == null)
                return;

            if (menu.allClickableComponents != null)
            {
                if (!menu.allClickableComponents.Contains(_prevButton))
                    menu.allClickableComponents.Add(_prevButton);
                if (!menu.allClickableComponents.Contains(_nextButton))
                    menu.allClickableComponents.Add(_nextButton);
            }

            if (Constants.TargetPlatform == GamePlatform.Android)
            {
                // Android: wire into the sidebar chain
                var anchor = menu.fillStacksButton;
                if (anchor != null)
                    anchor.downNeighborID = PrevButtonID;

                if (menu.colorPickerToggleButton != null)
                {
                    menu.colorPickerToggleButton.upNeighborID = PrevButtonID;
                    _prevButton.downNeighborID = menu.colorPickerToggleButton.myID;
                    _nextButton.downNeighborID = menu.colorPickerToggleButton.myID;
                }

                // Wire the 3rd (bottom) chest row's rightmost slot → prev arrow,
                // so you can reach arrows from the chest grid directly
                WireAndroidChestRowToArrows(menu);
            }
            else
            {
                // PC: wire arrows ↔ top row of chest grid for bidirectional navigation
                WirePCArrowsToChestGrid(menu);
            }
        }

        /// <summary>Wire PC arrow buttons to/from the top row of the chest inventory grid.</summary>
        private static void WirePCArrowsToChestGrid(ItemGrabMenu menu)
        {
            var slots = menu.ItemsToGrabMenu?.inventory;
            if (slots == null || slots.Count == 0)
                return;

            // Find the top row (minimum Y)
            int minY = int.MaxValue;
            foreach (var slot in slots)
            {
                if (slot.bounds.Y < minY) minY = slot.bounds.Y;
            }

            int prevCenterX = _prevButton.bounds.X + _prevButton.bounds.Width / 2;
            int nextCenterX = _nextButton.bounds.X + _nextButton.bounds.Width / 2;

            // Wire each top-row slot's upNeighborID to the nearest arrow
            int leftmostID = -1, rightmostID = -1;
            int leftmostX = int.MaxValue, rightmostX = int.MinValue;
            foreach (var slot in slots)
            {
                if (slot.bounds.Y != minY) continue;

                int slotCenterX = slot.bounds.X + slot.bounds.Width / 2;
                slot.upNeighborID = System.Math.Abs(slotCenterX - prevCenterX) < System.Math.Abs(slotCenterX - nextCenterX)
                    ? PrevButtonID : NextButtonID;

                if (slot.bounds.X < leftmostX) { leftmostX = slot.bounds.X; leftmostID = slot.myID; }
                if (slot.bounds.X > rightmostX) { rightmostX = slot.bounds.X; rightmostID = slot.myID; }
            }

            // Wire arrows down to the corner slots
            if (leftmostID >= 0)
                _prevButton.downNeighborID = leftmostID;
            if (rightmostID >= 0)
                _nextButton.downNeighborID = rightmostID;
        }

        /// <summary>Wire the bottom chest row's rightmost slot → prev arrow on Android sidebar.</summary>
        private static void WireAndroidChestRowToArrows(ItemGrabMenu menu)
        {
            var slots = menu.ItemsToGrabMenu?.inventory;
            if (slots == null || slots.Count == 0)
                return;

            // Find the bottom row (maximum Y) and its rightmost slot
            int maxY = int.MinValue;
            foreach (var slot in slots)
            {
                if (slot.bounds.Y > maxY) maxY = slot.bounds.Y;
            }

            int rightmostX = int.MinValue;
            ClickableComponent rightmostSlot = null;
            foreach (var slot in slots)
            {
                if (slot.bounds.Y == maxY && slot.bounds.X > rightmostX)
                {
                    rightmostX = slot.bounds.X;
                    rightmostSlot = slot;
                }
            }

            if (rightmostSlot != null)
            {
                rightmostSlot.rightNeighborID = PrevButtonID;
                _prevButton.leftNeighborID = rightmostSlot.myID;
            }
        }

        internal static void ClearArrowButtons()
        {
            _prevButton = null;
            _nextButton = null;
            _chestLabel = null;
        }

        // Use __0 instead of named param — Android names it "b", PC names it "button"
        private static bool ReceiveGamePadButton_Prefix(ItemGrabMenu __instance, Buttons __0)
        {
            if (!Config.EnableChestBrowser || !ChestBrowser.IsActive)
                return true;

            // LT/RT cycle chests — preserve current cursor position
            if (__0 == Buttons.LeftTrigger)
            {
                int snapID = __instance.currentlySnappedComponent?.myID ?? -1;
                ChestBrowser.CyclePrev(snapID);
                return false;
            }
            if (__0 == Buttons.RightTrigger)
            {
                int snapID = __instance.currentlySnappedComponent?.myID ?? -1;
                ChestBrowser.CycleNext(snapID);
                return false;
            }

            // A button on arrow buttons — stay on the arrow after cycling
            if (__0 == Buttons.A)
            {
                var snapped = __instance.currentlySnappedComponent;
                if (snapped != null)
                {
                    if (snapped.myID == PrevButtonID)
                    {
                        Game1.playSound("smallSelect");
                        ChestBrowser.CyclePrev(PrevButtonID);
                        return false;
                    }
                    if (snapped.myID == NextButtonID)
                    {
                        Game1.playSound("smallSelect");
                        ChestBrowser.CycleNext(NextButtonID);
                        return false;
                    }
                }
            }

            return true;
        }

        private static bool ReceiveKeyPress_Prefix(Keys key)
        {
            if (!Config.EnableChestBrowser || !ChestBrowser.IsActive)
                return true;

            if (key == Keys.Tab)
            {
                var keyboard = Keyboard.GetState();
                bool shift = keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift)
                          || keyboard.IsKeyDown(Keys.LeftControl) || keyboard.IsKeyDown(Keys.RightControl);

                if (shift)
                    ChestBrowser.CyclePrev();
                else
                    ChestBrowser.CycleNext();
                return false;
            }

            return true;
        }

        private static bool ReceiveLeftClick_Prefix(int x, int y)
        {
            if (!Config.EnableChestBrowser || !ChestBrowser.IsActive)
                return true;

            if (_prevButton != null && _prevButton.containsPoint(x, y))
            {
                Game1.playSound("smallSelect");
                ChestBrowser.CyclePrev();
                return false;
            }

            if (_nextButton != null && _nextButton.containsPoint(x, y))
            {
                Game1.playSound("smallSelect");
                ChestBrowser.CycleNext();
                return false;
            }

            return true;
        }

        /// <summary>Suppress junimoNoteIcon BEFORE the game draws it — nulling in postfix is too late.</summary>
        private static void Draw_Prefix(ItemGrabMenu __instance)
        {
            if (!Config.EnableChestBrowser || !ChestBrowser.IsActive)
                return;

            // junimoNoteIcon appears/disappears dynamically when hovering bundle items,
            // which triggers populateClickableComponentList and disrupts our arrow wiring.
            // Null it before draw so the game never renders it.
            __instance.junimoNoteIcon = null;
        }

        /// <summary>
        /// Draw arrows during InventoryMenu.draw (the chest grid). This places them in the
        /// z-order BEFORE ItemGrabMenu's tooltip drawing, so tooltips naturally appear on top.
        /// </summary>
        private static void InventoryMenu_Draw_Postfix(InventoryMenu __instance, SpriteBatch b)
        {
            if (_prevButton == null || _nextButton == null || !ChestBrowser.IsActive)
                return;
            if (!(Game1.activeClickableMenu is ItemGrabMenu igm) || __instance != igm.ItemsToGrabMenu)
                return;

            _prevButton.draw(b);
            _nextButton.draw(b);
        }

        private static void Draw_Postfix(ItemGrabMenu __instance, SpriteBatch b)
        {
            if (!Config.EnableChestBrowser || !ChestBrowser.IsActive)
                return;

            // Re-inject and re-wire every frame
            InjectArrowButtons(__instance);

            // Arrows are drawn in InventoryMenu_Draw_Postfix (before tooltips in z-order).
            // Only draw the label here — it's at the top of the menu, above the tooltip area.
            if (!string.IsNullOrEmpty(_chestLabel))
            {
                var font = Game1.smallFont;
                var textSize = font.MeasureString(_chestLabel);
                var grab = __instance.ItemsToGrabMenu;
                float centerX = grab.xPositionOnScreen + grab.width / 2f;
                float textX = centerX - textSize.X / 2f;

                // On Android fullscreen, yPositionOnScreen is ~0, so place inside the top border
                // On PC, place just above the menu frame
                float textY;
                if (Constants.TargetPlatform == GamePlatform.Android)
                    textY = __instance.yPositionOnScreen + 12;
                else
                    textY = __instance.yPositionOnScreen - 37;

                // Black outline (draw offset in 4 directions)
                var outlineColor = Color.Black;
                var pos = new Vector2(textX, textY);
                b.DrawString(font, _chestLabel, pos + new Vector2(-2, 0), outlineColor);
                b.DrawString(font, _chestLabel, pos + new Vector2(2, 0), outlineColor);
                b.DrawString(font, _chestLabel, pos + new Vector2(0, -2), outlineColor);
                b.DrawString(font, _chestLabel, pos + new Vector2(0, 2), outlineColor);
                b.DrawString(font, _chestLabel, pos, Color.White);
            }

            // Redraw cursor on top
            __instance.drawMouse(b);
        }

        private static void PerformHoverAction_Postfix(ItemGrabMenu __instance, int x, int y)
        {
            if (!Config.EnableChestBrowser || !ChestBrowser.IsActive)
                return;

            _prevButton?.tryHover(x, y, 0.2f);
            _nextButton?.tryHover(x, y, 0.2f);
        }
    }
}
