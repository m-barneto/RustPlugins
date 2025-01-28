#pragma warning disable IDE0051 // Remove unused private members

using Facepunch;
using System.Collections.Generic;
using System.Linq;

namespace Oxide.Plugins {
    [Info("CreativeCupboards", "Mattdokn", "1.0.0")]
    [Description("No more managing upkeep in creative.")]
    public class CreativeCupboards : RustPlugin {
        public const string USE_CREATIVE_CUPBOARD = "creativecupboard.use";

        void OnServerInitialized() {
            // Go through all cupboards on server
            // Replace with tracked cupboards later
            var cupboards = BaseNetworkable.serverEntities.OfType<BuildingPrivlidge>().Where((b) => {
                return true;// covalence.Players.FindPlayerById(b.OwnerID.ToString()).HasPermission(USE_CREATIVE_CUPBOARD);
            }).ToArray();

            Puts($"Found {cupboards.Length} cupboards.");

            var upkeepCost = Pool.Get<List<ItemAmount>>();
            for (int i = 0; i < cupboards.Length; i++) {
                var c = cupboards[i];
                c.CalculateUpkeepCostAmounts(upkeepCost);
                for (int j = 0; j < upkeepCost.Count; j++) {
                    var itemAmount = upkeepCost[j];
                    Puts($"{itemAmount.itemDef.displayName.english}: {itemAmount.amount}");
                }
            }
            Pool.FreeUnmanaged(ref upkeepCost);
        }
    }
}
