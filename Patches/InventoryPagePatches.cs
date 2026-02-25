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

        // High ID to avoid collision — 898 conflicts with an existing PC component
        private const int DeliveryIconID = 73541;

        private static ClickableTextureComponent _deliveryIcon;
        private static Texture2D _junimoTexture;
        private static bool _positioned;

        // Cached reflection for tooltip fields (private on Android, public on PC)
        private static FieldInfo _hoverTextField;
        private static FieldInfo _hoverTitleField;
        private static FieldInfo _hoveredItemField;

        // Android-only: InventoryMenu.drawInfoPanel(SpriteBatch, bool) for mobile floating tooltips
        private static MethodInfo _drawInfoPanelMethod;

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

            // Patch populateClickableComponentList on the base class — InventoryPage doesn't override it.
            // Filter by type inside the postfix. This also handles ItemGrabMenu re-injection.
            harmony.Patch(
                original: AccessTools.Method(typeof(IClickableMenu), nameof(IClickableMenu.populateClickableComponentList)),
                postfix: new HarmonyMethod(typeof(InventoryPagePatches), nameof(PopulateClickableComponentList_Postfix))
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

            // PC only: ensure wiring is correct before navigation runs in receiveGamePadButton.
            // On PC, InventoryPage overrides this method. On Android it doesn't, so skip.
            if (Constants.TargetPlatform != GamePlatform.Android)
            {
                harmony.Patch(
                    original: AccessTools.Method(typeof(InventoryPage), nameof(InventoryPage.receiveGamePadButton)),
                    prefix: new HarmonyMethod(typeof(InventoryPagePatches), nameof(ReceiveGamePadButton_Prefix))
                );
            }

            _junimoTexture = Game1.content.Load<Texture2D>("Characters\\Junimo");

            _hoverTextField = AccessTools.Field(typeof(InventoryPage), "hoverText");
            _hoverTitleField = AccessTools.Field(typeof(InventoryPage), "hoverTitle");
            _hoveredItemField = AccessTools.Field(typeof(InventoryPage), "hoveredItem");

            // Android has InventoryMenu.drawInfoPanel(SpriteBatch, bool) — PC doesn't
            _drawInfoPanelMethod = AccessTools.Method(typeof(InventoryMenu), "drawInfoPanel", new[] { typeof(SpriteBatch), typeof(bool) });
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

            // Temporary position — will be repositioned between sort and trash once
            // allClickableComponents is available (populate postfix or first draw)
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
                upNeighborID = organizeButton.myID,
                downNeighborID = 105,    // trash can
                leftNeighborID = -7777,
                rightNeighborID = -7777,
                hoverText = "Delivery Service"
            };
            _positioned = false;

            InjectDeliveryIcon(__instance);
        }

        /// <summary>Re-inject components after the game rebuilds allClickableComponents via reflection.</summary>
        private static void PopulateClickableComponentList_Postfix(IClickableMenu __instance)
        {
            if (!Config.EnableChestBrowser)
                return;

            if (__instance is InventoryPage invPage && _deliveryIcon != null)
            {
                InjectDeliveryIcon(invPage);
                PositionBetweenSortAndTrash(invPage);
                // PC only: wire neighbors ourselves (Android: AC's WireSidebarChain handles it)
                if (Constants.TargetPlatform != GamePlatform.Android)
                    WirePCNeighbors(invPage);
            }

            if (__instance is ItemGrabMenu grabMenu && ChestBrowser.IsActive)
                ItemGrabMenuPatches.InjectArrowButtons(grabMenu);
        }

        private static void InjectDeliveryIcon(InventoryPage page)
        {
            if (_deliveryIcon == null)
                return;

            if (page.allClickableComponents != null && !page.allClickableComponents.Contains(_deliveryIcon))
                page.allClickableComponents.Add(_deliveryIcon);
        }

        /// <summary>
        /// Android only: reposition the Junimo icon between sort(106) and trash(105) by Y coordinate.
        /// AC's WireSidebarChain uses Y position to determine chain order.
        /// PC position (organizeButton.Y + 76) is set in the constructor and left as-is.
        /// </summary>
        private static void PositionBetweenSortAndTrash(InventoryPage page)
        {
            if (_deliveryIcon == null || _positioned || page.allClickableComponents == null)
                return;
            if (Constants.TargetPlatform != GamePlatform.Android)
            {
                _positioned = true;
                return;
            }

            ClickableComponent sort = null;
            ClickableComponent trash = null;
            foreach (var cc in page.allClickableComponents)
            {
                if (cc.myID == 106) sort = cc;
                else if (cc.myID == 105) trash = cc;
            }

            if (sort == null || trash == null)
                return;

            // Center vertically between bottom of sort and top of trash
            int gapTop = sort.bounds.Y + sort.bounds.Height;
            int gapBottom = trash.bounds.Y;
            int iconY = gapTop + (gapBottom - gapTop - _deliveryIcon.bounds.Height) / 2;

            _deliveryIcon.bounds = new Rectangle(
                sort.bounds.X, iconY,
                _deliveryIcon.bounds.Width, _deliveryIcon.bounds.Height);
            _positioned = true;
        }

        /// <summary>PC-only wiring: Sort(106) → Junimo(73541) → Trash(105).</summary>
        private static void WirePCNeighbors(InventoryPage page)
        {
            if (_deliveryIcon == null || page.allClickableComponents == null)
                return;

            ClickableComponent sort = null;
            ClickableComponent trash = null;
            foreach (var cc in page.allClickableComponents)
            {
                if (cc.myID == 106) sort = cc;
                else if (cc.myID == 105) trash = cc;
            }

            if (sort != null)
            {
                sort.downNeighborID = DeliveryIconID;
                _deliveryIcon.upNeighborID = 106;
            }
            _deliveryIcon.downNeighborID = 105;
            if (trash != null)
                trash.upNeighborID = DeliveryIconID;
        }

        private static void Draw_Postfix(InventoryPage __instance, SpriteBatch b)
        {
            if (!Config.EnableChestBrowser || _deliveryIcon == null)
                return;

            // Always re-inject — populateClickableComponentList rebuilds the list from
            // scratch via reflection and removes our icon every time it runs
            InjectDeliveryIcon(__instance);
            if (!_positioned)
                PositionBetweenSortAndTrash(__instance);

            // PC: re-apply wiring every frame as backup
            if (Constants.TargetPlatform != GamePlatform.Android)
                WirePCNeighbors(__instance);

            // Draw manually with a green tint — the Junimo sprite is white/pale without one
            b.Draw(
                _deliveryIcon.texture,
                new Vector2(_deliveryIcon.bounds.X, _deliveryIcon.bounds.Y),
                _deliveryIcon.sourceRect,
                new Color(80, 200, 80),  // Junimo green
                0f,
                Vector2.Zero,
                _deliveryIcon.scale,
                SpriteEffects.None,
                0.86f
            );

            // Redraw tooltips on top of our icon so they aren't hidden behind it
            // Android: redraw mobile floating tooltip via InventoryMenu.drawInfoPanel
            if (_drawInfoPanelMethod != null && __instance.inventory != null)
                _drawInfoPanelMethod.Invoke(__instance.inventory, new object[] { b, true });

            // PC: redraw standard tooltips
            var hoveredItem = _hoveredItemField?.GetValue(__instance) as Item;
            var hoverText = _hoverTextField?.GetValue(__instance) as string;

            if (hoveredItem != null)
            {
                IClickableMenu.drawToolTip(b, hoveredItem.getDescription(), hoveredItem.DisplayName, hoveredItem, Game1.player.CursorSlotItem != null);
            }
            else if (!string.IsNullOrEmpty(hoverText))
            {
                IClickableMenu.drawHoverText(b, hoverText, Game1.smallFont);
            }

            __instance.drawMouse(b);
        }

        /// <summary>PC only: ensure icon is in the list and wiring is correct before navigation runs.</summary>
        private static void ReceiveGamePadButton_Prefix(InventoryPage __instance)
        {
            if (!Config.EnableChestBrowser || _deliveryIcon == null)
                return;

            InjectDeliveryIcon(__instance);
            WirePCNeighbors(__instance);
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
