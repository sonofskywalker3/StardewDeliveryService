using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using HarmonyLib;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;

namespace StardewDeliveryService.Patches
{
    /// <summary>Adds a Junimo delivery icon to the inventory page that opens the chest browser.</summary>
    internal static class InventoryPagePatches
    {
        private static ModConfig Config;
        private static IMonitor Monitor;

        // Use 898 to avoid conflict with any game IDs (shirts=108, etc.)
        private const int DeliveryIconID = 898;

        private static ClickableTextureComponent _deliveryIcon;
        private static Texture2D _junimoTexture;

        internal static void Init(ModConfig config, IMonitor monitor)
        {
            Config = config;
            Monitor = monitor;
        }

        internal static void Apply(Harmony harmony)
        {
            harmony.Patch(
                original: AccessTools.Constructor(typeof(InventoryPage), new[] { typeof(int), typeof(int), typeof(int), typeof(int) }),
                postfix: new HarmonyMethod(typeof(InventoryPagePatches), nameof(Constructor_Postfix))
            );

            harmony.Patch(
                original: AccessTools.Method(typeof(InventoryPage), nameof(InventoryPage.draw), new[] { typeof(SpriteBatch) }),
                postfix: new HarmonyMethod(typeof(InventoryPagePatches), nameof(Draw_Postfix))
            );

            harmony.Patch(
                original: AccessTools.Method(typeof(InventoryPage), nameof(InventoryPage.receiveLeftClick)),
                postfix: new HarmonyMethod(typeof(InventoryPagePatches), nameof(ReceiveLeftClick_Postfix))
            );

            harmony.Patch(
                original: AccessTools.Method(typeof(InventoryPage), nameof(InventoryPage.performHoverAction)),
                postfix: new HarmonyMethod(typeof(InventoryPagePatches), nameof(PerformHoverAction_Postfix))
            );

            // Load Junimo character texture
            _junimoTexture = Game1.content.Load<Texture2D>("Characters\\Junimo");
        }

        private static void Constructor_Postfix(InventoryPage __instance)
        {
            if (!Config.EnableChestBrowser)
                return;

            // Try InventoryPage.organizeButton first (PC), then InventoryMenu.organizeButton (Android)
            var organizeButton = __instance.organizeButton;
            if (organizeButton == null)
            {
                var field = AccessTools.Field(typeof(InventoryMenu), "organizeButton");
                if (field != null && __instance.inventory != null)
                    organizeButton = field.GetValue(__instance.inventory) as ClickableTextureComponent;
            }

            if (organizeButton == null)
            {
                Monitor?.Log("Could not find organize button — delivery icon not added", LogLevel.Warn);
                return;
            }

            // Position below the organize button with a bit more spacing
            _deliveryIcon = new ClickableTextureComponent(
                bounds: new Rectangle(organizeButton.bounds.X, organizeButton.bounds.Y + 76, 64, 64),
                texture: _junimoTexture ?? Game1.mouseCursors,
                sourceRect: _junimoTexture != null
                    ? new Rectangle(0, 0, 16, 16)       // Junimo character frame 0
                    : new Rectangle(331, 374, 15, 14),   // fallback: CC icon
                scale: 4f
            )
            {
                myID = DeliveryIconID,
                upNeighborID = 106,      // organize button
                downNeighborID = 105,    // trash can
                leftNeighborID = -7777,
                rightNeighborID = -7777,
                hoverText = "Delivery Service"
            };

            // Wire organize button down to us, and trash can up to us
            organizeButton.downNeighborID = DeliveryIconID;
            if (__instance.trashCan != null)
                __instance.trashCan.upNeighborID = DeliveryIconID;

            if (__instance.allClickableComponents != null)
                __instance.allClickableComponents.Add(_deliveryIcon);
        }

        private static void Draw_Postfix(InventoryPage __instance, SpriteBatch b)
        {
            if (!Config.EnableChestBrowser || _deliveryIcon == null)
                return;

            // Draw manually with a green tint — the Junimo sprite is white/pale without one
            b.Draw(
                _deliveryIcon.texture,
                new Vector2(_deliveryIcon.bounds.X, _deliveryIcon.bounds.Y),
                _deliveryIcon.sourceRect,
                new Color(80, 200, 80),  // Junimo green
                0f,
                Vector2.Zero,
                _deliveryIcon.scale * (_deliveryIcon.drawShadow ? 1f : 1f + (_deliveryIcon.scale > 1f ? 0f : 0.1f * (float)System.Math.Sin(Game1.currentGameTime.TotalGameTime.TotalMilliseconds / 300.0))),
                SpriteEffects.None,
                0.86f
            );
        }

        private static void ReceiveLeftClick_Postfix(InventoryPage __instance, int x, int y)
        {
            if (!Config.EnableChestBrowser || _deliveryIcon == null)
                return;

            if (_deliveryIcon.containsPoint(x, y))
            {
                Game1.playSound("dwop");
                ChestBrowser.Open();
            }
        }

        private static void PerformHoverAction_Postfix(InventoryPage __instance, int x, int y)
        {
            if (!Config.EnableChestBrowser || _deliveryIcon == null)
                return;

            _deliveryIcon.tryHover(x, y, 0.2f);
        }
    }
}
