namespace StardewDeliveryService
{
    /// <summary>Which entity provides the delivery service.</summary>
    public enum DeliveryProvider
    {
        /// <summary>Service not yet unlocked.</summary>
        None,

        /// <summary>Junimos deliver items for free (earned via Community Center bundles).</summary>
        Junimo,

        /// <summary>Shane delivers items for a fee (Joja route replacement).</summary>
        Shane
    }

    /// <summary>Per-save data tracking delivery service progression.</summary>
    public class DeliverySaveData
    {
        public DeliveryProvider Provider { get; set; } = DeliveryProvider.None;
        public bool UnlockCutscenePlayed { get; set; }
        public bool JojaCutscenePlayed { get; set; }
        public bool ShaneCutscenePlayed { get; set; }
    }
}
