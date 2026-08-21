using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace BalanceAndVarietyRework
{
    [BepInPlugin("com.Draken0015.BVR", "Balance and Variety Rework", BaseVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string BaseVersion = "1.0.9";

        // Expose the dynamically generated version hash for multiplayer desync checks
        public static string FullVersionWithHash { get; private set; }

        // ==========================================================================
        // Configuration Entries
        // ==========================================================================

        // Missile Balance Entries

        // IR Missiles Buff Entries
        public static ConfigEntry<bool> EnableIRMissilesBuff;
        public static ConfigEntry<float> FlareCountMultiplier;
        public static ConfigEntry<float> FlareRejectionMultiplier;

        // R9 / RAM45 SARH Lock Persistence Entries
        public static ConfigEntry<bool> EnableR9LockPersistenceBuff;
        public static ConfigEntry<float> R9LockPersistenceValue;
        public static ConfigEntry<bool> EnableRAM45LockPersistenceBuff;
        public static ConfigEntry<float> RAM45LockPersistenceValue;

        // R9 / RAM45 SARH Relock Entries
        public static ConfigEntry<bool> EnableR9SARHRelock;
        public static ConfigEntry<float> R9SARHRelockDelay;
        public static ConfigEntry<int> R9SARHRelockAttempts;

        public static ConfigEntry<bool> EnableRAM45SARHRelock;
        public static ConfigEntry<float> RAM45SARHRelockDelay;
        public static ConfigEntry<int> RAM45SARHRelockAttempts;

        // Cricket Balance Entries
        public static ConfigEntry<bool> EnableCricketLynchpinx14Double;
        public static ConfigEntry<bool> EnableCricketKingpinx8Double;

        // Compass Balance Entries
        public static ConfigEntry<bool> EnableCompassLynchpinx14Double;
        public static ConfigEntry<bool> EnableCompassKingpinx8Double;

        // Vagrant Balance Entries
        public static ConfigEntry<bool> EnableVagrantLynchpinx14Double;
        public static ConfigEntry<bool> EnableVagrantKingpinx8Double;

        // Ibis Balance Entries
        public static ConfigEntry<bool> EnableIbisLynchpinx14Double;
        public static ConfigEntry<bool> EnableIbisKingpinx8Double;

        // Chicane Balance Entries
        public static ConfigEntry<bool> EnableChicaneProxyGun;
        public static ConfigEntry<bool> EnableChicaneScythesSingle;
        public static ConfigEntry<bool> EnableChicaneScythesDouble;
        public static ConfigEntry<bool> EnableChicaneInternalLynchpinx14;
        public static ConfigEntry<bool> EnableChicaneInternalKingpinx8;
        public static ConfigEntry<bool> EnableChicaneBayPylonSymmetryFix;

        // Revoker Balance Entries
        public static ConfigEntry<bool> EnableRevokerLynchpinx14Double;
        public static ConfigEntry<bool> EnableRevokerKingpinx8Double;

        // Vortex Balance Entries
        public static ConfigEntry<bool> EnableVortexLynchpinx14Double;
        public static ConfigEntry<bool> EnableVortexKingpinx8Double;

        // Tarantula Balance Entries
        public static ConfigEntry<bool> EnableTarantulaLynchpinx14Double;
        public static ConfigEntry<bool> EnableTarantulaKingpinx8Double;

        // Ifrit Balance Entries
        public static ConfigEntry<bool> EnableIfritLynchpinx14Double;
        public static ConfigEntry<bool> EnableIfritKingpinx8Double;

        // Medusa Balance Entries
        public static ConfigEntry<bool> EnableMedusaLaserBuff;
        public static ConfigEntry<float> MedusaLaserPowerDraw;
        public static ConfigEntry<bool> EnableMedusaLynchpinx14Double;
        public static ConfigEntry<bool> EnableMedusaKingpinx8Double;
        public static ConfigEntry<bool> EnableMedusaSAMRadar2Single;
        public static ConfigEntry<bool> EnableMedusaSAMRadar2Double;

        private void Awake()
        {
            // 1. Bind notices FIRST to guarantee they appear at the top of the ConfigManager window
            Config.Bind("Important Notices", "Restart Required", "Changes made here require a full game restart to apply.",
                new ConfigDescription("Please restart the game after changing any settings for them to take effect.", null,
                new ConfigurationManagerAttributes { ReadOnly = true, HideDefaultButton = true, Order = 100 }));

            var hashDisplay = Config.Bind("Important Notices", "Current Config Hash", "Calculating...",
                new ConfigDescription("Compare this 6-character hash with other players to ensure your settings match for multiplayer. (Updates on game restart)", null,
                new ConfigurationManagerAttributes { ReadOnly = true, HideDefaultButton = true, Order = 99 }));

            // 2. Bind functional configs

            // Missile Balance Changes

            // IR Missiles Buff
            EnableIRMissilesBuff = Config.Bind("Missile Balance Changes", "Enable IR Missiles Buff", true, "Master toggle to enable the custom flare rejection and flare count multipliers.");
            FlareCountMultiplier = Config.Bind("Missile Balance Changes", "Flare Count Multiplier", 2.0f, "Multiplies the total number of flares on all aircraft (e.g., 2.0 = double flares, 0.5 = half flares).");
            FlareRejectionMultiplier = Config.Bind("Missile Balance Changes", "Flare Rejection Multiplier", 2.0f, "Multiplies the flare rejection stat on all IR missiles. Higher values make them harder to decoy (e.g., 2.0 = double rejection).");

            // R9 Lock Persistence Change
            EnableR9LockPersistenceBuff = Config.Bind("Missile Balance Changes", "Enable R9 Lock Persistence Buff", true, "Master toggle to enable the custom R9 lock persistence value.");
            R9LockPersistenceValue = Config.Bind("Missile Balance Changes", "R9 Lock Persistence Value", 3.0f, "Sets the lock persistence duration for the R9's SARH seeker, measured in seconds. Higher values keep the lock active for longer after the target sucessfully jams or is obscured. 600 effectively makes it relock infinitely.");

            // RAM45 Lock Persistence Change
            EnableRAM45LockPersistenceBuff = Config.Bind("Missile Balance Changes", "Enable RAM45 Lock Persistence Buff", true, "Master toggle to enable the custom RAM45 lock persistence value.");
            RAM45LockPersistenceValue = Config.Bind("Missile Balance Changes", "RAM45 Lock Persistence Value", 3.0f, "Sets the lock persistence duration for the RAM45's SARH seeker, measured in seconds. Higher values keep the lock active for longer after the target sucessfully jams or is obscured. 600 effectively makes it relock infinitely.");

            // R9 SARH Relock Change
            EnableR9SARHRelock = Config.Bind("Missile Balance Changes", "Enable R9 SARH Relock", true,
                "Master toggle to enable automatic R9 SARH relock attempts after lockPersistence allows the seeker to drop its target.");

            R9SARHRelockDelay = Config.Bind("Missile Balance Changes", "R9 SARH Relock Delay", 3.0f,
                "Seconds the R9 waits after it is left without a lock before attempting to relock. This timer starts after lockPersistence expires.");

            R9SARHRelockAttempts = Config.Bind("Missile Balance Changes", "R9 SARH Relock Attempts", 0,
                "Number of R9 relock attempts. 0 = infinite attempts.");

            // RAM45 SARH Relock Change
            EnableRAM45SARHRelock = Config.Bind("Missile Balance Changes", "Enable RAM45 SARH Relock", true,
                "Master toggle to enable automatic RAM45 SARH relock attempts after lockPersistence allows the seeker to drop its target.");

            RAM45SARHRelockDelay = Config.Bind("Missile Balance Changes", "RAM45 SARH Relock Delay", 3.0f,
                "Seconds the RAM45 waits after it is left without a lock before attempting to relock. This timer starts after lockPersistence expires.");

            RAM45SARHRelockAttempts = Config.Bind("Missile Balance Changes", "RAM45 SARH Relock Attempts", 0,
                "Number of RAM45 relock attempts. 0 = infinite attempts.");

            // Cricket Changes
            EnableCricketLynchpinx14Double = Config.Bind("CI-22 Cricket Changes", "Enable Cricket Lynchpin x14 Double", true, "Enables the AGR-18 Lynchpin x14 double rocket pod on the Cricket's hardpoint sets 2 and 3.");
            EnableCricketKingpinx8Double = Config.Bind("CI-22 Cricket Changes", "Enable Cricket Kingpin x8 Double", true, "Enables the AGR-24 Kingpin x8 double rocket pod on the Cricket's hardpoint sets 2 and 3.");

            // Compass Changes
            EnableCompassLynchpinx14Double = Config.Bind("T/A-30 Compass Changes", "Enable Compass Lynchpin x14 Double", true, "Enables the AGR-18 Lynchpin x14 double rocket pod on the Compass's hardpoint set 1.");
            EnableCompassKingpinx8Double = Config.Bind("T/A-30 Compass Changes", "Enable Compass Kingpin x8 Double", true, "Enables the AGR-24 Kingpin x8 double rocket pod on the Compass's hardpoint set 1.");

            // Vagrant Changes
            EnableVagrantLynchpinx14Double = Config.Bind("VT-7 Vagrant Changes", "Enable Vagrant Lynchpin x14 Double", true, "Enables the AGR-18 Lynchpin x14 double rocket pod on the Vagrant's hardpoint set 2.");
            EnableVagrantKingpinx8Double = Config.Bind("VT-7 Vagrant Changes", "Enable Vagrant Kingpin x8 Double", true, "Enables the AGR-24 Kingpin x8 double rocket pod on the Vagrant's hardpoint set 2.");

            // Ibis Changes
            EnableIbisLynchpinx14Double = Config.Bind("UH-90 Ibis Changes", "Enable Ibis Lynchpin x14 Double", true, "Enables the AGR-18 Lynchpin x14 double rocket pod on the Ibis's hardpoint sets 0 and 1.");
            EnableIbisKingpinx8Double = Config.Bind("UH-90 Ibis Changes", "Enable Ibis Kingpin x8 Double", true, "Enables the AGR-24 Kingpin x8 double rocket pod on the Ibis's hardpoint sets 0 and 1.");

            // Chicane Changes
            EnableChicaneProxyGun = Config.Bind("SAH-46 Chicane Changes", "Enable Chicane Proximity Fuse 30mm Gun", true, "Enables the proximity fuse on the Chicane's nosegun.");
            EnableChicaneScythesSingle = Config.Bind("SAH-46 Chicane Changes", "Enable Chicane Inner Wing Scythe x1", true, "Enables AAM-24 Single mounts onto the Chicane's inner stub pylons.");
            EnableChicaneScythesDouble = Config.Bind("SAH-46 Chicane Changes", "Enable Chicane Inner Wing Scythe x2", true, "Enables AAM-24 Double mounts onto the Chicane's inner stub pylons.");
            EnableChicaneInternalLynchpinx14 = Config.Bind("SAH-46 Chicane Changes", "Enable Chicane Internal Lynchpin x14", true, "Enables AGR-18 Lynchpin x14 rocket pod in the Chicane's internal bays.");
            EnableChicaneInternalKingpinx8 = Config.Bind("SAH-46 Chicane Changes", "Enable Chicane Internal Kingpin x8", true, "Enables AGR-24 Kingpin x8 rocket pod in the Chicane's internal bays.");
            EnableChicaneBayPylonSymmetryFix = Config.Bind("SAH-46 Chicane Changes", "Enable Chicane Bay Pylon Symmetry Fix", true, "Centers the Chicane's right internal weapon bay pylon by setting its local X position to 0.");

            // Revoker Changes
            EnableRevokerLynchpinx14Double = Config.Bind("FS-12 Revoker Changes", "Enable Revoker Lynchpin x14 Double", true, "Enables the AGR-18 Lynchpin x14 double rocket pod on the Revoker's hardpoint set 2.");
            EnableRevokerKingpinx8Double = Config.Bind("FS-12 Revoker Changes", "Enable Revoker Kingpin x8 Double", true, "Enables the AGR-24 Kingpin x8 double rocket pod on the Revoker's hardpoint set 2.");

            // Vortex Changes
            EnableVortexLynchpinx14Double = Config.Bind("FS-20 Vortex Changes", "Enable Vortex Lynchpin x14 Double", true, "Enables the AGR-18 Lynchpin x14 double rocket pod on the Vortex's hardpoint set 3.");
            EnableVortexKingpinx8Double = Config.Bind("FS-20 Vortex Changes", "Enable Vortex Kingpin x8 Double", true, "Enables the AGR-24 Kingpin x8 double rocket pod on the Vortex's hardpoint set 3.");

            // Tarantula Changes
            EnableTarantulaLynchpinx14Double = Config.Bind("VL-49 Tarantula Changes", "Enable Tarantula Lynchpin x14 Double", true, "Enables the AGR-18 Lynchpin x14 double rocket pod on the Tarantula's hardpoint sets 4 and 5.");
            EnableTarantulaKingpinx8Double = Config.Bind("VL-49 Tarantula Changes", "Enable Tarantula Kingpin x8 Double", true, "Enables the AGR-24 Kingpin x8 double rocket pod on the Tarantula's hardpoint sets 4 and 5.");

            // Ifrit Changes
            EnableIfritLynchpinx14Double = Config.Bind("KR-67 Ifrit Changes", "Enable Ifrit Lynchpin x14 Double", true, "Enables the AGR-18 Lynchpin x14 double rocket pod on the Ifrit's hardpoint sets 4 and 5.");
            EnableIfritKingpinx8Double = Config.Bind("KR-67 Ifrit Changes", "Enable Ifrit Kingpin x8 Double", true, "Enables the AGR-24 Kingpin x8 double rocket pod on the Ifrit's hardpoint sets 4 and 5.");

            // Medusa Changes
            EnableMedusaLaserBuff = Config.Bind("EW-25 Medusa Changes", "Enable Medusa Laser Buff", true, "Master toggle to enable modifications to the Medusa's internal laser weapon.");
            MedusaLaserPowerDraw = Config.Bind("EW-25 Medusa Changes", "Medusa Laser Power Draw Value", 60.0f, "Sets the power draw of the Medusa's laser. (Vanilla is 120).");
            EnableMedusaLynchpinx14Double = Config.Bind("EW-25 Medusa Changes", "Enable Medusa Lynchpin x14 Double", true, "Enables the AGR-18 Lynchpin x14 double rocket pod on the Medusa's hardpoint set 3.");
            EnableMedusaKingpinx8Double = Config.Bind("EW-25 Medusa Changes", "Enable Medusa Kingpin x8 Double", true, "Enables the AGR-24 Kingpin x8 double rocket pod on the Medusa's hardpoint set 3.");
            EnableMedusaSAMRadar2Single = Config.Bind("EW-25 Medusa Changes", "Enable Medusa R9 Stratolance x1", true, "Enables the R9 Stratolance x1 mount on the Medusa's hardpoint sets 3 and 4.");
            EnableMedusaSAMRadar2Double = Config.Bind("EW-25 Medusa Changes", "Enable Medusa R9 Stratolance x2", true, "Enables the R9 Stratolance x2 mount on the Medusa's hardpoint set 4.");

            // 3. Generate Config Hash and format the new version string
            string configHash = GenerateConfigHash();
            FullVersionWithHash = $"{BaseVersion}-{configHash}";

            // 4. Update the notice retroactively with the generated hash
            hashDisplay.Value = configHash;
            Logger.LogInfo($"Mod Version Loaded: {FullVersionWithHash}");

            // 5. Register all Harmony patches

            // Missile Balance Changes
            Harmony.CreateAndPatchAll(typeof(StatsPatch));
            Harmony.CreateAndPatchAll(typeof(SARHLockPersistencePatch));
            Harmony.CreateAndPatchAll(typeof(SARHRelockPatch));

            // Cricket Changes
            Harmony.CreateAndPatchAll(typeof(CricketLynchpinx14DoublePatch));
            Harmony.CreateAndPatchAll(typeof(CricketKingpinx8DoublePatch));

            // Compass Changes
            Harmony.CreateAndPatchAll(typeof(CompassLynchpinx14DoublePatch));
            Harmony.CreateAndPatchAll(typeof(CompassKingpinx8DoublePatch));

            // Vagrant Changes
            Harmony.CreateAndPatchAll(typeof(VagrantLynchpinx14DoublePatch));
            Harmony.CreateAndPatchAll(typeof(VagrantKingpinx8DoublePatch));

            // Ibis Changes
            Harmony.CreateAndPatchAll(typeof(IbisLynchpinx14DoublePatch));
            Harmony.CreateAndPatchAll(typeof(IbisKingpinx8DoublePatch));

            // Chicane Changes
            Harmony.CreateAndPatchAll(typeof(ProxyGunPatch));
            Harmony.CreateAndPatchAll(typeof(ChicaneScythePatch));
            Harmony.CreateAndPatchAll(typeof(ChicaneInternalLynchpinx14Patch));
            Harmony.CreateAndPatchAll(typeof(ChicaneInternalKingpinx8Patch));
            Harmony.CreateAndPatchAll(typeof(ChicaneBayPylonSymmetryFixPatch));

            // Revoker Changes
            Harmony.CreateAndPatchAll(typeof(RevokerLynchpinx14DoublePatch));
            Harmony.CreateAndPatchAll(typeof(RevokerKingpinx8DoublePatch));

            // Vortex Changes
            Harmony.CreateAndPatchAll(typeof(VortexLynchpinx14DoublePatch));
            Harmony.CreateAndPatchAll(typeof(VortexKingpinx8DoublePatch));

            // Tarantula Changes
            Harmony.CreateAndPatchAll(typeof(TarantulaLynchpinx14DoublePatch));
            Harmony.CreateAndPatchAll(typeof(TarantulaKingpinx8DoublePatch));

            // Ifrit Changes
            Harmony.CreateAndPatchAll(typeof(IfritLynchpinx14DoublePatch));
            Harmony.CreateAndPatchAll(typeof(IfritKingpinx8DoublePatch));

            // Medusa Changes
            Harmony.CreateAndPatchAll(typeof(MedusaLaserPatch));
            Harmony.CreateAndPatchAll(typeof(MedusaLynchpinx14DoublePatch));
            Harmony.CreateAndPatchAll(typeof(MedusaKingpinx8DoublePatch));
            Harmony.CreateAndPatchAll(typeof(MedusaSAMRadar2SinglePatch));
            Harmony.CreateAndPatchAll(typeof(MedusaSAMRadar2DoublePatch));

            Logger.LogInfo("BVR - Balance and Variety Rework Mod Loaded!");
        }

        // Generates a short, deterministic alphanumeric hash based on the current config values
        private string GenerateConfigHash()
        {
            string combinedConfigs =
                $"{EnableIRMissilesBuff.Value}_{FlareCountMultiplier.Value}_{FlareRejectionMultiplier.Value}_{EnableR9LockPersistenceBuff.Value}_{R9LockPersistenceValue.Value}_{EnableRAM45LockPersistenceBuff.Value}_{RAM45LockPersistenceValue.Value}_{EnableR9SARHRelock.Value}_{R9SARHRelockDelay.Value}_{R9SARHRelockAttempts.Value}_{EnableRAM45SARHRelock.Value}_{RAM45SARHRelockDelay.Value}_{RAM45SARHRelockAttempts.Value}_" +
                $"{EnableCricketLynchpinx14Double.Value}_{EnableCricketKingpinx8Double.Value}_" +
                $"{EnableCompassLynchpinx14Double.Value}_{EnableCompassKingpinx8Double.Value}_" +
                $"{EnableVagrantLynchpinx14Double.Value}_{EnableVagrantKingpinx8Double.Value}_" +
                $"{EnableIbisLynchpinx14Double.Value}_{EnableIbisKingpinx8Double.Value}_" +
                $"{EnableChicaneProxyGun.Value}_{EnableChicaneScythesSingle.Value}_{EnableChicaneScythesDouble.Value}_{EnableChicaneInternalLynchpinx14.Value}_{EnableChicaneInternalKingpinx8.Value}_{EnableChicaneBayPylonSymmetryFix.Value}_" +
                $"{EnableRevokerLynchpinx14Double.Value}_{EnableRevokerKingpinx8Double.Value}_" +
                $"{EnableVortexLynchpinx14Double.Value}_{EnableVortexKingpinx8Double.Value}_" +
                $"{EnableTarantulaLynchpinx14Double.Value}_{EnableTarantulaKingpinx8Double.Value}_" +
                $"{EnableIfritLynchpinx14Double.Value}_{EnableIfritKingpinx8Double.Value}_" +
                $"{EnableMedusaLaserBuff.Value}_{MedusaLaserPowerDraw.Value}_{EnableMedusaLynchpinx14Double.Value}_{EnableMedusaKingpinx8Double.Value}_{EnableMedusaSAMRadar2Single.Value}_{EnableMedusaSAMRadar2Double.Value}";

            using (var md5 = MD5.Create())
            {
                byte[] hashBytes = md5.ComputeHash(Encoding.UTF8.GetBytes(combinedConfigs));
                return BitConverter.ToString(hashBytes).Replace("-", "").Substring(0, 6);
            }
        }
    }

    // ====================================================================================================
    // IMPORTANT NOTICES
    // Config Category: Important Notices
    // ====================================================================================================

    // ====================================================================================================
    // MISSILE BALANCE CHANGES
    // Config Category: Missile Balance Changes
    // ====================================================================================================

    // ====================================================================================================
    // IR MISSILES BUFF
    // ====================================================================================================
    public class ModifiedStatsFlag : MonoBehaviour { }

    [HarmonyPatch(typeof(WeaponManager), "Awake")]
    public static class StatsPatch
    {
        private static bool hasSweptStats = false;

        public static void Prefix()
        {
            if (!Plugin.EnableIRMissilesBuff.Value) return;
            if (hasSweptStats) return;

            var allFlares = Resources.FindObjectsOfTypeAll<FlareEjector>();
            foreach (var flare in allFlares)
            {
                ApplyFlareMultiplier(flare);
            }

            var allSeekers = Resources.FindObjectsOfTypeAll<IRSeeker>();
            foreach (var seeker in allSeekers)
            {
                ApplySeekerMultiplier(seeker);
            }

            hasSweptStats = true;
            Debug.Log("[IRMissileBuff] Successfully swept and multiplied all FlareEjector and IRSeeker blueprints!");
        }

        private static void ApplyFlareMultiplier(FlareEjector flare)
        {
            if (flare != null && flare.GetComponent<ModifiedStatsFlag>() == null)
            {
                var traverse = Traverse.Create(flare);

                int currentMax = traverse.Field("maxAmmo").GetValue<int>();
                int currentAmmo = traverse.Field("ammo").GetValue<int>();

                float multiplier = Plugin.FlareCountMultiplier.Value;

                traverse.Field("maxAmmo").SetValue(Mathf.RoundToInt(currentMax * multiplier));
                traverse.Field("ammo").SetValue(Mathf.RoundToInt(currentAmmo * multiplier));

                flare.gameObject.AddComponent<ModifiedStatsFlag>();
            }
        }

        private static void ApplySeekerMultiplier(IRSeeker seeker)
        {
            if (seeker != null && seeker.GetComponent<ModifiedStatsFlag>() == null)
            {
                var traverse = Traverse.Create(seeker);

                float currentRejection = traverse.Field("flareRejection").GetValue<float>();
                float multiplier = Plugin.FlareRejectionMultiplier.Value;

                traverse.Field("flareRejection").SetValue(currentRejection * multiplier);

                seeker.gameObject.AddComponent<ModifiedStatsFlag>();
            }
        }
    }

    // ====================================================================================================
    // SARH LOCK PERSISTENCE
    // Config Category: Missile Balance Changes
    // ====================================================================================================
    [HarmonyPatch(typeof(WeaponManager), "Awake")]
    public static class SARHLockPersistencePatch
    {
        private static bool hasPatchedR9LockPersistence = false;
        private static bool hasPatchedRAM45LockPersistence = false;

        public static void Prefix()
        {
            if (!Plugin.EnableR9LockPersistenceBuff.Value && !Plugin.EnableRAM45LockPersistenceBuff.Value) return;

            if (Plugin.EnableR9LockPersistenceBuff.Value && !hasPatchedR9LockPersistence)
            {
                hasPatchedR9LockPersistence = ApplySARHLockPersistence("SAM_Radar2", Plugin.R9LockPersistenceValue.Value, "R9LockPersistence");
            }

            if (Plugin.EnableRAM45LockPersistenceBuff.Value && !hasPatchedRAM45LockPersistence)
            {
                hasPatchedRAM45LockPersistence = ApplySARHLockPersistence("SAM_Radar1", Plugin.RAM45LockPersistenceValue.Value, "RAM45LockPersistence");
            }
        }

        private static bool ApplySARHLockPersistence(string targetName, float lockPersistence, string logTag)
        {
            bool success = false;

            var allGameObjects = Resources.FindObjectsOfTypeAll<GameObject>();

            foreach (var go in allGameObjects)
            {
                if (go == null) continue;

                // Handles both prefab assets and scene instances.
                string cleanedName = go.name.Replace("(Clone)", "");
                if (cleanedName != targetName) continue;

                var allComponents = go.GetComponentsInChildren<Component>(true);

                foreach (var comp in allComponents)
                {
                    if (comp == null) continue;

                    var type = comp.GetType();
                    if (type == null || type.Name != "SARHSeeker") continue;

                    if (comp.gameObject.GetComponent<ModifiedStatsFlag>() != null)
                    {
                        success = true;
                        continue;
                    }

                    if (TrySetLockPersistence(comp, lockPersistence))
                    {
                        comp.gameObject.AddComponent<ModifiedStatsFlag>();
                        success = true;
                        Debug.Log($"[{logTag}] Successfully set lockPersistence={lockPersistence} on {go.name}.");
                    }
                }
            }

            if (!success)
            {
                Debug.LogWarning($"[{logTag}] Could not find SARHSeeker.lockPersistence on {targetName}.");
            }
            else
            {
                Debug.Log($"[{logTag}] Master Prefab sweep complete!");
            }

            return success;
        }

        private static bool TrySetLockPersistence(object target, float value)
        {
            var traverse = Traverse.Create(target);

            // Try field first.
            var field = traverse.Field("lockPersistence");
            if (field.FieldExists())
            {
                field.SetValue(value);
                return true;
            }

            // Fallback if lockPersistence is exposed as a property.
            var property = traverse.Property("lockPersistence");
            if (property.PropertyExists())
            {
                property.SetValue(value);
                return true;
            }

            return false;
        }
    }

    // ====================================================================================================
    // SARH RELOCK
    // Config Category: Missile Balance Changes
    // ====================================================================================================
    public class SARHRelockController : MonoBehaviour
    {
        private SARHSeeker seeker;
        private Traverse seekerTraverse;
        private Missile cachedMissile;

        private float relockDelay = 3f;
        private int maxAttempts = 0;
        private float relockTimer = 0f;
        private bool waitingForRelock = false;
        private int attemptsUsed = 0;
        private bool initialized = false;

        public void Setup(SARHSeeker targetSeeker, float delay, int attempts)
        {
            seeker = targetSeeker;
            seekerTraverse = Traverse.Create(seeker);

            relockDelay = Mathf.Max(0f, delay);
            maxAttempts = Mathf.Max(0, attempts);

            attemptsUsed = 0;
            waitingForRelock = false;
            relockTimer = 0f;
            initialized = false;
            cachedMissile = null;
        }

        private void Update()
        {
            if (seeker == null)
            {
                UnityEngine.Object.Destroy(this);
                return;
            }

            if (seekerTraverse == null)
            {
                seekerTraverse = Traverse.Create(seeker);
            }

            if (!initialized)
            {
                initialized = true;
                return;
            }

            Missile missile = GetMissile();
            bool isLocked = missile != null && missile.seekerMode == Missile.SeekerMode.activeLock;

            if (isLocked)
            {
                attemptsUsed = 0;
                waitingForRelock = false;
                relockTimer = 0f;
                return;
            }

            Transform currentTargetTransform = GetField<Transform>("targetTransform");
            Unit targetUnit = GetField<Unit>("targetUnit");

            if (targetUnit == null)
            {
                waitingForRelock = false;
                return;
            }

            if (currentTargetTransform != null)
            {
                waitingForRelock = false;
                relockTimer = 0f;
                return;
            }

            if (!waitingForRelock)
            {
                if (CanAttemptRelock())
                {
                    waitingForRelock = true;
                    relockTimer = Mathf.Max(0f, relockDelay);
                }
            }
            else
            {
                DecayJam(Time.deltaTime);

                relockTimer -= Time.deltaTime;

                if (relockTimer <= 0f)
                {
                    TryRelock();
                }
            }
        }

        private bool CanAttemptRelock()
        {
            return maxAttempts == 0 || attemptsUsed < maxAttempts;
        }

        private void TryRelock()
        {
            attemptsUsed++;

            // Immediate jam decay when the relock attempt is made.
            DecayJam(Mathf.Max(Time.deltaTime, 1f));

            Unit targetUnit = GetField<Unit>("targetUnit");
            if (targetUnit == null || targetUnit.disabled)
            {
                waitingForRelock = false;
                return;
            }

            Transform newTargetTransform = targetUnit.GetRandomPart();
            if (newTargetTransform == null)
            {
                if (CanAttemptRelock())
                {
                    relockTimer = Mathf.Max(0f, relockDelay);
                }
                else
                {
                    waitingForRelock = false;
                }

                return;
            }

            SetField("targetTransform", newTargetTransform);
            SetField("timeWithoutTrack", 0f);
            SetField("lastTrackingCheck", 0f);

            TryResubscribeJamEvent();

            waitingForRelock = false;
        }

        private void DecayJam(float deltaTime)
        {
            float jam = GetField<float>("jamAccumulation");

            if (jam <= 0f)
            {
                if (jam != 0f)
                {
                    SetField("jamAccumulation", 0f);
                }

                return;
            }

            float tolerance = GetField<float>("jamTolerance");

            jam -= Mathf.Max(jam, 0.2f) * Mathf.Max(tolerance, 0.1f) * deltaTime;
            SetField("jamAccumulation", Mathf.Clamp01(jam));
        }

        private Missile GetMissile()
        {
            if (cachedMissile != null)
            {
                return cachedMissile;
            }

            if (seekerTraverse == null)
            {
                return null;
            }

            var field = seekerTraverse.Field("missile");
            if (field.FieldExists())
            {
                cachedMissile = field.GetValue<Missile>();
                return cachedMissile;
            }

            var property = seekerTraverse.Property("missile");
            if (property.PropertyExists())
            {
                cachedMissile = property.GetValue<Missile>();
            }

            return cachedMissile;
        }

        private T GetField<T>(string fieldName)
        {
            if (seekerTraverse == null)
            {
                return default(T);
            }

            var field = seekerTraverse.Field(fieldName);

            if (!field.FieldExists())
            {
                return default(T);
            }

            return field.GetValue<T>();
        }

        private void SetField<T>(string fieldName, T value)
        {
            if (seekerTraverse == null)
            {
                return;
            }

            var field = seekerTraverse.Field(fieldName);

            if (field.FieldExists())
            {
                field.SetValue(value);
            }
        }

        private void TryResubscribeJamEvent()
        {
            try
            {
                Missile missile = GetMissile();
                if (missile == null || seeker == null)
                {
                    return;
                }
                var method = AccessTools.Method(typeof(SARHSeeker), "SARHSeeker_OnJam", new Type[] { typeof(Unit.JamEventArgs) });
                if (method == null)
                {
                    return;
                }

                // FIX: Use standard Reflection to find the event, as AccessTools.Event does not exist.
                var eventInfo = missile.GetType().GetEvent("onJam", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                
                if (eventInfo != null)
                {
                    var handler = Delegate.CreateDelegate(eventInfo.EventHandlerType, seeker, method);
                    eventInfo.RemoveEventHandler(missile, handler);
                    eventInfo.AddEventHandler(missile, handler);
                    return;
                }
                
                // Fallback: If 'onJam' is exposed as a public Delegate field instead of a C# event
                var field = AccessTools.Field(missile.GetType(), "onJam");
                if (field != null && typeof(Delegate).IsAssignableFrom(field.FieldType))
                {
                    var currentDelegate = field.GetValue(missile) as Delegate;
                    var handler = Delegate.CreateDelegate(field.FieldType, seeker, method);
                    if (currentDelegate != null)
                    {
                        currentDelegate = Delegate.Remove(currentDelegate, handler);
                    }
                    currentDelegate = Delegate.Combine(currentDelegate, handler);
                    field.SetValue(missile, currentDelegate);
                }
            }
            catch
            {
                // If the event cannot be reattached, allow the relock attempt to continue.
            }
        }
    }
    
    [HarmonyPatch(typeof(SARHSeeker), "Initialize", new Type[] { typeof(Unit), typeof(GlobalPosition) })]
    public static class SARHRelockPatch
    {
        public static void Postfix(SARHSeeker __instance, Unit target)
        {
            if (__instance == null || target == null) return;

            if (!TryGetRelockSettings(__instance, out float delay, out int attempts)) return;

            var controller = __instance.GetComponent<SARHRelockController>();
            if (controller == null)
            {
                controller = __instance.gameObject.AddComponent<SARHRelockController>();
            }

            controller.Setup(__instance, delay, attempts);
        }

        private static bool TryGetRelockSettings(SARHSeeker seeker, out float delay, out int attempts)
        {
            delay = 0f;
            attempts = 0;

            if (seeker == null) return false;

            string rootName = seeker.transform != null && seeker.transform.root != null
                ? seeker.transform.root.gameObject.name
                : seeker.gameObject.name;

            rootName = rootName.Replace("(Clone)", "");

            if (rootName.Contains("SAM_Radar2") && Plugin.EnableR9SARHRelock.Value)
            {
                delay = Mathf.Max(0f, Plugin.R9SARHRelockDelay.Value);
                attempts = Mathf.Max(0, Plugin.R9SARHRelockAttempts.Value);
                return true;
            }

            if (rootName.Contains("SAM_Radar1") && Plugin.EnableRAM45SARHRelock.Value)
            {
                delay = Mathf.Max(0f, Plugin.RAM45SARHRelockDelay.Value);
                attempts = Mathf.Max(0, Plugin.RAM45SARHRelockAttempts.Value);
                return true;
            }

            return false;
        }
    }

    // ====================================================================================================
    // SHARED PREFAB VAULT
    // Shared hidden storage object for custom weapon prefabs.
    // Future weapon prefab generation patches should use this instead of creating their own vaults.
    // ====================================================================================================
    internal static class PrefabVault
    {
        private static GameObject vault;

        public static GameObject Get()
        {
            if (vault == null)
            {
                // Create a hidden root object. Anything placed inside will have activeInHierarchy = false.
                vault = new GameObject("BVR_PrefabVault");
                UnityEngine.Object.DontDestroyOnLoad(vault);
                vault.SetActive(false);

                // HideAndDontSave completely hides the vault from RUE and the scene hierarchy
                vault.hideFlags = HideFlags.HideAndDontSave;
            }

            return vault;
        }
    }

    // ====================================================================================================
    // CUSTOM WEAPONS - REUSED ASSETS
    // Shared generation framework for duplicated/reused weapon prefabs and mounts.
    // These weapon prefabs are aircraft-agnostic and exist independently of any specific airframe.
    // Future custom weapon patches that reuse existing assets should use this section.
    // ====================================================================================================
    internal static class CustomWeaponsReusedAssets
    {
        private static WeaponMount externalSAMRadar2SingleMount;
        private static WeaponMount externalSAMRadar2DoubleMount;
        private static WeaponMount externalLynchpinx14DoubleMount;
        private static WeaponMount externalKingpinx8DoubleMount;
        private static WeaponMount internalLynchpinx14DoubleMount;
        private static WeaponMount internalKingpinx8DoubleMount;

        private const float InternalLynchpinRailDelay = 0.5f;
        private const float InternalKingpinRailDelay = 0.5f;



        // ====================================================================================================
        // CLONED POD LIVERY FIX / COLORABLE MOUNT MERGE
        // ====================================================================================================

        private static void MirrorColorableMountToClonedPod(GameObject prefabRoot, GameObject sourcePod, GameObject clonedPod)
        {
            if (prefabRoot == null || sourcePod == null || clonedPod == null)
                return;

            ColorableMount rootMount = EnsureRootColorableMount(prefabRoot);
            if (rootMount == null)
            {
                Debug.LogWarning($"[CustomWeaponsReusedAssets] No ColorableMount found on {prefabRoot.name}. Cloned pod livery fix skipped.");
                return;
            }

            var rootTraverse = Traverse.Create(rootMount);
            var colorField = rootTraverse.Field("colorableRenderers");
            var skinField = rootTraverse.Field("skinnableRenderers");

            if (!colorField.FieldExists() || !skinField.FieldExists())
            {
                Debug.LogWarning($"[CustomWeaponsReusedAssets] Could not access ColorableMount renderer arrays on {prefabRoot.name}.");
                return;
            }

            var colorList = new List<Renderer>(colorField.GetValue<Renderer[]>() ?? new Renderer[0]);
            var skinList = new List<Renderer>(skinField.GetValue<Renderer[]>() ?? new Renderer[0]);

            AddMirroredRenderers(colorList, sourcePod.transform, clonedPod.transform);
            AddMirroredRenderers(skinList, sourcePod.transform, clonedPod.transform);

            colorField.SetValue(colorList.Where(r => r != null).ToArray());
            skinField.SetValue(skinList.Where(r => r != null).ToArray());

            Debug.Log($"[CustomWeaponsReusedAssets] Updated ColorableMount on {prefabRoot.name}. Colorable renderers: {colorList.Count}, Skinnable renderers: {skinList.Count}.");
        }

        private static ColorableMount EnsureRootColorableMount(GameObject prefabRoot)
        {
            if (prefabRoot == null)
                return null;

            ColorableMount[] existingMounts = prefabRoot.GetComponentsInChildren<ColorableMount>(true);
            if (existingMounts == null || existingMounts.Length == 0)
                return null;

            ColorableMount rootMount = prefabRoot.GetComponent<ColorableMount>();
            if (rootMount == null)
                rootMount = prefabRoot.AddComponent<ColorableMount>();

            var rootTraverse = Traverse.Create(rootMount);
            var rootColorField = rootTraverse.Field("colorableRenderers");
            var rootSkinField = rootTraverse.Field("skinnableRenderers");

            if (!rootColorField.FieldExists() || !rootSkinField.FieldExists())
                return rootMount;

            var colorList = new List<Renderer>(rootColorField.GetValue<Renderer[]>() ?? new Renderer[0]);
            var skinList = new List<Renderer>(rootSkinField.GetValue<Renderer[]>() ?? new Renderer[0]);

            foreach (ColorableMount mount in existingMounts)
            {
                if (mount == null || mount == rootMount)
                    continue;

                var mountTraverse = Traverse.Create(mount);

                var childColorField = mountTraverse.Field("colorableRenderers");
                if (childColorField.FieldExists())
                {
                    Renderer[] childColors = childColorField.GetValue<Renderer[]>() ?? new Renderer[0];
                    foreach (Renderer r in childColors)
                        AddUniqueRenderer(colorList, r);
                }

                var childSkinField = mountTraverse.Field("skinnableRenderers");
                if (childSkinField.FieldExists())
                {
                    Renderer[] childSkins = childSkinField.GetValue<Renderer[]>() ?? new Renderer[0];
                    foreach (Renderer r in childSkins)
                        AddUniqueRenderer(skinList, r);
                }

                // Consolidate to one root ColorableMount.
                // ColorableMount is one-shot anyway: it registers renderers and destroys itself.
                UnityEngine.Object.DestroyImmediate(mount);
            }

            rootColorField.SetValue(colorList.Where(r => r != null).ToArray());
            rootSkinField.SetValue(skinList.Where(r => r != null).ToArray());

            return rootMount;
        }

        private static void AddMirroredRenderers(List<Renderer> renderers, Transform sourceRoot, Transform clonedRoot)
        {
            if (renderers == null || sourceRoot == null || clonedRoot == null)
                return;

            Renderer[] originals = renderers.Where(r => r != null).ToArray();

            foreach (Renderer original in originals)
            {
                if (!IsUnderTransform(original.transform, sourceRoot))
                    continue;

                Renderer mirrored = FindMirroredRenderer(original, sourceRoot, clonedRoot);
                AddUniqueRenderer(renderers, mirrored);
            }
        }

        private static Renderer FindMirroredRenderer(Renderer original, Transform sourceRoot, Transform clonedRoot)
        {
            if (original == null || sourceRoot == null || clonedRoot == null)
                return null;

            Transform targetTransform = FindMirroredTransform(original.transform, sourceRoot, clonedRoot);
            if (targetTransform == null)
                return null;

            Renderer[] originalRenderers = original.GetComponents<Renderer>();

            int rendererIndex = -1;
            for (int i = 0; i < originalRenderers.Length; i++)
            {
                if (originalRenderers[i] == original)
                {
                    rendererIndex = i;
                    break;
                }
            }

            Renderer[] clonedRenderers = targetTransform.GetComponents<Renderer>();
            if (clonedRenderers.Length == 0)
                return null;

            if (rendererIndex >= 0 && rendererIndex < clonedRenderers.Length)
                return clonedRenderers[rendererIndex];

            return clonedRenderers[0];
        }

        private static Transform FindMirroredTransform(Transform original, Transform sourceRoot, Transform clonedRoot)
        {
            if (original == null || sourceRoot == null || clonedRoot == null)
                return null;

            if (!IsUnderTransform(original, sourceRoot))
                return null;

            if (original == sourceRoot)
                return clonedRoot;

            List<int> siblingPath = new List<int>();
            Transform current = original;

            while (current != null && current != sourceRoot)
            {
                siblingPath.Insert(0, current.GetSiblingIndex());
                current = current.parent;
            }

            Transform target = clonedRoot;

            foreach (int siblingIndex in siblingPath)
            {
                if (target == null || siblingIndex < 0 || siblingIndex >= target.childCount)
                    return null;

                target = target.GetChild(siblingIndex);
            }

            return target;
        }

        private static bool IsUnderTransform(Transform child, Transform root)
        {
            if (child == null || root == null)
                return false;

            while (child != null)
            {
                if (child == root)
                    return true;

                child = child.parent;
            }

            return false;
        }

        private static void AddUniqueRenderer(List<Renderer> list, Renderer renderer)
        {
            if (renderer == null)
                return;

            int id = renderer.GetInstanceID();

            if (list.Any(existing => existing != null && existing.GetInstanceID() == id))
                return;

            list.Add(renderer);
        }



        // ====================================================================================================
        // EXTERNAL SAM_RADAR2 SINGLE (R9 STRATOLANCE x1)
        // ====================================================================================================
        public static WeaponMount GetExternalSAMRadar2Single()
        {
            if (externalSAMRadar2SingleMount != null) return externalSAMRadar2SingleMount;
            
            var allMounts = Resources.FindObjectsOfTypeAll<WeaponMount>();
            WeaponMount singleMount = allMounts.FirstOrDefault(w => w != null && w.jsonKey == "AAM4_single");
            if (singleMount == null)
            {
                singleMount = allMounts.FirstOrDefault(w => w != null && w.name == "AAM4_single");
            }
            if (singleMount == null || singleMount.prefab == null) return null;

            // 1. Duplicate the GameObject as a child of the shared disabled vault
            GameObject singlePrefab = UnityEngine.Object.Instantiate(singleMount.prefab, PrefabVault.Get().transform);
            singlePrefab.name = "SAM_Radar2_single";
            
            // Satisfies activeSelf = true, but activeInHierarchy = false
            singlePrefab.SetActive(true);

            // --- VISUAL AND PHYSICAL ASSET SWAPPING ---
            // Find the original SAM_Radar2 prefab in the game's loaded resources to steal its visuals and hitbox
            var allGameObjects = Resources.FindObjectsOfTypeAll<GameObject>();
            GameObject originalSamPrefab = allGameObjects.FirstOrDefault(go => go != null && go.name == "SAM_Radar2" && go.transform.parent == null);

            if (originalSamPrefab != null)
            {
                Transform pylon = singlePrefab.transform.Find("pylon");
                if (pylon != null)
                {
                    Transform missileChild = pylon.Find("aam4");
                    if (missileChild != null)
                    {
                        // 1. Rename the child to match the new weapon
                        missileChild.name = "sam_radar2";

                        // 2. Swap MeshFilter (The 3D Geometry)
                        MeshFilter origMf = originalSamPrefab.GetComponent<MeshFilter>();
                        MeshFilter newMf = missileChild.GetComponent<MeshFilter>();
                        if (origMf != null && newMf != null) 
                        {
                            newMf.sharedMesh = origMf.sharedMesh;
                        }

                        // 3. Swap MeshRenderer (The Textures/Materials)
                        MeshRenderer origMr = originalSamPrefab.GetComponent<MeshRenderer>();
                        MeshRenderer newMr = missileChild.GetComponent<MeshRenderer>();
                        if (origMr != null && newMr != null) 
                        {
                            newMr.sharedMaterials = origMr.sharedMaterials;
                        }

                        // 4. Neutralize LODGroup (Level of Detail)
                        // The AAM4 uses LODs. If we leave it enabled, it will try to render the old AAM4 meshes 
                        // at certain distances because its internal renderer references haven't been updated.
                        // Disabling it forces the game to always draw the high-poly MeshFilter we just swapped.
                        LODGroup newLod = missileChild.GetComponent<LODGroup>();
                        if (newLod != null) 
                        {
                            newLod.enabled = false;
                        }

                        // 5. Swap CapsuleCollider (The Physical Hitbox)
                        // The R9 is a different size than the AAM4. We copy the vanilla R9's collider 
                        // dimensions so the missile has the correct hitbox for damage and collisions.
                        CapsuleCollider origCol = originalSamPrefab.GetComponent<CapsuleCollider>();
                        CapsuleCollider newCol = missileChild.GetComponent<CapsuleCollider>();
                        if (origCol != null && newCol != null)
                        {
                            newCol.center = origCol.center;
                            newCol.radius = origCol.radius;
                            newCol.height = origCol.height;
                            newCol.direction = origCol.direction;
                        }
                    }
                }
            }
            else
            {
                Debug.LogWarning("[ExternalSAMRadar2Single] Could not find original 'SAM_Radar2' prefab in resources to copy mesh and collider!");
            }
            // --- END OF SWAPPING LOGIC ---

            // Find the info asset for SAM_Radar2
            var allAssets = Resources.FindObjectsOfTypeAll<UnityEngine.Object>();
            var samInfoAsset = allAssets.FirstOrDefault(o => o != null && o.name == "info_SAM_Radar2");

            // Sweep for the MountedMissile component and update its info to the R9 Stratolance
            foreach (var comp in singlePrefab.GetComponentsInChildren<Component>(true))
            {
                if (comp == null) continue;
                var type = comp.GetType();
                if (type == null || type.Name != "MountedMissile") continue;

                var traverseComp = Traverse.Create(comp);
                if (samInfoAsset != null)
                {
                    var infoField = traverseComp.Field("info");
                    if (infoField.FieldExists())
                    {
                        infoField.SetValue(samInfoAsset);
                    }
                    else
                    {
                        // Fallback if it's exposed as a property
                        var infoProperty = traverseComp.Property("info");
                        if (infoProperty.PropertyExists())
                        {
                            infoProperty.SetValue(samInfoAsset);
                        }
                    }
                }
            }

            // 2. Duplicate the WeaponMount and configure it
            WeaponMount newMount = UnityEngine.Object.Instantiate(singleMount);
            newMount.name = "SAM_Radar2_single";
            newMount.prefab = singlePrefab;

            var traverseMount = Traverse.Create(newMount);
            if (traverseMount.Field("mountName").FieldExists())
                traverseMount.Field("mountName").SetValue("R9 Stratolance x1");
            if (traverseMount.Field("jsonKey").FieldExists())
                traverseMount.Field("jsonKey").SetValue("SAM_Radar2_single");

            SetNetworkLookupIndex(traverseMount, "SAM_Radar2_single");

            externalSAMRadar2SingleMount = newMount;
            Debug.Log("[ExternalSAMRadar2Single] Custom R9 Stratolance x1 prefab and mount generation complete!");
            return externalSAMRadar2SingleMount;
        }



        // ====================================================================================================
        // EXTERNAL SAM_RADAR2 DOUBLE (R9 STRATOLANCE x2)
        // ====================================================================================================
        public static WeaponMount GetExternalSAMRadar2Double()
        {
            if (externalSAMRadar2DoubleMount != null) return externalSAMRadar2DoubleMount;

            var allMounts = Resources.FindObjectsOfTypeAll<WeaponMount>();
            WeaponMount armDoubleMount = allMounts.FirstOrDefault(w => w != null && w.jsonKey == "ARM1_double");
            if (armDoubleMount == null)
            {
                armDoubleMount = allMounts.FirstOrDefault(w => w != null && w.name == "ARM1_double");
            }

            if (armDoubleMount == null || armDoubleMount.prefab == null) return null;

            // 1. Duplicate the GameObject as a child of the shared disabled vault
            GameObject doublePrefab = UnityEngine.Object.Instantiate(armDoubleMount.prefab, PrefabVault.Get().transform);
            doublePrefab.name = "SAM_Radar2_double";

            // Satisfies activeSelf = true, but activeInHierarchy = false
            doublePrefab.SetActive(true);

            // --- VISUAL AND PHYSICAL ASSET SWAPPING ---
            // Find the original SAM_Radar2 prefab in the game's loaded resources to steal its visuals and hitbox
            var allGameObjects = Resources.FindObjectsOfTypeAll<GameObject>();
            GameObject originalSamPrefab = allGameObjects.FirstOrDefault(go => go != null && go.name == "SAM_Radar2" && go.transform.parent == null);

            if (originalSamPrefab != null)
            {
                int swappedMissiles = 0;

                var allChildren = doublePrefab.GetComponentsInChildren<Transform>(true);
                foreach (var child in allChildren)
                {
                    if (child == null || child == doublePrefab.transform) continue;

                    // Only process the two direct pylons. This intentionally ignores "pylon (1)".
                    if (child.parent != doublePrefab.transform) continue;
                    if (child.name != "pylon") continue;

                    Transform missileChild = child.Find("ARM1");
                    if (missileChild == null) continue;

                    // Rename the child to match the new weapon
                    missileChild.name = "sam_radar2";

                    SwapMissileToSamRadar2(missileChild, originalSamPrefab);
                    swappedMissiles++;
                }

                if (swappedMissiles < 2)
                {
                    Debug.LogWarning($"[ExternalSAMRadar2Double] Expected 2 ARM1 missiles under ARM1_double pylons, but swapped {swappedMissiles}.");
                }
            }
            else
            {
                Debug.LogWarning("[ExternalSAMRadar2Double] Could not find original 'SAM_Radar2' prefab in resources to copy mesh and collider!");
            }
            // --- END OF SWAPPING LOGIC ---

            // Find the info asset for SAM_Radar2
            var allAssets = Resources.FindObjectsOfTypeAll<UnityEngine.Object>();
            var samInfoAsset = allAssets.FirstOrDefault(o => o != null && o.name == "info_SAM_Radar2");

            // Sweep for the MountedMissile components and update their info to the R9 Stratolance
            foreach (var comp in doublePrefab.GetComponentsInChildren<Component>(true))
            {
                if (comp == null) continue;

                var type = comp.GetType();
                if (type == null || type.Name != "MountedMissile") continue;

                var traverseComp = Traverse.Create(comp);
                if (samInfoAsset != null)
                {
                    var infoField = traverseComp.Field("info");
                    if (infoField.FieldExists())
                    {
                        infoField.SetValue(samInfoAsset);
                    }
                    else
                    {
                        // Fallback if it's exposed as a property
                        var infoProperty = traverseComp.Property("info");
                        if (infoProperty.PropertyExists())
                        {
                            infoProperty.SetValue(samInfoAsset);
                        }
                    }
                }
            }

            // 2. Duplicate the WeaponMount and configure it
            WeaponMount newMount = UnityEngine.Object.Instantiate(armDoubleMount);
            newMount.name = "SAM_Radar2_double";
            newMount.prefab = doublePrefab;

            var traverseMount = Traverse.Create(newMount);
            if (traverseMount.Field("mountName").FieldExists())
                traverseMount.Field("mountName").SetValue("R9 Stratolance x2");
            if (traverseMount.Field("jsonKey").FieldExists())
                traverseMount.Field("jsonKey").SetValue("SAM_Radar2_double");

            SetNetworkLookupIndex(traverseMount, "SAM_Radar2_double");

            externalSAMRadar2DoubleMount = newMount;
            Debug.Log("[ExternalSAMRadar2Double] Custom R9 Stratolance x2 prefab and mount generation complete!");
            return externalSAMRadar2DoubleMount;
        }

        private static void SwapMissileToSamRadar2(Transform missileChild, GameObject originalSamPrefab)
        {
            if (missileChild == null || originalSamPrefab == null) return;

            // Swap MeshFilter (The 3D Geometry)
            MeshFilter origMf = originalSamPrefab.GetComponent<MeshFilter>();
            MeshFilter newMf = missileChild.GetComponent<MeshFilter>();
            if (origMf != null && newMf != null)
            {
                newMf.sharedMesh = origMf.sharedMesh;
            }

            // Swap MeshRenderer (The Textures/Materials)
            MeshRenderer origMr = originalSamPrefab.GetComponent<MeshRenderer>();
            MeshRenderer newMr = missileChild.GetComponent<MeshRenderer>();
            if (origMr != null && newMr != null)
            {
                newMr.sharedMaterials = origMr.sharedMaterials;
            }

            // Neutralize LODGroup (Level of Detail)
            // The ARM1 missiles may use LODs. If we leave it enabled, it can try to render the old ARM1 meshes
            // at certain distances because its internal renderer references haven't been updated.
            // Disabling it forces the game to always draw the high-poly MeshFilter we just swapped.
            LODGroup newLod = missileChild.GetComponent<LODGroup>();
            if (newLod != null)
            {
                newLod.enabled = false;
            }

            // Swap CapsuleCollider (The Physical Hitbox)
            // The R9 is a different size than the ARM1. We copy the vanilla R9's collider
            // dimensions so the missile has the correct hitbox for damage and collisions.
            CapsuleCollider origCol = originalSamPrefab.GetComponent<CapsuleCollider>();
            CapsuleCollider newCol = missileChild.GetComponent<CapsuleCollider>();
            if (origCol != null)
            {
                if (newCol == null)
                {
                    newCol = missileChild.gameObject.AddComponent<CapsuleCollider>();
                }

                newCol.center = origCol.center;
                newCol.radius = origCol.radius;
                newCol.height = origCol.height;
                newCol.direction = origCol.direction;
                newCol.isTrigger = origCol.isTrigger;
            }
        }



        // ====================================================================================================
        // EXTERNAL LYNCHPIN X14 DOUBLE
        // ====================================================================================================
        public static WeaponMount GetExternalLynchpinx14Double()
        {
            if (externalLynchpinx14DoubleMount != null) return externalLynchpinx14DoubleMount;

            var allMounts = Resources.FindObjectsOfTypeAll<WeaponMount>();
            WeaponMount singleMount = allMounts.FirstOrDefault(w => w != null && w.name == "RocketPod1_single");

            if (singleMount == null || singleMount.prefab == null) return null;

            // 1. Duplicate the GameObject as a child of the shared disabled vault
            GameObject doublePrefab = UnityEngine.Object.Instantiate(singleMount.prefab, PrefabVault.Get().transform);
            doublePrefab.name = "RocketPod1_double";

            // This satisfies the requirement: activeSelf = true, but active(InHierarchy) = false,
            // so that the weapon will be visible when spawned, but it won't float randomly in the game world when the game starts.
            doublePrefab.SetActive(true);

            // Set the local position of the entire double pod prefab assembly
            doublePrefab.transform.localPosition = new Vector3(0f, 0f, 0f);

            Transform firstPod = doublePrefab.transform.Find("pod");
            if (firstPod != null)
            {
                firstPod.localPosition = new Vector3(0.13f, -0.15f, 0.19f);
                firstPod.localEulerAngles = Vector3.zero;

                Transform secondPod = UnityEngine.Object.Instantiate(firstPod.gameObject, doublePrefab.transform).transform;
                secondPod.name = "pod";
                secondPod.localPosition = new Vector3(-0.13f, -0.15f, 0.19f);
                secondPod.localEulerAngles = Vector3.zero;
            }

            // This external variant does not need railDelay or missileBay overrides.

            // 2. Duplicate the WeaponMount and configure it
            WeaponMount doubleMount = UnityEngine.Object.Instantiate(singleMount);
            doubleMount.name = "RocketPod1_double";
            doubleMount.prefab = doublePrefab;

            var traverseMount = Traverse.Create(doubleMount);

            // Update basic definition names
            if (traverseMount.Field("mountName").FieldExists())
                traverseMount.Field("mountName").SetValue("AGR-18 Lynchpin x14");

            if (traverseMount.Field("jsonKey").FieldExists())
                traverseMount.Field("jsonKey").SetValue("RocketPod1_double");

            SetNetworkLookupIndex(traverseMount, "RocketPod1_double");

            externalLynchpinx14DoubleMount = doubleMount;

            Debug.Log("[ExternalLynchpinx14Double] Custom double Lynchpin prefab and mount generation complete!");

            return externalLynchpinx14DoubleMount;
        }

        // ====================================================================================================
        // EXTERNAL KINGPIN X8 DOUBLE
        // ====================================================================================================
        public static WeaponMount GetExternalKingpinx8Double()
        {
            if (externalKingpinx8DoubleMount != null) return externalKingpinx8DoubleMount;

            var allMounts = Resources.FindObjectsOfTypeAll<WeaponMount>();
            WeaponMount singleMount = allMounts.FirstOrDefault(w => w != null && w.name == "Rocket2_4Pod");

            // Fallback in case the WeaponMount name differs from the prefab name.
            if (singleMount == null)
            {
                singleMount = allMounts.FirstOrDefault(w => w != null && w.prefab != null && w.prefab.name == "Rocket2_4Pod");
            }

            if (singleMount == null || singleMount.prefab == null) return null;

            // 1. Duplicate the GameObject as a child of the shared disabled vault
            GameObject doublePrefab = UnityEngine.Object.Instantiate(singleMount.prefab, PrefabVault.Get().transform);
            doublePrefab.name = "Rocket2_4Podx2";

            // This satisfies the requirement: activeSelf = true, but active(InHierarchy) = false,
            // so that the weapon will be visible when spawned, but it won't float randomly in the game world when the game starts.
            doublePrefab.SetActive(true);

            // Set the local position of the entire double pod prefab assembly.
            doublePrefab.transform.localPosition = new Vector3(0f, -0.1f, 0f);

            Transform firstPod = doublePrefab.transform.Find("pod");
            if (firstPod != null)
            {
                firstPod.localPosition = new Vector3(0.14f, -0.15f, -0.005f);
                firstPod.localEulerAngles = new Vector3(0f, 0f, 45f);

                Transform secondPod = UnityEngine.Object.Instantiate(firstPod.gameObject, doublePrefab.transform).transform;
                secondPod.name = "pod";
                secondPod.localPosition = new Vector3(-0.14f, -0.15f, -0.005f);
                secondPod.localEulerAngles = new Vector3(0f, 0f, -45f);
                // Fix livery inheritance for the cloned pod.
                MirrorColorableMountToClonedPod(doublePrefab, firstPod.gameObject, secondPod.gameObject);
            }

            // Reposition the pylon child to correct alignment
            Transform pylon = doublePrefab.transform.Find("pylon");
            if (pylon != null)
            {
                pylon.localPosition = new Vector3(0f, 0.038f, 0f);
            }

            // This external variant does not need railDelay or missileBay overrides.

            // 2. Duplicate the WeaponMount and configure it
            WeaponMount doubleMount = UnityEngine.Object.Instantiate(singleMount);
            doubleMount.name = "Rocket2_4Podx2";
            doubleMount.prefab = doublePrefab;

            var traverseMount = Traverse.Create(doubleMount);

            // Update basic definition names
            if (traverseMount.Field("mountName").FieldExists())
                traverseMount.Field("mountName").SetValue("AGR-24 Kingpin x8");

            if (traverseMount.Field("jsonKey").FieldExists())
                traverseMount.Field("jsonKey").SetValue("Rocket2_4Podx2");

            SetNetworkLookupIndex(traverseMount, "Rocket2_4Podx2");

            externalKingpinx8DoubleMount = doubleMount;

            Debug.Log("[ExternalKingpinx8Double] Custom double Kingpin prefab and mount generation complete!");

            return externalKingpinx8DoubleMount;
        }

        // ====================================================================================================
        // INTERNAL LYNCHPIN X14 DOUBLE
        // ====================================================================================================
        public static WeaponMount GetInternalLynchpinx14Double()
        {
            if (internalLynchpinx14DoubleMount != null) return internalLynchpinx14DoubleMount;

            var allMounts = Resources.FindObjectsOfTypeAll<WeaponMount>();
            WeaponMount singleMount = allMounts.FirstOrDefault(w => w != null && w.name == "RocketPod1_single");

            if (singleMount == null || singleMount.prefab == null) return null;

            // 1. Duplicate the GameObject as a child of the shared disabled vault
            GameObject doublePrefab = UnityEngine.Object.Instantiate(singleMount.prefab, PrefabVault.Get().transform);
            doublePrefab.name = "RocketPod1_double_internal";

            // This satisfies the requirement: activeSelf = true, but active(InHierarchy) = false,
            // so that the weapon will be visible when spawned, but it won't float randomly in the game world when the game starts.
            doublePrefab.SetActive(true);

            // Set the local position of the entire double pod prefab assembly
            doublePrefab.transform.localPosition = new Vector3(0f, -0.05f, 0f);

            Transform firstPod = doublePrefab.transform.Find("pod");
            if (firstPod != null)
            {
                firstPod.localPosition = new Vector3(0.13f, -0.15f, 0.19f);
                firstPod.localEulerAngles = Vector3.zero;

                Transform secondPod = UnityEngine.Object.Instantiate(firstPod.gameObject, doublePrefab.transform).transform;
                secondPod.name = "pod";
                secondPod.localPosition = new Vector3(-0.13f, -0.15f, 0.19f);
                secondPod.localEulerAngles = Vector3.zero;
            }

            // Force MountedMissile.railDelay = 0.5 on all rocket objects inside both pods.
            // This affects the prefab itself, so later spawned RocketPod1_double_internal(Clone)
            // objects should inherit the modified value.
            SetMountedMissileRailDelay(doublePrefab, InternalLynchpinRailDelay);

            // 2. Duplicate the WeaponMount and configure it
            WeaponMount doubleMount = UnityEngine.Object.Instantiate(singleMount);
            doubleMount.name = "RocketPod1_double_internal";
            doubleMount.prefab = doublePrefab;

            var traverseMount = Traverse.Create(doubleMount);

            // Update basic definition names
            if (traverseMount.Field("mountName").FieldExists())
                traverseMount.Field("mountName").SetValue("AGR-18 Lynchpin x14");

            if (traverseMount.Field("jsonKey").FieldExists())
                traverseMount.Field("jsonKey").SetValue("RocketPod1_double_internal");

            // Fix for internal bay requirement breaking the spawn logic
            if (traverseMount.Field("missileBay").FieldExists())
                traverseMount.Field("missileBay").SetValue(true);
            else if (traverseMount.Property("missileBay").PropertyExists())
                traverseMount.Property("missileBay").SetValue(true);

            SetNetworkLookupIndex(traverseMount, "RocketPod1_double_internal");

            internalLynchpinx14DoubleMount = doubleMount;

            Debug.Log("[InternalLynchpinx14Double] Custom internal double Lynchpin prefab and mount generation complete!");

            return internalLynchpinx14DoubleMount;
        }

        // ====================================================================================================
        // INTERNAL KINGPIN X8 DOUBLE
        // ====================================================================================================
        public static WeaponMount GetInternalKingpinx8Double()
        {
            if (internalKingpinx8DoubleMount != null) return internalKingpinx8DoubleMount;

            var allMounts = Resources.FindObjectsOfTypeAll<WeaponMount>();
            WeaponMount singleMount = allMounts.FirstOrDefault(w => w != null && w.name == "Rocket2_4Pod");

            // Fallback in case the WeaponMount name differs from the prefab name.
            if (singleMount == null)
            {
                singleMount = allMounts.FirstOrDefault(w => w != null && w.prefab != null && w.prefab.name == "Rocket2_4Pod");
            }

            if (singleMount == null || singleMount.prefab == null) return null;

            // 1. Duplicate the GameObject as a child of the shared disabled vault
            GameObject doublePrefab = UnityEngine.Object.Instantiate(singleMount.prefab, PrefabVault.Get().transform);
            doublePrefab.name = "Rocket2_4Podx2_internal";

            // This satisfies the requirement: activeSelf = true, but active(InHierarchy) = false,
            // so that the weapon will be visible when spawned, but it won't float randomly in the game world when the game starts.
            doublePrefab.SetActive(true);

            // Set the local position of the entire double pod prefab assembly.
            doublePrefab.transform.localPosition = new Vector3(0f, -0.11f, 0.13f);

            Transform firstPod = doublePrefab.transform.Find("pod");
            if (firstPod != null)
            {
                firstPod.localPosition = new Vector3(0.14f, -0.15f, -0.005f);
                firstPod.localEulerAngles = new Vector3(0f, 0f, 45f);

                Transform secondPod = UnityEngine.Object.Instantiate(firstPod.gameObject, doublePrefab.transform).transform;
                secondPod.name = "pod";
                secondPod.localPosition = new Vector3(-0.14f, -0.15f, -0.005f);
                secondPod.localEulerAngles = new Vector3(0f, 0f, -45f);
                // Fix livery inheritance for the cloned pod.
                MirrorColorableMountToClonedPod(doublePrefab, firstPod.gameObject, secondPod.gameObject);
            }

            // Reposition the pylon child to correct internal bay alignment
            Transform pylon = doublePrefab.transform.Find("pylon");
            if (pylon != null)
            {
                pylon.localPosition = new Vector3(0f, 0.038f, 0f);
            }

            // Force MountedMissile.railDelay = 0.5 on all rocket objects inside both pods.
            // The Kingpin pod contains rocket1 through rocket4, but this sweep catches all MountedMissile components automatically.
            // This affects the prefab itself, so later spawned Rocket2_4Podx2_internal(Clone)
            // objects should inherit the modified value.
            SetMountedMissileRailDelay(doublePrefab, InternalKingpinRailDelay);

            // 2. Duplicate the WeaponMount and configure it
            WeaponMount doubleMount = UnityEngine.Object.Instantiate(singleMount);
            doubleMount.name = "Rocket2_4Podx2_internal";
            doubleMount.prefab = doublePrefab;

            var traverseMount = Traverse.Create(doubleMount);

            // Update basic definition names
            if (traverseMount.Field("mountName").FieldExists())
                traverseMount.Field("mountName").SetValue("AGR-24 Kingpin x8");

            if (traverseMount.Field("jsonKey").FieldExists())
                traverseMount.Field("jsonKey").SetValue("Rocket2_4Podx2_internal");

            // Fix for internal bay requirement breaking the spawn logic
            if (traverseMount.Field("missileBay").FieldExists())
                traverseMount.Field("missileBay").SetValue(true);
            else if (traverseMount.Property("missileBay").PropertyExists())
                traverseMount.Property("missileBay").SetValue(true);

            SetNetworkLookupIndex(traverseMount, "Rocket2_4Podx2_internal");

            internalKingpinx8DoubleMount = doubleMount;

            Debug.Log("[InternalKingpinx8Double] Custom internal double Kingpin prefab and mount generation complete!");

            return internalKingpinx8DoubleMount;
        }

        private static void SetNetworkLookupIndex(Traverse traverseMount, string networkKey)
        {
            // Fix for Network Lookup Index conflict destroying the ghost duplicate
            int customNetworkId = Mathf.Abs(networkKey.GetHashCode());

            var backingField = traverseMount.Field("<INetworkDefinition.LookupIndex>k__BackingField");
            if (backingField.FieldExists())
            {
                backingField.SetValue(customNetworkId);
            }

            var interfaceProperty = traverseMount.Property("INetworkDefinition.LookupIndex");
            if (interfaceProperty.PropertyExists())
            {
                interfaceProperty.SetValue(customNetworkId);
            }
        }

        private static void SetMountedMissileRailDelay(GameObject prefabRoot, float delay)
        {
            if (prefabRoot == null) return;

            int patchedCount = 0;

            // Scan the entire custom prefab. This will catch both pods and all rocket children:
            // RocketPod1_double_internal/pod/rocket1 ... rocket7
            // Rocket2_4Podx2_internal/pod/rocket1 ... rocket4
            foreach (var component in prefabRoot.GetComponentsInChildren<Component>(true))
            {
                if (component == null) continue;

                var type = component.GetType();
                if (type == null) continue;

                // Adjust this if the game uses a derived or differently named component.
                if (type.Name != "MountedMissile") continue;

                if (TrySetRailDelay(component, delay))
                {
                    patchedCount++;
                }
            }

            if (patchedCount == 0)
            {
                Debug.LogWarning($"[CustomWeaponsReusedAssets] No MountedMissile.railDelay values were set on {prefabRoot.name}. Check that the component/field name is correct and that rockets exist in the prefab.");
            }
            else
            {
                Debug.Log($"[CustomWeaponsReusedAssets] Set railDelay={delay} on {patchedCount} MountedMissile component(s) inside {prefabRoot.name}.");
            }
        }

        private static bool TrySetRailDelay(object target, float delay)
        {
            var traverse = Traverse.Create(target);

            // Try field first.
            var field = traverse.Field("railDelay");
            if (field.FieldExists())
            {
                field.SetValue(delay);
                return true;
            }

            // Fallback if railDelay is exposed as a property.
            var property = traverse.Property("railDelay");
            if (property.PropertyExists())
            {
                property.SetValue(delay);
                return true;
            }

            return false;
        }
    }

    // ====================================================================================================
    // CI-22 CRICKET CHANGES (COIN)
    // Config Category: CI-22 Cricket Changes
    // ====================================================================================================

    // ====================================================================================================
    // CRICKET LYNCHPIN X14 DOUBLE
    // ====================================================================================================
    [HarmonyPatch(typeof(WeaponManager), "Awake")]
    public static class CricketLynchpinx14DoublePatch
    {
        private static bool hasPatchedCricketLynchpinx14Double = false;

        public static void Prefix()
        {
            if (!Plugin.EnableCricketLynchpinx14Double.Value) return;
            if (hasPatchedCricketLynchpinx14Double) return;

            WeaponMount doubleMount = CustomWeaponsReusedAssets.GetExternalLynchpinx14Double();
            if (doubleMount == null) return;

            // Add to COIN hardpoint sets 2 and 3
            var allWeaponManagers = Resources.FindObjectsOfTypeAll<WeaponManager>();

            foreach (var wm in allWeaponManagers)
            {
                if (wm == null || wm.transform == null || wm.transform.root == null) continue;

                if (wm.transform.root.name.Contains("COIN"))
                {
                    if (wm.hardpointSets != null && wm.hardpointSets.Length > 3)
                    {
                        bool updated = false;

                        var hardpointSet2 = wm.hardpointSets[2];
                        if (hardpointSet2 != null && hardpointSet2.weaponOptions != null && !hardpointSet2.weaponOptions.Contains(doubleMount))
                        {
                            hardpointSet2.weaponOptions.Add(doubleMount);
                            updated = true;
                        }

                        var hardpointSet3 = wm.hardpointSets[3];
                        if (hardpointSet3 != null && hardpointSet3.weaponOptions != null && !hardpointSet3.weaponOptions.Contains(doubleMount))
                        {
                            hardpointSet3.weaponOptions.Add(doubleMount);
                            updated = true;
                        }

                        if (updated)
                        {
                            Debug.Log($"[CricketLynchpinx14Double] Successfully injected double rockets into {wm.gameObject.name} hardpoint sets 2 and 3.");
                        }
                    }
                }
            }

            hasPatchedCricketLynchpinx14Double = true;
            Debug.Log("[CricketLynchpinx14Double] Master Prefab injection complete!");
        }
    }

    // ====================================================================================================
    // CRICKET KINGPIN X8 DOUBLE
    // ====================================================================================================
    [HarmonyPatch(typeof(WeaponManager), "Awake")]
    public static class CricketKingpinx8DoublePatch
    {
        private static bool hasPatchedCricketKingpinx8Double = false;

        public static void Prefix()
        {
            if (!Plugin.EnableCricketKingpinx8Double.Value) return;
            if (hasPatchedCricketKingpinx8Double) return;

            WeaponMount doubleMount = CustomWeaponsReusedAssets.GetExternalKingpinx8Double();
            if (doubleMount == null) return;

            // Add to COIN hardpoint sets 2 and 3
            var allWeaponManagers = Resources.FindObjectsOfTypeAll<WeaponManager>();

            foreach (var wm in allWeaponManagers)
            {
                if (wm == null || wm.transform == null || wm.transform.root == null) continue;

                if (wm.transform.root.name.Contains("COIN"))
                {
                    if (wm.hardpointSets != null && wm.hardpointSets.Length > 3)
                    {
                        bool updated = false;

                        var hardpointSet2 = wm.hardpointSets[2];
                        if (hardpointSet2 != null && hardpointSet2.weaponOptions != null && !hardpointSet2.weaponOptions.Contains(doubleMount))
                        {
                            hardpointSet2.weaponOptions.Add(doubleMount);
                            updated = true;
                        }

                        var hardpointSet3 = wm.hardpointSets[3];
                        if (hardpointSet3 != null && hardpointSet3.weaponOptions != null && !hardpointSet3.weaponOptions.Contains(doubleMount))
                        {
                            hardpointSet3.weaponOptions.Add(doubleMount);
                            updated = true;
                        }

                        if (updated)
                        {
                            Debug.Log($"[CricketKingpinx8Double] Successfully injected double rockets into {wm.gameObject.name} hardpoint sets 2 and 3.");
                        }
                    }
                }
            }

            hasPatchedCricketKingpinx8Double = true;
            Debug.Log("[CricketKingpinx8Double] Master Prefab injection complete!");
        }
    }

    // ====================================================================================================
    // T/A-30 COMPASS CHANGES (trainer)
    // Config Category: T/A-30 Compass Changes
    // ====================================================================================================

    // ====================================================================================================
    // COMPASS LYNCHPIN X14 DOUBLE
    // ====================================================================================================
    [HarmonyPatch(typeof(WeaponManager), "Awake")]
    public static class CompassLynchpinx14DoublePatch
    {
        private static bool hasPatchedCompassLynchpinx14Double = false;

        public static void Prefix()
        {
            if (!Plugin.EnableCompassLynchpinx14Double.Value) return;
            if (hasPatchedCompassLynchpinx14Double) return;

            WeaponMount doubleMount = CustomWeaponsReusedAssets.GetExternalLynchpinx14Double();
            if (doubleMount == null) return;

            // Add to trainer hardpoint set 1
            var allWeaponManagers = Resources.FindObjectsOfTypeAll<WeaponManager>();

            foreach (var wm in allWeaponManagers)
            {
                if (wm == null || wm.transform == null || wm.transform.root == null) continue;

                if (wm.transform.root.name.Contains("trainer"))
                {
                    if (wm.hardpointSets != null && wm.hardpointSets.Length > 1)
                    {
                        bool updated = false;

                        var hardpointSet1 = wm.hardpointSets[1];
                        if (hardpointSet1 != null && hardpointSet1.weaponOptions != null && !hardpointSet1.weaponOptions.Contains(doubleMount))
                        {
                            hardpointSet1.weaponOptions.Add(doubleMount);
                            updated = true;
                        }

                        if (updated)
                        {
                            Debug.Log($"[CompassLynchpinx14Double] Successfully injected double rockets into {wm.gameObject.name} hardpoint set 1.");
                        }
                    }
                }
            }

            hasPatchedCompassLynchpinx14Double = true;
            Debug.Log("[CompassLynchpinx14Double] Master Prefab injection complete!");
        }
    }

    // ====================================================================================================
    // COMPASS KINGPIN X8 DOUBLE
    // ====================================================================================================
    [HarmonyPatch(typeof(WeaponManager), "Awake")]
    public static class CompassKingpinx8DoublePatch
    {
        private static bool hasPatchedCompassKingpinx8Double = false;

        public static void Prefix()
        {
            if (!Plugin.EnableCompassKingpinx8Double.Value) return;
            if (hasPatchedCompassKingpinx8Double) return;

            WeaponMount doubleMount = CustomWeaponsReusedAssets.GetExternalKingpinx8Double();
            if (doubleMount == null) return;

            // Add to trainer hardpoint set 1
            var allWeaponManagers = Resources.FindObjectsOfTypeAll<WeaponManager>();

            foreach (var wm in allWeaponManagers)
            {
                if (wm == null || wm.transform == null || wm.transform.root == null) continue;

                if (wm.transform.root.name.Contains("trainer"))
                {
                    if (wm.hardpointSets != null && wm.hardpointSets.Length > 1)
                    {
                        bool updated = false;

                        var hardpointSet1 = wm.hardpointSets[1];
                        if (hardpointSet1 != null && hardpointSet1.weaponOptions != null && !hardpointSet1.weaponOptions.Contains(doubleMount))
                        {
                            hardpointSet1.weaponOptions.Add(doubleMount);
                            updated = true;
                        }

                        if (updated)
                        {
                            Debug.Log($"[CompassKingpinx8Double] Successfully injected double rockets into {wm.gameObject.name} hardpoint set 1.");
                        }
                    }
                }
            }

            hasPatchedCompassKingpinx8Double = true;
            Debug.Log("[CompassKingpinx8Double] Master Prefab injection complete!");
        }
    }

    // ====================================================================================================
    // VT-7 VAGRANT CHANGES (VTOLTrainer1)
    // Config Category: VT-7 Vagrant Changes
    // ====================================================================================================

    // ====================================================================================================
    // VAGRANT LYNCHPIN X14 DOUBLE
    // ====================================================================================================
    [HarmonyPatch(typeof(WeaponManager), "Awake")]
    public static class VagrantLynchpinx14DoublePatch
    {
        private static bool hasPatchedVagrantLynchpinx14Double = false;

        public static void Prefix()
        {
            if (!Plugin.EnableVagrantLynchpinx14Double.Value) return;
            if (hasPatchedVagrantLynchpinx14Double) return;

            WeaponMount doubleMount = CustomWeaponsReusedAssets.GetExternalLynchpinx14Double();
            if (doubleMount == null) return;

            // Add to VTOLTrainer1 hardpoint set 3
            var allWeaponManagers = Resources.FindObjectsOfTypeAll<WeaponManager>();

            foreach (var wm in allWeaponManagers)
            {
                if (wm == null || wm.transform == null || wm.transform.root == null) continue;

                if (wm.transform.root.name.Contains("VTOLTrainer1"))
                {
                    if (wm.hardpointSets != null && wm.hardpointSets.Length > 2)
                    {
                        bool updated = false;

                        var hardpointSet3 = wm.hardpointSets[3];
                        if (hardpointSet3 != null && hardpointSet3.weaponOptions != null && !hardpointSet3.weaponOptions.Contains(doubleMount))
                        {
                            hardpointSet3.weaponOptions.Add(doubleMount);
                            updated = true;
                        }

                        if (updated)
                        {
                            Debug.Log($"[VagrantLynchpinx14Double] Successfully injected double rockets into {wm.gameObject.name} hardpoint set 3.");
                        }
                    }
                }
            }

            hasPatchedVagrantLynchpinx14Double = true;
            Debug.Log("[VagrantLynchpinx14Double] Master Prefab injection complete!");
        }
    }

    // ====================================================================================================
    // VAGRANT KINGPIN X8 DOUBLE
    // ====================================================================================================
    [HarmonyPatch(typeof(WeaponManager), "Awake")]
    public static class VagrantKingpinx8DoublePatch
    {
        private static bool hasPatchedVagrantKingpinx8Double = false;

        public static void Prefix()
        {
            if (!Plugin.EnableVagrantKingpinx8Double.Value) return;
            if (hasPatchedVagrantKingpinx8Double) return;

            WeaponMount doubleMount = CustomWeaponsReusedAssets.GetExternalKingpinx8Double();
            if (doubleMount == null) return;

            // Add to VTOLTrainer1 hardpoint set 3
            var allWeaponManagers = Resources.FindObjectsOfTypeAll<WeaponManager>();

            foreach (var wm in allWeaponManagers)
            {
                if (wm == null || wm.transform == null || wm.transform.root == null) continue;

                if (wm.transform.root.name.Contains("VTOLTrainer1"))
                {
                    if (wm.hardpointSets != null && wm.hardpointSets.Length > 2)
                    {
                        bool updated = false;

                        var hardpointSet3 = wm.hardpointSets[3];
                        if (hardpointSet3 != null && hardpointSet3.weaponOptions != null && !hardpointSet3.weaponOptions.Contains(doubleMount))
                        {
                            hardpointSet3.weaponOptions.Add(doubleMount);
                            updated = true;
                        }

                        if (updated)
                        {
                            Debug.Log($"[VagrantKingpinx8Double] Successfully injected double rockets into {wm.gameObject.name} hardpoint set 3.");
                        }
                    }
                }
            }

            hasPatchedVagrantKingpinx8Double = true;
            Debug.Log("[VagrantKingpinx8Double] Master Prefab injection complete!");
        }
    }

    // ====================================================================================================
    // UH-90 IBIS CHANGES (UtilityHelo1)
    // Config Category: UH-90 Ibis Changes
    // ====================================================================================================

    // ====================================================================================================
    // IBIS LYNCHPIN X14 DOUBLE
    // ====================================================================================================
    [HarmonyPatch(typeof(WeaponManager), "Awake")]
    public static class IbisLynchpinx14DoublePatch
    {
        private static bool hasPatchedIbisLynchpinx14Double = false;

        public static void Prefix()
        {
            if (!Plugin.EnableIbisLynchpinx14Double.Value) return;
            if (hasPatchedIbisLynchpinx14Double) return;

            WeaponMount doubleMount = CustomWeaponsReusedAssets.GetExternalLynchpinx14Double();
            if (doubleMount == null) return;

            // Add to UtilityHelo1 hardpoint sets 0 and 1
            var allWeaponManagers = Resources.FindObjectsOfTypeAll<WeaponManager>();

            foreach (var wm in allWeaponManagers)
            {
                if (wm == null || wm.transform == null || wm.transform.root == null) continue;

                if (wm.transform.root.name.Contains("UtilityHelo1"))
                {
                    if (wm.hardpointSets != null && wm.hardpointSets.Length > 1)
                    {
                        bool updated = false;

                        var hardpointSet0 = wm.hardpointSets[0];
                        if (hardpointSet0 != null && hardpointSet0.weaponOptions != null && !hardpointSet0.weaponOptions.Contains(doubleMount))
                        {
                            hardpointSet0.weaponOptions.Add(doubleMount);
                            updated = true;
                        }

                        var hardpointSet1 = wm.hardpointSets[1];
                        if (hardpointSet1 != null && hardpointSet1.weaponOptions != null && !hardpointSet1.weaponOptions.Contains(doubleMount))
                        {
                            hardpointSet1.weaponOptions.Add(doubleMount);
                            updated = true;
                        }

                        if (updated)
                        {
                            Debug.Log($"[IbisLynchpinx14Double] Successfully injected double rockets into {wm.gameObject.name} hardpoint sets 0 and 1.");
                        }
                    }
                }
            }

            hasPatchedIbisLynchpinx14Double = true;
            Debug.Log("[IbisLynchpinx14Double] Master Prefab injection complete!");
        }
    }

    // ====================================================================================================
    // IBIS KINGPIN X8 DOUBLE
    // ====================================================================================================
    [HarmonyPatch(typeof(WeaponManager), "Awake")]
    public static class IbisKingpinx8DoublePatch
    {
        private static bool hasPatchedIbisKingpinx8Double = false;

        public static void Prefix()
        {
            if (!Plugin.EnableIbisKingpinx8Double.Value) return;
            if (hasPatchedIbisKingpinx8Double) return;

            WeaponMount doubleMount = CustomWeaponsReusedAssets.GetExternalKingpinx8Double();
            if (doubleMount == null) return;

            // Add to UtilityHelo1 hardpoint sets 0 and 1
            var allWeaponManagers = Resources.FindObjectsOfTypeAll<WeaponManager>();

            foreach (var wm in allWeaponManagers)
            {
                if (wm == null || wm.transform == null || wm.transform.root == null) continue;

                if (wm.transform.root.name.Contains("UtilityHelo1"))
                {
                    if (wm.hardpointSets != null && wm.hardpointSets.Length > 1)
                    {
                        bool updated = false;

                        var hardpointSet0 = wm.hardpointSets[0];
                        if (hardpointSet0 != null && hardpointSet0.weaponOptions != null && !hardpointSet0.weaponOptions.Contains(doubleMount))
                        {
                            hardpointSet0.weaponOptions.Add(doubleMount);
                            updated = true;
                        }

                        var hardpointSet1 = wm.hardpointSets[1];
                        if (hardpointSet1 != null && hardpointSet1.weaponOptions != null && !hardpointSet1.weaponOptions.Contains(doubleMount))
                        {
                            hardpointSet1.weaponOptions.Add(doubleMount);
                            updated = true;
                        }

                        if (updated)
                        {
                            Debug.Log($"[IbisKingpinx8Double] Successfully injected double rockets into {wm.gameObject.name} hardpoint sets 0 and 1.");
                        }
                    }
                }
            }

            hasPatchedIbisKingpinx8Double = true;
            Debug.Log("[IbisKingpinx8Double] Master Prefab injection complete!");
        }
    }

    // ====================================================================================================
    // SAH-46 CHICANE CHANGES (AttackHelo1)
    // Config Category: SAH-46 Chicane Changes
    // ====================================================================================================

    // ====================================================================================================
    // CHICANE PROXIMITY FUSE NOSEGUN
    // ====================================================================================================
    [HarmonyPatch(typeof(WeaponManager), "Awake")]
    public static class ProxyGunPatch
    {
        private static bool hasPatchedGun = false;

        public static void Prefix()
        {
            if (!Plugin.EnableChicaneProxyGun.Value) return;
            if (hasPatchedGun) return;

            var allWeaponManagers = Resources.FindObjectsOfTypeAll<WeaponManager>();

            foreach (var wm in allWeaponManagers)
            {
                if (wm == null || wm.transform == null || wm.transform.root == null) continue;

                if (wm.transform.root.name.Contains("AttackHelo1"))
                {
                    bool success = TryPatchProxyGun(wm.transform.root.gameObject);

                    if (success)
                    {
                        Debug.Log($"[ChicaneProxyGun] Successfully enabled proxy timer on: {wm.gameObject.name}");
                    }
                }
            }

            hasPatchedGun = true;
            Debug.Log("[ChicaneProxyGun] Master Prefab sweep complete!");
        }

        private static bool TryPatchProxyGun(GameObject rootVehicle)
        {
            var allComponents = rootVehicle.GetComponentsInChildren<MonoBehaviour>(true);

            foreach (var comp in allComponents)
            {
                var compTraverse = Traverse.Create(comp);

                var stationsField = compTraverse.Field("weaponStations");
                if (stationsField.FieldExists())
                {
                    var stationsList = stationsField.GetValue<IList>();

                    if (stationsList != null && stationsList.Count > 0)
                    {
                        var firstStation = stationsList[0];
                        var stationTraverse = Traverse.Create(firstStation);

                        var weaponsField = stationTraverse.Field("Weapons");
                        if (weaponsField.FieldExists())
                        {
                            var weaponsList = weaponsField.GetValue<IList>();

                            if (weaponsList != null && weaponsList.Count > 0)
                            {
                                var firstWeapon = weaponsList[0];
                                var weaponTraverse = Traverse.Create(firstWeapon);

                                var proxyTimerField = weaponTraverse.Field("proximityTimer");
                                if (proxyTimerField.FieldExists())
                                {
                                    proxyTimerField.SetValue(true);
                                    return true;
                                }
                            }
                        }
                    }
                }
            }

            return false;
        }
    }

    // ====================================================================================================
    // CHICANE INNER WING SCYTHES
    // ====================================================================================================
    [HarmonyPatch(typeof(WeaponManager), "Awake")]
    public static class ChicaneScythePatch
    {
        private static bool hasPatchedPrefab = false;

        public static void Prefix(WeaponManager __instance)
        {
            if (!Plugin.EnableChicaneScythesSingle.Value && !Plugin.EnableChicaneScythesDouble.Value) return;
            if (hasPatchedPrefab) return;

            var allMounts = Resources.FindObjectsOfTypeAll<WeaponMount>();

            WeaponMount aam2Single = allMounts.FirstOrDefault(w => w.jsonKey == "AAM2_single");
            WeaponMount aam2Double = allMounts.FirstOrDefault(w => w.jsonKey == "AAM2_double");

            if (aam2Single == null || aam2Double == null) return;

            var allWeaponManagers = Resources.FindObjectsOfTypeAll<WeaponManager>();

            foreach (var wm in allWeaponManagers)
            {
                if (wm == null || wm.transform == null || wm.transform.root == null) continue;

                if (wm.transform.root.name.Contains("AttackHelo1"))
                {
                    if (wm.hardpointSets != null && wm.hardpointSets.Length > 2)
                    {
                        var stubPylons = wm.hardpointSets[2];
                        if (stubPylons == null || stubPylons.weaponOptions == null) continue;

                        bool updated = false;

                        if (Plugin.EnableChicaneScythesSingle.Value && !stubPylons.weaponOptions.Any(w => w != null && w.jsonKey == "AAM2_single"))
                        {
                            stubPylons.weaponOptions.Add(aam2Single);
                            updated = true;
                        }

                        if (Plugin.EnableChicaneScythesDouble.Value && !stubPylons.weaponOptions.Any(w => w != null && w.jsonKey == "AAM2_double"))
                        {
                            stubPylons.weaponOptions.Add(aam2Double);
                            updated = true;
                        }

                        if (updated)
                        {
                            Debug.Log($"[ChicaneScythe] Successfully dynamically injected AAM-24 mounts into: {wm.gameObject.name}");
                        }
                    }
                }
            }

            hasPatchedPrefab = true;
            Debug.Log("[ChicaneScythe] Successfully patched Chicane Prefabs with selected Scythe configs!");
        }
    }

    // ====================================================================================================
    // CHICANE INTERNAL BAY LYNCHPIN X14
    // ====================================================================================================
    [HarmonyPatch(typeof(WeaponManager), "Awake")]
    public static class ChicaneInternalLynchpinx14Patch
    {
        private static bool hasPatchedInternalLynchpinx14 = false;

        public static void Prefix()
        {
            if (!Plugin.EnableChicaneInternalLynchpinx14.Value) return;
            if (hasPatchedInternalLynchpinx14) return;

            WeaponMount doubleMount = CustomWeaponsReusedAssets.GetInternalLynchpinx14Double();
            if (doubleMount == null) return;

            // Add to AttackHelo1 Internal Bays
            var allWeaponManagers = Resources.FindObjectsOfTypeAll<WeaponManager>();

            foreach (var wm in allWeaponManagers)
            {
                if (wm == null || wm.transform == null || wm.transform.root == null) continue;

                if (wm.transform.root.name.Contains("AttackHelo1"))
                {
                    if (wm.hardpointSets != null && wm.hardpointSets.Length > 1)
                    {
                        var internalBays = wm.hardpointSets[1];

                        if (internalBays != null && internalBays.weaponOptions != null && !internalBays.weaponOptions.Contains(doubleMount))
                        {
                            internalBays.weaponOptions.Add(doubleMount);
                            Debug.Log($"[ChicaneInternalLynchpinx14] Successfully injected double rockets into {wm.gameObject.name} internal bays.");
                        }
                    }
                }
            }

            hasPatchedInternalLynchpinx14 = true;
            Debug.Log("[ChicaneInternalLynchpinx14] Master Prefab injection complete!");
        }
    }

    // ====================================================================================================
    // CHICANE INTERNAL BAY KINGPIN X8
    // ====================================================================================================
    [HarmonyPatch(typeof(WeaponManager), "Awake")]
    public static class ChicaneInternalKingpinx8Patch
    {
        private static bool hasPatchedInternalKingpinx8 = false;

        public static void Prefix()
        {
            if (!Plugin.EnableChicaneInternalKingpinx8.Value) return;
            if (hasPatchedInternalKingpinx8) return;

            WeaponMount doubleMount = CustomWeaponsReusedAssets.GetInternalKingpinx8Double();
            if (doubleMount == null) return;

            // Add to AttackHelo1 Internal Bays
            var allWeaponManagers = Resources.FindObjectsOfTypeAll<WeaponManager>();

            foreach (var wm in allWeaponManagers)
            {
                if (wm == null || wm.transform == null || wm.transform.root == null) continue;

                if (wm.transform.root.name.Contains("AttackHelo1"))
                {
                    if (wm.hardpointSets != null && wm.hardpointSets.Length > 1)
                    {
                        var internalBays = wm.hardpointSets[1];

                        if (internalBays != null && internalBays.weaponOptions != null && !internalBays.weaponOptions.Contains(doubleMount))
                        {
                            internalBays.weaponOptions.Add(doubleMount);
                            Debug.Log($"[ChicaneInternalKingpinx8] Successfully injected double rockets into {wm.gameObject.name} internal bays.");
                        }
                    }
                }
            }

            hasPatchedInternalKingpinx8 = true;
            Debug.Log("[ChicaneInternalKingpinx8] Master Prefab injection complete!");
        }
    }

    // ====================================================================================================
    // CHICANE BAY PYLON SYMMETRY FIX
    // ====================================================================================================
    [HarmonyPatch(typeof(WeaponManager), "Awake")]
    public static class ChicaneBayPylonSymmetryFixPatch
    {
        private static bool hasPatchedBayPylon = false;

        private const string BayPylonPath = "weaponBay_R/weaponDoorHinge_Ra/weaponDoorHinge_Rb/pylon_bay_R";
        private static readonly Vector3 BayPylonLocalPosition = new Vector3(0f, -0.35f, -0.1f);

        public static void Prefix()
        {
            if (!Plugin.EnableChicaneBayPylonSymmetryFix.Value) return;
            if (hasPatchedBayPylon) return;

            bool patchedAny = false;

            var allWeaponManagers = Resources.FindObjectsOfTypeAll<WeaponManager>();

            foreach (var wm in allWeaponManagers)
            {
                if (wm == null || wm.transform == null || wm.transform.root == null) continue;

                if (wm.transform.root.name.Contains("AttackHelo1"))
                {
                    bool success = TryFixBayPylon(wm.transform.root.gameObject);

                    if (success)
                    {
                        Debug.Log($"[ChicaneBayPylonSymmetryFix] Successfully centered bay pylon on: {wm.gameObject.name}");
                        patchedAny = true;
                    }
                }
            }

            if (patchedAny)
            {
                hasPatchedBayPylon = true;
                Debug.Log("[ChicaneBayPylonSymmetryFix] Master Prefab sweep complete!");
            }
        }

        private static bool TryFixBayPylon(GameObject rootVehicle)
        {
            if (rootVehicle == null) return false;

            var pylon = rootVehicle.transform.Find(BayPylonPath);
            if (pylon == null) return false;

            pylon.localPosition = BayPylonLocalPosition;
            return true;
        }
    }

    // ====================================================================================================
    // A-19 BRAWLER CHANGES (CAS1)
    // Config Category: A-19 Brawler Changes
    // ====================================================================================================

    // ====================================================================================================
    // FS-12 REVOKER CHANGES (Fighter1)
    // Config Category: FS-12 Revoker Changes
    // ====================================================================================================

    // ====================================================================================================
    // REVOKER LYNCHPIN X14 DOUBLE
    // ====================================================================================================
    [HarmonyPatch(typeof(WeaponManager), "Awake")]
    public static class RevokerLynchpinx14DoublePatch
    {
        private static bool hasPatchedRevokerLynchpinx14Double = false;

        public static void Prefix()
        {
            if (!Plugin.EnableRevokerLynchpinx14Double.Value) return;
            if (hasPatchedRevokerLynchpinx14Double) return;

            WeaponMount doubleMount = CustomWeaponsReusedAssets.GetExternalLynchpinx14Double();
            if (doubleMount == null) return;

            // Add to Fighter1 hardpoint set 2
            var allWeaponManagers = Resources.FindObjectsOfTypeAll<WeaponManager>();

            foreach (var wm in allWeaponManagers)
            {
                if (wm == null || wm.transform == null || wm.transform.root == null) continue;

                if (wm.transform.root.name.Contains("Fighter1") && !wm.transform.root.name.Contains("SmallFighter1"))
                {
                    if (wm.hardpointSets != null && wm.hardpointSets.Length > 2)
                    {
                        bool updated = false;

                        var hardpointSet2 = wm.hardpointSets[2];
                        if (hardpointSet2 != null && hardpointSet2.weaponOptions != null && !hardpointSet2.weaponOptions.Contains(doubleMount))
                        {
                            hardpointSet2.weaponOptions.Add(doubleMount);
                            updated = true;
                        }

                        if (updated)
                        {
                            Debug.Log($"[RevokerLynchpinx14Double] Successfully injected double rockets into {wm.gameObject.name} hardpoint set 2.");
                        }
                    }
                }
            }

            hasPatchedRevokerLynchpinx14Double = true;
            Debug.Log("[RevokerLynchpinx14Double] Master Prefab injection complete!");
        }
    }

    // ====================================================================================================
    // REVOKER KINGPIN X8 DOUBLE
    // ====================================================================================================
    [HarmonyPatch(typeof(WeaponManager), "Awake")]
    public static class RevokerKingpinx8DoublePatch
    {
        private static bool hasPatchedRevokerKingpinx8Double = false;

        public static void Prefix()
        {
            if (!Plugin.EnableRevokerKingpinx8Double.Value) return;
            if (hasPatchedRevokerKingpinx8Double) return;

            WeaponMount doubleMount = CustomWeaponsReusedAssets.GetExternalKingpinx8Double();
            if (doubleMount == null) return;

            // Add to Fighter1 hardpoint set 2
            var allWeaponManagers = Resources.FindObjectsOfTypeAll<WeaponManager>();

            foreach (var wm in allWeaponManagers)
            {
                if (wm == null || wm.transform == null || wm.transform.root == null) continue;

                if (wm.transform.root.name.Contains("Fighter1") && !wm.transform.root.name.Contains("SmallFighter1"))
                {
                    if (wm.hardpointSets != null && wm.hardpointSets.Length > 2)
                    {
                        bool updated = false;

                        var hardpointSet2 = wm.hardpointSets[2];
                        if (hardpointSet2 != null && hardpointSet2.weaponOptions != null && !hardpointSet2.weaponOptions.Contains(doubleMount))
                        {
                            hardpointSet2.weaponOptions.Add(doubleMount);
                            updated = true;
                        }

                        if (updated)
                        {
                            Debug.Log($"[RevokerKingpinx8Double] Successfully injected double rockets into {wm.gameObject.name} hardpoint set 2.");
                        }
                    }
                }
            }

            hasPatchedRevokerKingpinx8Double = true;
            Debug.Log("[RevokerKingpinx8Double] Master Prefab injection complete!");
        }
    }

    // ====================================================================================================
    // FS-20 VORTEX CHANGES (SmallFighter1)
    // Config Category: FS-20 Vortex Changes
    // ====================================================================================================

    // ====================================================================================================
    // VORTEX LYNCHPIN X14 DOUBLE
    // ====================================================================================================
    [HarmonyPatch(typeof(WeaponManager), "Awake")]
    public static class VortexLynchpinx14DoublePatch
    {
        private static bool hasPatchedVortexLynchpinx14Double = false;

        public static void Prefix()
        {
            if (!Plugin.EnableVortexLynchpinx14Double.Value) return;
            if (hasPatchedVortexLynchpinx14Double) return;

            WeaponMount doubleMount = CustomWeaponsReusedAssets.GetExternalLynchpinx14Double();
            if (doubleMount == null) return;

            // Add to smallFighter1 hardpoint set 3
            var allWeaponManagers = Resources.FindObjectsOfTypeAll<WeaponManager>();

            foreach (var wm in allWeaponManagers)
            {
                if (wm == null || wm.transform == null || wm.transform.root == null) continue;

                if (wm.transform.root.name.Contains("SmallFighter1"))
                {
                    if (wm.hardpointSets != null && wm.hardpointSets.Length > 3)
                    {
                        bool updated = false;

                        var hardpointSet3 = wm.hardpointSets[3];
                        if (hardpointSet3 != null && hardpointSet3.weaponOptions != null && !hardpointSet3.weaponOptions.Contains(doubleMount))
                        {
                            hardpointSet3.weaponOptions.Add(doubleMount);
                            updated = true;
                        }

                        if (updated)
                        {
                            Debug.Log($"[VortexLynchpinx14Double] Successfully injected double rockets into {wm.gameObject.name} hardpoint set 3.");
                        }
                    }
                }
            }

            hasPatchedVortexLynchpinx14Double = true;
            Debug.Log("[VortexLynchpinx14Double] Master Prefab injection complete!");
        }
    }

    // ====================================================================================================
    // VORTEX KINGPIN X8 DOUBLE
    // ====================================================================================================
    [HarmonyPatch(typeof(WeaponManager), "Awake")]
    public static class VortexKingpinx8DoublePatch
    {
        private static bool hasPatchedVortexKingpinx8Double = false;

        public static void Prefix()
        {
            if (!Plugin.EnableVortexKingpinx8Double.Value) return;
            if (hasPatchedVortexKingpinx8Double) return;

            WeaponMount doubleMount = CustomWeaponsReusedAssets.GetExternalKingpinx8Double();
            if (doubleMount == null) return;

            // Add to SmallFighter1 hardpoint set 3
            var allWeaponManagers = Resources.FindObjectsOfTypeAll<WeaponManager>();

            foreach (var wm in allWeaponManagers)
            {
                if (wm == null || wm.transform == null || wm.transform.root == null) continue;

                if (wm.transform.root.name.Contains("SmallFighter1"))
                {
                    if (wm.hardpointSets != null && wm.hardpointSets.Length > 3)
                    {
                        bool updated = false;

                        var hardpointSet3 = wm.hardpointSets[3];
                        if (hardpointSet3 != null && hardpointSet3.weaponOptions != null && !hardpointSet3.weaponOptions.Contains(doubleMount))
                        {
                            hardpointSet3.weaponOptions.Add(doubleMount);
                            updated = true;
                        }

                        if (updated)
                        {
                            Debug.Log($"[VortexKingpinx8Double] Successfully injected double rockets into {wm.gameObject.name} hardpoint set 3.");
                        }
                    }
                }
            }

            hasPatchedVortexKingpinx8Double = true;
            Debug.Log("[VortexKingpinx8Double] Master Prefab injection complete!");
        }
    }

    // ====================================================================================================
    // VL-49 TARANTULA CHANGES (QuadVTOL1)
    // Config Category: VL-49 Tarantula Changes
    // ====================================================================================================

    // ====================================================================================================
    // TARANTULA LYNCHPIN X14 DOUBLE
    // ====================================================================================================
    [HarmonyPatch(typeof(WeaponManager), "Awake")]
    public static class TarantulaLynchpinx14DoublePatch
    {
        private static bool hasPatchedTarantulaLynchpinx14Double = false;

        public static void Prefix()
        {
            if (!Plugin.EnableTarantulaLynchpinx14Double.Value) return;
            if (hasPatchedTarantulaLynchpinx14Double) return;

            WeaponMount doubleMount = CustomWeaponsReusedAssets.GetExternalLynchpinx14Double();
            if (doubleMount == null) return;

            // Add to QuadVTOL1 hardpoint sets 4 and 5
            var allWeaponManagers = Resources.FindObjectsOfTypeAll<WeaponManager>();

            foreach (var wm in allWeaponManagers)
            {
                if (wm == null || wm.transform == null || wm.transform.root == null) continue;

                if (wm.transform.root.name.Contains("QuadVTOL1"))
                {
                    if (wm.hardpointSets != null && wm.hardpointSets.Length > 5)
                    {
                        bool updated = false;

                        var hardpointSet4 = wm.hardpointSets[4];
                        if (hardpointSet4 != null && hardpointSet4.weaponOptions != null && !hardpointSet4.weaponOptions.Contains(doubleMount))
                        {
                            hardpointSet4.weaponOptions.Add(doubleMount);
                            updated = true;
                        }

                        var hardpointSet5 = wm.hardpointSets[5];
                        if (hardpointSet5 != null && hardpointSet5.weaponOptions != null && !hardpointSet5.weaponOptions.Contains(doubleMount))
                        {
                            hardpointSet5.weaponOptions.Add(doubleMount);
                            updated = true;
                        }

                        if (updated)
                        {
                            Debug.Log($"[TarantulaLynchpinx14Double] Successfully injected double rockets into {wm.gameObject.name} hardpoint sets 4 and 5.");
                        }
                    }
                }
            }

            hasPatchedTarantulaLynchpinx14Double = true;
            Debug.Log("[TarantulaLynchpinx14Double] Master Prefab injection complete!");
        }
    }

    // ====================================================================================================
    // TARANTULA KINGPIN X8 DOUBLE
    // ====================================================================================================
    [HarmonyPatch(typeof(WeaponManager), "Awake")]
    public static class TarantulaKingpinx8DoublePatch
    {
        private static bool hasPatchedTarantulaKingpinx8Double = false;

        public static void Prefix()
        {
            if (!Plugin.EnableTarantulaKingpinx8Double.Value) return;
            if (hasPatchedTarantulaKingpinx8Double) return;

            WeaponMount doubleMount = CustomWeaponsReusedAssets.GetExternalKingpinx8Double();
            if (doubleMount == null) return;

            // Add to QuadVTOL1 hardpoint sets 4 and 5
            var allWeaponManagers = Resources.FindObjectsOfTypeAll<WeaponManager>();

            foreach (var wm in allWeaponManagers)
            {
                if (wm == null || wm.transform == null || wm.transform.root == null) continue;

                if (wm.transform.root.name.Contains("QuadVTOL1"))
                {
                    if (wm.hardpointSets != null && wm.hardpointSets.Length > 5)
                    {
                        bool updated = false;

                        var hardpointSet4 = wm.hardpointSets[4];
                        if (hardpointSet4 != null && hardpointSet4.weaponOptions != null && !hardpointSet4.weaponOptions.Contains(doubleMount))
                        {
                            hardpointSet4.weaponOptions.Add(doubleMount);
                            updated = true;
                        }

                        var hardpointSet5 = wm.hardpointSets[5];
                        if (hardpointSet5 != null && hardpointSet5.weaponOptions != null && !hardpointSet5.weaponOptions.Contains(doubleMount))
                        {
                            hardpointSet5.weaponOptions.Add(doubleMount);
                            updated = true;
                        }

                        if (updated)
                        {
                            Debug.Log($"[TarantulaKingpinx8Double] Successfully injected double rockets into {wm.gameObject.name} hardpoint sets 4 and 5.");
                        }
                    }
                }
            }

            hasPatchedTarantulaKingpinx8Double = true;
            Debug.Log("[TarantulaKingpinx8Double] Master Prefab injection complete!");
        }
    }

    // ====================================================================================================
    // KR-67 IFRIT CHANGES (Multirole1)
    // Config Category: KR-67 Ifrit Changes
    // ====================================================================================================

    // ====================================================================================================
    // IFRIT LYNCHPIN X14 DOUBLE
    // ====================================================================================================
    [HarmonyPatch(typeof(WeaponManager), "Awake")]
    public static class IfritLynchpinx14DoublePatch
    {
        private static bool hasPatchedIfritLynchpinx14Double = false;

        public static void Prefix()
        {
            if (!Plugin.EnableIfritLynchpinx14Double.Value) return;
            if (hasPatchedIfritLynchpinx14Double) return;

            WeaponMount doubleMount = CustomWeaponsReusedAssets.GetExternalLynchpinx14Double();
            if (doubleMount == null) return;

            // Add to Multirole1 hardpoint sets 4 and 5
            var allWeaponManagers = Resources.FindObjectsOfTypeAll<WeaponManager>();

            foreach (var wm in allWeaponManagers)
            {
                if (wm == null || wm.transform == null || wm.transform.root == null) continue;

                if (wm.transform.root.name.Contains("Multirole1"))
                {
                    if (wm.hardpointSets != null && wm.hardpointSets.Length > 5)
                    {
                        bool updated = false;

                        var hardpointSet4 = wm.hardpointSets[4];
                        if (hardpointSet4 != null && hardpointSet4.weaponOptions != null && !hardpointSet4.weaponOptions.Contains(doubleMount))
                        {
                            hardpointSet4.weaponOptions.Add(doubleMount);
                            updated = true;
                        }

                        var hardpointSet5 = wm.hardpointSets[5];
                        if (hardpointSet5 != null && hardpointSet5.weaponOptions != null && !hardpointSet5.weaponOptions.Contains(doubleMount))
                        {
                            hardpointSet5.weaponOptions.Add(doubleMount);
                            updated = true;
                        }

                        if (updated)
                        {
                            Debug.Log($"[IfritLynchpinx14Double] Successfully injected double rockets into {wm.gameObject.name} hardpoint sets 4 and 5.");
                        }
                    }
                }
            }

            hasPatchedIfritLynchpinx14Double = true;
            Debug.Log("[IfritLynchpinx14Double] Master Prefab injection complete!");
        }
    }

    // ====================================================================================================
    // IFRIT KINGPIN X8 DOUBLE
    // ====================================================================================================
    [HarmonyPatch(typeof(WeaponManager), "Awake")]
    public static class IfritKingpinx8DoublePatch
    {
        private static bool hasPatchedIfritKingpinx8Double = false;

        public static void Prefix()
        {
            if (!Plugin.EnableIfritKingpinx8Double.Value) return;
            if (hasPatchedIfritKingpinx8Double) return;

            WeaponMount doubleMount = CustomWeaponsReusedAssets.GetExternalKingpinx8Double();
            if (doubleMount == null) return;

            // Add to Multirole1 hardpoint sets 4 and 5
            var allWeaponManagers = Resources.FindObjectsOfTypeAll<WeaponManager>();

            foreach (var wm in allWeaponManagers)
            {
                if (wm == null || wm.transform == null || wm.transform.root == null) continue;

                if (wm.transform.root.name.Contains("Multirole1"))
                {
                    if (wm.hardpointSets != null && wm.hardpointSets.Length > 5)
                    {
                        bool updated = false;

                        var hardpointSet4 = wm.hardpointSets[4];
                        if (hardpointSet4 != null && hardpointSet4.weaponOptions != null && !hardpointSet4.weaponOptions.Contains(doubleMount))
                        {
                            hardpointSet4.weaponOptions.Add(doubleMount);
                            updated = true;
                        }

                        var hardpointSet5 = wm.hardpointSets[5];
                        if (hardpointSet5 != null && hardpointSet5.weaponOptions != null && !hardpointSet5.weaponOptions.Contains(doubleMount))
                        {
                            hardpointSet5.weaponOptions.Add(doubleMount);
                            updated = true;
                        }

                        if (updated)
                        {
                            Debug.Log($"[IfritKingpinx8Double] Successfully injected double rockets into {wm.gameObject.name} hardpoint sets 4 and 5.");
                        }
                    }
                }
            }

            hasPatchedIfritKingpinx8Double = true;
            Debug.Log("[IfritKingpinx8Double] Master Prefab injection complete!");
        }
    }

    // ====================================================================================================
    // EW-25 MEDUSA CHANGES (EW1)
    // Config Category: EW-25 Medusa Changes
    // ====================================================================================================

    // ====================================================================================================
    // MEDUSA LASER BUFF
    // ====================================================================================================
    [HarmonyPatch(typeof(WeaponManager), "Awake")]
    public static class MedusaLaserPatch
    {
        private static bool hasPatchedLaser = false;

        public static void Prefix()
        {
            if (!Plugin.EnableMedusaLaserBuff.Value) return;
            if (hasPatchedLaser) return;

            var allTransforms = Resources.FindObjectsOfTypeAll<Transform>();
            bool patchedAny = false;

            foreach (var t in allTransforms)
            {
                if (t == null) continue;

                if (t.name.Contains("Laser_EW1"))
                {
                    bool success = TryPatchMedusaLaser(t.gameObject);

                    if (success)
                    {
                        Debug.Log($"[MedusaLaserBuff] Successfully modified laser power draw on: {t.gameObject.name}");
                        patchedAny = true;
                    }
                }
            }

            if (patchedAny)
            {
                hasPatchedLaser = true;
                Debug.Log("[MedusaLaserBuff] Master Prefab sweep for Medusa complete!");
            }
        }

        private static bool TryPatchMedusaLaser(GameObject laserRoot)
        {
            bool success = false;

            var allComponents = laserRoot.GetComponentsInChildren<MonoBehaviour>(true);

            foreach (var comp in allComponents)
            {
                if (comp == null) continue;

                if (comp.gameObject.name.Contains("Laser") || comp.GetType().Name.Contains("Laser"))
                {
                    if (comp.gameObject.GetComponent<ModifiedStatsFlag>() != null) continue;

                    var compTraverse = Traverse.Create(comp);

                    var powerField = compTraverse.Field("power");
                    if (powerField.FieldExists())
                    {
                        powerField.SetValue(Plugin.MedusaLaserPowerDraw.Value);
                        comp.gameObject.AddComponent<ModifiedStatsFlag>();
                        success = true;
                    }
                }
            }

            return success;
        }
    }

    // ====================================================================================================
    // MEDUSA LYNCHPIN X14 DOUBLE
    // ====================================================================================================
    [HarmonyPatch(typeof(WeaponManager), "Awake")]
    public static class MedusaLynchpinx14DoublePatch
    {
        private static bool hasPatchedMedusaLynchpinx14Double = false;

        public static void Prefix()
        {
            if (!Plugin.EnableMedusaLynchpinx14Double.Value) return;
            if (hasPatchedMedusaLynchpinx14Double) return;

            WeaponMount doubleMount = CustomWeaponsReusedAssets.GetExternalLynchpinx14Double();
            if (doubleMount == null) return;

            // Add to EW1 hardpoint set 3
            var allWeaponManagers = Resources.FindObjectsOfTypeAll<WeaponManager>();

            foreach (var wm in allWeaponManagers)
            {
                if (wm == null || wm.transform == null || wm.transform.root == null) continue;

                if (wm.transform.root.name.Contains("EW1"))
                {
                    if (wm.hardpointSets != null && wm.hardpointSets.Length > 3)
                    {
                        bool updated = false;

                        var hardpointSet3 = wm.hardpointSets[3];
                        if (hardpointSet3 != null && hardpointSet3.weaponOptions != null && !hardpointSet3.weaponOptions.Contains(doubleMount))
                        {
                            hardpointSet3.weaponOptions.Add(doubleMount);
                            updated = true;
                        }

                        if (updated)
                        {
                            Debug.Log($"[MedusaLynchpinx14Double] Successfully injected double rockets into {wm.gameObject.name} hardpoint set 3.");
                        }
                    }
                }
            }

            hasPatchedMedusaLynchpinx14Double = true;
            Debug.Log("[MedusaLynchpinx14Double] Master Prefab injection complete!");
        }
    }

    // ====================================================================================================
    // MEDUSA KINGPIN X8 DOUBLE
    // ====================================================================================================
    [HarmonyPatch(typeof(WeaponManager), "Awake")]
    public static class MedusaKingpinx8DoublePatch
    {
        private static bool hasPatchedMedusaKingpinx8Double = false;

        public static void Prefix()
        {
            if (!Plugin.EnableMedusaKingpinx8Double.Value) return;
            if (hasPatchedMedusaKingpinx8Double) return;

            WeaponMount doubleMount = CustomWeaponsReusedAssets.GetExternalKingpinx8Double();
            if (doubleMount == null) return;

            // Add to EW1 hardpoint set 3
            var allWeaponManagers = Resources.FindObjectsOfTypeAll<WeaponManager>();

            foreach (var wm in allWeaponManagers)
            {
                if (wm == null || wm.transform == null || wm.transform.root == null) continue;

                if (wm.transform.root.name.Contains("EW1"))
                {
                    if (wm.hardpointSets != null && wm.hardpointSets.Length > 3)
                    {
                        bool updated = false;

                        var hardpointSet3 = wm.hardpointSets[3];
                        if (hardpointSet3 != null && hardpointSet3.weaponOptions != null && !hardpointSet3.weaponOptions.Contains(doubleMount))
                        {
                            hardpointSet3.weaponOptions.Add(doubleMount);
                            updated = true;
                        }

                        if (updated)
                        {
                            Debug.Log($"[MedusaKingpinx8Double] Successfully injected double rockets into {wm.gameObject.name} hardpoint set 3.");
                        }
                    }
                }
            }

            hasPatchedMedusaKingpinx8Double = true;
            Debug.Log("[MedusaKingpinx8Double] Master Prefab injection complete!");
        }
    }



    // ====================================================================================================
    // MEDUSA SAM_RADAR2 SINGLE (R9 STRATOLANCE x1)
    // ====================================================================================================
    [HarmonyPatch(typeof(WeaponManager), "Awake")]
    public static class MedusaSAMRadar2SinglePatch
    {
        private static bool hasPatchedMedusaSAMRadar2Single = false;
        public static void Prefix()
        {
            if (!Plugin.EnableMedusaSAMRadar2Single.Value) return;
            if (hasPatchedMedusaSAMRadar2Single) return;
            
            WeaponMount singleMount = CustomWeaponsReusedAssets.GetExternalSAMRadar2Single();
            if (singleMount == null) return;
            
            // Add to EW1 hardpoint sets 3 and 4
            var allWeaponManagers = Resources.FindObjectsOfTypeAll<WeaponManager>();
            foreach (var wm in allWeaponManagers)
            {
                if (wm == null || wm.transform == null || wm.transform.root == null) continue;
                if (wm.transform.root.name.Contains("EW1"))
                {
                    if (wm.hardpointSets != null && wm.hardpointSets.Length > 4)
                    {
                        bool updated = false;
                        var hardpointSet3 = wm.hardpointSets[3];
                        if (hardpointSet3 != null && hardpointSet3.weaponOptions != null && !hardpointSet3.weaponOptions.Contains(singleMount))
                        {
                            hardpointSet3.weaponOptions.Add(singleMount);
                            updated = true;
                        }
                        var hardpointSet4 = wm.hardpointSets[4];
                        if (hardpointSet4 != null && hardpointSet4.weaponOptions != null && !hardpointSet4.weaponOptions.Contains(singleMount))
                        {
                            hardpointSet4.weaponOptions.Add(singleMount);
                            updated = true;
                        }
                        if (updated)
                        {
                            Debug.Log($"[MedusaSAMRadar2Single] Successfully injected R9 Stratolance x1 into {wm.gameObject.name} hardpoint sets 3 and 4.");
                        }
                    }
                }
            }
            hasPatchedMedusaSAMRadar2Single = true;
            Debug.Log("[MedusaSAMRadar2Single] Master Prefab injection complete!");
        }
    }



    // ====================================================================================================
    // MEDUSA SAM_RADAR2 DOUBLE (R9 STRATOLANCE x2)
    // ====================================================================================================
    [HarmonyPatch(typeof(WeaponManager), "Awake")]
    public static class MedusaSAMRadar2DoublePatch
    {
        private static bool hasPatchedMedusaSAMRadar2Double = false;

        public static void Prefix()
        {
            if (!Plugin.EnableMedusaSAMRadar2Double.Value) return;
            if (hasPatchedMedusaSAMRadar2Double) return;

            WeaponMount doubleMount = CustomWeaponsReusedAssets.GetExternalSAMRadar2Double();
            if (doubleMount == null) return;

            // Add to EW1 hardpoint set 4
            var allWeaponManagers = Resources.FindObjectsOfTypeAll<WeaponManager>();
            foreach (var wm in allWeaponManagers)
            {
                if (wm == null || wm.transform == null || wm.transform.root == null) continue;

                if (wm.transform.root.name.Contains("EW1"))
                {
                    if (wm.hardpointSets != null && wm.hardpointSets.Length > 4)
                    {
                        bool updated = false;

                        var hardpointSet4 = wm.hardpointSets[4];
                        if (hardpointSet4 != null && hardpointSet4.weaponOptions != null && !hardpointSet4.weaponOptions.Contains(doubleMount))
                        {
                            hardpointSet4.weaponOptions.Add(doubleMount);
                            updated = true;
                        }

                        if (updated)
                        {
                            Debug.Log($"[MedusaSAMRadar2Double] Successfully injected R9 Stratolance x2 into {wm.gameObject.name} hardpoint set 4.");
                        }
                    }
                }
            }

            hasPatchedMedusaSAMRadar2Double = true;
            Debug.Log("[MedusaSAMRadar2Double] Master Prefab injection complete!");
        }
    }



    // ====================================================================================================
    // SFB-81 DARKREACH CHANGES (Darkreach)
    // Config Category: SFB-81 Darkreach Changes
    // ====================================================================================================

    // ====================================================================================================
    // ALKYON AB-4 CHANGES (FastBomber1)
    // Config Category: Alkyon AB-4 Changes
    // ====================================================================================================

    // ====================================================================================================
    // BepInEx ConfigurationManager Attributes (Duck-Typed)
    // ====================================================================================================
#pragma warning disable CS0169, CS0414, CS0649
    internal sealed class ConfigurationManagerAttributes
    {
        public bool? ReadOnly;
        public bool? HideDefaultButton;
        public int? Order;
    }
#pragma warning restore CS0169, CS0414, CS0649
}