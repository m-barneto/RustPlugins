#pragma warning disable IDE0051 // Remove unused private members

using Facepunch;
using Oxide.Core.Libraries.Covalence;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Oxide.Plugins {
    [Info("BuildTools", "Mattdokn", "1.0.0")]
    [Description("Tools to simplify building.")]
    public class BuildTools : RustPlugin {
        public const string USE_CREATIVE_CUPBOARD = "buildtools.creativecupboard";
        public const string USE_REMOVE_HAMMER = "buildtools.hammerremove";

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

        void OnPlayerInput(IPlayer iPlayer, InputState input) {
            if (input.IsDown(BUTTON.RELOAD)) {
                if (!(iPlayer).HasPermission(USE_REMOVE_HAMMER)) return;
                BasePlayer player = (BasePlayer)iPlayer;
                var helditemName = player.GetActiveItem()?.info?.shortname;
                if (helditemName == null) return;

                if (helditemName.Equals("hammer") || helditemName.Equals("toolgun")) {
                    // Send out raycast and kill entity
                    RaycastHit hit;
                    if (!Physics.Raycast(player.eyes.HeadRay(), out hit, 5f)) return;

                    DecayEntity hitEnt = hit.GetEntity() as DecayEntity;

                    if (hitEnt == null) return;
                    if (hitEnt.OwnerID != player.OwnerID) return;

                    hitEnt.Kill();
                }
            }
        }

        private void OnEntitySpawned(BuildingPrivlidge networkable) {
            //if (!(networkable is BuildingPrivlidge)) return;
            var cupboard = (BuildingPrivlidge)networkable;
            var player = BasePlayer.FindByID(cupboard.OwnerID);
            if (((IPlayer)player).HasPermission("RAG")) {

            }
        }
    }
}
