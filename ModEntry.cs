using System;
using Microsoft.Xna.Framework;
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
            helper.Events.Display.MenuChanged += this.OnMenuChanged;
        }

        private void OnGameLaunched(object sender, GameLaunchedEventArgs e)
        {
            RegisterConfigMenu();
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
        void AddBoolOption(IManifest mod, Func<bool> getValue, Action<bool> setValue, Func<string> name, Func<string> tooltip = null, string fieldId = null);
    }
}
