#pragma warning disable IDE0051 // Remove unused private members

using Facepunch;
using Oxide.Core;
using Oxide.Core.Libraries.Covalence;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static Construction;
using static Oxide.Plugins.BuildTools;

namespace Oxide.Plugins {
    [Info("BuildTools", "Mattdokn", "1.0.0")]
    [Description("Tools to simplify building.")]
    public class BuildTools : RustPlugin {
        public const string USE_CREATIVE_CUPBOARD = "buildtools.creativecupboard";
        public const string USE_REMOVE_HAMMER = "buildtools.hammerremove";

        public const string USE_BGRADE = "buildtools.bgrade";
        public const string USE_SKINNED_GRADES = "buildtools.bgrade_skinned";

        public const float REPLENISH_CUPBOARD_INTERVAL = 60f;

        public List<BuildingPrivlidge> cupboards = new List<BuildingPrivlidge>();

        public Dictionary<ulong, PlayerSelectionData> playerSelections = new Dictionary<ulong, PlayerSelectionData>();

        Coroutine replenish;

        #region Hooks
        void Loaded() {
            Puts("Disabling upkeep!");
            RunSilentCommand("decay.upkeep", "false");

            permission.RegisterPermission(USE_CREATIVE_CUPBOARD, this);
            permission.RegisterPermission(USE_REMOVE_HAMMER, this);

            permission.RegisterPermission(USE_BGRADE, this);
            permission.RegisterPermission(USE_SKINNED_GRADES, this);

            // Go through active players and setup player data
            for (int i = 0; i < BasePlayer.activePlayerList.Count; i++) {
                OnPlayerConnected(BasePlayer.activePlayerList[i]);
            }

            // Go through all cupboards on server
            // Replace with tracked cupboards later
            cupboards = BaseNetworkable.serverEntities.OfType<BuildingPrivlidge>().Where((b) => {
                return permission.UserHasPermission(b.OwnerID.ToString(), USE_CREATIVE_CUPBOARD);
            }).ToList();

            replenish = ServerMgr.Instance.StartCoroutine(ReplenishCupboards());
        }

        void Unload() {
            ServerMgr.Instance.StopCoroutine(replenish);
        }

        void OnPlayerInput(BasePlayer player, InputState input) {
            var helditemName = player.GetActiveItem()?.info?.shortname;
            if (helditemName == null) return;

            // If it's a hammer
            if (IsHoldingHammer(player)) {
                // If we hit r
                if (input.WasJustPressed(BUTTON.RELOAD)) {
                    // Have perm
                    if (!permission.UserHasPermission(player.UserIDString, USE_REMOVE_HAMMER)) return;

                    // Send out raycast and kill entity
                    RaycastHit hit;
                    if (!Physics.Raycast(player.eyes.HeadRay(), out hit, 25f)) return;

                    DecayEntity hitEnt = hit.GetEntity() as DecayEntity;

                    if (hitEnt == null) return;
                    if (hitEnt.OwnerID != player.userID) return;

                    hitEnt.Kill();
                }
                if (input.WasJustPressed(BUTTON.FIRE_PRIMARY)) {
                    // Have perm
                    if (!permission.UserHasPermission(player.UserIDString, USE_BGRADE)) return;

                    PlayerSelectionData data;
                    if (!playerSelections.TryGetValue(player.userID, out data)) return;

                    // Send out raycast and kill entity
                    RaycastHit hit;
                    if (!Physics.Raycast(player.eyes.HeadRay(), out hit, 25f)) return;

                    BuildingBlock bb = hit.GetEntity() as BuildingBlock;

                    if (bb == null) return;
                    if (bb.OwnerID != player.userID) return;

                    UpdateBuildingBlock(bb, data.GetGrade(), data.GetCurrentSkin().skin, data.GetCurrentColor());
                }
            }
        }

        //OnServerCommand inventory.lighttoggle
        // TODO can we switch to this void since we never want to return non-null
        object OnServerCommand(ConsoleSystem.Arg arg) {
            if (arg.Connection == null) return null;


            var player = arg.Player();
            if (player == null) return null;

            PlayerSelectionData playerSelectionData = null;
            if (!playerSelections.TryGetValue(player.userID, out playerSelectionData)) {
                return null;
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

            return null;
        }

        // Detect when TC has been placed
        void OnEntitySpawned(BuildingPrivlidge cupboard) {
            var player = BasePlayer.FindByID(cupboard.OwnerID);
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

            playerSelections.Add(player.userID, new PlayerSelectionData());
        }
        // Unload player selection data
        void OnPlayerDisconnected(BasePlayer player, string reason) {
            if (!playerSelections.ContainsKey(player.userID)) {
                Puts($"Error! Player {player.UserIDString} : {player.displayName} does not exist within playerSelections loaded data!");
                return;
            }
            playerSelections.Remove(player.userID);
        }

        // When player places structure handle auto bgrade and skin
        private void OnEntityBuilt(Planner plan, GameObject gameObject) {
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

        // Upgrade for free, select skin here
        object OnPayForUpgrade(BasePlayer player, BuildingBlock block, ConstructionGrade gradeTarget) {
            PlayerSelectionData playerData;
            if (!playerSelections.TryGetValue(player.userID, out playerData)) return false;
            Interface.Oxide.NextTick(() => {
                playerData.SetSkin(block.grade, block.skinID);
            });
            return false;
        }
        // Place for free
        object OnPayForPlacement(BasePlayer player, Planner planner, Construction construction) {
            return false;
        }
        
        // Test below hooks!
        bool CanAffordToPlace(BasePlayer player, Planner planner, Construction construction) {
            return true;
        }

        void OnItemDeployed(Deployer deployer, BaseEntity entity, BaseEntity slotEntity) {
            Puts("OnItemDeployed works!");
        }


        bool CanDemolish(BasePlayer player, BuildingBlock block, BuildingGrade.Enum grade) {
            Puts("CanDemolish works!");
            return true;
        }

        #endregion

        #region Methods
        IEnumerator ReplenishCupboards() {
            while (true) {
                // Loop through all authed cupboards
                for (int i = 0; i < cupboards.Count; i++) {
                    var cb = cupboards[i];
                    cb.cachedProtectedMinutes = 50000f;
                    cb.nextProtectedCalcTime = Time.realtimeSinceStartup + 50000f;
                    cb.SendNetworkUpdateImmediate();
                    yield return CoroutineEx.waitForSeconds(REPLENISH_CUPBOARD_INTERVAL / cupboards.Count);
                }
                if (cupboards.Count == 0) yield return CoroutineEx.waitForSeconds(30f);
            }
        }

        private static ConsoleSystem.Arg RunSilentCommand(string strCommand, params object[] args) {
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
            bb.GetBuilding()?.Dirty();
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
        public class PlayerSelectionData {
            BuildingGrade.Enum selectedGrade = BuildingGrade.Enum.Twigs;
            Dictionary<BuildingGrade.Enum, BuildingSkin> selectedSkins;
            uint selectedColor = 1;
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
            }
            public void SetGrade(int grade) {
                selectedGrade = (BuildingGrade.Enum)grade;
            }
            public void IncrementGrade() {
                int g = (int)selectedGrade + 1;
                if (g >= (int)BuildingGrade.Enum.Count) g = 0;
                SetGrade(g);
            }
            public void DecrementGrade() {
                int g = (int)selectedGrade - 1;
                if (g <= 0) g = (int)BuildingGrade.Enum.TopTier;
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

        #endregion
    }
}
