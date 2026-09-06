using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BalanceAndVarietyRework
{
    [BepInPlugin("com.Draken0015.BVR", "Balance and Variety Rework", BaseVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string BaseVersion = "1.3.0";
        public static Plugin Instance { get; private set; }
        public static string FullVersionWithHash { get; private set; }
        public static List<ConfigEntryBase> AllRegisteredConfigs = new List<ConfigEntryBase>();

        // Static references for non-weapon patches to access their config values
        public static ConfigEntry<bool> EnableIRMissilesBuff;
        public static ConfigEntry<float> FlareCountMultiplier;
        public static ConfigEntry<float> FlareRejectionMultiplier;
        public static ConfigEntry<bool> EnableR9LockPersistenceBuff;
        public static ConfigEntry<float> R9LockPersistenceValue;
        public static ConfigEntry<bool> EnableRAM45LockPersistenceBuff;
        public static ConfigEntry<float> RAM45LockPersistenceValue;
        public static ConfigEntry<bool> EnableR9SARHRelock;
        public static ConfigEntry<float> R9SARHRelockDelay;
        public static ConfigEntry<int> R9SARHRelockAttempts;
        public static ConfigEntry<bool> EnableRAM45SARHRelock;
        public static ConfigEntry<float> RAM45SARHRelockDelay;
        public static ConfigEntry<int> RAM45SARHRelockAttempts;
        public static ConfigEntry<float> ALMC450RCS;
        public static ConfigEntry<float> AGM99RCS;
        public static ConfigEntry<float> AShM300RCS;
        public static ConfigEntry<float> ALND420ktRCS;
        public static ConfigEntry<bool> EnableChicaneProxyGun;
        public static ConfigEntry<bool> EnableChicaneBayPylonSymmetryFix;
        public static ConfigEntry<bool> EnableMedusaLaserBuff;
        public static ConfigEntry<float> MedusaLaserPowerDraw;

        private void Awake()
        {
            Instance = this;
            BindImportantNotices();
            BindFunctionalConfigs();
            BindBlueprinterWeapons();
            TryImportPendingConfigSeed();
            FinalizeVersionAndHash();
            RegisterHarmonyPatches();
            BlueprintWeaponToggleSystem.Initialize(this);
            Logger.LogInfo("BVR - Balance and Variety Rework Mod Loaded!");
        }

        private ConfigEntry<T> BindAndTrack<T>(string section, string key, T defaultValue, string description, bool exportToSeed = true, ConfigurationManagerAttributes attributes = null)
        {
            ConfigDescription configDesc = attributes != null ? new ConfigDescription(description, null, attributes) : new ConfigDescription(description);
            var entry = Config.Bind(section, key, defaultValue, configDesc);
            if (exportToSeed) AllRegisteredConfigs.Add(entry);
            return entry;
        }

        private void BindImportantNotices()
        {
            BindAndTrack("Important Notices", "Restart Required", "Changes made here require a full game restart to apply.", "Please restart the game after changing any settings.", false, new ConfigurationManagerAttributes { ReadOnly = true, HideDefaultButton = true, Order = 100 });
            BindAndTrack("Important Notices", "Mod Version", $"v{BaseVersion}", "The currently installed version.", false, new ConfigurationManagerAttributes { ReadOnly = true, HideDefaultButton = true, Order = 99 });
            BindAndTrack("Important Notices", "Current Config Hash", "Calculating...", "Compare this hash with other players.", false, new ConfigurationManagerAttributes { ReadOnly = true, HideDefaultButton = true, Order = 98 });
            BindAndTrack("Important Notices", "Current Config Seed", "Calculating...", "Copy this seed to share your configuration.", false, new ConfigurationManagerAttributes { ReadOnly = true, HideDefaultButton = true, Order = 97 });
            BindAndTrack("Important Notices", "Import Config Seed", "", "Paste a seed here and restart.", false, new ConfigurationManagerAttributes { HideDefaultButton = true, Order = 96 });
        }

        private void BindFunctionalConfigs()
        {
            EnableIRMissilesBuff = BindAndTrack("Missile Balance - IR", "Enable IR Missiles Buff", true, "Master toggle for IR buffs.");
            FlareCountMultiplier = BindAndTrack("Missile Balance - IR", "Flare Count Multiplier", 2.0f, "Multiplies total flares.");
            FlareRejectionMultiplier = BindAndTrack("Missile Balance - IR", "Flare Rejection Multiplier", 2.0f, "Multiplies flare rejection.");

            EnableR9LockPersistenceBuff = BindAndTrack("Missile Balance - SARH", "Enable R9 Lock Persistence", true, "Master toggle.");
            R9LockPersistenceValue = BindAndTrack("Missile Balance - SARH", "R9 Lock Persistence Value", 3.0f, "Lock persistence duration.");
            EnableRAM45LockPersistenceBuff = BindAndTrack("Missile Balance - SARH", "Enable RAM45 Lock Persistence", true, "Master toggle.");
            RAM45LockPersistenceValue = BindAndTrack("Missile Balance - SARH", "RAM45 Lock Persistence Value", 3.0f, "Lock persistence duration.");

            EnableR9SARHRelock = BindAndTrack("Missile Balance - SARH", "Enable R9 SARH Relock", true, "Master toggle.");
            R9SARHRelockDelay = BindAndTrack("Missile Balance - SARH", "R9 SARH Relock Delay", 3.0f, "Delay before relock.");
            R9SARHRelockAttempts = BindAndTrack("Missile Balance - SARH", "R9 SARH Relock Attempts", 0, "0 = infinite.");
            EnableRAM45SARHRelock = BindAndTrack("Missile Balance - SARH", "Enable RAM45 SARH Relock", true, "Master toggle.");
            RAM45SARHRelockDelay = BindAndTrack("Missile Balance - SARH", "RAM45 SARH Relock Delay", 3.0f, "Delay before relock.");
            RAM45SARHRelockAttempts = BindAndTrack("Missile Balance - SARH", "RAM45 SARH Relock Attempts", 0, "0 = infinite.");

            ALMC450RCS = BindAndTrack("Missile Balance - Cruise", "ALM-C450 RCS", 0.0005f, "Vanilla is 0.005.");
            AGM99RCS = BindAndTrack("Missile Balance - Cruise", "AGM-99 RCS", 0.008f, "Vanilla is 0.008.");
            AShM300RCS = BindAndTrack("Missile Balance - Cruise", "AShM-300 RCS", 0.005f, "Vanilla is 0.005.");
            ALND420ktRCS = BindAndTrack("Missile Balance - Cruise", "ALND-4 (20kt) RCS", 0.001f, "Vanilla is 0.005.");

            EnableChicaneProxyGun = BindAndTrack("SAH-46 Chicane Changes", "Enable Proximity Fuse 30mm Gun", true, "Enables proxy fuse.");
            EnableChicaneBayPylonSymmetryFix = BindAndTrack("SAH-46 Chicane Changes", "Enable Bay Pylon Symmetry Fix", true, "Centers right bay pylon.");

            EnableMedusaLaserBuff = BindAndTrack("EW-25 Medusa Changes", "Enable Laser Buff", true, "Master toggle.");
            MedusaLaserPowerDraw = BindAndTrack("EW-25 Medusa Changes", "Laser Power Draw Value", 60.0f, "Vanilla is 120.");
        }

        private void BindBlueprinterWeapons()
        {
            foreach (var def in BlueprintWeaponRegistry.Definitions)
                def.ConfigEntry = BindAndTrack(def.Section, def.Key, def.DefaultValue, def.Description);
        }

        private void TryImportPendingConfigSeed()
        {
            var importEntry = Config["Important Notices", "Import Config Seed"];
            if (string.IsNullOrWhiteSpace(importEntry.BoxedValue as string)) return;
            if (TryImportConfigSeed(importEntry.BoxedValue as string)) Logger.LogInfo("BVR - Seed imported.");
            else Logger.LogWarning("BVR - Seed import failed.");
            importEntry.BoxedValue = string.Empty;
        }

        private void FinalizeVersionAndHash()
        {
            string hash = GenerateConfigHash();
            FullVersionWithHash = $"{BaseVersion}-{hash}";
            Config["Important Notices", "Current Config Hash"].BoxedValue = hash;
            Config["Important Notices", "Current Config Seed"].BoxedValue = GenerateConfigSeed();
            Config["Important Notices", "Mod Version"].BoxedValue = $"v{BaseVersion}";
            Config.Save();
        }

        private void RegisterHarmonyPatches()
        {
            Type[] patchTypes = { typeof(StatsPatch), typeof(SARHLockPersistencePatch), typeof(SARHRelockPatch), typeof(CruiseMissileRCSPatch), typeof(ProxyGunPatch), typeof(ChicaneBayPylonSymmetryFixPatch), typeof(MedusaLaserPatch), typeof(BlueprintWeaponDisablePatch) };
            foreach (Type t in patchTypes) Harmony.CreateAndPatchAll(t);
        }

        private const string SeedFormatPrefix = "BVR1";
        private const string SeedPayloadPrefix = "BVRSEED";
        private string GenerateConfigHash() { using (var md5 = MD5.Create()) return BitConverter.ToString(md5.ComputeHash(Encoding.UTF8.GetBytes(GenerateSeedPayload(false)))).Replace("-", "").Substring(0, 6); }
        private string GenerateConfigSeed() => $"{SeedFormatPrefix}-{ToUrlSafeBase64(GenerateSeedPayload(true))}";
        private string GenerateSeedPayload(bool meta)
        {
            StringBuilder p = new StringBuilder();
            p.Append(meta ? SeedPayloadPrefix + "|" + BaseVersion : "BVRHASH");
            foreach (var e in AllRegisteredConfigs.OrderBy(c => c.Definition.Section).ThenBy(c => c.Definition.Key))
                p.Append('|').Append(e.Definition.Key).Append(':').Append(GetTypeKey(e.SettingType)).Append(':').Append(Uri.EscapeDataString(ConvertValueToString(e.BoxedValue, e.SettingType)));
            return p.ToString();
        }
        private bool TryImportConfigSeed(string seed)
        {
            try {
                if (string.IsNullOrWhiteSpace(seed)) return false;
                seed = seed.Trim(); if (seed.StartsWith(SeedFormatPrefix + "-")) seed = seed.Substring(SeedFormatPrefix.Length + 1);
                string payload = FromUrlSafeBase64(seed); if (string.IsNullOrEmpty(payload)) return false;
                string[] parts = payload.Split('|'); if (parts.Length < 2 || parts[0] != SeedPayloadPrefix) return false;
                var dict = AllRegisteredConfigs.ToDictionary(c => c.Definition.Key, c => c); int count = 0;
                for (int i = 2; i < parts.Length; i++) {
                    string[] ep = parts[i].Split(':'); if (ep.Length != 3) continue;
                    if (dict.TryGetValue(ep[0], out ConfigEntryBase entry)) { entry.BoxedValue = ConvertStringToValue(Uri.UnescapeDataString(ep[2]), entry.SettingType); count++; }
                }
                if (count > 0) Config.Save(); return count > 0;
            } catch { return false; }
        }
        private static string GetTypeKey(Type t) => t == typeof(bool) ? "bool" : t == typeof(int) ? "int" : t == typeof(float) ? "float" : t == typeof(double) ? "double" : t == typeof(long) ? "long" : t == typeof(string) ? "string" : t.Name.ToLowerInvariant();
        private static string ConvertValueToString(object v, Type t) => v == null ? "" : v is bool b ? (b ? "True" : "False") : v is float f ? f.ToString("R", CultureInfo.InvariantCulture) : v is double d ? d.ToString("R", CultureInfo.InvariantCulture) : v is int i ? i.ToString(CultureInfo.InvariantCulture) : v is long l ? l.ToString(CultureInfo.InvariantCulture) : Convert.ToString(v, CultureInfo.InvariantCulture) ?? "";
        private static object ConvertStringToValue(string r, Type t) { Type u = Nullable.GetUnderlyingType(t) ?? t; if (u == typeof(string)) return r ?? ""; if (string.IsNullOrEmpty(r)) return u.IsValueType ? Activator.CreateInstance(u) : null; if (u == typeof(bool)) return r == "1" || bool.Parse(r); if (u == typeof(float)) return float.Parse(r, NumberStyles.Float, CultureInfo.InvariantCulture); if (u == typeof(double)) return double.Parse(r, NumberStyles.Float, CultureInfo.InvariantCulture); if (u == typeof(int)) return int.Parse(r, CultureInfo.InvariantCulture); if (u == typeof(long)) return long.Parse(r, CultureInfo.InvariantCulture); return Convert.ChangeType(r, u, CultureInfo.InvariantCulture); }
        private static string ToUrlSafeBase64(string t) => Convert.ToBase64String(Encoding.UTF8.GetBytes(t)).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        private static string FromUrlSafeBase64(string d) { if (string.IsNullOrWhiteSpace(d)) return null; d = d.Replace('-', '+').Replace('_', '/'); int m = d.Length % 4; if (m == 2) d += "=="; else if (m == 3) d += "="; else if (m == 1) return null; try { return Encoding.UTF8.GetString(Convert.FromBase64String(d)); } catch { return null; } }
    }

    internal class BlueprintWeaponDefinition
    {
        public string Section, Key, Description; public bool DefaultValue;
        public string[] AircraftRootNames; public string AircraftRootContains, ExcludeRootContains;
        public int[] HardpointSets; public string[] BlueprintKeys;
        public ConfigEntry<bool> ConfigEntry;
    }

    internal static class BlueprintWeaponRegistry
    {
        public static readonly List<BlueprintWeaponDefinition> Definitions = new List<BlueprintWeaponDefinition>
        {
            new BlueprintWeaponDefinition { Section = "CI-22 Cricket Changes", Key = "Enable Cricket Kingpin x8 Double", Description = "Enables Blueprinter BVR_Rocket2_4Podx2 on sets 2, 3", DefaultValue = true, AircraftRootNames = new[] { "COIN" }, AircraftRootContains = "COIN", HardpointSets = new[] { 2, 3 }, BlueprintKeys = new[] { "BVR_Rocket2_4Podx2" } },
            new BlueprintWeaponDefinition { Section = "CI-22 Cricket Changes", Key = "Enable Cricket Lynchpin x14 Double", Description = "Enables Blueprinter BVR_RocketPod1_double on sets 2, 3", DefaultValue = true, AircraftRootNames = new[] { "COIN" }, AircraftRootContains = "COIN", HardpointSets = new[] { 2, 3 }, BlueprintKeys = new[] { "BVR_RocketPod1_double" } },
            new BlueprintWeaponDefinition { Section = "T/A-30 Compass Changes", Key = "Enable Compass Kingpin x8 Double", Description = "Enables Blueprinter BVR_Rocket2_4Podx2 on set 1", DefaultValue = true, AircraftRootNames = new[] { "trainer" }, AircraftRootContains = "trainer", HardpointSets = new[] { 1 }, BlueprintKeys = new[] { "BVR_Rocket2_4Podx2" } },
            new BlueprintWeaponDefinition { Section = "T/A-30 Compass Changes", Key = "Enable Compass Lynchpin x14 Double", Description = "Enables Blueprinter BVR_RocketPod1_double on set 1", DefaultValue = true, AircraftRootNames = new[] { "trainer" }, AircraftRootContains = "trainer", HardpointSets = new[] { 1 }, BlueprintKeys = new[] { "BVR_RocketPod1_double" } },
            new BlueprintWeaponDefinition { Section = "VT-7 Vagrant Changes", Key = "Enable Vagrant Kingpin x8 Double", Description = "Enables Blueprinter BVR_Rocket2_4Podx2 on set 3", DefaultValue = true, AircraftRootNames = new[] { "VTOLTrainer1" }, AircraftRootContains = "VTOLTrainer1", HardpointSets = new[] { 3 }, BlueprintKeys = new[] { "BVR_Rocket2_4Podx2" } },
            new BlueprintWeaponDefinition { Section = "VT-7 Vagrant Changes", Key = "Enable Vagrant Lynchpin x14 Double", Description = "Enables Blueprinter BVR_RocketPod1_double on set 3", DefaultValue = true, AircraftRootNames = new[] { "VTOLTrainer1" }, AircraftRootContains = "VTOLTrainer1", HardpointSets = new[] { 3 }, BlueprintKeys = new[] { "BVR_RocketPod1_double" } },
            new BlueprintWeaponDefinition { Section = "UH-90 Ibis Changes", Key = "Enable Ibis Kingpin x8 Double", Description = "Enables Blueprinter BVR_Rocket2_4Podx2 on sets 0, 1", DefaultValue = true, AircraftRootNames = new[] { "UtilityHelo1" }, AircraftRootContains = "UtilityHelo1", HardpointSets = new[] { 0, 1 }, BlueprintKeys = new[] { "BVR_Rocket2_4Podx2" } },
            new BlueprintWeaponDefinition { Section = "UH-90 Ibis Changes", Key = "Enable Ibis Lynchpin x14 Double", Description = "Enables Blueprinter BVR_RocketPod1_double on sets 0, 1", DefaultValue = true, AircraftRootNames = new[] { "UtilityHelo1" }, AircraftRootContains = "UtilityHelo1", HardpointSets = new[] { 0, 1 }, BlueprintKeys = new[] { "BVR_RocketPod1_double" } },
            new BlueprintWeaponDefinition { Section = "SAH-46 Chicane Changes", Key = "Enable Chicane Scythe x2", Description = "Enables Blueprinter AAM2_double on set 2", DefaultValue = true, AircraftRootNames = new[] { "AttackHelo1" }, AircraftRootContains = "AttackHelo1", HardpointSets = new[] { 2 }, BlueprintKeys = new[] { "AAM2_double" } },
            new BlueprintWeaponDefinition { Section = "SAH-46 Chicane Changes", Key = "Enable Chicane Scythe x1", Description = "Enables Blueprinter AAM2_single on set 2", DefaultValue = true, AircraftRootNames = new[] { "AttackHelo1" }, AircraftRootContains = "AttackHelo1", HardpointSets = new[] { 2 }, BlueprintKeys = new[] { "AAM2_single" } },
            new BlueprintWeaponDefinition { Section = "SAH-46 Chicane Changes", Key = "Enable Chicane Internal Kingpin x8", Description = "Enables Blueprinter BVR_Rocket2_4Podx2_BayDoor on set 1", DefaultValue = true, AircraftRootNames = new[] { "AttackHelo1" }, AircraftRootContains = "AttackHelo1", HardpointSets = new[] { 1 }, BlueprintKeys = new[] { "BVR_Rocket2_4Podx2_BayDoor" } },
            new BlueprintWeaponDefinition { Section = "SAH-46 Chicane Changes", Key = "Enable Chicane Internal Lynchpin x14", Description = "Enables Blueprinter BVR_RocketPod1_double_BayDoor on set 1", DefaultValue = true, AircraftRootNames = new[] { "AttackHelo1" }, AircraftRootContains = "AttackHelo1", HardpointSets = new[] { 1 }, BlueprintKeys = new[] { "BVR_RocketPod1_double_BayDoor" } },
            new BlueprintWeaponDefinition { Section = "FS-12 Revoker Changes", Key = "Enable Revoker Kingpin x8 Double", Description = "Enables Blueprinter BVR_Rocket2_4Podx2 on set 2", DefaultValue = true, AircraftRootNames = new[] { "Fighter1" }, AircraftRootContains = "Fighter1", ExcludeRootContains = "SmallFighter1", HardpointSets = new[] { 2 }, BlueprintKeys = new[] { "BVR_Rocket2_4Podx2" } },
            new BlueprintWeaponDefinition { Section = "FS-12 Revoker Changes", Key = "Enable Revoker Lynchpin x14 Double", Description = "Enables Blueprinter BVR_RocketPod1_double on set 2", DefaultValue = true, AircraftRootNames = new[] { "Fighter1" }, AircraftRootContains = "Fighter1", ExcludeRootContains = "SmallFighter1", HardpointSets = new[] { 2 }, BlueprintKeys = new[] { "BVR_RocketPod1_double" } },
            new BlueprintWeaponDefinition { Section = "FS-20 Vortex Changes", Key = "Enable Vortex Kingpin x8 Double", Description = "Enables Blueprinter BVR_Rocket2_4Podx2 on set 3", DefaultValue = true, AircraftRootNames = new[] { "SmallFighter1" }, AircraftRootContains = "SmallFighter1", HardpointSets = new[] { 3 }, BlueprintKeys = new[] { "BVR_Rocket2_4Podx2" } },
            new BlueprintWeaponDefinition { Section = "FS-20 Vortex Changes", Key = "Enable Vortex Lynchpin x14 Double", Description = "Enables Blueprinter BVR_RocketPod1_double on set 3", DefaultValue = true, AircraftRootNames = new[] { "SmallFighter1" }, AircraftRootContains = "SmallFighter1", HardpointSets = new[] { 3 }, BlueprintKeys = new[] { "BVR_RocketPod1_double" } },
            new BlueprintWeaponDefinition { Section = "VL-49 Tarantula Changes", Key = "Enable Tarantula Kingpin x8 Double", Description = "Enables Blueprinter BVR_Rocket2_4Podx2 on sets 4, 5", DefaultValue = true, AircraftRootNames = new[] { "QuadVTOL1" }, AircraftRootContains = "QuadVTOL1", HardpointSets = new[] { 4, 5 }, BlueprintKeys = new[] { "BVR_Rocket2_4Podx2" } },
            new BlueprintWeaponDefinition { Section = "VL-49 Tarantula Changes", Key = "Enable Tarantula Lynchpin x14 Double", Description = "Enables Blueprinter BVR_RocketPod1_double on sets 4, 5", DefaultValue = true, AircraftRootNames = new[] { "QuadVTOL1" }, AircraftRootContains = "QuadVTOL1", HardpointSets = new[] { 4, 5 }, BlueprintKeys = new[] { "BVR_RocketPod1_double" } },
            new BlueprintWeaponDefinition { Section = "VL-49 Tarantula Changes", Key = "Enable Tarantula 20mm Rotary Cannon", Description = "Enables Blueprinter BVR_turret_20mm_rotary on set 3", DefaultValue = true, AircraftRootNames = new[] { "QuadVTOL1" }, AircraftRootContains = "QuadVTOL1", HardpointSets = new[] { 3 }, BlueprintKeys = new[] { "BVR_turret_20mm_rotary" } },
            new BlueprintWeaponDefinition { Section = "VL-49 Tarantula Changes", Key = "Enable Tarantula 57mm Side Mount", Description = "Enables Blueprinter BVR_turret_57mm_SideMount on set 2", DefaultValue = true, AircraftRootNames = new[] { "QuadVTOL1" }, AircraftRootContains = "QuadVTOL1", HardpointSets = new[] { 2 }, BlueprintKeys = new[] { "BVR_turret_57mm_SideMount" } },
            new BlueprintWeaponDefinition { Section = "VL-49 Tarantula Changes", Key = "Enable Tarantula 57mm Belly Mount", Description = "Enables Blueprinter BVR_turret_57mm_BellyMount on set 2", DefaultValue = true, AircraftRootNames = new[] { "QuadVTOL1" }, AircraftRootContains = "QuadVTOL1", HardpointSets = new[] { 2 }, BlueprintKeys = new[] { "BVR_turret_57mm_BellyMount" } },
            new BlueprintWeaponDefinition { Section = "KR-67 Ifrit Changes", Key = "Enable Ifrit Kingpin x8 Double", Description = "Enables Blueprinter BVR_Rocket2_4Podx2 on set 4", DefaultValue = true, AircraftRootNames = new[] { "Multirole1" }, AircraftRootContains = "Multirole1", HardpointSets = new[] { 4 }, BlueprintKeys = new[] { "BVR_Rocket2_4Podx2" } },
            new BlueprintWeaponDefinition { Section = "KR-67 Ifrit Changes", Key = "Enable Ifrit Lynchpin x14 Double", Description = "Enables Blueprinter BVR_RocketPod1_double on set 4", DefaultValue = true, AircraftRootNames = new[] { "Multirole1" }, AircraftRootContains = "Multirole1", HardpointSets = new[] { 4 }, BlueprintKeys = new[] { "BVR_RocketPod1_double" } },
            new BlueprintWeaponDefinition { Section = "EW-25 Medusa Changes", Key = "Enable Medusa Kingpin x8 Double", Description = "Enables Blueprinter BVR_Rocket2_4Podx2 on sets 3, 4", DefaultValue = true, AircraftRootNames = new[] { "EW1" }, AircraftRootContains = "EW1", HardpointSets = new[] { 3, 4 }, BlueprintKeys = new[] { "BVR_Rocket2_4Podx2" } },
            new BlueprintWeaponDefinition { Section = "EW-25 Medusa Changes", Key = "Enable Medusa Lynchpin x14 Double", Description = "Enables Blueprinter BVR_RocketPod1_double on sets 3, 4", DefaultValue = true, AircraftRootNames = new[] { "EW1" }, AircraftRootContains = "EW1", HardpointSets = new[] { 3, 4 }, BlueprintKeys = new[] { "BVR_RocketPod1_double" } },
            new BlueprintWeaponDefinition { Section = "EW-25 Medusa Changes", Key = "Enable Medusa RAM-45 x3", Description = "Enables Blueprinter BVR_SAM_Radar1x3 on sets 3, 4", DefaultValue = true, AircraftRootNames = new[] { "EW1" }, AircraftRootContains = "EW1", HardpointSets = new[] { 3, 4 }, BlueprintKeys = new[] { "BVR_SAM_Radar1x3" } },
            new BlueprintWeaponDefinition { Section = "EW-25 Medusa Changes", Key = "Enable Medusa Internal RAM-45 x3", Description = "Enables Blueprinter BVR_SAM_Radar1x3_Internal on set 1", DefaultValue = true, AircraftRootNames = new[] { "EW1" }, AircraftRootContains = "EW1", HardpointSets = new[] { 1 }, BlueprintKeys = new[] { "BVR_SAM_Radar1x3_Internal" } },
            new BlueprintWeaponDefinition { Section = "EW-25 Medusa Changes", Key = "Enable Medusa R9 Stratolance x2", Description = "Enables Blueprinter BVR_SAM_Radar2x2 on sets 3, 4", DefaultValue = true, AircraftRootNames = new[] { "EW1" }, AircraftRootContains = "EW1", HardpointSets = new[] { 3, 4 }, BlueprintKeys = new[] { "BVR_SAM_Radar2x2" } },
            new BlueprintWeaponDefinition { Section = "EW-25 Medusa Changes", Key = "Enable Medusa Internal R9 Stratolance x2", Description = "Enables Blueprinter BVR_SAM_Radar2x2_Internal on set 1", DefaultValue = true, AircraftRootNames = new[] { "EW1" }, AircraftRootContains = "EW1", HardpointSets = new[] { 1 }, BlueprintKeys = new[] { "BVR_SAM_Radar2x2_Internal" } }
        };
    }

#pragma warning disable CS0169, CS0414, CS0649
    internal sealed class ConfigurationManagerAttributes { public bool? ReadOnly; public bool? HideDefaultButton; public int? Order; }
#pragma warning restore CS0169, CS0414, CS0649
    public class ModifiedStatsFlag : MonoBehaviour { }
    internal static class ObjectNameUtility
    {
        public static string RemoveCloneSuffix(string n) => string.IsNullOrEmpty(n) ? "" : n.Replace("(Clone)", "");
        public static string GetCleanRootName(GameObject o) { if (o == null) return ""; Transform r = o.transform?.root; return RemoveCloneSuffix((r?.gameObject ?? o).name); }
        public static bool IsUnderNamedObject(GameObject o, string t) { if (o == null) return false; Transform c = o.transform; while (c != null) { if (RemoveCloneSuffix(c.gameObject.name) == t) return true; c = c.parent; } return false; }
    }

    internal sealed class BlueprintWeaponRule
    {
        public string[] RootNames; public string RootContains, ExcludeRootContains, DisplayName; public Func<bool> IsEnabled; public string[] BlueprintKeys; public int[] HardpointSets;
        public bool MatchesAircraft(string n) { if (string.IsNullOrEmpty(n)) return false; if (RootNames != null && RootNames.Any(r => string.Equals(r, n, StringComparison.OrdinalIgnoreCase))) return true; if (!string.IsNullOrEmpty(RootContains)) { if (!string.IsNullOrEmpty(ExcludeRootContains) && n.IndexOf(ExcludeRootContains, StringComparison.OrdinalIgnoreCase) >= 0) return false; return n.IndexOf(RootContains, StringComparison.OrdinalIgnoreCase) >= 0; } return false; }
        public bool MatchesWeapon(object o) { if (BlueprintToggleReflection.IsNull(o) || BlueprintKeys == null) return false; var ids = BlueprintToggleReflection.GetIdentifiers(o); return BlueprintKeys.Any(k => ids.Any(i => string.Equals(i, k, StringComparison.OrdinalIgnoreCase))); }
    }

    internal static class BlueprintWeaponToggleSystem
    {
        private static Plugin plugin; private static bool initialized, awakeApplyQueued; private static List<BlueprintWeaponRule> rules = new List<BlueprintWeaponRule>(); private static readonly HashSet<string> appliedLogKeys = new HashSet<string>();
        public static void Initialize(Plugin o) { if (initialized || o == null) return; plugin = o; rules = BlueprintWeaponRegistry.Definitions.Select(d => new BlueprintWeaponRule { RootNames = d.AircraftRootNames, RootContains = d.AircraftRootContains, ExcludeRootContains = d.ExcludeRootContains, DisplayName = d.Key, IsEnabled = () => d.ConfigEntry?.Value ?? false, BlueprintKeys = d.BlueprintKeys, HardpointSets = d.HardpointSets }).ToList(); SceneManager.sceneLoaded += (s, m) => plugin.StartCoroutine(DelayedApply(new[] { 0.5f, 2f, 5f })); plugin.StartCoroutine(Poll()); initialized = true; }
        public static void NotifyWeaponManagerAwake(WeaponManager w) { if (!initialized || awakeApplyQueued) return; awakeApplyQueued = true; plugin.StartCoroutine(DelayedApply(new[] { 0.5f, 2f, 5f }, () => awakeApplyQueued = false)); }
        private static IEnumerator Poll() { yield return new WaitForSecondsRealtime(2f); float end = Time.unscaledTime + 60f; while (Time.unscaledTime < end) { ApplyAll(); yield return new WaitForSecondsRealtime(0.5f); } ApplyAll(); }
        private static IEnumerator DelayedApply(float[] d, Action cb = null) { foreach (var f in d) { yield return new WaitForSecondsRealtime(f); ApplyAll(); } cb?.Invoke(); }
        private static void ApplyAll() { foreach (var w in Resources.FindObjectsOfTypeAll<WeaponManager>()) Apply(w); }
        private static void Apply(WeaponManager w) { if (BlueprintToggleReflection.IsNull(w) || w.transform == null) return; string n = ObjectNameUtility.GetCleanRootName(w.transform.root?.gameObject ?? w.gameObject); if (string.IsNullOrEmpty(n)) return; foreach (var r in rules) { if (!r.MatchesAircraft(n)) continue; bool dis = !r.IsEnabled(); foreach (int s in r.HardpointSets) { if (w.hardpointSets == null || s < 0 || s >= w.hardpointSets.Length) continue; object hs = w.hardpointSets[s]; if (BlueprintToggleReflection.IsNull(hs)) continue; var opts = BlueprintToggleReflection.GetWeaponOptions(hs); if (opts == null) continue; foreach (object o in opts) if (r.MatchesWeapon(o) && BlueprintToggleReflection.TrySetDisabled(o, dis)) { string k = $"{n}|{r.DisplayName}|{s}|{dis}"; if (appliedLogKeys.Add(k)) Debug.Log($"[BVR] {(dis ? "Disabled" : "Enabled")} '{r.DisplayName}' on {n} set {s}."); } } } }
    }

    internal static class BlueprintToggleReflection
    {
        private const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        private static readonly string[] D = { "Disabled", "disabled", "isDisabled", "IsDisabled" }, W = { "weaponOptions", "WeaponOptions", "options", "weapons" }, I = { "jsonKey", "blueprintKey", "key", "id", "name", "mountName", "weaponName" }, N = { "mount", "weaponMount", "weaponOption", "option", "weapon" }, P = { "prefab", "Prefab", "gameObject", "GameObject" };
        public static bool IsNull(object o) => o == null || (o is UnityEngine.Object u && u == null);
        public static IEnumerable GetWeaponOptions(object h) { if (IsNull(h)) return null; foreach (var n in W) if (TryGet(h, n, out object v) && v is IEnumerable e && !(v is string)) return e; return null; }
        public static List<string> GetIdentifiers(object o) { List<string> l = new List<string>(); if (IsNull(o)) return l; if (o is Component c && c.gameObject != null) Add(l, c.gameObject.name); if (o is UnityEngine.Object u) Add(l, u.name); foreach (var n in I) if (TryGet(o, n, out object v) && !IsNull(v)) Add(l, v is string s ? s : v is UnityEngine.Object x ? x.name : v.ToString()); foreach (var n in P) if (TryGet(o, n, out object v) && !IsNull(v)) Add(l, v is GameObject g ? g.name : v is UnityEngine.Object x ? x.name : ""); return l; }
        public static bool TrySetDisabled(object o, bool d) => !IsNull(o) && SetRec(o, d, 0, out _);
        private static bool SetRec(object t, bool d, int depth, out bool f) { f = false; if (IsNull(t) || depth > 2) return false; if (SetBool(t, d, ref f)) return true; if (f) return false; foreach (var n in N) if (TryGet(t, n, out object x) && !IsNull(x) && !ReferenceEquals(x, t)) { bool nf = false; if (SetRec(x, d, depth + 1, out nf)) { f = true; return true; } if (nf) f = true; } return false; }
        private static bool SetBool(object t, bool v, ref bool f) { foreach (var n in D) if (TryGet(t, n, out object c)) { f = true; if (Conv(c) != v && TrySet(t, n, v)) return true; } return false; }
        private static bool Conv(object v) => v is bool b ? b : v is string s ? (s == "1" || bool.TryParse(s, out bool p) && p) : false;
        public static bool TryGet(object t, string n, out object v) { v = null; if (IsNull(t)) return false; for (Type x = t.GetType(); x != null && x != typeof(object); x = x.BaseType) { var f = x.GetField(n, F); if (f != null) { v = f.GetValue(t); return true; } var p = x.GetProperty(n, F); if (p != null && p.CanRead) { v = p.GetValue(t); return true; } } return false; }
        public static bool TrySet(object t, string n, object v) { if (IsNull(t)) return false; for (Type x = t.GetType(); x != null && x != typeof(object); x = x.BaseType) { var f = x.GetField(n, F); if (f != null) { f.SetValue(t, ConvT(v, f.FieldType)); return true; } var p = x.GetProperty(n, F); if (p != null && p.CanWrite) { p.SetValue(t, ConvT(v, p.PropertyType)); return true; } } return false; }
        private static object ConvT(object v, Type t) => v == null ? (t.IsValueType ? Activator.CreateInstance(t) : null) : (t.IsInstanceOfType(v) ? v : Convert.ChangeType(v, Nullable.GetUnderlyingType(t) ?? t, CultureInfo.InvariantCulture));
        private static void Add(List<string> l, string i) { if (!string.IsNullOrEmpty(i)) { string c = ObjectNameUtility.RemoveCloneSuffix(i).Trim(); if (!string.IsNullOrEmpty(c) && !l.Contains(c)) l.Add(c); } }
    }

    [HarmonyPatch(typeof(WeaponManager), "Awake")] public static class BlueprintWeaponDisablePatch { [HarmonyPostfix, HarmonyPriority(Priority.Last)] public static void Postfix(WeaponManager __instance) => BlueprintWeaponToggleSystem.NotifyWeaponManagerAwake(__instance); }

    // Remaining patches identical to previous version
    [HarmonyPatch(typeof(WeaponManager), "Awake")]
    public static class StatsPatch
    {
        private static bool hasSwept = false;
        public static void Prefix()
        {
            if (!Plugin.EnableIRMissilesBuff.Value || hasSwept) return;
            foreach (var f in Resources.FindObjectsOfTypeAll<FlareEjector>()) { if (f == null || f.GetComponent<ModifiedStatsFlag>() != null) continue; var t = Traverse.Create(f); var m = t.Field("maxAmmo"); var a = t.Field("ammo"); if (!m.FieldExists() || !a.FieldExists()) continue; m.SetValue(Mathf.RoundToInt(m.GetValue<int>() * Plugin.FlareCountMultiplier.Value)); a.SetValue(Mathf.RoundToInt(a.GetValue<int>() * Plugin.FlareCountMultiplier.Value)); f.gameObject.AddComponent<ModifiedStatsFlag>(); }
            foreach (var s in Resources.FindObjectsOfTypeAll<IRSeeker>()) { if (s == null || s.GetComponent<ModifiedStatsFlag>() != null) continue; var t = Traverse.Create(s); var r = t.Field("flareRejection"); if (!r.FieldExists()) continue; r.SetValue(r.GetValue<float>() * Plugin.FlareRejectionMultiplier.Value); s.gameObject.AddComponent<ModifiedStatsFlag>(); }
            hasSwept = true;
        }
    }

    [HarmonyPatch(typeof(WeaponManager), "Awake")]
    public static class SARHLockPersistencePatch
    {
        private static bool pR9 = false, pRAM = false;
        public static void Prefix()
        {
            if (!Plugin.EnableR9LockPersistenceBuff.Value && !Plugin.EnableRAM45LockPersistenceBuff.Value) return;
            if (Plugin.EnableR9LockPersistenceBuff.Value && !pR9) pR9 = Apply("SAM_Radar2", Plugin.R9LockPersistenceValue.Value);
            if (Plugin.EnableRAM45LockPersistenceBuff.Value && !pRAM) pRAM = Apply("SAM_Radar1", Plugin.RAM45LockPersistenceValue.Value);
        }
        private static bool Apply(string n, float v) { bool s = false; foreach (var x in Resources.FindObjectsOfTypeAll<SARHSeeker>()) { if (x == null || !ObjectNameUtility.IsUnderNamedObject(x.gameObject, n) || x.GetComponent<ModifiedStatsFlag>() != null) continue; var t = Traverse.Create(x); var f = t.Field("lockPersistence"); if (f.FieldExists()) { f.SetValue(v); x.gameObject.AddComponent<ModifiedStatsFlag>(); s = true; } } return s; }
    }

    public class SARHRelockController : MonoBehaviour
    {
        private SARHSeeker seeker; private Traverse st; private Traverse tf, tu, tw, tl, ja, jt, mf; private Missile cm; private float d; private int ma, au; private float rt; private bool w, i;
        public void Setup(SARHSeeker s, float delay, int att) { seeker = s; d = Mathf.Max(0f, delay); ma = Mathf.Max(0, att); au = 0; w = false; rt = 0f; i = false; cm = null; if (s != null) { st = Traverse.Create(s); Init(); } }
        private void Init() { tf = C("targetTransform"); tu = C("targetUnit"); tw = C("timeWithoutTrack"); tl = C("lastTrackingCheck"); ja = C("jamAccumulation"); jt = C("jamTolerance"); mf = C("missile"); }
        private Traverse C(string n) { var f = st.Field(n); if (f.FieldExists()) return f; var p = st.Property(n); if (p.PropertyExists()) return p; return null; }
        private void Update() { if (seeker == null) { Destroy(this); return; } if (st == null) { st = Traverse.Create(seeker); Init(); } if (!i) { i = true; return; } Missile m = G(); if (m != null && m.seekerMode == Missile.SeekerMode.activeLock) { au = 0; w = false; rt = 0f; return; } Transform ct = tf?.GetValue<Transform>(); Unit u = tu?.GetValue<Unit>(); if (u == null || ct != null) { w = false; rt = 0f; return; } if (!w) { if (ma == 0 || au < ma) { w = true; rt = d; } } else { D(Time.deltaTime); rt -= Time.deltaTime; if (rt <= 0f) T(); } }
        private void T() { au++; D(Mathf.Max(Time.deltaTime, 1f)); Unit u = tu?.GetValue<Unit>(); if (u == null || u.disabled) { w = false; return; } Transform nt = u.GetRandomPart(); if (nt == null) { if (ma == 0 || au < ma) rt = d; else w = false; return; } tf?.SetValue(nt); tw?.SetValue(0f); tl?.SetValue(0f); w = false; }
        private void D(float dt) { float j = ja?.GetValue<float>() ?? 0f; if (j <= 0f) return; float t = jt?.GetValue<float>() ?? 0.1f; j -= Mathf.Max(j, 0.2f) * Mathf.Max(t, 0.1f) * dt; ja?.SetValue(Mathf.Clamp01(j)); }
        private Missile G() { if (cm != null) return cm; if (mf != null) cm = mf.GetValue<Missile>(); return cm; }
    }

    [HarmonyPatch(typeof(SARHSeeker), "Initialize", new Type[] { typeof(Unit), typeof(GlobalPosition) })]
    public static class SARHRelockPatch
    {
        public static void Postfix(SARHSeeker __instance, Unit target)
        {
            if (__instance == null || target == null) return;
            string rn = ObjectNameUtility.GetCleanRootName(__instance.gameObject);
            float delay = 0; int att = 0; bool apply = false;
            if (rn.Contains("SAM_Radar2") && Plugin.EnableR9SARHRelock.Value) { delay = Plugin.R9SARHRelockDelay.Value; att = Plugin.R9SARHRelockAttempts.Value; apply = true; }
            else if (rn.Contains("SAM_Radar1") && Plugin.EnableRAM45SARHRelock.Value) { delay = Plugin.RAM45SARHRelockDelay.Value; att = Plugin.RAM45SARHRelockAttempts.Value; apply = true; }
            if (apply) { var c = __instance.GetComponent<SARHRelockController>() ?? __instance.gameObject.AddComponent<SARHRelockController>(); c.Setup(__instance, delay, att); }
        }
    }

    [HarmonyPatch(typeof(WeaponManager), "Awake")]
    public static class CruiseMissileRCSPatch
    {
        private static bool p = false;
        public static void Prefix() { if (p) return; Apply("CruiseMissile1", Plugin.ALMC450RCS.Value); Apply("AShM2", Plugin.AGM99RCS.Value); Apply("AShM1", Plugin.AShM300RCS.Value); Apply("CruiseMissile20kt", Plugin.ALND420ktRCS.Value); p = true; }
        private static void Apply(string n, float v) { foreach (var d in Resources.FindObjectsOfTypeAll<MissileDefinition>()) { if (d != null && ObjectNameUtility.RemoveCloneSuffix(d.name) == n) { var t = Traverse.Create(d); var f = t.Field("radarSize"); if (f.FieldExists()) f.SetValue(v); else { var pr = t.Property("radarSize"); if (pr.PropertyExists()) pr.SetValue(v); } } } }
    }

    [HarmonyPatch(typeof(WeaponManager), "Awake")]
    public static class ProxyGunPatch
    {
        private static bool p = false;
        public static void Prefix() { if (!Plugin.EnableChicaneProxyGun.Value || p) return; foreach (var w in Resources.FindObjectsOfTypeAll<WeaponManager>()) { if (w?.transform?.root != null && w.transform.root.name.Contains("AttackHelo1")) { var c = w.GetComponentsInChildren<MonoBehaviour>(true).FirstOrDefault(x => Traverse.Create(x).Field("weaponStations").FieldExists()); if (c != null) { var st = Traverse.Create(c).Field("weaponStations").GetValue<IList>(); if (st != null && st.Count > 0) { var ws = Traverse.Create(st[0]).Field("Weapons").GetValue<IList>(); if (ws != null && ws.Count > 0) { var pt = Traverse.Create(ws[0]).Field("proximityTimer"); if (pt.FieldExists()) pt.SetValue(true); } } } } } p = true; }
    }

    [HarmonyPatch(typeof(WeaponManager), "Awake")]
    public static class ChicaneBayPylonSymmetryFixPatch
    {
        private static bool p = false;
        public static void Prefix() { if (!Plugin.EnableChicaneBayPylonSymmetryFix.Value || p) return; foreach (var w in Resources.FindObjectsOfTypeAll<WeaponManager>()) { if (w?.transform?.root != null && w.transform.root.name.Contains("AttackHelo1")) { var t = w.transform.root.Find("weaponBay_R/weaponDoorHinge_Ra/weaponDoorHinge_Rb/pylon_bay_R"); if (t != null) t.localPosition = new Vector3(0f, -0.35f, -0.1f); } } p = true; }
    }

    [HarmonyPatch(typeof(WeaponManager), "Awake")]
    public static class MedusaLaserPatch
    {
        private static bool p = false;
        public static void Prefix() { if (!Plugin.EnableMedusaLaserBuff.Value || p) return; foreach (var g in Resources.FindObjectsOfTypeAll<GameObject>()) { if (g != null && g.name.Contains("Laser_EW1")) { foreach (var c in g.GetComponentsInChildren<MonoBehaviour>(true)) { if (c != null && c.gameObject.GetComponent<ModifiedStatsFlag>() == null) { var pt = Traverse.Create(c).Field("power"); if (pt.FieldExists()) { pt.SetValue(Plugin.MedusaLaserPowerDraw.Value); c.gameObject.AddComponent<ModifiedStatsFlag>(); } } } } } p = true; }
    }
}