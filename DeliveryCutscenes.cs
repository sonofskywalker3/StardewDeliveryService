using StardewModdingAPI;
using StardewValley;

namespace StardewDeliveryService
{
    /// <summary>Event scripts for delivery service progression cutscenes.</summary>
    internal static class DeliveryCutscenes
    {
        internal static IMonitor Monitor;

        /// <summary>Junimo appears in FarmHouse to offer free delivery after first bundle completion.</summary>
        public static void PlayJunimoUnlock()
        {
            if (Game1.eventUp)
                return;

            var location = Game1.currentLocation;
            if (location == null)
                return;

            // Event string: no music, camera follows player, farmer and Junimo positioned
            // Farmer at tile 6,9 facing up (0), Junimo at tile 6,7 facing down (2)
            string script =
                "none/-1000 -1000/farmer 6 9 0 Junimo 6 7 2" +
                "/pause 500" +
                "/emote Junimo 16" +    // ! exclamation
                "/pause 800" +
                "/speak Junimo \"We've been watching you help the forest spirits!$h#$b#We can help you carry things between your chests.$h\"" +
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

        /// <summary>Junimo says farewell after player buys Joja membership.</summary>
        public static void PlayJojaBetrayal()
        {
            if (Game1.eventUp)
                return;

            var location = Game1.currentLocation;
            if (location == null)
                return;

            string script =
                "none/-1000 -1000/farmer 6 9 0 Junimo 6 7 2" +
                "/pause 500" +
                "/emote Junimo 28" +    // ... sad/ellipsis
                "/pause 800" +
                "/speak Junimo \"...\"" +
                "/pause 400" +
                "/speak Junimo \"We understand. Goodbye, friend.$s\"" +
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

        /// <summary>Shane offers paid delivery service after Joja route disables Junimos.</summary>
        public static void PlayShaneOffer()
        {
            if (Game1.eventUp)
                return;

            var location = Game1.currentLocation;
            if (location == null)
                return;

            // Shane at the door area (tile 3,7), farmer nearby
            string script =
                "none/-1000 -1000/farmer 6 9 0 Shane 3 7 1" +
                "/pause 500" +
                "/speak Shane \"Hey, farmer. Heard the little guys aren't helping you anymore.$s#$b#I could deliver stuff between your chests... for a small fee.$h\"" +
                "/pause 400" +
                "/speak Shane \"Just 1%% of the sell value. Not bad, right?$h\"" +
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
