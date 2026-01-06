using FFVI_ScreenReader.Core;

namespace FFVI_ScreenReader.Field
{
    /// <summary>
    /// Represents a vehicle (airship, chocobo, etc.)
    /// </summary>
    public class VehicleEntity : NavigableEntity
    {
        /// <summary>
        /// Transportation type ID
        /// </summary>
        public int TransportationId { get; set; }

        public override EntityCategory Category => EntityCategory.Vehicles;

        public override int Priority => 10;

        public override bool BlocksPathing => false;

        protected override string GetDisplayName()
        {
            return GetVehicleName(TransportationId);
        }

        protected override string GetEntityTypeName()
        {
            return "Vehicle";
        }

        public static string GetVehicleName(int id)
        {
            // Based on MapConstants.TransportationType enum
            switch (id)
            {
                case 1: return "Player";
                case 2: return "Ship";
                case 3: return "Airship";
                case 4: return "Symbol";
                case 5: return "Content";
                case 6: return "Submarine";
                case 7: return "Low Flying Airship";
                case 8: return "Special Airship";
                case 9: return "Yellow Chocobo";
                case 10: return "Black Chocobo";
                case 11: return "Boko";
                case 12: return "Magical Armor";
                default: return $"Vehicle {id}";
            }
        }
    }
}
