#pragma warning disable IDE0051 // Remove unused private members

using Facepunch;
using Oxide.Core.Libraries.Covalence;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Oxide.Plugins {
    [Info("BuildTools", "Mattdokn", "1.0.0")]
    [Description("Tools to simplify building.")]
    public class BuildTools : RustPlugin {
        public const string USE_CREATIVE_CUPBOARD = "buildtools.creativecupboard";
        public const string USE_REMOVE_HAMMER = "buildtools.hammerremove";

        public const float REPLENISH_CUPBOARD_INTERVAL = 60f;

        public List<BuildingPrivlidge> cupboards = new List<BuildingPrivlidge>();

        Coroutine replenish;

        #region Hooks
        void Loaded() {
            Puts("Disabling upkeep!");
            RunSilentCommand("decay.upkeep", "false");

            permission.RegisterPermission(USE_CREATIVE_CUPBOARD, this);
            permission.RegisterPermission(USE_REMOVE_HAMMER, this);

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
            }
        }

        //OnServerCommand inventory.lighttoggle
        object OnServerCommand(ConsoleSystem.Arg arg) {
            if (arg.Connection == null || arg.cmd.FullName != "inventory.lighttoggle") {
                return null;
            }


            var player = arg.Player();
            if (player == null) return null;

            // If holding hammer
            if (IsHoldingHammer(player)) {
                if (player.serverInput.IsDown(BUTTON.DUCK) || player.serverInput.IsDown(BUTTON.SPRINT)) {
                    // Go down a grade
                    Puts("Down");
                } else {
                    //Go up a grade
                    Puts("Up");
                }
            }
            return null;
        }

        private void OnEntitySpawned(BuildingPrivlidge cupboard) {
            var player = BasePlayer.FindByID(cupboard.OwnerID);
            if (!permission.UserHasPermission(player.UserIDString, USE_CREATIVE_CUPBOARD)) return;
            // Authed user placed cupboard, add it to iterated list
            cupboards.Add(cupboard);
            timer.Once(2f, () => {
                cupboard.cachedProtectedMinutes = 50000f;
                cupboard.nextProtectedCalcTime = Time.realtimeSinceStartup + 50000f;
                cupboard.SendNetworkUpdateImmediate();
            });

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

        /// <summary>
        ///     Run a console command without any output into the console
        /// </summary>
        /// <param name="strCommand">Target command</param>
        /// <param name="args">Target args</param>
        /// <returns>A <see cref="ConsoleSystem.Arg" /> with the outcome of the command</returns>
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

        #endregion
    }
}
