#pragma warning disable IDE0051 // Remove unused private members

using Facepunch;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Libraries.Covalence;
using ProtoBuf;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security;
using UnityEngine;
using VLB;
using static BuildingBlock;
using static BuildingGrade;
using static Facepunch.Pool;

namespace Oxide.Plugins {
    [Info("BuildTools", "Mattdokn", "1.0.0")]
    [Description("Tools to simplify building.")]
    public class BuildTools : RustPlugin {
        public const string USE_CREATIVE_CUPBOARD = "buildtools.creativecupboard";
        public const string USE_REMOVE_HAMMER = "buildtools.hammerremove";
        public const string USE_BGRADE = "buildtools.bgrade";
        public const string BUILD_FOR_FREE = "buildtools.buildforfree";
        static bool initialised = false;
        public static BuildTools Instance;


        public Dictionary<ulong, PlayerSelectionData> playerSelections = new Dictionary<ulong, PlayerSelectionData>();

        DataFileSystem playerDataFileSystem;

        // Wipe cooldown time data
        private static DateTime Epoch = new DateTime(1970, 1, 1, 0, 0, 0);
        private static double CurrentTime => DateTime.UtcNow.Subtract(Epoch).TotalSeconds;

        #region Hooks
        void Init() {
            Instance = this;
            Unsubscribe(nameof(OnEntitySpawned));
        }

        void OnServerInitialized(bool initial) {
            initialised = true;
            Subscribe(nameof(OnEntitySpawned));
        }

        void Loaded() {
            Puts("Disabling upkeep!");
            RunSilentCommand("decay.upkeep", "false");

            permission.RegisterPermission(USE_CREATIVE_CUPBOARD, this);
            permission.RegisterPermission(USE_REMOVE_HAMMER, this);

            permission.RegisterPermission(USE_BGRADE, this);
            permission.RegisterPermission(BUILD_FOR_FREE, this);

            // Setup our data file system
            playerDataFileSystem = new DataFileSystem($"{Interface.Oxide.DataDirectory}\\{Name}");

            // Go through active players and setup player data
            for (int i = 0; i < BasePlayer.activePlayerList.Count; i++) {
                OnPlayerConnected(BasePlayer.activePlayerList[i]);
            }
        }


        // Save player data for all connected players
        void Unload() => SaveData();
        void OnServerSave() => SaveData();

        // Handles our conditions for watching player input
        void OnActiveItemChanged(BasePlayer player, Item oldItem, Item newItem) {
            if (player == null || player.IsNpc || newItem == null) return;

            if (newItem.info.shortname.Equals("hammer") || newItem.info.shortname.Equals("toolgun") || newItem.info.shortname.Equals("building.planner")) {
                player.GetOrAddComponent<PlayerInputMonitor>();
            }
        }

        // inventory.lighttoggle & client.selectedshippingcontainerblockcolour
        // Handle when user presses F to change bgrade level as well as updating selected shipping container color
        void OnServerCommand(ConsoleSystem.Arg arg) {
            if (arg.Connection == null) return;


            var player = arg.Player();
            if (player == null) return;

            PlayerSelectionData playerSelectionData = null;
            if (!playerSelections.TryGetValue(player.userID, out playerSelectionData)) {
                return;
            }


            if (arg.cmd.FullName.Equals("inventory.lighttoggle")) {
                // If holding hammer
                if (IsHoldingHammer(player)) {
                    if (player.serverInput.IsDown(BUTTON.DUCK) || player.serverInput.IsDown(BUTTON.SPRINT)) {
                        // Go down a grade
                        playerSelectionData.DecrementGrade();
                    } else {
                        //Go up a grade
                        playerSelectionData.IncrementGrade();
                    }
                }
            } else if (arg.cmd.FullName.Equals("global.setinfo") && arg.HasArg("client.selectedshippingcontainerblockcolour")) {
                playerSelectionData.SetColor((uint)arg.GetInt(1, 1));
            }

            return;
        }

