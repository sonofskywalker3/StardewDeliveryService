namespace StardewDeliveryService
{
    public class ModConfig
    {
        /// <summary>Enable the chest browser icon on the inventory page.</summary>
        public bool EnableChestBrowser { get; set; } = true;

        /// <summary>Require Community Center progress to unlock the delivery service.
        /// When false, the service is always available with no cutscenes and no cost.</summary>
        public bool EnableProgressionGating { get; set; } = true;
    }
}
