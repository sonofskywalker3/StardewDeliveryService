using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using HarmonyLib;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;

namespace StardewDeliveryService.Patches
{
    /// <summary>Patches ItemGrabMenu for chest cycling — LB/RB, Tab/Shift+Tab, and arrow buttons.</summary>
    internal static class ItemGrabMenuPatches
    {
        private static ModConfig Config;
        private static IMonitor Monitor;

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
                postfix: new HarmonyMethod(typeof(ItemGrabMenuPatches), nameof(Draw_Postfix))
            );

            harmony.Patch(
                original: AccessTools.Method(typeof(ItemGrabMenu), nameof(ItemGrabMenu.performHoverAction)),
                postfix: new HarmonyMethod(typeof(ItemGrabMenuPatches), nameof(PerformHoverAction_Postfix))
            );
        }

        /// <summary>Create arrow buttons and label when the chest browser opens an ItemGrabMenu.</summary>
        internal static void SetupArrowButtons(ItemGrabMenu menu, string label)
        {
            _chestLabel = label;

            // Arrow buttons positioned above the menu, flanking the title/message area
            int arrowY = menu.yPositionOnScreen - 56;

            _prevButton = new ClickableTextureComponent(
                new Rectangle(menu.xPositionOnScreen - 80, arrowY, 64, 64),
                Game1.mouseCursors,
                new Rectangle(352, 495, 12, 11),  // left arrow from mouseCursors
                4f
            )
            {
                hoverText = "Previous chest (Shift+Tab)"
            };

            _nextButton = new ClickableTextureComponent(
                new Rectangle(menu.xPositionOnScreen + menu.width + 16, arrowY, 64, 64),
                Game1.mouseCursors,
                new Rectangle(365, 495, 12, 11),  // right arrow from mouseCursors
                4f
            )
            {
                hoverText = "Next chest (Tab)"
            };
        }

        internal static void ClearArrowButtons()
        {
            _prevButton = null;
            _nextButton = null;
            _chestLabel = null;
        }

        private static bool ReceiveGamePadButton_Prefix(Buttons button)
        {
            if (!Config.EnableChestBrowser || !ChestBrowser.IsActive)
                return true;

            if (button == Buttons.LeftShoulder)
            {
                ChestBrowser.CyclePrev();
                return false;
            }

            if (button == Buttons.RightShoulder)
            {
                ChestBrowser.CycleNext();
                return false;
            }

            return true;
        }

        private static bool ReceiveKeyPress_Prefix(Keys key)
        {
            if (!Config.EnableChestBrowser || !ChestBrowser.IsActive)
                return true;

            var keyboard = Keyboard.GetState();
            bool shift = keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift);

            if (key == Keys.Tab)
            {
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

        private static void Draw_Postfix(ItemGrabMenu __instance, SpriteBatch b)
        {
            if (!Config.EnableChestBrowser || !ChestBrowser.IsActive)
                return;

            _prevButton?.draw(b);
            _nextButton?.draw(b);

            // Draw chest label on the top border of the menu frame
            if (!string.IsNullOrEmpty(_chestLabel))
            {
                var font = Game1.smallFont;
                var textSize = font.MeasureString(_chestLabel);
                // Center over the chest inventory grid, not the full menu width
                var grab = __instance.ItemsToGrabMenu;
                float centerX = grab.xPositionOnScreen + grab.width / 2f;
                float textX = centerX - textSize.X / 2f;
                float textY = __instance.yPositionOnScreen - 37;

                // Black outline (draw offset in 4 directions)
                var outlineColor = Color.Black;
                var pos = new Vector2(textX, textY);
                b.DrawString(font, _chestLabel, pos + new Vector2(-2, 0), outlineColor);
                b.DrawString(font, _chestLabel, pos + new Vector2(2, 0), outlineColor);
                b.DrawString(font, _chestLabel, pos + new Vector2(0, -2), outlineColor);
                b.DrawString(font, _chestLabel, pos + new Vector2(0, 2), outlineColor);
                // White text on top
                b.DrawString(font, _chestLabel, pos, Color.White);
            }

            // Redraw cursor on top so arrows/label don't cover it
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
