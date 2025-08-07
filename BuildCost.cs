using Facepunch;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using VLB;

namespace Oxide.Plugins {
    [Info("BuildCost", "Mattdokn", "1.0.0")]
    [Description("Calculates resource cost of bases.")]
    public class BuildCost : RustPlugin {
        Dictionary<uint, BuildingPrivlidge> cupboardCache = new Dictionary<uint, BuildingPrivlidge>();

        public static BuildCost Instance;
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
        [Command("cost")]
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
            // If player is already in a cost calculation session, update it to use the new cupboard
            player.GetOrAddComponent<BuildCostComponent>().InitializeCupboard(privilege);
            return;
            // Draw our sphere at the center
            //building.GetDominatingBuildingPrivilege().bounds.center
            //SphereEntity sphereEntity = GameManager.server.CreateEntity("assets/bundled/prefabs/modding/events/twitch/br_sphere.prefab", privilege.ServerPosition) as SphereEntity;
            //sphereEntity.lerpSpeed = 5f;
            //sphereEntity.lerpRadius = 50f;
            //sphereEntity.currentRadius = 50f;
            //sphereEntity.enabled = true;
            //sphereEntity.Spawn();
            // we have center, get all entities within 50m
            Stopwatch sw = Stopwatch.StartNew();
            PooledList<BuildingBlock> entities = Pool.Get<PooledList<BuildingBlock>>();
            BaseEntity.Query.Server.GetInSphere(privilege.ServerPosition, 50f, entities);
            player.ChatMessage(sw.Elapsed.TotalMilliseconds + "ms to get all entities in range.");
            sw.Restart();
            // Go through each and find building ids that arent our main tc
            uint mainTc = privilege.buildingID;
            HashSet<uint> toolCupboards = new HashSet<uint>() {
                mainTc
            };
            int entityCount = entities.Count;
            for (int i = 0; i < entityCount; i++) {
                if (entities[i].buildingID != mainTc) toolCupboards.Add(entities[i].buildingID);
            }

            entities.Clear();
            GetBuildingBlocks(toolCupboards, entities);
            player.ChatMessage(sw.Elapsed.TotalMilliseconds + "ms to get all connected buildings and their entities.");
            player.ChatMessage($"Found {entities.Count} building blocks accounting for {toolCupboards.Count} cupboards.");
            sw.Stop();


            // Calculate building cost
            // TODO optimize by making a lookup table for block definitions and their costs
            Dictionary<string, float> totalCosts = new Dictionary<string, float>();
            foreach (BuildingBlock block in entities) {
                if (block == null || !block.IsValid()) continue;
                if (block.blockDefinition == null) continue;
                List<ItemAmount> cost = block.BuildCost();
                // iterate over cost and add each item type count
                foreach (ItemAmount itemAmount in cost) {
                    if (itemAmount == null || itemAmount.amount <= 0) continue;
                    if (!totalCosts.ContainsKey(itemAmount.itemDef.shortname)) {
                        totalCosts[itemAmount.itemDef.shortname] = 0;
                    }
                    totalCosts[itemAmount.itemDef.shortname] += itemAmount.amount;
                }
            }

            Pool.Free(ref entities);

            // Send message containing cost breakdown to player
            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"Base cost for {entities.Count} building blocks:");
            foreach (var kvp in totalCosts) {
                // Convert shortname of item to real name
                ItemDefinition itemDef = ItemManager.FindItemDefinition(kvp.Key);
                if (itemDef == null) continue;
                sb.AppendLine($"{itemDef.displayName.english}: {kvp.Value}");
            }
            player.ChatMessage(sb.ToString());

        }
        #endregion

        #region Helpers

        void GetBuildingBlocks(IEnumerable<uint> buildingIds, List<BuildingBlock> buildingBlocks) {
            if (buildingBlocks == null) return;

            foreach (uint buildingId in buildingIds) {
                if (cupboardCache.TryGetValue(buildingId, out BuildingPrivlidge cupboard)) {
                    BuildingManager.Building building = cupboard.GetBuilding();
                    if (building != null) {
                        foreach (DecayEntity decayEntity in building.decayEntities) {
                            BuildingBlock block = decayEntity as BuildingBlock;
                            if (block == null) continue;
                            if (!buildingBlocks.Contains(block)) {
                                buildingBlocks.Add(block);
                            }
                        }
                    }
                }
            }
        }

        #endregion

        #region Build Cost Component
        class BuildCostComponent : MonoBehaviour {
            private BasePlayer player;
            private SphereEntity sphereEntity;
            private float buildingRadius = 50f;

            // Component added when we want to calculate build cost. TC should already be found before this is started.

            void Start() {
                player = GetComponent<BasePlayer>();
                // Setup UI here
                SetupUI();
                InvokeRepeating(nameof(UpdateUI), 1f, 1f);  //1s delay, repeat every 1s
            }

            void OnDestroy() {
                // Destroy UI here
                DestroyUI();
            }

            void Update() {
                if (player == null || !player.IsConnected) {
                    Destroy(this);
                    return;
                }
            }

            void SetupUI() {
                // Create UI components
            }

            void DestroyUI() {
                // Destroy any UI elements created
            }

            void UpdateUI() {
                // Update UI with current cost calculations
            }

            public void InitializeCupboard(BuildingPrivlidge cupboard) {
                // Setup sphere for the initial entity grab
                if (sphereEntity == null) {
                    sphereEntity = GameManager.server.CreateEntity("assets/bundled/prefabs/modding/events/twitch/br_sphere.prefab", cupboard.ServerPosition) as SphereEntity;
                    sphereEntity.lerpSpeed = 0f;
                    sphereEntity.currentRadius = buildingRadius;
                    sphereEntity.enabled = true;
                    sphereEntity.Spawn();
                } else {
                    // Update sphere position
                    sphereEntity.transform.position = cupboard.ServerPosition;
                }
            }

            void IncreaseRadius() {

            }

            void DecreaseRadius() {
            
            }
        }
        #endregion
    }
}
