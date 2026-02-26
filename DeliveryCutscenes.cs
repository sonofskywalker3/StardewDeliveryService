using System.Linq;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;

namespace StardewDeliveryService
{
    /// <summary>Event scripts for delivery service progression cutscenes.
    /// All trigger on the Farm when the player walks outside — Junimo/Shane on the porch.</summary>
    internal static class DeliveryCutscenes
    {
        internal static IMonitor Monitor;

        /// <summary>Unique ID for the Junimo TemporaryAnimatedSprite so we can clean it up.</summary>
        private const int JunimoSpriteId = 777777;

        /// <summary>Get the farmhouse porch entry tile. Farmer appears here when exiting.</summary>
        private static Point GetPorchEntry()
        {
            try { return Game1.getFarm().GetMainFarmHouseEntry(); }
            catch { return new Point(64, 15); } // standard farm fallback
        }

        /// <summary>Add a visible bouncing Junimo sprite at the given tile position.
        /// The Event system's built-in "Junimo" actor doesn't render on the Farm,
        /// so we use a TemporaryAnimatedSprite instead.</summary>
        private static void SpawnJunimoSprite(GameLocation location, int tileX, int tileY, Color color)
        {
            // Junimo idle bounce: frames 8-11 on the sprite sheet (row 1, columns 0-3)
            // Sheet is 128px wide = 8 columns of 16px sprites
            location.temporarySprites.Add(new TemporaryAnimatedSprite(
                "Characters\\Junimo",
                new Rectangle(0, 16, 16, 16), // idle frames start at row 1
                100f,   // animation interval (ms per frame)
                4,      // animation length (4 frames)
                999,    // loops (enough for any cutscene duration)
                new Vector2(tileX * 64, tileY * 64 - 32),
                flicker: false,
                flipped: false,
                layerDepth: (tileY * 64f) / 10000f + 0.001f,
                alphaFade: 0f,
                color: color,
                scale: 3f, // Junimo scale: 0.75 * 4 = 3
                scaleChange: 0f,
                rotation: 0f,
                rotationChange: 0f
            ) { id = JunimoSpriteId });
        }

        /// <summary>Remove the Junimo sprite with a sparkle puff effect and sound.</summary>
        private static void CleanupJunimoSprite(GameLocation location)
        {
            // Find the sprite to get its position before removing
            var junimoSprite = location.temporarySprites.FirstOrDefault(s => s.id == JunimoSpriteId);
            if (junimoSprite != null)
            {
                var pos = junimoSprite.Position;

                // Sparkle burst — same sprite indices (5, 46) the game uses for CC Junimo effects
                for (int i = 0; i < 8; i++)
                {
                    location.temporarySprites.Add(new TemporaryAnimatedSprite(
                        Game1.random.Next(2) == 0 ? 5 : 46,
                        pos + new Vector2(Game1.random.Next(-8, 24), Game1.random.Next(-8, 24)),
                        Color.LimeGreen
                    )
                    {
                        motion = new Vector2(
                            (float)(Game1.random.NextDouble() * 2.0 - 1.0),
                            (float)(Game1.random.NextDouble() * -2.0)
                        ),
                        alphaFade = 0.02f,
                        layerDepth = 1f,
                        scale = 1f
                    });
                }
                location.localSound("junimoMeep1");

                location.temporarySprites.Remove(junimoSprite);
            }
        }

        /// <summary>Junimo waiting on the porch when player walks outside after completing a bundle.</summary>
        public static void PlayJunimoUnlock()
        {
            if (Game1.eventUp)
                return;

            var location = Game1.currentLocation;
            if (location is not Farm)
                return;

            // Clear flag immediately so re-entering the Farm doesn't replay
            DeliveryState.ClearCutsceneFlag();

            var porch = GetPorchEntry();
            int fx = porch.X, fy = porch.Y;
            int jx = porch.X, jy = porch.Y + 2;

            // Spawn a green Junimo sprite 2 tiles south of the door
            SpawnJunimoSprite(location, jx, jy, Color.LimeGreen);

            // Event with just the farmer — Junimo visual is handled by the TAS above
            string script =
                $"none/{fx} {fy}/farmer {fx} {fy} 2" +
                "/pause 800" +
                "/message \"A small Junimo bounces excitedly on your porch!\"" +
                "/pause 400" +
                "/message \"He hands you a small pin. I wonder what it does?\"" +
                "/pause 800" +
                "/end";

            var evt = new Event(script, null, "SDS_JunimoUnlock");
            evt.onEventFinished = () =>
            {
                CleanupJunimoSprite(location);
                DeliveryState.UnlockJunimo();
                Game1.addHUDMessage(new HUDMessage("Junimo Delivery Service unlocked!") { noIcon = true });
                Monitor?.Log("Junimo unlock cutscene completed", LogLevel.Info);
            };

            location.startEvent(evt);
        }

        /// <summary>Junimo on the porch, says farewell after player buys Joja membership.</summary>
        public static void PlayJojaBetrayal()
        {
            if (Game1.eventUp)
                return;

            var location = Game1.currentLocation;
            if (location is not Farm)
                return;

            DeliveryState.ClearCutsceneFlag();

            var porch = GetPorchEntry();
            int fx = porch.X, fy = porch.Y;
            int jx = porch.X, jy = porch.Y + 2;

            // Spawn a green Junimo sprite (slower animation for sad mood)
            SpawnJunimoSprite(location, jx, jy, Color.LimeGreen);

            string script =
                $"none/{fx} {fy}/farmer {fx} {fy} 2" +
                "/pause 800" +
                "/message \"The Junimo looks at you sadly...\"" +
                "/pause 400" +
                "/message \"It seems to understand. With a quiet wave, it vanishes.\"" +
                "/pause 600" +
                "/end";

            var evt = new Event(script, null, "SDS_JojaBetrayal");
            evt.onEventFinished = () =>
            {
                CleanupJunimoSprite(location);
                DeliveryState.DisableJunimoForJoja();
                Game1.addHUDMessage(new HUDMessage("The Junimos have stopped helping.") { noIcon = true });
                Monitor?.Log("Joja betrayal cutscene completed", LogLevel.Info);
            };

            location.startEvent(evt);
        }

        /// <summary>Shane on the porch offering paid delivery after Joja route.</summary>
        public static void PlayShaneOffer()
        {
            if (Game1.eventUp)
                return;

            var location = Game1.currentLocation;
            if (location is not Farm)
                return;

            DeliveryState.ClearCutsceneFlag();

            var porch = GetPorchEntry();
            int fx = porch.X, fy = porch.Y;
            int sx = porch.X + 1, sy = porch.Y + 2;

            // Shane has portrait data so 'speak' works here
            string script =
                $"none/{fx} {fy}/farmer {fx} {fy} 2 Shane {sx} {sy} 3" +
                "/pause 500" +
                $"/speak Shane \"Hey, farmer. Heard the little guys aren't helping you anymore.$s#$b#I could deliver stuff between your chests... for a small fee.$h\"" +
                "/pause 400" +
                $"/speak Shane \"Just 1%% of the sell value. Not bad, right?$h\"" +
                "/pause 600" +
                "/end";

            var evt = new Event(script, null, "SDS_ShaneOffer");
            evt.onEventFinished = () =>
            {
                DeliveryState.UnlockShane();
                Game1.addHUDMessage(new HUDMessage("Shane's Delivery Service unlocked! (1% fee per item)") { noIcon = true });
                Monitor?.Log("Shane offer cutscene completed", LogLevel.Info);
            };

            location.startEvent(evt);
        }
    }
}