        // Detect when TC has been placed
        void OnEntitySpawned(BuildingPrivlidge cupboard) {
            BasePlayer player = BasePlayer.FindByID(cupboard.OwnerID);
            if (!player) {
                return;
            }
            if (!permission.UserHasPermission(player.UserIDString, USE_CREATIVE_CUPBOARD)) return;
            // Authed user placed cupboard set it up
            cupboard.inventory.Clear();
            cupboard.inventory.AddItem(ItemManager.FindItemDefinition("wood"), 999999, 0, ItemContainer.LimitStack.None);
            cupboard.inventory.AddItem(ItemManager.FindItemDefinition("stones"), 999999, 0, ItemContainer.LimitStack.None);
            cupboard.inventory.AddItem(ItemManager.FindItemDefinition("metal.fragments"), 999999, 0, ItemContainer.LimitStack.None);
            cupboard.inventory.AddItem(ItemManager.FindItemDefinition("metal.refined"), 999999, 0, ItemContainer.LimitStack.None);
            cupboard.inventory.SetLocked(true);
        }

        // Setup player selection data
        void OnPlayerConnected(BasePlayer player) {
            if (playerSelections.ContainsKey(player.userID)) {
                Puts($"Error! Player {player.UserIDString} : {player.displayName} already exists within playerSelections loaded data!");
                return;
            }

            PlayerSelectionData data = LoadPlayerData(player);
            if (data == null) {
                Puts($"Error loading data for player {player.UserIDString} : {player.displayName}");
                return;
            }

            playerSelections.Add(player.userID, data);
        }
        // Unload and Save player selection data
        void OnPlayerDisconnected(BasePlayer player, string reason) {
            if (!playerSelections.ContainsKey(player.userID)) {
                Puts($"Error! Player {player.UserIDString} : {player.displayName} does not exist within playerSelections loaded data!");
                return;
            }

            SavePlayerData(player);

            playerSelections.Remove(player.userID);
        }


        // When player places structure handle auto bgrade and skin
        void OnEntityBuilt(Planner plan, GameObject gameObject) {
            var player = plan?.GetOwnerPlayer();

            if (player == null) return;
            if (plan.isTypeDeployable) return;
            if (!permission.UserHasPermission(player.UserIDString, USE_BGRADE)) return;

            var buildingBlock = gameObject.GetComponent<BuildingBlock>();
            if (buildingBlock == null) return;

            if (!player.CanBuild()) return;

            PlayerSelectionData playerData;
            if (!playerSelections.TryGetValue(player.userID, out playerData)) {
                return;
            }

            BuildingGrade.Enum grade = playerData.GetGrade();

            if (Interface.Call("OnStructureUpgrade", buildingBlock, player, grade) != null) {
                return;
            }

            UpdateBuildingBlock(buildingBlock, grade, playerData.GetCurrentSkin().skin, playerData.GetCurrentColor());
        }

        // Upgrade building blocks for free, update selected skin here
        object OnPayForUpgrade(BasePlayer player, BuildingBlock block, ConstructionGrade gradeTarget) {
            PlayerSelectionData playerData;
            if (!playerSelections.TryGetValue(player.userID, out playerData)) return false;
            Interface.Oxide.NextTick(() => {
                playerData.SetSkin(block.grade, block.skinID);
            });
            return false;
        }

        // Place deployables for free
        object OnPayForPlacement(BasePlayer player, Planner planner, Construction construction) {
            return false;
        }

        // Teleport to map marker hook
        object OnMapMarkerAdd(BasePlayer player, MapNote note) {
            if (player == null || note == null || player.GetActiveItem() == null) return null;

            if (player.GetActiveItem().info.shortname.Equals("map")) {
                var pos = note.worldPosition;
                pos.y = TerrainMeta.HeightMap.GetHeight(pos);
                RaycastHit hitInfo;

                if (Physics.Raycast(
                    new Vector3(pos.x, pos.y + 200f, pos.z),
                    Vector3.down,
                    out hitInfo,
                    float.MaxValue,
                    LayerMask.GetMask("Water", "Solid"))) {

                    pos.y = Mathf.Max(hitInfo.point.y, pos.y);
                }

                player.Teleport(pos);
                player.RemoveFromTriggers();
                player.ForceUpdateTriggers();
                return false;
            }
            return null;
        }
        #endregion

        #region Methods
        static ConsoleSystem.Arg RunSilentCommand(string strCommand, params object[] args) {
            var command = ConsoleSystem.BuildCommand(strCommand, args);
            var arg = new ConsoleSystem.Arg(ConsoleSystem.Option.Unrestricted, command);
            if (arg.Invalid || !arg.cmd.Variable) return null;

            var oldArgs = ConsoleSystem.CurrentArgs;
            ConsoleSystem.CurrentArgs = arg;
            arg.cmd.Call(arg);
            ConsoleSystem.CurrentArgs = oldArgs;
            return arg;
        }

