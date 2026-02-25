using System.Collections.Generic;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Locations;
using StardewValley.Objects;

namespace StardewDeliveryService
{
    /// <summary>Info about a discovered chest.</summary>
    internal record ChestInfo(Chest Chest, string Label, string LocationName);

    /// <summary>Scans all game locations for player chests.</summary>
    internal static class ChestScanner
    {
        internal static IMonitor Monitor;

        /// <summary>Get all browsable chests with labels, for the chest browser UI.</summary>
        public static List<ChestInfo> GetAllChests()
        {
            var results = new List<ChestInfo>();

            foreach (GameLocation location in Game1.locations)
            {
                string locName = location.DisplayName ?? location.Name;

                // Check fridge in farmhouse — only if kitchen upgrade purchased (upgradeLevel >= 1)
                if (location is FarmHouse farmHouse
                    && farmHouse.upgradeLevel >= 1
                    && farmHouse.fridge.Value is Chest fridge)
                {
                    results.Add(new ChestInfo(fridge, "Fridge", locName));
                }

                // Chests placed in this location
                AddChestsFromLocation(location, locName, results);

                // Chests inside buildings
                foreach (var building in location.buildings)
                {
                    GameLocation indoors = building.indoors.Value;
                    if (indoors == null)
                        continue;

                    string indoorName = indoors.DisplayName ?? indoors.Name ?? building.buildingType.Value;

                    if (indoors is FarmHouse indoorFarmHouse
                        && indoorFarmHouse.upgradeLevel >= 1
                        && indoorFarmHouse.fridge.Value is Chest indoorFridge)
                    {
                        results.Add(new ChestInfo(indoorFridge, "Fridge", indoorName));
                    }

                    AddChestsFromLocation(indoors, indoorName, results);
                }
            }

            return results;
        }

        private static void AddChestsFromLocation(GameLocation location, string locationName, List<ChestInfo> results)
        {
            foreach (var pair in location.objects.Pairs)
            {
                if (pair.Value is Chest chest && IsBrowsableChest(chest))
                {
                    string label = GetChestLabel(chest);
                    results.Add(new ChestInfo(chest, label, locationName));
                }
            }
        }

        private static bool IsBrowsableChest(Chest chest)
        {
            if (!chest.playerChest.Value)
                return false;

            if (chest.SpecialChestType == Chest.SpecialChestTypes.MiniShippingBin)
                return false;
            if (chest.SpecialChestType == Chest.SpecialChestTypes.JunimoChest)
                return false;

            return true;
        }

        private static string GetChestLabel(Chest chest)
        {
            if (!string.IsNullOrEmpty(chest.Name) && chest.Name != "Chest" && chest.Name != "Big Chest")
                return chest.Name;

            return chest.DisplayName ?? "Chest";
        }
    }
}
