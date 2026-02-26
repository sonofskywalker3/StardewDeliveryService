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

        /// <summary>Get the farmhouse porch entry tile. Farmer appears here when exiting.</summary>
        private static Point GetPorchEntry()
        {
            try { return Game1.getFarm().GetMainFarmHouseEntry(); }
            catch { return new Point(64, 15); } // standard farm fallback
        }

        /// <summary>Junimo waiting on the porch when player walks outside after completing a bundle.</summary>
        public static void PlayJunimoUnlock()
        {
            if (Game1.eventUp)
                return;

            var location = Game1.currentLocation;
            if (location is not Farm)
                return;

            var porch = GetPorchEntry();
            // Farmer at the door, Junimo 2 tiles south on the path facing the farmer
            int fx = porch.X, fy = porch.Y;
            int jx = porch.X, jy = porch.Y + 2;

            string script =
                $"none/-1000 -1000/farmer {fx} {fy} 2 Junimo {jx} {jy} 0" +
                "/pause 500" +
                "/emote Junimo 16" +    // ! exclamation
                "/pause 800" +
                $"/speak Junimo \"We've been watching you help the forest spirits!$h#$b#We can help you carry things between your chests.$h\"" +
                "/pause 400" +
                "/emote Junimo 20" +    // heart
                "/pause 1000" +
                "/end";

            var evt = new Event(script, null, "SDS_JunimoUnlock");
            evt.onEventFinished = () =>
            {
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

            var porch = GetPorchEntry();
            int fx = porch.X, fy = porch.Y;
            int jx = porch.X, jy = porch.Y + 2;

            string script =
                $"none/-1000 -1000/farmer {fx} {fy} 2 Junimo {jx} {jy} 0" +
                "/pause 500" +
                "/emote Junimo 28" +    // ... sad/ellipsis
                "/pause 800" +
                "/speak Junimo \"...\"" +
                "/pause 400" +
                $"/speak Junimo \"We understand. Goodbye, friend.$s\"" +
                "/pause 600" +
                "/end";

            var evt = new Event(script, null, "SDS_JojaBetrayal");
            evt.onEventFinished = () =>
            {
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

            var porch = GetPorchEntry();
            int fx = porch.X, fy = porch.Y;
            // Shane standing to the right of the path, facing left toward the farmer
            int sx = porch.X + 1, sy = porch.Y + 2;

            string script =
                $"none/-1000 -1000/farmer {fx} {fy} 2 Shane {sx} {sy} 3" +
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
