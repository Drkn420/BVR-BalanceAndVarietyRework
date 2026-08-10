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
        public const string BaseVersion = "1.0.7";

        // Expose the dynamically generated version hash for multiplayer desync checks
        public static string FullVersionWithHash { get; private set; }

        // Configuration Entries

        //Missile Balance Entries
        public static ConfigEntry<bool> EnableIRMissilesBuff;
        public static ConfigEntry<float> FlareCountMultiplier;
        public static ConfigEntry<float> FlareRejectionMultiplier;
        
        // Chicane Balance Entries
        public static ConfigEntry<bool> EnableChicaneProxyGun;
        public static ConfigEntry<bool> EnableChicaneScythesSingle;
        public static ConfigEntry<bool> EnableChicaneScythesDouble;
        public static ConfigEntry<bool> EnableChicaneInternalLynchpinx14;

        // Medusa Balance Entries
        public static ConfigEntry<bool> EnableMedusaLaserBuff;
        public static ConfigEntry<float> MedusaLaserPowerDraw;

        private void Awake()
        {
            // 1. Bind notices FIRST to guarantee they appear at the top of the ConfigManager window
            Config.Bind("0. Important Notice (Hover over entries for more information.)", "Restart Required", "Changes made here require a full game restart to apply.",
                new ConfigDescription("Please restart the game after changing any settings for them to take effect.", null,
                new ConfigurationManagerAttributes { ReadOnly = true, HideDefaultButton = true, Order = 100 }));

            var hashDisplay = Config.Bind("0. Important Notice (Hover over entries for more information.)", "Current Config Hash", "Calculating...",
                new ConfigDescription("Compare this 6-character hash with other players to ensure your settings match for multiplayer. (Updates on game restart)", null,
                new ConfigurationManagerAttributes { ReadOnly = true, HideDefaultButton = true, Order = 99 }));

            // 2. Bind functional configs
            EnableIRMissilesBuff = Config.Bind("Missile Balance", "Enable IR Missiles Buff", true, "Master toggle to enable the custom flare rejection and flare count multipliers.");
            FlareCountMultiplier = Config.Bind("Missile Balance", "Flare Count Multiplier", 2.0f, "Multiplies the total number of flares on all aircraft (e.g., 2.0 = double flares, 0.5 = half flares).");
            FlareRejectionMultiplier = Config.Bind("Missile Balance", "Flare Rejection Multiplier", 2.0f, "Multiplies the flare rejection stat on all IR missiles. Higher values make them harder to decoy (e.g., 2.0 = double rejection).");

            EnableChicaneProxyGun = Config.Bind("Chicane Balance", "Enable Chicane Proximity Fuse 30mm Gun", true, "Enables the proximity fuse on the Chicane's nosegun.");

            EnableChicaneScythesSingle = Config.Bind("Chicane Balance", "Enable Chicane Inner Wing Scythe x1", false, "Enables AAM-24 Single mounts onto the Chicane's inner stub pylons.");
            EnableChicaneScythesDouble = Config.Bind("Chicane Balance", "Enable Chicane Inner Wing Scythe x2", false, "Enables AAM-24 Double mounts onto the Chicane's inner stub pylons.");
            EnableChicaneInternalLynchpinx14 = Config.Bind("Chicane Balance", "Enable Chicane Internal Lynchpin x14", true, "Enables AGR-18 Lynchpin x14 rocket pod in the Chicane's internal bays.");

            EnableMedusaLaserBuff = Config.Bind("Medusa Balance", "Enable Medusa Laser Buff", true, "Master toggle to enable modifications to the Medusa's internal laser weapon.");
            MedusaLaserPowerDraw = Config.Bind("Medusa Balance", "Medusa Laser Power Draw Value", 60.0f, "Sets the power draw of the Medusa's laser. (Vanilla is 120).");

            // 3. Generate Config Hash and format the new version string
            string configHash = GenerateConfigHash();
            FullVersionWithHash = $"{BaseVersion}-{configHash}";

            // 4. Update the notice retroactively with the generated hash
            hashDisplay.Value = configHash;

            Logger.LogInfo($"Mod Version Loaded: {FullVersionWithHash}");

            // 5. Register all Harmony patches
            Harmony.CreateAndPatchAll(typeof(StatsPatch));
            Harmony.CreateAndPatchAll(typeof(ProxyGunPatch));
            Harmony.CreateAndPatchAll(typeof(ChicaneScythePatch));
            Harmony.CreateAndPatchAll(typeof(ChicaneInternalLynchpinx14Patch));
            Harmony.CreateAndPatchAll(typeof(MedusaLaserPatch));

            Logger.LogInfo("BVR - Balance and Variety Rework Mod Loaded!");
        }

        // Generates a short, deterministic alphanumeric hash based on the current config values
        private string GenerateConfigHash()
        {
            string combinedConfigs = $"{EnableIRMissilesBuff.Value}_{FlareCountMultiplier.Value}_{FlareRejectionMultiplier.Value}_" +
                                     $"{EnableChicaneProxyGun.Value}_{EnableChicaneScythesSingle.Value}_{EnableChicaneScythesDouble.Value}_{EnableChicaneInternalLynchpinx14.Value}_" +
                                     $"{EnableMedusaLaserBuff.Value}_{MedusaLaserPowerDraw.Value}";

            using (var md5 = MD5.Create())
            {
                byte[] hashBytes = md5.ComputeHash(Encoding.UTF8.GetBytes(combinedConfigs));
                return BitConverter.ToString(hashBytes).Replace("-", "").Substring(0, 6);
            }
        }
    }



    // ====================================================================================================
    // 1. IR MISSILES BUFF
    // Config Category: Missile Balance
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
    // 2. CHICANE PROXIMITY FUSE NOSEGUN
    // Config Category: Chicane Balance
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
    // 3. CHICANE INNER WING SCYTHES
    // Config Category: Chicane Balance
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
                if (wm.transform.root.name.Contains("AttackHelo1"))
                {
                    if (wm.hardpointSets != null && wm.hardpointSets.Length > 2)
                    {
                        var stubPylons = wm.hardpointSets[2];
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
    // 3.5 CHICANE INTERNAL BAY LYNCHPIN X14
    // Config Category: Chicane Balance
    // ====================================================================================================

    [HarmonyPatch(typeof(WeaponManager), "Awake")]
    public static class ChicaneInternalLynchpinx14Patch
    {
        private static bool hasPatchedInternalLynchpinx14 = false;
        private static GameObject prefabVault; // Holds our custom prefabs safely

        public static void Prefix()
        {
            if (!Plugin.EnableChicaneInternalLynchpinx14.Value) return;
            if (hasPatchedInternalLynchpinx14) return;

            var allMounts = Resources.FindObjectsOfTypeAll<WeaponMount>();
            WeaponMount singleMount = allMounts.FirstOrDefault(w => w.name == "RocketPod1_single");

            if (singleMount == null || singleMount.prefab == null) return;

            // 1. Create a "Prefab Vault"
            // This is a disabled root object. Anything placed inside will have activeInHierarchy = false.
            if (prefabVault == null)
            {
                prefabVault = new GameObject("BVR_PrefabVault");
                UnityEngine.Object.DontDestroyOnLoad(prefabVault);
                prefabVault.SetActive(false); 
                // HideAndDontSave completely hides the vault from RUE and the scene hierarchy
                prefabVault.hideFlags = HideFlags.HideAndDontSave; 
            }

            // 2. Duplicate the GameObject as a child of the disabled vault
            GameObject doublePrefab = UnityEngine.Object.Instantiate(singleMount.prefab, prefabVault.transform);
            doublePrefab.name = "RocketPod1_internal_double";
            
            // This satisfies the requirement: activeSelf = true, but active(InHierarchy) = false, so that the weapon will be visible when spawned, but it wont float randomly in the game world when the game starts.
            doublePrefab.SetActive(true);

            // Set the local position of the entire double pod prefab assembly
            doublePrefab.transform.localPosition = new Vector3(0f, -0.05f, 0f);

            Transform firstPod = doublePrefab.transform.Find("pod");
            if (firstPod != null)
            {
                firstPod.localPosition = new Vector3(0.13f, -0.15f, 0.3f); 
                firstPod.localEulerAngles = Vector3.zero; 
                Transform secondPod = UnityEngine.Object.Instantiate(firstPod.gameObject, doublePrefab.transform).transform;
                secondPod.name = "pod";
                secondPod.localPosition = new Vector3(-0.13f, -0.15f, 0.3f); 
                secondPod.localEulerAngles = Vector3.zero; 
            }

            // 3. Duplicate the WeaponMount and configure it
            WeaponMount doubleMount = UnityEngine.Object.Instantiate(singleMount);
            doubleMount.name = "RocketPod1_internal_double";
            doubleMount.prefab = doublePrefab;

            var traverseMount = Traverse.Create(doubleMount);

            // Update basic definition names
            if (traverseMount.Field("mountName").FieldExists())
                traverseMount.Field("mountName").SetValue("AGR-18 Lynchpin x14");
                
            if (traverseMount.Field("jsonKey").FieldExists())
                traverseMount.Field("jsonKey").SetValue("RocketPod1_internal_double");

            // Fix for internal bay requirement breaking the spawn logic
            if (traverseMount.Field("missileBay").FieldExists())
                traverseMount.Field("missileBay").SetValue(true);
            else if (traverseMount.Property("missileBay").PropertyExists())
                traverseMount.Property("missileBay").SetValue(true);

            // Fix for Network Lookup Index conflict destroying the ghost duplicate
            int customNetworkId = Mathf.Abs("RocketPod1_internal_double".GetHashCode()); 
            
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

            // 4. Add to AttackHelo1 Internal Bays
            var allWeaponManagers = Resources.FindObjectsOfTypeAll<WeaponManager>();
            foreach (var wm in allWeaponManagers)
            {
                if (wm.transform.root.name.Contains("AttackHelo1"))
                {
                    if (wm.hardpointSets != null && wm.hardpointSets.Length > 1)
                    {
                        var internalBays = wm.hardpointSets[1];

                        if (internalBays.weaponOptions != null && !internalBays.weaponOptions.Contains(doubleMount))
                        {
                            internalBays.weaponOptions.Add(doubleMount);
                            Debug.Log($"[ChicaneInternalLynchpinx14] Successfully injected double rockets into {wm.gameObject.name} internal bays.");
                        }
                    }
                }
            }

            hasPatchedInternalLynchpinx14 = true;
            Debug.Log("[ChicaneInternalLynchpinx14] Master Prefab sweep and generation complete!");
        }
    }
   


    // ====================================================================================================
    // 4. MEDUSA LASER BUFF
    // Config Category: Medusa Balance
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