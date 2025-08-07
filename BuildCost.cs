using Facepunch;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Oxide.Plugins {
    [Info("BuildCost", "Mattdokn", "1.0.0")]
    [Description("Calculates resource cost of bases.")]
    public class BuildCost : RustPlugin {
        Dictionary<uint, BuildingPrivlidge> cupboardCache = new Dictionary<uint, BuildingPrivlidge>();
        #region Hooks
        void Init() {
            Unsubscribe(nameof(OnEntitySpawned));
            Unsubscribe(nameof(OnEntityKill));
        }

        void OnServerInitialized(bool initial) {
            // Get all cupboards on server and add them to our cache
            IEnumerable<BuildingPrivlidge> cupboards = BaseNetworkable.serverEntities
                .OfType<BuildingPrivlidge>();
            foreach (BuildingPrivlidge cupboard in cupboards) {
                cupboardCache[cupboard.buildingID] = cupboard;
            }

            Subscribe(nameof(OnEntitySpawned));
            Subscribe(nameof(OnEntityKill));
        }

        void OnEntitySpawned(BuildingPrivlidge cupboard) {
            Puts("Spawned cupboard");
            // Add the new cupboard to our cache
            if (!cupboardCache.ContainsKey(cupboard.buildingID)) {
                cupboardCache[cupboard.buildingID] = cupboard;
            }
        }
        void OnEntityKill(BuildingPrivlidge cupboard) {
            Puts("Killed cupboard");
            // Remove the cupboard from our cache
            if (cupboardCache.ContainsKey(cupboard.buildingID)) {
                cupboardCache.Remove(cupboard.buildingID);
            }
        }
        #endregion
        #region Commands
        [Command("calc")]
        private void CalculateCost(BasePlayer player, string command, string[] args) {
            if (player.GetBuildingPrivilege() == null) {
                player.ChatMessage("Could not find a nearby tool cupboard!");
                return;
            }
            BuildingPrivlidge privilege = player.GetBuildingPrivilege();
            BuildingManager.Building building = privilege.GetBuilding();

            if (building == null) {
                player.ChatMessage("No building attached to tool cupboard.");
                return;
            }
            // Draw our sphere at the center
            //building.GetDominatingBuildingPrivilege().bounds.center
            //SphereEntity sphereEntity = GameManager.server.CreateEntity("assets/bundled/prefabs/modding/events/twitch/br_sphere.prefab", privilege.ServerPosition) as SphereEntity;
            //sphereEntity.lerpSpeed = 5f;
            //sphereEntity.lerpRadius = 50f;
            //sphereEntity.currentRadius = 50f;
            //sphereEntity.enabled = true;
            //sphereEntity.Spawn();
            // we have center, get all entities within 50m
            PooledList<BuildingBlock> entities = Pool.Get<PooledList<BuildingBlock>>();
            BaseEntity.Query.Server.GetInSphere(privilege.ServerPosition, 50f, entities);
            // Go through each and find building ids that arent our main tc
            uint mainTc = privilege.buildingID;
            HashSet<uint> externalTcs = new HashSet<uint>();
            int entityCount = entities.Count;
            for (int i = 0; i < entityCount; i++) {
                if (entities[i].buildingID != mainTc) externalTcs.Add(entities[i].buildingID);
            }
            player.ChatMessage($"Found {externalTcs.Count + 1} tool cupboards. Blocks: {entities.Count}");
            Pool.Free(ref entities);
        }
        #endregion
    }
}
