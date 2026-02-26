using System;
using HarmonyLib;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;
using StardewValley.Objects;
using StardewDeliveryService.Patches;

namespace StardewDeliveryService
{
    public class ModEntry : Mod
    {
        internal static ModConfig Config;

        public override void Entry(IModHelper helper)
        {
            Config = helper.ReadConfig<ModConfig>();

            // Init patch classes
            ChestScanner.Monitor = this.Monitor;
            ChestBrowser.Monitor = this.Monitor;
            DeliveryCutscenes.Monitor = this.Monitor;
            InventoryPagePatches.Init(Config, this.Monitor);
            ItemGrabMenuPatches.Init(Config, this.Monitor);

            // Apply Harmony patches
            var harmony = new Harmony(this.ModManifest.UniqueID);
            InventoryPagePatches.Apply(harmony);
            ItemGrabMenuPatches.Apply(harmony);

            this.Monitor.Log("Stardew Delivery Service loaded", LogLevel.Info);

            // Console commands
            helper.ConsoleCommands.Add("sds_scan", "List all chests and their contents.", this.ScanChests);

            // Events
            helper.Events.GameLoop.GameLaunched += this.OnGameLaunched;
            helper.Events.GameLoop.SaveLoaded += this.OnSaveLoaded;
            helper.Events.GameLoop.Saving += this.OnSaving;
            helper.Events.GameLoop.DayStarted += this.OnDayStarted;
            helper.Events.Display.MenuChanged += this.OnMenuChanged;
        }

        private void OnGameLaunched(object sender, GameLaunchedEventArgs e)
        {
            RegisterConfigMenu();
        }

        private void OnSaveLoaded(object sender, SaveLoadedEventArgs e)
        {
            if (Config.EnableProgressionGating)
                DeliveryState.OnSaveLoaded(this.Helper, this.Monitor);
        }

        private void OnSaving(object sender, SavingEventArgs e)
        {
            if (Config.EnableProgressionGating)
                DeliveryState.OnSaving(this.Helper);
        }

        private void OnDayStarted(object sender, DayStartedEventArgs e)
        {
            if (!Config.EnableProgressionGating)
                return;

            DeliveryState.CheckMorningCutscenes();

            // Delay cutscene start to avoid conflicting with morning events
            if (DeliveryState.ShouldPlayJunimoUnlock)
                DelayedAction.functionAfterDelay(() => DeliveryCutscenes.PlayJunimoUnlock(), 1500);
            else if (DeliveryState.ShouldPlayJojaCutscene)
                DelayedAction.functionAfterDelay(() => DeliveryCutscenes.PlayJojaBetrayal(), 1500);
            else if (DeliveryState.ShouldPlayShaneCutscene)
                DelayedAction.functionAfterDelay(() => DeliveryCutscenes.PlayShaneOffer(), 1500);
        }

        private void OnMenuChanged(object sender, MenuChangedEventArgs e)
        {
            if (ChestBrowser.IsActive && e.OldMenu is ItemGrabMenu && e.NewMenu is not ItemGrabMenu)
                ChestBrowser.OnMenuClosed();
        }

        private void RegisterConfigMenu()
        {
            var configMenu = this.Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
            if (configMenu is null)
                return;

            configMenu.Register(
                mod: this.ModManifest,
                reset: () => Config = new ModConfig(),
                save: () => this.Helper.WriteConfig(Config)
            );

            configMenu.AddBoolOption(
                mod: this.ModManifest,
                name: () => "Chest Browser",
                tooltip: () => "Add a delivery icon to the inventory page that opens a remote chest browser.",
                getValue: () => Config.EnableChestBrowser,
                setValue: value => Config.EnableChestBrowser = value
            );

            configMenu.AddSectionTitle(
                mod: this.ModManifest,
                text: () => "Progression"
            );

            configMenu.AddBoolOption(
                mod: this.ModManifest,
                name: () => "Progression Gating",
                tooltip: () => "Require Community Center progress to unlock deliveries.\nComplete a bundle to earn Junimo delivery (free).\nJoja members get Shane's delivery (1% fee).\nWhen disabled, delivery is always available with no cost.",
                getValue: () => Config.EnableProgressionGating,
                setValue: value => Config.EnableProgressionGating = value
            );
        }

        private void ScanChests(string command, string[] args)
        {
            if (!Context.IsWorldReady)
            {
                this.Monitor.Log("Load a save first!", LogLevel.Warn);
                return;
            }

            var chests = ChestScanner.GetAllChests();
            this.Monitor.Log($"Found {chests.Count} chests across all locations:", LogLevel.Info);
            foreach (var info in chests)
            {
                var items = info.Chest.GetItemsForPlayer();
                this.Monitor.Log($"  {info.Label} ({info.LocationName}): {items.Count} item stacks", LogLevel.Info);
                foreach (var item in items)
                {
                    if (item != null)
                        this.Monitor.Log($"    {item.Stack}x {item.DisplayName} [{item.QualifiedItemId}]", LogLevel.Info);
                }
            }
        }
    }

    public interface IGenericModConfigMenuApi
    {
        void Register(IManifest mod, Action reset, Action save, bool titleScreenOnly = false);
        void AddSectionTitle(IManifest mod, Func<string> text, Func<string> tooltip = null);
        void AddBoolOption(IManifest mod, Func<bool> getValue, Action<bool> setValue, Func<string> name, Func<string> tooltip = null, string fieldId = null);
    }
}