        bool IsHoldingHammer(BasePlayer player) {
            var helditemName = player.GetActiveItem()?.info?.shortname;
            if (helditemName == null) return false;

            // If it's a hammer
            return helditemName.Equals("hammer") || helditemName.Equals("toolgun");

        }

        void UpdateBuildingBlock(BuildingBlock bb, BuildingGrade.Enum grade, ulong skin = 0, uint color = 0) {
            bb.ChangeGradeAndSkin(grade, skin, true);
            bb.SetCustomColour(color);
            bb.SetHealthToMax();
            bb.StartBeingRotatable();
            bb.SendNetworkUpdate();
            bb.UpdateSkin();
            bb.ResetUpkeepTime();

            bb.CancelInvoke(bb.StopBeingRotatable);
            bb.CancelInvoke(bb.StopBeingDemolishable);

            bb.SetFlag(BlockFlags.CanRotate, true);
            bb.SetFlag(StabilityEntity.DemolishFlag, true);

            bb.GetBuilding()?.Dirty();
        }

        void SaveData() {
            // Go through active players and save player data
            BasePlayer player;
            for (int i = 0; i < BasePlayer.activePlayerList.Count; i++) {
                player = BasePlayer.activePlayerList[i];
                if (!playerSelections.ContainsKey(player.userID)) {
                    Puts($"Error! Player {player.UserIDString} : {player.displayName} does not exist within playerSelections loaded data!");
                    return;
                }

                SavePlayerData(player);
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
            player.ChatMessage($"Found {building.buildingPrivileges.Count} tool cupboards. Blocks: {entities.Count}");
            Pool.Free(ref entities);
        }
        #endregion

        #region BGradeSkins
        public class BuildingSkin {
            public string name;
            public ulong skin;
            public BuildingSkin(string name, ulong skin) {
                this.name = name;
                this.skin = skin;
            }
        }
        static Dictionary<BuildingGrade.Enum, List<BuildingSkin>> BGradeSkins = new Dictionary<BuildingGrade.Enum, List<BuildingSkin>>() {
            {BuildingGrade.Enum.Twigs, new List<BuildingSkin>() {
                new BuildingSkin("Twig", 0)
            } },
            {BuildingGrade.Enum.Wood, new List<BuildingSkin>() {
                new BuildingSkin("Wood", 0),
                new BuildingSkin("Frontier", 10232),
                new BuildingSkin("Gingerbread", 2)
            } },
            {BuildingGrade.Enum.Stone, new List<BuildingSkin>() {
                new BuildingSkin("Stone", 0),
                new BuildingSkin("Adobe", 10220),
                new BuildingSkin("Brick", 10223),
                new BuildingSkin("Brutalist", 10225)
            } },
            {BuildingGrade.Enum.Metal, new List<BuildingSkin>() {
                new BuildingSkin("Metal", 0),
                new BuildingSkin("Container", 10221)
            } },
            {BuildingGrade.Enum.TopTier, new List<BuildingSkin>() {
                new BuildingSkin("HQM", 0)
            } },
        };
        #endregion

        #region Player Selections
        [JsonObject(MemberSerialization.OptIn)]
        public class PlayerSelectionData {
            [JsonProperty]
            BuildingGrade.Enum selectedGrade = BuildingGrade.Enum.Twigs;
            [JsonProperty]
            uint selectedColor = 1;
            [JsonProperty]
            Dictionary<BuildingGrade.Enum, BuildingSkin> selectedSkins;

            [JsonProperty]
            public double lastUpdatedTime = CurrentTime;

            public bool changed = false;

            public PlayerSelectionData() {
                selectedSkins = new Dictionary<BuildingGrade.Enum, BuildingSkin>() {
                    { BuildingGrade.Enum.Twigs, BGradeSkins[BuildingGrade.Enum.Twigs][0] },
                    { BuildingGrade.Enum.Wood, BGradeSkins[BuildingGrade.Enum.Wood][0] },
                    { BuildingGrade.Enum.Stone, BGradeSkins[BuildingGrade.Enum.Stone][0] },
                    { BuildingGrade.Enum.Metal, BGradeSkins[BuildingGrade.Enum.Metal][0] },
                    { BuildingGrade.Enum.TopTier, BGradeSkins[BuildingGrade.Enum.TopTier][0] },
                };
            }
            public void SetGrade(BuildingGrade.Enum grade) {
                selectedGrade = grade;
                changed = true;
            }
            public void SetGrade(int grade) {
                selectedGrade = (BuildingGrade.Enum)grade;
                changed = true;
            }
            public void IncrementGrade() {
                int g = (int)selectedGrade + 1;
                if (g >= (int)BuildingGrade.Enum.Count) g = (int)BuildingGrade.Enum.TopTier;
                SetGrade(g);
            }
            public void DecrementGrade() {
                int g = (int)selectedGrade - 1;
                if (g <= 0) g = (int)BuildingGrade.Enum.Twigs;
                SetGrade(g);
            }
            public BuildingGrade.Enum GetGrade() {
                return selectedGrade;
            }
            public bool SetSkin(BuildingGrade.Enum grade, ulong skin) {
                var buildingSkin = BGradeSkins[grade].Where((s) => s.skin == skin).First();
                if (buildingSkin == null) {
                    return false;
                }
                selectedSkins[grade] = buildingSkin;
                changed = true;
                return true;
            }
            public BuildingSkin GetSkin(BuildingGrade.Enum grade) {
                return selectedSkins[grade];
            }
            public BuildingSkin GetCurrentSkin() {
                return GetSkin(selectedGrade);
            }
            public bool SetColor(uint color) {
                if (color <= 0 || color > 16) return false;
                selectedColor = color;
                changed = true;
                return true;
            }
            public uint GetColor() {
                return selectedColor;
            }
            public uint GetCurrentColor() {
                if (!selectedSkins[selectedGrade].name.Equals("Container")) return 0;
                return selectedColor;
            }
        }
        #endregion

        #region Data
        void SavePlayerData(BasePlayer player) {
            // Make sure the player data is different from the default
            PlayerSelectionData playerData;
            if (!playerSelections.TryGetValue(player.userID, out playerData)) return;

            if (!playerData.changed) return;

            playerData.lastUpdatedTime = CurrentTime;

            // Save data to our filesystem
            playerDataFileSystem.WriteObject($"{player.UserIDString}", playerData);
        }
        PlayerSelectionData LoadPlayerData(BasePlayer player) {
            // If the player doesn't have a file, create a new PlayerData instance for them
            if (!playerDataFileSystem.ExistsDatafile(player.UserIDString)) {
                return new PlayerSelectionData();
            }
            // Otherwise, return the loaded file
            PlayerSelectionData data = playerDataFileSystem.ReadObject<PlayerSelectionData>($"{player.UserIDString}");
            return data;
        }
        #endregion

        #region InputComponent
        class PlayerInputMonitor : MonoBehaviour {
            private BasePlayer player;
            void Start() {
                player = GetComponent<BasePlayer>();
            }
            void Update() {
                if (player == null || !player.IsConnected || player.GetActiveItem() == null) {
                    Destroy(this);
                    return;
                }
                string activeItemName = player.GetActiveItem().info.shortname;
                if (activeItemName != "hammer" && activeItemName != "toolgun" && activeItemName != "building.planner") {
                    Destroy(this);
                    return;
                }


                if (player.serverInput.WasJustPressed(BUTTON.RELOAD)) {
                    if (Instance.IsHoldingHammer(player)) {
                        // Have perm
                        if (!Instance.permission.UserHasPermission(player.UserIDString, USE_REMOVE_HAMMER)) return;

                        // Send out raycast and kill entity
                        RaycastHit hit;
                        if (!Physics.Raycast(player.eyes.HeadRay(), out hit, 25f)) return;

                        DecayEntity hitEnt = hit.GetEntity() as DecayEntity;

                        if (hitEnt == null) return;
                        if (hitEnt.OwnerID != player.userID) return;

                        hitEnt.Kill();
                    }
                }
                if (player.serverInput.IsDown(BUTTON.FIRE_SECONDARY)) {
                    // Have perm
                    if (!Instance.permission.UserHasPermission(player.UserIDString, USE_BGRADE)) return;

                    PlayerSelectionData data;
                    if (!Instance.playerSelections.TryGetValue(player.userID, out data)) return;

                    // Send out raycast and kill entity
                    RaycastHit hit;
                    if (!Physics.Raycast(player.eyes.HeadRay(), out hit, 25f)) return;
                    if (hit.distance <= 2f + float.Epsilon) return;

                    BuildingBlock bb = hit.GetEntity() as BuildingBlock;

                    if (bb == null) return;
                    if (bb.OwnerID != player.userID) return;

                    Instance.UpdateBuildingBlock(bb, data.GetGrade(), data.GetCurrentSkin().skin, data.GetCurrentColor());
                }
            }
        }
        #endregion

        #region CalculationSphere

        #endregion
    }
}
