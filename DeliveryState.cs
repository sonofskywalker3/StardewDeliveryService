using StardewModdingAPI;
using StardewValley;

namespace StardewDeliveryService
{
    /// <summary>Manages delivery service progression state — load/save, bundle checks, cutscene triggers.</summary>
    internal static class DeliveryState
    {
        private const string SaveKey = "delivery-state";

        private static DeliverySaveData _data = new();
        private static IMonitor _monitor;

        /// <summary>Whether the delivery service is unlocked (any provider active).</summary>
        public static bool IsUnlocked => _data.Provider != DeliveryProvider.None;

        /// <summary>The current delivery provider.</summary>
        public static DeliveryProvider Provider => _data.Provider;

        /// <summary>Whether the Junimo unlock cutscene should play this morning.</summary>
        public static bool ShouldPlayJunimoUnlock { get; private set; }

        /// <summary>Whether the Joja betrayal cutscene should play this morning.</summary>
        public static bool ShouldPlayJojaCutscene { get; private set; }

        /// <summary>Whether the Shane offer cutscene should play this morning.</summary>
        public static bool ShouldPlayShaneCutscene { get; private set; }

        /// <summary>Load save data when a save is loaded.</summary>
        public static void OnSaveLoaded(IModHelper helper, IMonitor monitor)
        {
            _monitor = monitor;
            _data = helper.Data.ReadSaveData<DeliverySaveData>(SaveKey) ?? new DeliverySaveData();
            _monitor.Log($"Delivery state loaded: Provider={_data.Provider}, UnlockPlayed={_data.UnlockCutscenePlayed}, JojaPlayed={_data.JojaCutscenePlayed}, ShanePlayed={_data.ShaneCutscenePlayed}", LogLevel.Trace);
        }

        /// <summary>Save data before the game saves.</summary>
        public static void OnSaving(IModHelper helper)
        {
            helper.Data.WriteSaveData(SaveKey, _data);
        }

        /// <summary>Check morning conditions and set cutscene flags. Called on DayStarted.</summary>
        public static void CheckMorningCutscenes()
        {
            ShouldPlayJunimoUnlock = false;
            ShouldPlayJojaCutscene = false;
            ShouldPlayShaneCutscene = false;

            // Priority 1: Junimo unlock — any bundle complete and cutscene not yet played
            if (!_data.UnlockCutscenePlayed && CountCompletedBundles() >= 1)
            {
                ShouldPlayJunimoUnlock = true;
                _monitor?.Log("Morning check: Junimo unlock cutscene queued", LogLevel.Trace);
                return; // Only one cutscene per day
            }

            // Priority 2: Joja betrayal — player has JojaMember mail and currently has Junimo provider
            if (!_data.JojaCutscenePlayed && _data.Provider == DeliveryProvider.Junimo
                && Game1.MasterPlayer.mailReceived.Contains("JojaMember"))
            {
                ShouldPlayJojaCutscene = true;
                _monitor?.Log("Morning check: Joja betrayal cutscene queued", LogLevel.Trace);
                return;
            }

            // Priority 3: Shane offer — Joja cutscene played but Shane cutscene not yet played
            if (_data.JojaCutscenePlayed && !_data.ShaneCutscenePlayed
                && _data.Provider == DeliveryProvider.None)
            {
                ShouldPlayShaneCutscene = true;
                _monitor?.Log("Morning check: Shane offer cutscene queued", LogLevel.Trace);
                return;
            }
        }

        /// <summary>Mark the Junimo service as unlocked.</summary>
        public static void UnlockJunimo()
        {
            _data.Provider = DeliveryProvider.Junimo;
            _data.UnlockCutscenePlayed = true;
            _monitor?.Log("Delivery service unlocked: Junimo", LogLevel.Info);
        }

        /// <summary>Disable Junimo service after Joja membership.</summary>
        public static void DisableJunimoForJoja()
        {
            _data.Provider = DeliveryProvider.None;
            _data.JojaCutscenePlayed = true;
            _monitor?.Log("Junimo delivery disabled (Joja route)", LogLevel.Info);
        }

        /// <summary>Unlock Shane's paid delivery service.</summary>
        public static void UnlockShane()
        {
            _data.Provider = DeliveryProvider.Shane;
            _data.ShaneCutscenePlayed = true;
            _monitor?.Log("Delivery service unlocked: Shane", LogLevel.Info);
        }

        /// <summary>Count how many bundles are fully completed.</summary>
        private static int CountCompletedBundles()
        {
            var bundles = Game1.netWorldState?.Value?.Bundles;
            if (bundles == null)
                return 0;

            int count = 0;
            foreach (var pair in bundles.Pairs)
            {
                bool[] ingredients = pair.Value;
                if (ingredients == null || ingredients.Length == 0)
                    continue;

                bool allComplete = true;
                foreach (bool done in ingredients)
                {
                    if (!done)
                    {
                        allComplete = false;
                        break;
                    }
                }
                if (allComplete)
                    count++;
            }

            return count;
        }
    }
}
