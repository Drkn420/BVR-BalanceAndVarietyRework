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



// NOTE: Three blank lines are intentionally kept between distinct code blocks for readability.
//
// ============================================================================
// MAINTENANCE GUIDE
// ============================================================================
//
// 1. New config-driven gameplay features should bind config entries through
//    Plugin.BindRestartRequired. This automatically:
//       - Marks them as restart-required by description and startup caching,
//         while remaining editable in the config manager.
//       - Adds them to the seed/hash system when exportToSeed is true.
//
// 2. New features must read values from RuntimeSettings, not directly from
//    live ConfigEntry values, unless the feature is explicitly designed to
//    support live changes. This mod is intended to require a full restart.
//
// 3. New features must fail loudly. If an expected prefab, component, field,
//    property, or hierarchy path is missing, log an error with enough context
//    to diagnose the issue. Use MissingMemberLog.ErrorOnce or Log.Error.
//
// 4. New Harmony patches must be registered in Plugin.RegisterHarmonyPatches.
//
// 5. New patches should be idempotent. They may run multiple times before the
//    intended Unity objects are loaded, so they should retry safely and only
//    mark themselves applied after success.
//
// ============================================================================

namespace BalanceAndVarietyRework
{
    [BepInPlugin("com.Draken0015.BVR", "Balance and Variety Rework", BaseVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string BaseVersion = "1.2.5";

        // Seed format version is separate from mod version so future seed layout
        // changes can fail loudly instead of silently importing wrong data.
        private const int SeedFormatVersion = 2;
        private const string SeedFormatPrefix = "BVR2";
        private const string SeedPayloadPrefix = "BVRSEED";
        private const string HashPayloadPrefix = "BVRHASH";

        public static Plugin Instance { get; private set; }
        public static string FullVersionWithHash { get; private set; }
        public static List<ConfigEntryBase> AllRegisteredConfigs = new List<ConfigEntryBase>();

        // Static references for config binding and seed export.
        // Runtime gameplay code should use RuntimeSettings instead of reading these live.
        public static ConfigEntry<bool> EnableIRMissilesBuff;
        public static ConfigEntry<float> FlareCountMultiplier;
        public static ConfigEntry<float> FlareRejectionMultiplier;
        public static ConfigEntry<float> MMRS3MaxTurnRate;
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
        public static ConfigEntry<bool> EnableScytheLoftFactor;
        public static ConfigEntry<float> ScytheLoftFactorValue;
        public static ConfigEntry<bool> EnableScimitarLoftFactor;
        public static ConfigEntry<float> ScimitarLoftFactorValue;
        public static ConfigEntry<bool> EnableChicaneProxyGun;
        public static ConfigEntry<bool> EnableChicaneBayPylonSymmetryFix;
        public static ConfigEntry<bool> EnableMedusaLaserBuff;
        public static ConfigEntry<float> MedusaLaserPowerDraw;
        public static ConfigEntry<bool> EnableSpaagSingleMagazine;
        public static ConfigEntry<int> SpaagMagazineCapacity;
        public static ConfigEntry<int> SpaagMagazines;
        public static ConfigEntry<bool> EnableCorvetteSingleMagazine;
        public static ConfigEntry<int> CorvetteCannonMagazineCapacity;
        public static ConfigEntry<int> CorvetteCannonMagazineCount;
        public static ConfigEntry<bool> EnableDynamoRailgunSelfDestructVFX;
        public static ConfigEntry<bool> EnableAnnexArrestingCables;
        public static ConfigEntry<bool> EnableCursorLFDArrestingCables;

        private void Awake()
        {
            Instance = this;
            Log.Info($"BVR - Starting Balance and Variety Rework v{BaseVersion}.");

            BindImportantNotices();
            BindFunctionalConfigs();
            BindBlueprinterWeapons();
            BindCanopyGlass();
            TryImportPendingConfigSeed();

            // Restart-only semantics:
            // Capture all values after seed import so no later runtime config change
            // can affect already initialized systems.
            RuntimeSettings.Capture();
            FinalizeVersionAndHash();

            RegisterHarmonyPatches();
            BlueprintWeaponRemovalSystem.Initialize(this);
            ArrestingCableSystem.Initialize(this);
            ChicaneBayPylonSymmetrySystem.Initialize(this);

            Log.Info("BVR - Balance and Variety Rework Mod Loaded!");
        }

        private void OnDestroy()
        {
            BlueprintWeaponRemovalSystem.Shutdown();
            ChicaneBayPylonSymmetrySystem.Shutdown();
        }

        private ConfigEntry<T> BindAndTrack<T>(
            string section,
            string key,
            T defaultValue,
            string description,
            bool exportToSeed = true,
            ConfigurationManagerAttributes attributes = null)
        {
            if (string.IsNullOrEmpty(section) || string.IsNullOrEmpty(key))
            {
                Log.Error($"BVR tried to bind a config entry with an invalid section or key. Section='{section}', Key='{key}'.");
            }

            string id = GetConfigId(section, key);
            if (AllRegisteredConfigs.Any(c => GetConfigId(c.Definition.Section, c.Definition.Key) == id))
            {
                Log.Error($"Duplicate tracked config entry detected: [{section}] {key}. Seed/hash export may become unstable.");
            }

            ConfigDescription configDesc = attributes != null
                ? new ConfigDescription(description, null, attributes)
                : new ConfigDescription(description);

            ConfigEntry<T> entry = Config.Bind(section, key, defaultValue, configDesc);

            if (exportToSeed)
                AllRegisteredConfigs.Add(entry);

            return entry;
        }

        private ConfigEntry<T> BindRestartRequired<T>(string section, string key, T defaultValue, string description)
        {
            // Restart-only semantics are enforced by RuntimeSettings.Capture, not by
            // locking the config manager UI. Keep these entries editable so users can
            // change them in-game, then restart to apply the new values.
            return BindAndTrack(
                section,
                key,
                defaultValue,
                description + " Requires a full game restart.",
                true,
                null);
        }

        private void BindImportantNotices()
        {
            BindAndTrack(
                "Important Notices",
                "Restart Required",
                "Changes made here require a full game restart to apply.",
                "Please restart the game after changing any settings.",
                false,
                new ConfigurationManagerAttributes { ReadOnly = true, HideDefaultButton = true, Order = 100 });

            BindAndTrack(
                "Important Notices",
                "Mod Version",
                $"v{BaseVersion}",
                "The currently installed version.",
                false,
                new ConfigurationManagerAttributes { ReadOnly = true, HideDefaultButton = true, Order = 99 });

            BindAndTrack(
                "Important Notices",
                "Current Config Hash",
                "Calculating...",
                "Compare this hash with other players. The hash includes the mod version. If it does not match even after importing a seed, a mod version mismatch is likely.",
                false,
                new ConfigurationManagerAttributes { ReadOnly = true, HideDefaultButton = true, Order = 98 });

            BindAndTrack(
                "Important Notices",
                "Current Config Seed",
                "Calculating...",
                "Copy this seed to share your configuration. The seed includes the mod version and config sections. If an imported seed does not produce the same hash, a mod version mismatch is likely.",
                false,
                new ConfigurationManagerAttributes { ReadOnly = true, HideDefaultButton = true, Order = 97 });

            BindAndTrack(
                "Important Notices",
                "Import Config Seed",
                "",
                "Paste a seed here and restart. The seed is imported during the next startup and does not apply while the current game session is running.",
                false,
                new ConfigurationManagerAttributes { HideDefaultButton = true, Order = 96 });
        }

        private void BindFunctionalConfigs()
        {
            EnableIRMissilesBuff = BindRestartRequired(
                "Missile Balance - IR",
                "Enable IR Missiles Buff",
                true,
                "Master toggle for IR buffs.");

            FlareCountMultiplier = BindRestartRequired(
                "Missile Balance - IR",
                "Flare Count Multiplier",
                2.0f,
                "Multiplies total flares.");

            FlareRejectionMultiplier = BindRestartRequired(
                "Missile Balance - IR",
                "Flare Rejection Multiplier",
                2.0f,
                "Multiplies flare rejection.");

            MMRS3MaxTurnRate = BindRestartRequired(
                "Missile Balance - IR",
                "MMR-S3 Max Turn Rate",
                45.0f,
                "Missile.maxTurnRate for the MMR-S3 (AAM1). Vanilla is 180.");

            EnableR9LockPersistenceBuff = BindRestartRequired(
                "Missile Balance - SARH",
                "Enable R9 Lock Persistence",
                true,
                "Master toggle.");

            R9LockPersistenceValue = BindRestartRequired(
                "Missile Balance - SARH",
                "R9 Lock Persistence Value",
                3.0f,
                "Lock persistence duration.");

            EnableRAM45LockPersistenceBuff = BindRestartRequired(
                "Missile Balance - SARH",
                "Enable RAM45 Lock Persistence",
                true,
                "Master toggle.");

            RAM45LockPersistenceValue = BindRestartRequired(
                "Missile Balance - SARH",
                "RAM45 Lock Persistence Value",
                3.0f,
                "Lock persistence duration.");

            EnableR9SARHRelock = BindRestartRequired(
                "Missile Balance - SARH",
                "Enable R9 SARH Relock",
                true,
                "Master toggle.");

            R9SARHRelockDelay = BindRestartRequired(
                "Missile Balance - SARH",
                "R9 SARH Relock Delay",
                3.0f,
                "Delay before relock.");

            R9SARHRelockAttempts = BindRestartRequired(
                "Missile Balance - SARH",
                "R9 SARH Relock Attempts",
                0,
                "0 = infinite.");

            EnableRAM45SARHRelock = BindRestartRequired(
                "Missile Balance - SARH",
                "Enable RAM45 SARH Relock",
                true,
                "Master toggle.");

            RAM45SARHRelockDelay = BindRestartRequired(
                "Missile Balance - SARH",
                "RAM45 SARH Relock Delay",
                3.0f,
                "Delay before relock.");

            RAM45SARHRelockAttempts = BindRestartRequired(
                "Missile Balance - SARH",
                "RAM45 SARH Relock Attempts",
                0,
                "0 = infinite.");

            ALMC450RCS = BindRestartRequired(
                "Missile Balance - Cruise",
                "ALM-C450 RCS",
                0.0005f,
                "Vanilla is 0.005.");

            AGM99RCS = BindRestartRequired(
                "Missile Balance - Cruise",
                "AGM-99 RCS",
                0.008f,
                "Vanilla is 0.008.");

            AShM300RCS = BindRestartRequired(
                "Missile Balance - Cruise",
                "AShM-300 RCS",
                0.005f,
                "Vanilla is 0.005.");

            ALND420ktRCS = BindRestartRequired(
                "Missile Balance - Cruise",
                "ALND-4 (20kt) RCS",
                0.001f,
                "Vanilla is 0.005.");

            EnableScytheLoftFactor = BindRestartRequired(
                "Missile Balance - ARH",
                "Enable Scythe Loft Factor",
                true,
                "Master toggle.");

            ScytheLoftFactorValue = BindRestartRequired(
                "Missile Balance - ARH",
                "Scythe Loft Factor Value",
                0.7f,
                "ARHSeeker.loftAmount for the Scythe (AAM2). Vanilla is 0.7.");

            EnableScimitarLoftFactor = BindRestartRequired(
                "Missile Balance - ARH",
                "Enable Scimitar Loft Factor",
                true,
                "Master toggle.");

            ScimitarLoftFactorValue = BindRestartRequired(
                "Missile Balance - ARH",
                "Scimitar Loft Factor Value",
                0.1f,
                "ARHSeeker.loftAmount for the Scimitar (AAM4). Vanilla is 0.1.");

            EnableChicaneProxyGun = BindRestartRequired(
                "SAH-46 Chicane Changes",
                "Enable Proximity Fuse 30mm Gun",
                true,
                "Enables proxy fuse.");

            EnableChicaneBayPylonSymmetryFix = BindRestartRequired(
                "SAH-46 Chicane Changes",
                "Enable Bay Pylon Symmetry Fix",
                true,
                "Centers right bay pylon.");

            EnableMedusaLaserBuff = BindRestartRequired(
                "EW-25 Medusa Changes",
                "Enable Laser Buff",
                true,
                "Master toggle.");

            MedusaLaserPowerDraw = BindRestartRequired(
                "EW-25 Medusa Changes",
                "Laser Power Draw Value",
                60.0f,
                "Vanilla is 120.");

            EnableSpaagSingleMagazine = BindRestartRequired(
                "AeroSentry SPAAG Changes",
                "Enable SPAAG Single Magazine Gun",
                true,
                "Master toggle. Consolidates the AeroSentry SPAAG gun ammo into one magazine. Vanilla Gun.magazineCapacity is 25 and Gun.magazines is 40.");

            SpaagMagazineCapacity = BindRestartRequired(
                "AeroSentry SPAAG Changes",
                "SPAAG Gun Magazine Capacity",
                1025,
                "Gun.magazineCapacity for SPAAG1/Turret/gun. Vanilla is 25.");

            SpaagMagazines = BindRestartRequired(
                "AeroSentry SPAAG Changes",
                "SPAAG Gun Magazine Count",
                0,
                "Gun.magazines for SPAAG1/Turret/gun. Vanilla is 40.");

            EnableCorvetteSingleMagazine = BindRestartRequired(
                "Shard Class Corvette Changes",
                "Enable Single Magazine 57mm Cannon",
                true,
                "Master toggle. Consolidates the Shard Class Corvette 57mm cannon ammo into one magazine.");

            CorvetteCannonMagazineCapacity = BindRestartRequired(
                "Shard Class Corvette Changes",
                "Corvette 57mm Cannon Magazine Capacity",
                1206,
                "Gun.magazineCapacity for Corvette1/bow1/turret_F/cannon_F.");

            CorvetteCannonMagazineCount = BindRestartRequired(
                "Shard Class Corvette Changes",
                "Corvette 57mm Cannon Magazine Count",
                0,
                "Gun.magazines for Corvette1/bow1/turret_F/cannon_F.");

            EnableDynamoRailgunSelfDestructVFX = BindRestartRequired(
                "Dynamo Class Destroyer Changes",
                "Enable Railgun Self-Destruct VFX",
                true,
                "Master toggle. Sets the Dynamo railgun's self-destruct effect to explosion_10kg.");

            EnableAnnexArrestingCables = BindRestartRequired(
                "Annex Carrier Changes",
                "Enable Annex Carrier Arresting Cables",
                true,
                "Master toggle. Adds Hyperion Fleet Carrier arresting cables to the Annex Carrier.");

            EnableCursorLFDArrestingCables = BindRestartRequired(
                "Cursor LFD Changes",
                "Enable Cursor LFD Arresting Cables",
                true,
                "Master toggle. Adds Hyperion Fleet Carrier arresting cables to the Cursor LFD.");
        }

        private void BindBlueprinterWeapons()
        {
            foreach (BlueprintWeaponDefinition def in BlueprintWeaponRegistry.Definitions)
            {
                if (def == null)
                {
                    Log.Error("BlueprintWeaponRegistry contains a null definition. This definition will be skipped.");
                    continue;
                }
                def.ConfigEntry = BindRestartRequired(def.Section, def.Key, def.DefaultValue, def.Description);
            }
        }

        private void BindCanopyGlass()
        {
            foreach (CanopyGlassDefinition def in CanopyGlassRegistry.Definitions)
            {
                if (def == null)
                {
                    Log.Error("CanopyGlassRegistry contains a null definition. This definition will be skipped.");
                    continue;
                }
                def.ConfigEntry = BindRestartRequired(def.Section, def.Key, def.DefaultValue, def.Description);
            }
        }

        private void TryImportPendingConfigSeed()
        {
            ConfigEntryBase importEntry = Config["Important Notices", "Import Config Seed"];
            if (importEntry == null)
            {
                Log.Error("The 'Import Config Seed' config entry could not be found. Seed import is disabled.");
                return;
            }

            string seed = importEntry.BoxedValue as string;
            if (string.IsNullOrWhiteSpace(seed))
                return;

            Log.Info("BVR - Import Config Seed is present. Attempting import before runtime settings are cached.");

            if (TryImportConfigSeed(seed))
                Log.Info("BVR - Seed imported successfully.");
            else
                Log.Error("BVR - Seed import failed. The seed field will still be cleared to avoid repeating the same failure on every startup.");

            importEntry.BoxedValue = string.Empty;
        }

        private void FinalizeVersionAndHash()
        {
            string hash = GenerateConfigHash();
            string seed = GenerateConfigSeed();
            FullVersionWithHash = $"{BaseVersion}-{hash}";

            SetNoticeValue("Current Config Hash", hash);
            SetNoticeValue("Current Config Seed", seed);
            SetNoticeValue("Mod Version", $"v{BaseVersion}");
            Config.Save();

            Log.Info($"BVR - Config hash '{hash}' generated for mod version '{BaseVersion}'.");
        }

        private void SetNoticeValue(string key, string value)
        {
            ConfigEntryBase entry = Config["Important Notices", key];
            if (entry == null)
            {
                Log.Error($"Could not find Important Notice config key '{key}'.");
                return;
            }
            entry.BoxedValue = value;
        }

        private void RegisterHarmonyPatches()
        {
            Type[] patchTypes =
            {
                typeof(StatsPatch),
                typeof(MMRS3MaxTurnRatePatch),
                typeof(SARHLockPersistencePatch),
                typeof(SARHRelockPatch),
                typeof(CruiseMissileRCSPatch),
                typeof(ARHSeekerLoftPatch),
                typeof(ProxyGunPatch),
                typeof(ChicaneBayPylonSymmetryFixPatch),
                typeof(MedusaLaserPatch),
                typeof(SpaagSingleMagazinePatch),
                typeof(CorvetteSingleMagazinePatch),
                typeof(DynamoRailgunSelfDestructPatch),
                typeof(ShipAwakeArrestingCablePatch),
                typeof(CanopyGlassAwakePatch),
                typeof(BlueprintWeaponManagerInitPatch),
                typeof(BlueprintWeaponLoadPatch),
                typeof(BlueprintAISelectionPatch)
            };

            foreach (Type patchType in patchTypes)
            {
                try
                {
                    Harmony.CreateAndPatchAll(patchType);
                }
                catch (Exception ex)
                {
                    Log.Error($"Failed to register Harmony patches for {patchType.Name}. This feature will not work. {ex}");
                }
            }
        }

        private string GenerateConfigHash()
        {
            using (MD5 md5 = MD5.Create())
            {
                string payload = GeneratePayload(HashPayloadPrefix);
                string hex = BitConverter.ToString(md5.ComputeHash(Encoding.UTF8.GetBytes(payload))).Replace("-", "");
                return hex.Length >= 6 ? hex.Substring(0, 6) : hex;
            }
        }

        private string GenerateConfigSeed()
        {
            return $"{SeedFormatPrefix}-{ToUrlSafeBase64(GeneratePayload(SeedPayloadPrefix))}";
        }

        private string GeneratePayload(string prefix)
        {
            StringBuilder payload = new StringBuilder();
            // Payload format:
            // PREFIX|ModVersion|SeedFormatVersion|Section:Key:Type:Value|...
            payload.Append(prefix)
                   .Append('|')
                   .Append(BaseVersion)
                   .Append('|')
                   .Append(SeedFormatVersion);

            foreach (ConfigEntryBase entry in AllRegisteredConfigs
                .OrderBy(c => c.Definition.Section)
                .ThenBy(c => c.Definition.Key))
            {
                payload.Append('|').Append(EntryToSeedString(entry));
            }
            return payload.ToString();
        }

        private static string EntryToSeedString(ConfigEntryBase entry)
        {
            return
                Uri.EscapeDataString(entry.Definition.Section) + ":" +
                Uri.EscapeDataString(entry.Definition.Key) + ":" +
                GetTypeKey(entry.SettingType) + ":" +
                Uri.EscapeDataString(ConvertValueToString(entry.BoxedValue, entry.SettingType));
        }

        private bool TryImportConfigSeed(string seed)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(seed))
                {
                    Log.Warn("Seed import failed because the seed was empty.");
                    return false;
                }

                seed = seed.Trim();

                if (seed.StartsWith("BVR1-", StringComparison.Ordinal))
                {
                    Log.Error("Seed import failed because the seed uses the old BVR1 format. This rewrite uses the section-aware BVR2 format. Generate a new seed with the current mod version.");
                    return false;
                }

                if (!seed.StartsWith(SeedFormatPrefix + "-", StringComparison.Ordinal))
                {
                    Log.Error($"Seed import failed because the seed must start with '{SeedFormatPrefix}-'.");
                    return false;
                }

                string payload = FromUrlSafeBase64(seed.Substring(SeedFormatPrefix.Length + 1));
                if (string.IsNullOrEmpty(payload))
                {
                    Log.Error("Seed import failed because the seed is not valid URL-safe Base64.");
                    return false;
                }

                string[] parts = payload.Split('|');
                if (parts.Length < 3 || parts[0] != SeedPayloadPrefix)
                {
                    Log.Error($"Seed import failed because the payload is not a valid {SeedPayloadPrefix} payload.");
                    return false;
                }

                string seedModVersion = parts[1];
                if (!int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int seedFormatVersion))
                {
                    Log.Error("Seed import failed because the seed format version is not a number.");
                    return false;
                }

                if (seedFormatVersion != SeedFormatVersion)
                {
                    Log.Error($"Seed import failed because seed format version {seedFormatVersion} is not supported by this mod version. Expected format version {SeedFormatVersion}.");
                    return false;
                }

                if (!string.Equals(seedModVersion, BaseVersion, StringComparison.Ordinal))
                {
                    Log.Warn($"Seed was generated with mod version '{seedModVersion}', but this mod version is '{BaseVersion}'. Import will continue, but a version mismatch may cause missing or extra settings.");
                }

                Dictionary<string, ConfigEntryBase> configsById = new Dictionary<string, ConfigEntryBase>();
                foreach (ConfigEntryBase entry in AllRegisteredConfigs)
                {
                    string id = GetConfigId(entry.Definition.Section, entry.Definition.Key);
                    if (!configsById.ContainsKey(id))
                        configsById.Add(id, entry);
                    else
                        Log.Error($"Duplicate config entry detected while importing seed: [{entry.Definition.Section}] {entry.Definition.Key}.");
                }

                int applied = 0;
                int unknown = 0;
                int malformed = 0;

                for (int i = 3; i < parts.Length; i++)
                {
                    string entryText = parts[i];
                    if (string.IsNullOrEmpty(entryText))
                        continue;

                    string[] entryParts = entryText.Split(':');
                    if (entryParts.Length != 4)
                    {
                        malformed++;
                        Log.Warn($"Malformed seed entry ignored: '{entryText}'. Expected Section:Key:Type:Value with escaped section and key.");
                        continue;
                    }

                    string section = Uri.UnescapeDataString(entryParts[0]);
                    string key = Uri.UnescapeDataString(entryParts[1]);
                    string typeKey = entryParts[2];
                    string rawValue = entryParts[3];

                    string id = GetConfigId(section, key);
                    if (!configsById.TryGetValue(id, out ConfigEntryBase entry))
                    {
                        unknown++;
                        Log.Warn($"Seed contains unknown config entry [{section}] {key}. It may belong to another mod version.");
                        continue;
                    }

                    string expectedTypeKey = GetTypeKey(entry.SettingType);
                    if (!string.Equals(typeKey, expectedTypeKey, StringComparison.OrdinalIgnoreCase))
                    {
                        Log.Warn($"Seed value type mismatch for [{section}] {key}. Expected '{expectedTypeKey}', seed contains '{typeKey}'. Import will still attempt conversion.");
                    }

                    try
                    {
                        entry.BoxedValue = ConvertStringToValue(Uri.UnescapeDataString(rawValue), entry.SettingType);
                        applied++;
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"Failed to parse seed value for [{section}] {key}. Value text: '{rawValue}'. {ex.Message}");
                    }
                }

                if (malformed > 0)
                    Log.Warn($"Seed import encountered {malformed} malformed entr{(malformed == 1 ? "y" : "ies")}.");
                if (unknown > 0)
                    Log.Warn($"Seed import ignored {unknown} unknown config entr{(unknown == 1 ? "y" : "ies")}.");

                if (applied > 0)
                {
                    Config.Save();
                    return true;
                }

                Log.Error("Seed import parsed but did not apply any config values.");
                return false;
            }
            catch (Exception ex)
            {
                Log.Error($"Seed import failed unexpectedly. {ex}");
                return false;
            }
        }

        private static string GetConfigId(string section, string key)
        {
            return $"{section}::{key}";
        }

        private static string GetTypeKey(Type t)
        {
            if (t == typeof(bool)) return "bool";
            if (t == typeof(int)) return "int";
            if (t == typeof(float)) return "float";
            if (t == typeof(double)) return "double";
            if (t == typeof(long)) return "long";
            if (t == typeof(string)) return "string";
            return t.Name.ToLowerInvariant();
        }

        private static string ConvertValueToString(object value, Type type)
        {
            if (value == null)
                return "";
            if (value is bool b)
                return b ? "True" : "False";
            if (value is float f)
                return f.ToString("R", CultureInfo.InvariantCulture);
            if (value is double d)
                return d.ToString("R", CultureInfo.InvariantCulture);
            if (value is int i)
                return i.ToString(CultureInfo.InvariantCulture);
            if (value is long l)
                return l.ToString(CultureInfo.InvariantCulture);
            return Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
        }

        private static object ConvertStringToValue(string raw, Type type)
        {
            Type underlying = Nullable.GetUnderlyingType(type) ?? type;
            if (underlying == typeof(string))
                return raw ?? "";
            if (string.IsNullOrEmpty(raw))
                return underlying.IsValueType ? Activator.CreateInstance(underlying) : null;
            if (underlying == typeof(bool))
                return raw == "1" || bool.Parse(raw);
            if (underlying == typeof(float))
                return float.Parse(raw, NumberStyles.Float, CultureInfo.InvariantCulture);
            if (underlying == typeof(double))
                return double.Parse(raw, NumberStyles.Float, CultureInfo.InvariantCulture);
            if (underlying == typeof(int))
                return int.Parse(raw, CultureInfo.InvariantCulture);
            if (underlying == typeof(long))
                return long.Parse(raw, CultureInfo.InvariantCulture);
            return Convert.ChangeType(raw, underlying, CultureInfo.InvariantCulture);
        }

        private static string ToUrlSafeBase64(string text)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(text))
                .Replace('+', '-')
                .Replace('/', '_')
                .TrimEnd('=');
        }

        private static string FromUrlSafeBase64(string data)
        {
            if (string.IsNullOrWhiteSpace(data))
                return null;

            data = data.Replace('-', '+').Replace('_', '/');
            int mod = data.Length % 4;
            if (mod == 2)
                data += "==";
            else if (mod == 3)
                data += "=";
            else if (mod == 1)
                return null;

            try
            {
                return Encoding.UTF8.GetString(Convert.FromBase64String(data));
            }
            catch
            {
                return null;
            }
        }
    }



    // ========================================================================
    // Configuration Manager attribute support.
    // These fields are read by common BepInEx configuration manager plugins.
    // ========================================================================
#pragma warning disable CS0169, CS0414, CS0649
    internal sealed class ConfigurationManagerAttributes
    {
        public bool? ReadOnly;
        public bool? HideDefaultButton;
        public int? Order;
    }
#pragma warning restore CS0169, CS0414, CS0649



    // ========================================================================
    // Shared runtime marker component.
    // ========================================================================
    public class ModifiedStatsFlag : MonoBehaviour { }



    // ========================================================================
    // Central logging.
    // All features should use this instead of Debug.Log directly so failures
    // are visible in both the Unity console and the BepInEx log.
    // BepInEx automatically captures Unity's Debug.Log calls.
    // ========================================================================
    internal static class Log
    {
        private const string Prefix = "[BVR] ";

        public static void Info(string message)
        {
            Debug.Log(Prefix + message);
        }

        public static void Warn(string message)
        {
            Debug.LogWarning(Prefix + message);
        }

        public static void Error(string message)
        {
            Debug.LogError(Prefix + message);
        }

        public static void Exception(string feature, Exception ex)
        {
            Error($"{feature} failed unexpectedly. {ex}");
        }
    }



    // ========================================================================
    // One-time diagnostic logging.
    // Prevents repeated spam while still failing loudly the first time an
    // expected object, component, field, or path is missing.
    // ========================================================================
    internal static class MissingMemberLog
    {
        private static readonly HashSet<string> LoggedKeys = new HashSet<string>();

        public static void WarnOnce(string key, string message)
        {
            if (LoggedKeys.Add("W|" + key))
                Log.Warn(message);
        }

        public static void ErrorOnce(string key, string message)
        {
            if (LoggedKeys.Add("E|" + key))
                Log.Error(message);
        }
    }



    // ========================================================================
    // Startup-cached config values.
    // All gameplay code must read from here. This guarantees restart-required
    // semantics even if the config file or config manager changes later.
    // Config entries may be edited in the runtime config manager; those edits
    // are intended for the next startup and are intentionally not applied live.
    // ========================================================================
    internal static class RuntimeSettings
    {
        public static bool Captured { get; private set; }

        public static bool EnableIRMissilesBuff;
        public static float FlareCountMultiplier;
        public static float FlareRejectionMultiplier;
        public static float MMRS3MaxTurnRate;
        public static bool EnableR9LockPersistenceBuff;
        public static float R9LockPersistenceValue;
        public static bool EnableRAM45LockPersistenceBuff;
        public static float RAM45LockPersistenceValue;
        public static bool EnableR9SARHRelock;
        public static float R9SARHRelockDelay;
        public static int R9SARHRelockAttempts;
        public static bool EnableRAM45SARHRelock;
        public static float RAM45SARHRelockDelay;
        public static int RAM45SARHRelockAttempts;
        public static float ALMC450RCS;
        public static float AGM99RCS;
        public static float AShM300RCS;
        public static float ALND420ktRCS;
        public static bool EnableScytheLoftFactor;
        public static float ScytheLoftFactorValue;
        public static bool EnableScimitarLoftFactor;
        public static float ScimitarLoftFactorValue;
        public static bool EnableChicaneProxyGun;
        public static bool EnableChicaneBayPylonSymmetryFix;
        public static bool EnableMedusaLaserBuff;
        public static float MedusaLaserPowerDraw;
        public static bool EnableSpaagSingleMagazine;
        public static int SpaagMagazineCapacity;
        public static int SpaagMagazines;
        public static bool EnableCorvetteSingleMagazine;
        public static int CorvetteCannonMagazineCapacity;
        public static int CorvetteCannonMagazineCount;
        public static bool EnableDynamoRailgunSelfDestructVFX;
        public static bool EnableAnnexArrestingCables;
        public static bool EnableCursorLFDArrestingCables;

        public static void Capture()
        {
            if (Captured)
            {
                Log.Warn("RuntimeSettings.Capture was called more than once. Ignoring duplicate capture to preserve restart-only semantics.");
                return;
            }

            if (Plugin.EnableIRMissilesBuff == null)
            {
                Log.Error("Cannot capture runtime settings because functional config entries were not fully bound.");
                return;
            }

            EnableIRMissilesBuff = Plugin.EnableIRMissilesBuff.Value;
            FlareCountMultiplier = SafeFloat(Plugin.FlareCountMultiplier.Value, 2.0f, "Flare Count Multiplier");
            FlareRejectionMultiplier = SafeFloat(Plugin.FlareRejectionMultiplier.Value, 2.0f, "Flare Rejection Multiplier");
            MMRS3MaxTurnRate = SafeFloat(Plugin.MMRS3MaxTurnRate.Value, 45.0f, "MMR-S3 Max Turn Rate");
            EnableR9LockPersistenceBuff = Plugin.EnableR9LockPersistenceBuff.Value;
            R9LockPersistenceValue = SafeFloat(Plugin.R9LockPersistenceValue.Value, 3.0f, "R9 Lock Persistence Value");
            EnableRAM45LockPersistenceBuff = Plugin.EnableRAM45LockPersistenceBuff.Value;
            RAM45LockPersistenceValue = SafeFloat(Plugin.RAM45LockPersistenceValue.Value, 3.0f, "RAM45 Lock Persistence Value");
            EnableR9SARHRelock = Plugin.EnableR9SARHRelock.Value;
            R9SARHRelockDelay = SafeFloat(Plugin.R9SARHRelockDelay.Value, 3.0f, "R9 SARH Relock Delay");
            R9SARHRelockAttempts = Plugin.R9SARHRelockAttempts.Value;
            EnableRAM45SARHRelock = Plugin.EnableRAM45SARHRelock.Value;
            RAM45SARHRelockDelay = SafeFloat(Plugin.RAM45SARHRelockDelay.Value, 3.0f, "RAM45 SARH Relock Delay");
            RAM45SARHRelockAttempts = Plugin.RAM45SARHRelockAttempts.Value;
            ALMC450RCS = SafeFloat(Plugin.ALMC450RCS.Value, 0.0005f, "ALM-C450 RCS");
            AGM99RCS = SafeFloat(Plugin.AGM99RCS.Value, 0.008f, "AGM-99 RCS");
            AShM300RCS = SafeFloat(Plugin.AShM300RCS.Value, 0.005f, "AShM-300 RCS");
            ALND420ktRCS = SafeFloat(Plugin.ALND420ktRCS.Value, 0.001f, "ALND-4 (20kt) RCS");
            EnableScytheLoftFactor = Plugin.EnableScytheLoftFactor.Value;
            ScytheLoftFactorValue = SafeFloat(Plugin.ScytheLoftFactorValue.Value, 0.7f, "Scythe Loft Factor Value");
            EnableScimitarLoftFactor = Plugin.EnableScimitarLoftFactor.Value;
            ScimitarLoftFactorValue = SafeFloat(Plugin.ScimitarLoftFactorValue.Value, 0.1f, "Scimitar Loft Factor Value");
            EnableChicaneProxyGun = Plugin.EnableChicaneProxyGun.Value;
            EnableChicaneBayPylonSymmetryFix = Plugin.EnableChicaneBayPylonSymmetryFix.Value;
            EnableMedusaLaserBuff = Plugin.EnableMedusaLaserBuff.Value;
            MedusaLaserPowerDraw = SafeFloat(Plugin.MedusaLaserPowerDraw.Value, 60.0f, "Laser Power Draw Value");
            EnableSpaagSingleMagazine = Plugin.EnableSpaagSingleMagazine.Value;
            SpaagMagazineCapacity = SafeInt(Plugin.SpaagMagazineCapacity.Value, 1025, "SPAAG Gun Magazine Capacity");
            SpaagMagazines = SafeInt(Plugin.SpaagMagazines.Value, 0, "SPAAG Gun Magazine Count");
            EnableCorvetteSingleMagazine = Plugin.EnableCorvetteSingleMagazine.Value;
            CorvetteCannonMagazineCapacity = SafeInt(Plugin.CorvetteCannonMagazineCapacity.Value, 1206, "Corvette 57mm Cannon Magazine Capacity");
            CorvetteCannonMagazineCount = SafeInt(Plugin.CorvetteCannonMagazineCount.Value, 0, "Corvette 57mm Cannon Magazine Count");
            EnableDynamoRailgunSelfDestructVFX = Plugin.EnableDynamoRailgunSelfDestructVFX.Value;
            EnableAnnexArrestingCables = Plugin.EnableAnnexArrestingCables.Value;
            EnableCursorLFDArrestingCables = Plugin.EnableCursorLFDArrestingCables.Value;

            foreach (BlueprintWeaponDefinition definition in BlueprintWeaponRegistry.Definitions)
            {
                if (definition == null)
                {
                    Log.Error("BlueprintWeaponRegistry contains a null definition while capturing runtime settings.");
                    continue;
                }
                if (definition.ConfigEntry == null)
                {
                    Log.Error($"Blueprint config entry for [{definition.Section}] {definition.Key} was not bound. Falling back to default value.");
                    definition.CachedEnabled = definition.DefaultValue;
                }
                else
                {
                    definition.CachedEnabled = definition.ConfigEntry.Value;
                }
            }

            foreach (CanopyGlassDefinition definition in CanopyGlassRegistry.Definitions)
            {
                if (definition == null)
                {
                    Log.Error("CanopyGlassRegistry contains a null definition while capturing runtime settings.");
                    continue;
                }
                if (definition.ConfigEntry == null)
                {
                    Log.Error($"Canopy glass config entry for [{definition.Section}] {definition.Key} was not bound. Falling back to default value.");
                    definition.CachedDisabled = definition.DefaultValue;
                }
                else
                {
                    definition.CachedDisabled = definition.ConfigEntry.Value;
                }
            }

            Captured = true;
            Log.Info("BVR runtime settings captured. Any future config changes require a full game restart.");
        }

        private static int SafeInt(int value, int fallback, string name)
        {
            if (value < 0)
            {
                Log.Error($"Config value '{name}' is invalid ({value}). Using fallback value {fallback}.");
                return fallback;
            }
            return value;
        }

        private static float SafeFloat(float value, float fallback, string name)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                Log.Error($"Config value '{name}' is invalid ({value}). Using fallback value {fallback}.");
                return fallback;
            }
            return value;
        }
    }



    // ========================================================================
    // Object name and hierarchy helpers.
    // ========================================================================
    internal static class ObjectNameUtility
    {
        public static string RemoveCloneSuffix(string name)
        {
            return string.IsNullOrEmpty(name) ? "" : name.Replace("(Clone)", "");
        }

        public static string GetCleanRootName(GameObject obj)
        {
            if (obj == null)
                return "";
            Transform root = obj.transform?.root;
            return RemoveCloneSuffix((root?.gameObject ?? obj).name);
        }

        public static bool IsUnderNamedObject(GameObject obj, string targetName)
        {
            if (obj == null || string.IsNullOrEmpty(targetName))
                return false;
            Transform current = obj.transform;
            while (current != null)
            {
                if (RemoveCloneSuffix(current.gameObject.name) == targetName)
                    return true;
                current = current.parent;
            }
            return false;
        }

        public static string GetHierarchyPath(GameObject obj)
        {
            if (obj == null)
                return "<null>";
            string path = obj.name;
            Transform current = obj.transform.parent;
            while (current != null)
            {
                path = current.name + "/" + path;
                current = current.parent;
            }
            return path;
        }

        public static bool IsPrefabAsset(GameObject obj)
        {
            if (obj == null)
                return false;
            try
            {
                // Prefab assets generally do not belong to a valid scene.
                return !obj.scene.IsValid();
            }
            catch
            {
                return false;
            }
        }
    }



    // ========================================================================
    // Shared SARH missile matching.
    // Persistence and relock must use the same logic. Hierarchy matching is
    // preferred because it avoids accidental substring matches and works even
    // when the seeker is a child of the missile prefab.
    // ========================================================================
    internal static class SarhMissileMatcher
    {
        public const string R9RootName = "SAM_Radar2";
        public const string RAM45RootName = "SAM_Radar1";

        public static bool IsR9(GameObject obj)
        {
            return IsMatch(obj, R9RootName);
        }

        public static bool IsRAM45(GameObject obj)
        {
            return IsMatch(obj, RAM45RootName);
        }

        private static bool IsMatch(GameObject obj, string rootName)
        {
            if (obj == null || string.IsNullOrEmpty(rootName))
                return false;
            return ObjectNameUtility.IsUnderNamedObject(obj, rootName);
        }
    }



    // ========================================================================
    // Blueprint weapon definitions.
    // To add a new blueprint toggle:
    //   1. Add a new BlueprintWeaponDefinition below.
    //   2. Ensure Section, Key, Description, and DefaultValue are user-friendly.
    //   3. Ensure ExactAircraftRootName and ExactBlueprintKeys are correct.
    //   4. No additional binding code is required; Plugin.BindBlueprinterWeapons
    //      handles it automatically.
    //
    // Matching rules are intentionally strict:
    //   - The aircraft root name must exactly equal ExactAircraftRootName.
    //   - The weapon option identifier must exactly equal one ExactBlueprintKey.
    //   - No substring matching is allowed.
    // ========================================================================
    internal class BlueprintWeaponDefinition
    {
        public string Section, Key, Description;
        public bool DefaultValue;
        public string ExactAircraftRootName;
        public int[] HardpointSets;
        public string[] ExactBlueprintKeys;
        public ConfigEntry<bool> ConfigEntry;
        public bool CachedEnabled;
    }

    internal static class BlueprintWeaponRegistry
    {
        public static readonly List<BlueprintWeaponDefinition> Definitions = new List<BlueprintWeaponDefinition>
        {
            new BlueprintWeaponDefinition { Section = "CI-22 Cricket Changes", Key = "Enable Cricket Kingpin x8 Double", Description = "Enables Blueprinter BVR_Rocket2_4Podx2 on sets 2, 3", DefaultValue = true, ExactAircraftRootName = "COIN", HardpointSets = new[] { 2, 3 }, ExactBlueprintKeys = new[] { "BVR_Rocket2_4Podx2" } },
            new BlueprintWeaponDefinition { Section = "CI-22 Cricket Changes", Key = "Enable Cricket Lynchpin x14 Double", Description = "Enables Blueprinter BVR_RocketPod1_double on sets 2, 3", DefaultValue = true, ExactAircraftRootName = "COIN", HardpointSets = new[] { 2, 3 }, ExactBlueprintKeys = new[] { "BVR_RocketPod1_double" } },
            new BlueprintWeaponDefinition { Section = "T/A-30 Compass Changes", Key = "Enable Compass Kingpin x8 Double", Description = "Enables Blueprinter BVR_Rocket2_4Podx2 on set 1", DefaultValue = true, ExactAircraftRootName = "trainer", HardpointSets = new[] { 1 }, ExactBlueprintKeys = new[] { "BVR_Rocket2_4Podx2" } },
            new BlueprintWeaponDefinition { Section = "T/A-30 Compass Changes", Key = "Enable Compass Lynchpin x14 Double", Description = "Enables Blueprinter BVR_RocketPod1_double on set 1", DefaultValue = true, ExactAircraftRootName = "trainer", HardpointSets = new[] { 1 }, ExactBlueprintKeys = new[] { "BVR_RocketPod1_double" } },
            new BlueprintWeaponDefinition { Section = "VT-7 Vagrant Changes", Key = "Enable Vagrant Kingpin x8 Double", Description = "Enables Blueprinter BVR_Rocket2_4Podx2 on set 3", DefaultValue = true, ExactAircraftRootName = "VTOLTrainer1", HardpointSets = new[] { 3 }, ExactBlueprintKeys = new[] { "BVR_Rocket2_4Podx2" } },
            new BlueprintWeaponDefinition { Section = "VT-7 Vagrant Changes", Key = "Enable Vagrant Lynchpin x14 Double", Description = "Enables Blueprinter BVR_RocketPod1_double on set 3", DefaultValue = true, ExactAircraftRootName = "VTOLTrainer1", HardpointSets = new[] { 3 }, ExactBlueprintKeys = new[] { "BVR_RocketPod1_double" } },
            new BlueprintWeaponDefinition { Section = "UH-90 Ibis Changes", Key = "Enable Ibis Kingpin x8 Double", Description = "Enables Blueprinter BVR_Rocket2_4Podx2 on sets 0, 1", DefaultValue = true, ExactAircraftRootName = "UtilityHelo1", HardpointSets = new[] { 0, 1 }, ExactBlueprintKeys = new[] { "BVR_Rocket2_4Podx2" } },
            new BlueprintWeaponDefinition { Section = "UH-90 Ibis Changes", Key = "Enable Ibis Lynchpin x14 Double", Description = "Enables Blueprinter BVR_RocketPod1_double on sets 0, 1", DefaultValue = true, ExactAircraftRootName = "UtilityHelo1", HardpointSets = new[] { 0, 1 }, ExactBlueprintKeys = new[] { "BVR_RocketPod1_double" } },
            new BlueprintWeaponDefinition { Section = "UH-90 Ibis Changes", Key = "Enable Ibis Hexhound Munitions x1", Description = "Enables Blueprinter BVR_UGV1_Mx1 on sets 4, 5", DefaultValue = true, ExactAircraftRootName = "UtilityHelo1", HardpointSets = new[] { 4, 5 }, ExactBlueprintKeys = new[] { "BVR_UGV1_Mx1" } },
            new BlueprintWeaponDefinition { Section = "UH-90 Ibis Changes", Key = "Enable Ibis Hexhound ATGM x1", Description = "Enables Blueprinter BVR_UGV1_ATGMx1 on sets 4, 5", DefaultValue = true, ExactAircraftRootName = "UtilityHelo1", HardpointSets = new[] { 4, 5 }, ExactBlueprintKeys = new[] { "BVR_UGV1_ATGMx1" } },
            new BlueprintWeaponDefinition { Section = "SAH-46 Chicane Changes", Key = "Enable Chicane Scythe x2", Description = "Enables Blueprinter AAM2_double on set 2", DefaultValue = true, ExactAircraftRootName = "AttackHelo1", HardpointSets = new[] { 2 }, ExactBlueprintKeys = new[] { "AAM2_double" } },
            new BlueprintWeaponDefinition { Section = "SAH-46 Chicane Changes", Key = "Enable Chicane Scythe x1", Description = "Enables Blueprinter AAM2_single on set 2", DefaultValue = true, ExactAircraftRootName = "AttackHelo1", HardpointSets = new[] { 2 }, ExactBlueprintKeys = new[] { "AAM2_single" } },
            new BlueprintWeaponDefinition { Section = "SAH-46 Chicane Changes", Key = "Enable Chicane Internal Kingpin x8", Description = "Enables Blueprinter BVR_Rocket2_4Podx2_BayDoor on set 1", DefaultValue = true, ExactAircraftRootName = "AttackHelo1", HardpointSets = new[] { 1 }, ExactBlueprintKeys = new[] { "BVR_Rocket2_4Podx2_BayDoor" } },
            new BlueprintWeaponDefinition { Section = "SAH-46 Chicane Changes", Key = "Enable Chicane Internal Lynchpin x14", Description = "Enables Blueprinter BVR_RocketPod1_double_BayDoor on set 1", DefaultValue = true, ExactAircraftRootName = "AttackHelo1", HardpointSets = new[] { 1 }, ExactBlueprintKeys = new[] { "BVR_RocketPod1_double_BayDoor" } },
            new BlueprintWeaponDefinition { Section = "FS-12 Revoker Changes", Key = "Enable Revoker Kingpin x8 Double", Description = "Enables Blueprinter BVR_Rocket2_4Podx2 on set 2", DefaultValue = true, ExactAircraftRootName = "Fighter1", HardpointSets = new[] { 2 }, ExactBlueprintKeys = new[] { "BVR_Rocket2_4Podx2" } },
            new BlueprintWeaponDefinition { Section = "FS-12 Revoker Changes", Key = "Enable Revoker Lynchpin x14 Double", Description = "Enables Blueprinter BVR_RocketPod1_double on set 2", DefaultValue = true, ExactAircraftRootName = "Fighter1", HardpointSets = new[] { 2 }, ExactBlueprintKeys = new[] { "BVR_RocketPod1_double" } },
            new BlueprintWeaponDefinition { Section = "FS-12 Revoker Changes", Key = "Enable Revoker Kingpin x12 Triple", Description = "Enables Blueprinter Rocket2_4Podx3 on set 2", DefaultValue = true, ExactAircraftRootName = "Fighter1", HardpointSets = new[] { 2 }, ExactBlueprintKeys = new[] { "Rocket2_4Podx3" } },
            new BlueprintWeaponDefinition { Section = "FS-12 Revoker Changes", Key = "Enable Revoker Lynchpin x21 Triple", Description = "Enables Blueprinter RocketPod1_triple on set 2", DefaultValue = true, ExactAircraftRootName = "Fighter1", HardpointSets = new[] { 2 }, ExactBlueprintKeys = new[] { "RocketPod1_triple" } },
            new BlueprintWeaponDefinition { Section = "FS-20 Vortex Changes", Key = "Enable Vortex Kingpin x8 Double", Description = "Enables Blueprinter BVR_Rocket2_4Podx2 on set 3", DefaultValue = true, ExactAircraftRootName = "SmallFighter1", HardpointSets = new[] { 3 }, ExactBlueprintKeys = new[] { "BVR_Rocket2_4Podx2" } },
            new BlueprintWeaponDefinition { Section = "FS-20 Vortex Changes", Key = "Enable Vortex Lynchpin x14 Double", Description = "Enables Blueprinter BVR_RocketPod1_double on set 3", DefaultValue = true, ExactAircraftRootName = "SmallFighter1", HardpointSets = new[] { 3 }, ExactBlueprintKeys = new[] { "BVR_RocketPod1_double" } },
            new BlueprintWeaponDefinition { Section = "VL-49 Tarantula Changes", Key = "Enable Tarantula Kingpin x8 Double", Description = "Enables Blueprinter BVR_Rocket2_4Podx2 on sets 4, 5", DefaultValue = true, ExactAircraftRootName = "QuadVTOL1", HardpointSets = new[] { 4, 5 }, ExactBlueprintKeys = new[] { "BVR_Rocket2_4Podx2" } },
            new BlueprintWeaponDefinition { Section = "VL-49 Tarantula Changes", Key = "Enable Tarantula Lynchpin x14 Double", Description = "Enables Blueprinter BVR_RocketPod1_double on sets 4, 5", DefaultValue = true, ExactAircraftRootName = "QuadVTOL1", HardpointSets = new[] { 4, 5 }, ExactBlueprintKeys = new[] { "BVR_RocketPod1_double" } },
            new BlueprintWeaponDefinition { Section = "VL-49 Tarantula Changes", Key = "Enable Tarantula 20mm Rotary Cannon", Description = "Enables Blueprinter BVR_turret_20mm_rotary on set 3", DefaultValue = true, ExactAircraftRootName = "QuadVTOL1", HardpointSets = new[] { 3 }, ExactBlueprintKeys = new[] { "BVR_turret_20mm_rotary" } },
            new BlueprintWeaponDefinition { Section = "VL-49 Tarantula Changes", Key = "Enable Tarantula 57mm Side Mount", Description = "Enables Blueprinter BVR_turret_57mm_SideMount on set 2", DefaultValue = true, ExactAircraftRootName = "QuadVTOL1", HardpointSets = new[] { 2 }, ExactBlueprintKeys = new[] { "BVR_turret_57mm_SideMount" } },
            new BlueprintWeaponDefinition { Section = "VL-49 Tarantula Changes", Key = "Enable Tarantula 57mm Belly Mount", Description = "Enables Blueprinter BVR_turret_57mm_BellyMount on set 2", DefaultValue = true, ExactAircraftRootName = "QuadVTOL1", HardpointSets = new[] { 2 }, ExactBlueprintKeys = new[] { "BVR_turret_57mm_BellyMount" } },
            new BlueprintWeaponDefinition { Section = "VL-49 Tarantula Changes", Key = "Enable Tarantula SPAAG-1 x1", Description = "Enables Blueprinter BVR_SPAAG1x1 on sets 0, 1", DefaultValue = true, ExactAircraftRootName = "QuadVTOL1", HardpointSets = new[] { 0, 1 }, ExactBlueprintKeys = new[] { "BVR_SPAAG1x1" } },
            new BlueprintWeaponDefinition { Section = "VL-49 Tarantula Changes", Key = "Enable Tarantula SPAAG-2 x1", Description = "Enables Blueprinter BVR_SPAAG2x1 on sets 0, 1", DefaultValue = true, ExactAircraftRootName = "QuadVTOL1", HardpointSets = new[] { 0, 1 }, ExactBlueprintKeys = new[] { "BVR_SPAAG2x1" } },
            new BlueprintWeaponDefinition { Section = "VL-49 Tarantula Changes", Key = "Enable Tarantula HLT-MArt x1", Description = "Enables Blueprinter BVR_HLT-MArtx1 on sets 0, 1", DefaultValue = true, ExactAircraftRootName = "QuadVTOL1", HardpointSets = new[] { 0, 1 }, ExactBlueprintKeys = new[] { "BVR_HLT-MArtx1" } },
            new BlueprintWeaponDefinition { Section = "VL-49 Tarantula Changes", Key = "Enable Tarantula Truck2-MLRS x1", Description = "Enables Blueprinter BVR_Truck2-MLRSx1 on sets 0, 1", DefaultValue = true, ExactAircraftRootName = "QuadVTOL1", HardpointSets = new[] { 0, 1 }, ExactBlueprintKeys = new[] { "BVR_Truck2-MLRSx1" } },
            new BlueprintWeaponDefinition { Section = "KR-67 Ifrit Changes", Key = "Enable Ifrit Kingpin x8 Double", Description = "Enables Blueprinter BVR_Rocket2_4Podx2 on set 4", DefaultValue = true, ExactAircraftRootName = "Multirole1", HardpointSets = new[] { 4 }, ExactBlueprintKeys = new[] { "BVR_Rocket2_4Podx2" } },
            new BlueprintWeaponDefinition { Section = "KR-67 Ifrit Changes", Key = "Enable Ifrit Lynchpin x14 Double", Description = "Enables Blueprinter BVR_RocketPod1_double on set 4", DefaultValue = true, ExactAircraftRootName = "Multirole1", HardpointSets = new[] { 4 }, ExactBlueprintKeys = new[] { "BVR_RocketPod1_double" } },
            new BlueprintWeaponDefinition { Section = "EW-25 Medusa Changes", Key = "Enable Medusa Kingpin x8 Double", Description = "Enables Blueprinter BVR_Rocket2_4Podx2 on sets 3, 4", DefaultValue = true, ExactAircraftRootName = "EW1", HardpointSets = new[] { 3, 4 }, ExactBlueprintKeys = new[] { "BVR_Rocket2_4Podx2" } },
            new BlueprintWeaponDefinition { Section = "EW-25 Medusa Changes", Key = "Enable Medusa Lynchpin x14 Double", Description = "Enables Blueprinter BVR_RocketPod1_double on sets 3, 4", DefaultValue = true, ExactAircraftRootName = "EW1", HardpointSets = new[] { 3, 4 }, ExactBlueprintKeys = new[] { "BVR_RocketPod1_double" } },
            new BlueprintWeaponDefinition { Section = "EW-25 Medusa Changes", Key = "Enable Medusa Kingpin x12 Triple", Description = "Enables Blueprinter Rocket2_4Podx3 on sets 3, 4", DefaultValue = true, ExactAircraftRootName = "EW1", HardpointSets = new[] { 3, 4 }, ExactBlueprintKeys = new[] { "Rocket2_4Podx3" } },
            new BlueprintWeaponDefinition { Section = "EW-25 Medusa Changes", Key = "Enable Medusa Lynchpin x21 Triple", Description = "Enables Blueprinter RocketPod1_triple on sets 3, 4", DefaultValue = true, ExactAircraftRootName = "EW1", HardpointSets = new[] { 3, 4 }, ExactBlueprintKeys = new[] { "RocketPod1_triple" } },
            new BlueprintWeaponDefinition { Section = "EW-25 Medusa Changes", Key = "Enable Medusa RAM-45 x3", Description = "Enables Blueprinter BVR_SAM_Radar1x3 on sets 3, 4", DefaultValue = true, ExactAircraftRootName = "EW1", HardpointSets = new[] { 3, 4 }, ExactBlueprintKeys = new[] { "BVR_SAM_Radar1x3" } },
            new BlueprintWeaponDefinition { Section = "EW-25 Medusa Changes", Key = "Enable Medusa Internal RAM-45 x3", Description = "Enables Blueprinter BVR_SAM_Radar1x3_Internal on set 1", DefaultValue = true, ExactAircraftRootName = "EW1", HardpointSets = new[] { 1 }, ExactBlueprintKeys = new[] { "BVR_SAM_Radar1x3_Internal" } },
            new BlueprintWeaponDefinition { Section = "EW-25 Medusa Changes", Key = "Enable Medusa R9 Stratolance x2", Description = "Enables Blueprinter BVR_SAM_Radar2x2 on sets 3, 4", DefaultValue = true, ExactAircraftRootName = "EW1", HardpointSets = new[] { 3, 4 }, ExactBlueprintKeys = new[] { "BVR_SAM_Radar2x2" } },
            new BlueprintWeaponDefinition { Section = "EW-25 Medusa Changes", Key = "Enable Medusa Internal R9 Stratolance x2", Description = "Enables Blueprinter BVR_SAM_Radar2x2_Internal on set 1", DefaultValue = true, ExactAircraftRootName = "EW1", HardpointSets = new[] { 1 }, ExactBlueprintKeys = new[] { "BVR_SAM_Radar2x2_Internal" } }
        };
    }



    // ========================================================================
    // Canopy glass definitions.
    // To add a new canopy glass toggle:
    //   1. Add a new CanopyGlassDefinition below.
    //   2. Ensure Section, Key, Description, and DefaultValue are user-friendly.
    //   3. Ensure RootNames and GlassNames are correct.
    //   4. No additional binding code is required; Plugin.BindCanopyGlass
    //      handles it automatically.
    // ========================================================================
    internal class CanopyGlassDefinition
    {
        public string Section, Key, Description;
        public bool DefaultValue;
        public string[] RootNames;
        public string[] GlassNames;
        public ConfigEntry<bool> ConfigEntry;
        public bool CachedDisabled;
    }

    internal static class CanopyGlassRegistry
    {
        public static readonly List<CanopyGlassDefinition> Definitions = new List<CanopyGlassDefinition>
        {
            new CanopyGlassDefinition { Section = "CI-22 Cricket Changes", Key = "Disable Canopy Glass", Description = "Disables this aircraft's canopy glass for better visibility.", DefaultValue = true, RootNames = new[] { "COIN" }, GlassNames = new[] { "canopyGlass_F_interior", "canopy_glass_R_interior" } },
            new CanopyGlassDefinition { Section = "T/A-30 Compass Changes", Key = "Disable Canopy Glass", Description = "Disables this aircraft's canopy glass for better visibility.", DefaultValue = true, RootNames = new[] { "trainer" }, GlassNames = new[] { "canopyglass_R_int", "canopyglass_F_int", "canopyglass_FF_int" } },
            new CanopyGlassDefinition { Section = "VT-7 Vagrant Changes", Key = "Disable Canopy Glass", Description = "Disables this aircraft's canopy glass for better visibility.", DefaultValue = true, RootNames = new[] { "VTOLTrainer1" }, GlassNames = new[] { "canopy_R_int", "canopy_F_int" } },
            new CanopyGlassDefinition { Section = "UH-90 Ibis Changes", Key = "Disable Canopy Glass", Description = "Disables this aircraft's canopy glass for better visibility.", DefaultValue = true, RootNames = new[] { "UtilityHelo1" }, GlassNames = new[] { "canopyGlass_int", "cockpitWindowGlass_int", "doorGlass_L_int" } },
            new CanopyGlassDefinition { Section = "SAH-46 Chicane Changes", Key = "Disable Canopy Glass", Description = "Disables this aircraft's canopy glass for better visibility.", DefaultValue = true, RootNames = new[] { "AttackHelo1" }, GlassNames = new[] { "canopyglass_RL_interior", "canopyglass_interior", "canopyglass_FL_interior" } },
            new CanopyGlassDefinition { Section = "A-19 Brawler Changes", Key = "Disable Canopy Glass", Description = "Disables this aircraft's canopy glass for better visibility.", DefaultValue = true, RootNames = new[] { "CAS1" }, GlassNames = new[] { "canopyGlass_int", "canopyGlass_F_int" } },
            new CanopyGlassDefinition { Section = "FS-12 Revoker Changes", Key = "Disable Canopy Glass", Description = "Disables this aircraft's canopy glass for better visibility.", DefaultValue = true, RootNames = new[] { "Fighter1" }, GlassNames = new[] { "canopyGlass_interior" } },
            new CanopyGlassDefinition { Section = "FS-20 Vortex Changes", Key = "Disable Canopy Glass", Description = "Disables this aircraft's canopy glass for better visibility.", DefaultValue = true, RootNames = new[] { "SmallFighter1" }, GlassNames = new[] { "canopy_int" } },
            new CanopyGlassDefinition { Section = "VL-49 Tarantula Changes", Key = "Disable Canopy Glass", Description = "Disables this aircraft's canopy glass for better visibility.", DefaultValue = true, RootNames = new[] { "QuadVTOL1" }, GlassNames = new[] { "glass_cockpit_int", "door_FFL_glass_int", "door_FFR_glass_int", "nose_glass_int" } },
            new CanopyGlassDefinition { Section = "KR-67 Ifrit Changes", Key = "Disable Canopy Glass", Description = "Disables this aircraft's canopy glass for better visibility.", DefaultValue = true, RootNames = new[] { "Multirole1" }, GlassNames = new[] { "canopy_R_int", "canopy_F_int" } },
            new CanopyGlassDefinition { Section = "EW-25 Medusa Changes", Key = "Disable Canopy Glass", Description = "Disables this aircraft's canopy glass for better visibility.", DefaultValue = true, RootNames = new[] { "EW1" }, GlassNames = new[] { "canopy_int" } },
            new CanopyGlassDefinition { Section = "SFB-81 Darkreach Changes", Key = "Disable Canopy Glass", Description = "Disables this aircraft's canopy glass for better visibility.", DefaultValue = true, RootNames = new[] { "Darkreach" }, GlassNames = new[] { "canopyGlass_interior", "windows_interior" } },
            new CanopyGlassDefinition { Section = "Alkyon AB-4 Changes", Key = "Disable Canopy Glass", Description = "Disables this aircraft's canopy glass for better visibility.", DefaultValue = true, RootNames = new[] { "FastBomber1" }, GlassNames = new[] { "canopy_int", "doorGlass_L_int", "doorGlass_R_int" } }
        };
    }



    // ========================================================================
    // Runtime blueprint rule used by the removal system.
    // Matching is intentionally exact:
    //   - The cleaned aircraft root name must exactly equal ExactRootName.
    //   - The weapon option identifier must exactly equal one ExactBlueprintKey.
    // No substring matching or deeper hierarchy searching is performed.
    // ========================================================================
    internal sealed class BlueprintWeaponRule
    {
        public string ExactRootName;
        public string DisplayName;
        public bool Enabled;
        public string[] ExactBlueprintKeys;
        public int[] HardpointSets;

        public bool MatchesAircraft(string rootName)
        {
            if (string.IsNullOrEmpty(ExactRootName) || string.IsNullOrEmpty(rootName))
                return false;

            // Exact clean root name only. No Contains, no fallback, no guessing.
            return string.Equals(ExactRootName, rootName, StringComparison.Ordinal);
        }

        public bool MatchesWeaponOption(object weaponOption)
        {
            if (BlueprintRemovalReflection.IsNull(weaponOption) || ExactBlueprintKeys == null)
                return false;

            List<string> identifiers = BlueprintRemovalReflection.GetExactIdentifiers(weaponOption);
            return ExactBlueprintKeys.Any(key => identifiers.Any(id => string.Equals(id, key, StringComparison.Ordinal)));
        }
    }



    // ========================================================================
    // Reflection helper for blueprint weapon option removal.
    // This helper is intentionally narrow:
    //   - It looks directly for HardpointSet.weaponOptions.
    //   - It removes matching entries from that collection.
    //   - It does not recursively search nested weapon/mount/option objects.
    // ========================================================================
    internal static class BlueprintRemovalReflection
    {
        private const BindingFlags Flags =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        private static readonly string[] WeaponOptionsMemberNames =
        {
            "weaponOptions",
            "WeaponOptions"
        };

        private static readonly string[] IdentifierMemberNames =
        {
            "blueprintKey",
            "jsonKey",
            "key",
            "id",
            "name"
        };

        public static bool IsNull(object obj)
        {
            return obj == null || (obj is UnityEngine.Object unityObject && unityObject == null);
        }

        public static List<string> GetExactIdentifiers(object obj)
        {
            List<string> identifiers = new List<string>();
            if (IsNull(obj))
                return identifiers;

            if (obj is string directString)
                AddIdentifier(identifiers, directString);

            if (obj is Component component && component.gameObject != null)
                AddIdentifier(identifiers, component.gameObject.name);

            if (obj is UnityEngine.Object unityObject)
                AddIdentifier(identifiers, unityObject.name);

            foreach (string memberName in IdentifierMemberNames)
            {
                if (!TryGet(obj, memberName, out object value) || IsNull(value))
                    continue;

                if (value is string stringValue)
                {
                    AddIdentifier(identifiers, stringValue);
                }
                else if (value is UnityEngine.Object valueUnityObject)
                {
                    AddIdentifier(identifiers, valueUnityObject.name);
                }
            }

            return identifiers;
        }

        public static bool TryRemoveWeaponOptions(
            object hardpointSet,
            Func<object, bool> matcher,
            out int removedCount,
            out string failure)
        {
            removedCount = 0;
            failure = null;

            if (IsNull(hardpointSet))
            {
                failure = "hardpoint set is null";
                return false;
            }

            if (matcher == null)
            {
                failure = "matcher is null";
                return false;
            }

            FieldInfo field;
            PropertyInfo property;
            object collection;
            if (!TryGetWeaponOptionsMember(hardpointSet, out field, out property, out collection))
            {
                failure = "missing weaponOptions member";
                return false;
            }

            if (IsNull(collection))
            {
                failure = "weaponOptions member is null";
                return false;
            }

            if (collection is Array array)
            {
                List<object> kept = new List<object>();

                foreach (object item in array)
                {
                    if (!IsNull(item) && matcher(item))
                        removedCount++;
                    else
                        kept.Add(item);
                }

                if (removedCount == 0)
                    return true;

                Type elementType = array.GetType().GetElementType() ?? typeof(object);
                Array newArray = Array.CreateInstance(elementType, kept.Count);

                for (int i = 0; i < kept.Count; i++)
                    newArray.SetValue(kept[i], i);

                return TrySetWeaponOptionsMember(hardpointSet, field, property, newArray, out failure);
            }

            if (collection is IList list)
            {
                if (list.IsReadOnly || list.IsFixedSize)
                {
                    failure = $"weaponOptions list type '{collection.GetType().Name}' is read-only or fixed-size";
                    return false;
                }

                for (int i = list.Count - 1; i >= 0; i--)
                {
                    object item = list[i];
                    if (IsNull(item) || !matcher(item))
                        continue;

                    list.RemoveAt(i);
                    removedCount++;
                }

                return true;
            }

            failure = $"weaponOptions member type '{collection.GetType().Name}' is not an IList or Array";
            return false;
        }

        private static bool TryGetWeaponOptionsMember(
            object target,
            out FieldInfo field,
            out PropertyInfo property,
            out object value)
        {
            field = null;
            property = null;
            value = null;

            foreach (string memberName in WeaponOptionsMemberNames)
            {
                for (Type type = target.GetType(); type != null && type != typeof(object); type = type.BaseType)
                {
                    field = type.GetField(memberName, Flags);
                    if (field != null)
                    {
                        value = field.GetValue(target);
                        return true;
                    }

                    property = type.GetProperty(memberName, Flags);
                    if (property != null && property.CanRead)
                    {
                        value = property.GetValue(target);
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool TrySetWeaponOptionsMember(
            object target,
            FieldInfo field,
            PropertyInfo property,
            object newValue,
            out string failure)
        {
            failure = null;

            try
            {
                if (field != null)
                {
                    field.SetValue(target, newValue);
                    return true;
                }

                if (property != null && property.CanWrite)
                {
                    property.SetValue(target, newValue);
                    return true;
                }

                failure = "weaponOptions member is read-only";
                return false;
            }
            catch (Exception ex)
            {
                failure = ex.Message;
                return false;
            }
        }

        private static bool TryGet(object target, string memberName, out object value)
        {
            value = null;
            if (IsNull(target))
                return false;

            for (Type type = target.GetType(); type != null && type != typeof(object); type = type.BaseType)
            {
                FieldInfo field = type.GetField(memberName, Flags);
                if (field != null)
                {
                    value = field.GetValue(target);
                    return true;
                }

                PropertyInfo property = type.GetProperty(memberName, Flags);
                if (property != null && property.CanRead)
                {
                    value = property.GetValue(target);
                    return true;
                }
            }

            return false;
        }

        private static void AddIdentifier(List<string> list, string identifier)
        {
            if (string.IsNullOrEmpty(identifier))
                return;

            string clean = ObjectNameUtility.RemoveCloneSuffix(identifier).Trim();
            if (!string.IsNullOrEmpty(clean) && !list.Contains(clean))
                list.Add(clean);
        }
    }



    // ========================================================================
    // Blueprint weapon removal system.
    // Runs when the MainMenu scene is loaded and scans all loaded memory,
    // including prefab assets. It removes disabled blueprint options from
    // HardpointSet.weaponOptions instead of setting a disabled bool.
    //
    // This system is event-driven. It does not poll.
    // ========================================================================
    // ========================================================================
    // Blueprint weapon removal system.
    // Runs when any scene is loaded and scans all loaded memory, including
    // prefab assets. It removes disabled blueprint options from
    // HardpointSet.weaponOptions instead of setting a disabled bool.
    //
    // The sweep runs on MainMenu AND on the first non-MainMenu scene load.
    // This is critical because Blueprinter loads AFTER BVR in the plugin
    // order, so the MainMenu sweep may fire before Blueprinter has applied
    // its patches. The GameWorld scene sweep catches this case.
    //
    // This system is event-driven. It does not poll.
    // ========================================================================
    internal static class BlueprintWeaponRemovalSystem
    {
        private static bool initialized;
        private static bool sweepSucceeded;
        private static List<BlueprintWeaponRule> rules = new List<BlueprintWeaponRule>();
        private static readonly HashSet<string> removedLogKeys = new HashSet<string>();
        private static readonly HashSet<string> diagnosticLogKeys = new HashSet<string>();

        public static List<BlueprintWeaponRule> Rules => rules;
        public static HashSet<string> DiagnosticLogKeys => diagnosticLogKeys;

        public static bool IsWeaponMountDisabled(WeaponManager weaponManager, object weaponMount)
        {
            if (weaponManager == null || weaponMount == null)
                return false;

            string rootName = ObjectNameUtility.GetCleanRootName(weaponManager.transform.root?.gameObject ?? weaponManager.gameObject);
            if (string.IsNullOrEmpty(rootName))
                return false;

            List<string> identifiers = BlueprintRemovalReflection.GetExactIdentifiers(weaponMount);

            foreach (BlueprintWeaponRule rule in rules)
            {
                if (rule.Enabled)
                    continue;
                if (!rule.MatchesAircraft(rootName))
                    continue;

                if (rule.ExactBlueprintKeys != null && rule.ExactBlueprintKeys.Any(key => identifiers.Any(id => string.Equals(id, key, StringComparison.Ordinal))))
                {
                    return true;
                }
            }
            return false;
        }

        public static void Initialize(Plugin owner)
        {
            if (initialized)
                return;

            if (owner == null)
            {
                Log.Error("BlueprintWeaponRemovalSystem.Initialize was called with a null plugin instance.");
                return;
            }

            if (!RuntimeSettings.Captured)
            {
                Log.Error("BlueprintWeaponRemovalSystem.Initialize was called before RuntimeSettings.Capture. Blueprint removal will not run.");
                return;
            }

            rules.Clear();
            foreach (BlueprintWeaponDefinition definition in BlueprintWeaponRegistry.Definitions)
            {
                if (definition == null)
                {
                    Log.Error("BlueprintWeaponRegistry contains a null definition. This definition will be skipped.");
                    continue;
                }
                if (string.IsNullOrEmpty(definition.ExactAircraftRootName))
                {
                    Log.Error($"Blueprint definition [{definition.Section}] {definition.Key} is missing ExactAircraftRootName. This definition will be skipped.");
                    continue;
                }
                if (definition.ExactBlueprintKeys == null || definition.ExactBlueprintKeys.Length == 0)
                {
                    Log.Error($"Blueprint definition [{definition.Section}] {definition.Key} is missing ExactBlueprintKeys. This definition will be skipped.");
                    continue;
                }

                rules.Add(new BlueprintWeaponRule
                {
                    ExactRootName = definition.ExactAircraftRootName,
                    DisplayName = definition.Key,
                    Enabled = definition.CachedEnabled,
                    ExactBlueprintKeys = definition.ExactBlueprintKeys,
                    HardpointSets = definition.HardpointSets
                });
            }

            initialized = true;
            SceneManager.sceneLoaded += OnSceneLoaded;

            Scene activeScene = SceneManager.GetActiveScene();
            if (activeScene.IsValid())
                ApplyAll($"Active scene '{activeScene.name}' at initialization");

            Log.Info("Blueprint weapon removal system initialized using startup-cached config values. Application is event-driven and does not poll.");
        }

        public static void Shutdown()
        {
            if (!initialized)
                return;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            initialized = false;
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!initialized || !scene.IsValid())
                return;

            // Always run on MainMenu (original behavior).
            // Also run on the first non-MainMenu scene if the sweep has not
            // yet succeeded. This is critical because Blueprinter loads AFTER
            // BVR in the plugin order. The MainMenu sweep fires before
            // Blueprinter applies its patches to the prefab assets. By the
            // time the GameWorld scene loads, Blueprinter has finished and
            // the prefab assets contain the blueprint weapon options.
            if (scene.name == "MainMenu" || !sweepSucceeded)
            {
                ApplyAll($"Scene '{scene.name}' loaded");
            }
        }

        private static void ApplyAll(string reason)
        {
            if (!initialized)
                return;

            try
            {
                int found = 0;
                int totalRemoved = 0;

                foreach (WeaponManager weaponManager in Resources.FindObjectsOfTypeAll<WeaponManager>())
                {
                    if (BlueprintRemovalReflection.IsNull(weaponManager))
                        continue;
                    found++;
                    totalRemoved += Apply(weaponManager);
                }

                if (found > 0 && totalRemoved > 0)
                {
                    sweepSucceeded = true;
                }

                if (found == 0)
                {
                    MissingMemberLog.ErrorOnce(
                        "BlueprintWeaponRemoval.NoWeaponManagers",
                        $"[Blueprints] No WeaponManager instances were found during blueprint removal sweep. Reason='{reason}'. Loaded prefab assets and scene objects were both included.");
                }
                else
                {
                    string logKey = $"BlueprintWeaponRemoval.SweepComplete|{reason}";
                    if (diagnosticLogKeys.Add(logKey))
                    {
                        Log.Info($"[Blueprints] Completed blueprint removal sweep. WeaponManagers scanned={found}, options removed={totalRemoved}. Reason='{reason}'.");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Exception($"Blueprint weapon removal ({reason})", ex);
            }
        }

        private static int Apply(WeaponManager weaponManager)
        {
            if (BlueprintRemovalReflection.IsNull(weaponManager) || weaponManager.transform == null)
                return 0;

            string rootName = ObjectNameUtility.GetCleanRootName(weaponManager.transform.root?.gameObject ?? weaponManager.gameObject);
            if (string.IsNullOrEmpty(rootName))
                return 0;

            int totalRemoved = 0;

            foreach (BlueprintWeaponRule rule in rules)
            {
                if (!rule.MatchesAircraft(rootName))
                    continue;
                if (rule.Enabled)
                    continue;

                if (weaponManager.hardpointSets == null)
                {
                    MissingMemberLog.ErrorOnce(
                        $"BlueprintWeaponRemoval.HardpointArrayNull|{rootName}",
                        $"[Blueprints] WeaponManager on '{rootName}' has null hardpointSets.");
                    continue;
                }

                foreach (int hardpointSet in rule.HardpointSets)
                {
                    string removalKey = $"{rootName}|{rule.DisplayName}|{hardpointSet}";

                    if (hardpointSet < 0 || hardpointSet >= weaponManager.hardpointSets.Length)
                    {
                        MissingMemberLog.ErrorOnce(
                            $"BlueprintWeaponRemoval.HardpointOutOfRange|{removalKey}",
                            $"[Blueprints] Hardpoint set {hardpointSet} for '{rule.DisplayName}' on '{rootName}' is out of range. Count={weaponManager.hardpointSets.Length}.");
                        continue;
                    }

                    object hardpoint = weaponManager.hardpointSets[hardpointSet];
                    if (BlueprintRemovalReflection.IsNull(hardpoint))
                    {
                        MissingMemberLog.ErrorOnce(
                            $"BlueprintWeaponRemoval.HardpointNull|{removalKey}",
                            $"[Blueprints] Hardpoint set {hardpointSet} on '{rootName}' is null.");
                        continue;
                    }

                    int removedCount;
                    string failure;
                    if (!BlueprintRemovalReflection.TryRemoveWeaponOptions(hardpoint, rule.MatchesWeaponOption, out removedCount, out failure))
                    {
                        MissingMemberLog.ErrorOnce(
                            $"BlueprintWeaponRemoval.WeaponOptionsFailure|{removalKey}|{failure}",
                            $"[Blueprints] Could not remove '{rule.DisplayName}' from hardpoint set {hardpointSet} on '{rootName}'. Failure: {failure}.");
                        continue;
                    }

                    if (removedCount > 0)
                    {
                        totalRemoved += removedCount;
                        removedLogKeys.Add(removalKey);
                        string logKey = $"BlueprintWeaponRemoval.Removed|{removalKey}";
                        if (diagnosticLogKeys.Add(logKey))
                        {
                            Log.Info($"[Blueprints] Removed {removedCount} '{rule.DisplayName}' option(s) from {rootName} hardpoint set {hardpointSet}.");
                        }
                    }
                    else if (!removedLogKeys.Contains(removalKey))
                    {
                        MissingMemberLog.ErrorOnce(
                            $"BlueprintWeaponRemoval.WeaponMissing|{removalKey}",
                            $"[Blueprints] '{rule.DisplayName}' was not found on '{rootName}' hardpoint set {hardpointSet}. Verify ExactAircraftRootName, ExactBlueprintKeys, and hardpoint set.");
                    }
                }
            }

            return totalRemoved;
        }
    }



    // ========================================================================
    // IR missile buffs: flare count and flare rejection.
    // ========================================================================
    [HarmonyPatch(typeof(WeaponManager), "SpawnWeapons")]
    public static class StatsPatch
    {
        private static bool flareSweepApplied;
        private static bool seekerSweepApplied;

        public static void Postfix(WeaponManager __instance)
        {
            if (!RuntimeSettings.Captured)
            {
                Log.Error("IR missile buff patch ran before RuntimeSettings.Capture. This patch will be skipped.");
                return;
            }

            if (!RuntimeSettings.EnableIRMissilesBuff)
                return;

            try
            {
                ApplyFlareCountBuff();
                ApplyFlareRejectionBuff();
            }
            catch (Exception ex)
            {
                Log.Exception("IR missile buff", ex);
            }
        }

        private static void ApplyFlareCountBuff()
        {
            if (flareSweepApplied)
                return;

            int found = 0;
            int modified = 0;

            foreach (FlareEjector flareEjector in Resources.FindObjectsOfTypeAll<FlareEjector>())
            {
                if (flareEjector == null)
                    continue;

                found++;

                if (flareEjector.GetComponent<ModifiedStatsFlag>() != null)
                    continue;

                Traverse traverse = Traverse.Create(flareEjector);
                Traverse maxAmmoField = traverse.Field("maxAmmo");
                Traverse ammoField = traverse.Field("ammo");

                if (!maxAmmoField.FieldExists())
                {
                    MissingMemberLog.ErrorOnce(
                        "FlareEjector.maxAmmo",
                        $"[IR Buff] FlareEjector on '{ObjectNameUtility.GetHierarchyPath(flareEjector.gameObject)}' is missing field 'maxAmmo'.");
                    continue;
                }

                if (!ammoField.FieldExists())
                {
                    MissingMemberLog.ErrorOnce(
                        "FlareEjector.ammo",
                        $"[IR Buff] FlareEjector on '{ObjectNameUtility.GetHierarchyPath(flareEjector.gameObject)}' is missing field 'ammo'.");
                    continue;
                }

                int oldMax = maxAmmoField.GetValue<int>();
                int oldAmmo = ammoField.GetValue<int>();
                int newMax = Mathf.RoundToInt(oldMax * RuntimeSettings.FlareCountMultiplier);
                int newAmmo = Mathf.RoundToInt(oldAmmo * RuntimeSettings.FlareCountMultiplier);

                maxAmmoField.SetValue(newMax);
                ammoField.SetValue(newAmmo);

                flareEjector.gameObject.AddComponent<ModifiedStatsFlag>();
                modified++;
            }

            if (found > 0)
            {
                flareSweepApplied = true;
                if (modified > 0)
                    Log.Info($"[IR Buff] Adjusted flare ammo on {modified} FlareEjector(s). Multiplier={RuntimeSettings.FlareCountMultiplier}.");
                else
                    Log.Info("[IR Buff] FlareEjector(s) were already modified by this mod.");
            }
            else
            {
                MissingMemberLog.WarnOnce(
                    "IRBuff.FlareEjectorWaiting",
                    "[IR Buff] No FlareEjector instances found yet. Will retry when another WeaponManager awakens.");
            }
        }

        private static void ApplyFlareRejectionBuff()
        {
            if (seekerSweepApplied)
                return;

            int found = 0;
            int modified = 0;

            foreach (IRSeeker seeker in Resources.FindObjectsOfTypeAll<IRSeeker>())
            {
                if (seeker == null)
                    continue;

                found++;

                if (seeker.GetComponent<ModifiedStatsFlag>() != null)
                    continue;

                Traverse traverse = Traverse.Create(seeker);
                Traverse rejectionField = traverse.Field("flareRejection");

                if (!rejectionField.FieldExists())
                {
                    MissingMemberLog.ErrorOnce(
                        "IRSeeker.flareRejection",
                        $"[IR Buff] IRSeeker on '{ObjectNameUtility.GetHierarchyPath(seeker.gameObject)}' is missing field 'flareRejection'.");
                    continue;
                }

                rejectionField.SetValue(rejectionField.GetValue<float>() * RuntimeSettings.FlareRejectionMultiplier);
                seeker.gameObject.AddComponent<ModifiedStatsFlag>();
                modified++;
            }

            if (found > 0)
            {
                seekerSweepApplied = true;
                if (modified > 0)
                    Log.Info($"[IR Buff] Adjusted flare rejection on {modified} IRSeeker(s). Multiplier={RuntimeSettings.FlareRejectionMultiplier}.");
                else
                    Log.Info("[IR Buff] IRSeeker(s) were already modified by this mod.");
            }
            else
            {
                MissingMemberLog.WarnOnce(
                    "IRBuff.IRSeekerWaiting",
                    "[IR Buff] No IRSeeker instances found yet. Will retry when another WeaponManager awakens.");
            }
        }
    }



    // ========================================================================
    // MMR-S3 (AAM1) max turn rate patch.
    // Searches all loaded GameObjects named AAM1 and applies maxTurnRate only
    // to those that contain a Missile component. This avoids assuming a fixed
    // AAM1/Missile child path.
    // ========================================================================
    [HarmonyPatch(typeof(WeaponManager), "SpawnWeapons")]
    public static class MMRS3MaxTurnRatePatch
    {
        private const string AAM1Name = "AAM1";
        private static bool applied;

        public static void Postfix(WeaponManager __instance)
        {
            if (!RuntimeSettings.Captured)
            {
                Log.Error("MMR-S3 max turn rate patch ran before RuntimeSettings.Capture. This patch will be skipped.");
                return;
            }

            if (applied)
                return;

            try
            {
                applied = TryApply();
            }
            catch (Exception ex)
            {
                Log.Exception("MMR-S3 max turn rate patch", ex);
            }
        }

        private static bool TryApply()
        {
            HashSet<Missile> processed = new HashSet<Missile>();
            int aam1Objects = 0;
            int prefabMembers = 0;
            int prefabChanged = 0;
            int instanceMembers = 0;
            int instanceChanged = 0;

            foreach (GameObject gameObject in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (gameObject == null)
                    continue;

                if (ObjectNameUtility.RemoveCloneSuffix(gameObject.name) != AAM1Name)
                    continue;

                aam1Objects++;

                Missile[] missiles = gameObject.GetComponentsInChildren<Missile>(true);
                if (missiles == null || missiles.Length == 0)
                    continue;

                GameObject rootObject = gameObject;
                if (gameObject.transform != null && gameObject.transform.root != null)
                    rootObject = gameObject.transform.root.gameObject;

                bool isPrefabAsset = ObjectNameUtility.IsPrefabAsset(rootObject);

                foreach (Missile missile in missiles)
                {
                    if (missile == null || missile.gameObject == null)
                        continue;

                    if (!processed.Add(missile))
                        continue;

                    if (TrySetMaxTurnRate(missile, out bool wasChanged))
                    {
                        if (isPrefabAsset)
                        {
                            prefabMembers++;
                            if (wasChanged) prefabChanged++;
                        }
                        else
                        {
                            instanceMembers++;
                            if (wasChanged) instanceChanged++;
                        }
                    }
                }
            }

            foreach (Missile missile in Resources.FindObjectsOfTypeAll<Missile>())
            {
                if (missile == null || missile.gameObject == null)
                    continue;

                if (!ObjectNameUtility.IsUnderNamedObject(missile.gameObject, AAM1Name))
                    continue;

                if (!processed.Add(missile))
                    continue;

                GameObject rootObject = missile.gameObject;
                if (missile.transform != null && missile.transform.root != null)
                    rootObject = missile.transform.root.gameObject;

                bool isPrefabAsset = ObjectNameUtility.IsPrefabAsset(rootObject);

                if (TrySetMaxTurnRate(missile, out bool wasChanged))
                {
                    if (isPrefabAsset)
                    {
                        prefabMembers++;
                        if (wasChanged) prefabChanged++;
                    }
                    else
                    {
                        instanceMembers++;
                        if (wasChanged) instanceChanged++;
                    }
                }
            }

            if (aam1Objects == 0 && processed.Count == 0)
            {
                MissingMemberLog.WarnOnce(
                    "MMRS3MaxTurnRate.Waiting",
                    "[MMR-S3 Max Turn Rate] No AAM1 objects or Missile components under AAM1 found yet. Will retry when another WeaponManager awakens.");
                return false;
            }

            if (prefabMembers + instanceMembers == 0)
            {
                MissingMemberLog.ErrorOnce(
                    "MMRS3MaxTurnRate.NoUsableMissileMember",
                    $"[MMR-S3 Max Turn Rate] Found {aam1Objects} AAM1 object(s) and {processed.Count} Missile candidate(s), but none had a usable 'maxTurnRate' member.");
                return false;
            }

            if (prefabMembers == 0)
            {
                MissingMemberLog.WarnOnce(
                    "MMRS3MaxTurnRate.PrefabWaiting",
                    "[MMR-S3 Max Turn Rate] Found AAM1 Missile component(s), but no AAM1 prefab asset was modified yet. Will retry when another WeaponManager awakens.");
                return false;
            }

            if (prefabChanged + instanceChanged > 0)
                Log.Info($"[MMR-S3 Max Turn Rate] Set Missile.maxTurnRate to {RuntimeSettings.MMRS3MaxTurnRate} on {prefabChanged} prefab member(s) and {instanceChanged} active instance member(s).");
            else
                Log.Info("[MMR-S3 Max Turn Rate] AAM1 Missile component(s) were already at the configured max turn rate.");

            return true;
        }

        private static bool TrySetMaxTurnRate(Missile missile, out bool changed)
        {
            changed = false;

            Traverse traverse = Traverse.Create(missile);

            Traverse field = traverse.Field("maxTurnRate");
            if (field.FieldExists())
            {
                float current = field.GetValue<float>();
                if (current != RuntimeSettings.MMRS3MaxTurnRate)
                {
                    field.SetValue(RuntimeSettings.MMRS3MaxTurnRate);
                    changed = true;
                }
                return true;
            }

            Traverse property = traverse.Property("maxTurnRate");
            if (property.PropertyExists())
            {
                float current = property.GetValue<float>();
                if (current != RuntimeSettings.MMRS3MaxTurnRate)
                {
                    property.SetValue(RuntimeSettings.MMRS3MaxTurnRate);
                    changed = true;
                }
                return true;
            }

            MissingMemberLog.ErrorOnce(
                "Missile.maxTurnRate.AAM1",
                $"[MMR-S3 Max Turn Rate] Missile component on '{ObjectNameUtility.GetHierarchyPath(missile.gameObject)}' is missing field or property 'maxTurnRate'.");
            return false;
        }
    }



    // ========================================================================
    // SARH lock persistence.
    // Uses shared SarhMissileMatcher logic.
    // ========================================================================
    [HarmonyPatch(typeof(WeaponManager), "SpawnWeapons")]
    public static class SARHLockPersistencePatch
    {
        private static bool appliedR9;
        private static bool appliedRAM45;

        public static void Postfix(WeaponManager __instance)
        {
            if (!RuntimeSettings.Captured)
            {
                Log.Error("SARH lock persistence patch ran before RuntimeSettings.Capture. This patch will be skipped.");
                return;
            }

            if (!RuntimeSettings.EnableR9LockPersistenceBuff && !RuntimeSettings.EnableRAM45LockPersistenceBuff)
                return;

            try
            {
                if (RuntimeSettings.EnableR9LockPersistenceBuff && !appliedR9)
                {
                    appliedR9 = Apply(
                        "R9",
                        SarhMissileMatcher.R9RootName,
                        RuntimeSettings.R9LockPersistenceValue,
                        SarhMissileMatcher.IsR9);
                }

                if (RuntimeSettings.EnableRAM45LockPersistenceBuff && !appliedRAM45)
                {
                    appliedRAM45 = Apply(
                        "RAM-45",
                        SarhMissileMatcher.RAM45RootName,
                        RuntimeSettings.RAM45LockPersistenceValue,
                        SarhMissileMatcher.IsRAM45);
                }
            }
            catch (Exception ex)
            {
                Log.Exception("SARH lock persistence", ex);
            }
        }

        private static bool Apply(string label, string rootName, float value, Func<GameObject, bool> matcher)
        {
            int found = 0;
            int modified = 0;

            foreach (SARHSeeker seeker in Resources.FindObjectsOfTypeAll<SARHSeeker>())
            {
                if (seeker == null)
                    continue;

                if (!matcher(seeker.gameObject))
                    continue;

                found++;

                if (seeker.GetComponent<ModifiedStatsFlag>() != null)
                    continue;

                Traverse field = Traverse.Create(seeker).Field("lockPersistence");
                if (!field.FieldExists())
                {
                    MissingMemberLog.ErrorOnce(
                        $"SARHSeeker.lockPersistence.{label}",
                        $"[SARH Persistence] {label} seeker on '{ObjectNameUtility.GetHierarchyPath(seeker.gameObject)}' is missing field 'lockPersistence'.");
                    continue;
                }

                field.SetValue(value);
                seeker.gameObject.AddComponent<ModifiedStatsFlag>();
                modified++;
            }

            if (found > 0)
            {
                if (modified > 0)
                    Log.Info($"[SARH Persistence] Applied {label} lock persistence {value} to {modified} seeker(s).");
                else
                    Log.Info($"[SARH Persistence] {label} seeker(s) were already modified by this mod.");

                return true;
            }

            MissingMemberLog.WarnOnce(
                $"SARHPersistence.{label}.Waiting",
                $"[SARH Persistence] No {label} seekers under '{rootName}' found yet. Will retry when another WeaponManager awakens.");
            return false;
        }
    }



    // ========================================================================
    // SARH relock controller.
    // This component lives on the seeker and retries lost SARH tracking.
    // ========================================================================
    public class SARHRelockController : MonoBehaviour
    {
        private SARHSeeker seeker;
        private Traverse seekerTraverse;
        private Traverse targetTransformField;
        private Traverse targetUnitField;
        private Traverse timeWithoutTrackField;
        private Traverse lastTrackingCheckField;
        private Traverse jamAccumulationField;
        private Traverse jamToleranceField;
        private Traverse missileField;
        private Missile cachedMissile;
        private float relockDelay;
        private int maxAttempts;
        private int attemptsUsed;
        private float remainingDelay;
        private bool waitingForRelock;
        private bool initialized;
        private bool initializationFailed;

        public void Setup(SARHSeeker newSeeker, float delay, int attempts)
        {
            if (newSeeker == null)
            {
                Log.Error("[SARH Relock] Setup called with null SARHSeeker. Component will be destroyed.");
                Destroy(this);
                return;
            }

            seeker = newSeeker;
            relockDelay = Mathf.Max(0f, delay);
            maxAttempts = Mathf.Max(0, attempts);
            attemptsUsed = 0;
            waitingForRelock = false;
            remainingDelay = 0f;
            initialized = false;
            initializationFailed = false;
            cachedMissile = null;

            seekerTraverse = Traverse.Create(seeker);

            InitializeFieldReferences();
            ValidateFieldReferences();
        }

        private void InitializeFieldReferences()
        {
            targetTransformField = GetFieldOrProperty("targetTransform");
            targetUnitField = GetFieldOrProperty("targetUnit");
            timeWithoutTrackField = GetFieldOrProperty("timeWithoutTrack");
            lastTrackingCheckField = GetFieldOrProperty("lastTrackingCheck");
            jamAccumulationField = GetFieldOrProperty("jamAccumulation");
            jamToleranceField = GetFieldOrProperty("jamTolerance");
            missileField = GetFieldOrProperty("missile");
        }

        private Traverse GetFieldOrProperty(string name)
        {
            Traverse field = seekerTraverse.Field(name);
            if (field.FieldExists())
                return field;

            Traverse property = seekerTraverse.Property(name);
            if (property.PropertyExists())
                return property;

            return null;
        }

        private void ValidateFieldReferences()
        {
            if (targetTransformField == null || targetUnitField == null)
            {
                initializationFailed = true;
                Log.Error(
                    "[SARH Relock] Missing required field(s) on " +
                    $"'{ObjectNameUtility.GetHierarchyPath(seeker.gameObject)}'. " +
                    $"targetTransform={(targetTransformField != null)}, targetUnit={(targetUnitField != null)}. Relock disabled.");
                return;
            }

            if (timeWithoutTrackField == null)
            {
                MissingMemberLog.WarnOnce(
                    "SARHRelock.timeWithoutTrack",
                    "[SARH Relock] Optional field 'timeWithoutTrack' is missing. Relock may not reset tracking timers correctly.");
            }

            if (lastTrackingCheckField == null)
            {
                MissingMemberLog.WarnOnce(
                    "SARHRelock.lastTrackingCheck",
                    "[SARH Relock] Optional field 'lastTrackingCheck' is missing. Relock may not reset tracking checks correctly.");
            }

            if (jamAccumulationField == null)
            {
                MissingMemberLog.WarnOnce(
                    "SARHRelock.jamAccumulation",
                    "[SARH Relock] Optional field 'jamAccumulation' is missing. Relock will not decay jam accumulation.");
            }

            if (jamToleranceField == null)
            {
                MissingMemberLog.WarnOnce(
                    "SARHRelock.jamTolerance",
                    "[SARH Relock] Optional field 'jamTolerance' is missing. Relock will use a default tolerance for jam decay.");
            }

            if (missileField == null)
            {
                MissingMemberLog.WarnOnce(
                    "SARHRelock.missile",
                    "[SARH Relock] Optional field 'missile' is missing. Relock may not detect active missile lock correctly.");
            }
        }

        private void Update()
        {
            if (seeker == null)
            {
                Destroy(this);
                return;
            }

            if (initializationFailed)
                return;

            if (!initialized)
            {
                initialized = true;
                return;
            }

            try
            {
                UpdateRelock();
            }
            catch (Exception ex)
            {
                initializationFailed = true;
                Log.Exception("SARH Relock controller", ex);
            }
        }

        private void UpdateRelock()
        {
            Missile missile = GetMissile();
            if (missile != null && missile.seekerMode == Missile.SeekerMode.activeLock)
            {
                attemptsUsed = 0;
                waitingForRelock = false;
                remainingDelay = 0f;
                return;
            }

            Transform currentTargetTransform = targetTransformField?.GetValue<Transform>();
            Unit currentTargetUnit = targetUnitField?.GetValue<Unit>();

            if (currentTargetUnit == null || currentTargetTransform != null)
            {
                waitingForRelock = false;
                remainingDelay = 0f;
                return;
            }

            if (!waitingForRelock)
            {
                if (maxAttempts == 0 || attemptsUsed < maxAttempts)
                {
                    waitingForRelock = true;
                    remainingDelay = relockDelay;
                }
                return;
            }

            DecayJam(Time.deltaTime);

            remainingDelay -= Time.deltaTime;
            if (remainingDelay <= 0f)
                TryRelock();
        }

        private void TryRelock()
        {
            attemptsUsed++;
            DecayJam(Mathf.Max(Time.deltaTime, 1f));

            Unit targetUnit = targetUnitField?.GetValue<Unit>();
            if (targetUnit == null || targetUnit.disabled)
            {
                waitingForRelock = false;
                return;
            }

            Transform newTargetTransform = targetUnit.GetRandomPart();
            if (newTargetTransform == null)
            {
                if (maxAttempts == 0 || attemptsUsed < maxAttempts)
                    remainingDelay = relockDelay;
                else
                    waitingForRelock = false;

                MissingMemberLog.WarnOnce(
                    "SARHRelock.NoRandomPart",
                    "[SARH Relock] Target unit exists but GetRandomPart() returned null. Relock attempt skipped.");
                return;
            }

            targetTransformField?.SetValue(newTargetTransform);
            timeWithoutTrackField?.SetValue(0f);
            lastTrackingCheckField?.SetValue(0f);
            waitingForRelock = false;
        }

        private void DecayJam(float deltaTime)
        {
            if (jamAccumulationField == null)
                return;

            float jam = jamAccumulationField.GetValue<float>();
            if (jam <= 0f)
                return;

            float tolerance = jamToleranceField?.GetValue<float>() ?? 0.1f;
            jam -= Mathf.Max(jam, 0.2f) * Mathf.Max(tolerance, 0.1f) * deltaTime;
            jamAccumulationField.SetValue(Mathf.Clamp01(jam));
        }

        private Missile GetMissile()
        {
            if (cachedMissile != null)
                return cachedMissile;

            if (missileField != null)
                cachedMissile = missileField.GetValue<Missile>();

            return cachedMissile;
        }
    }



    // ========================================================================
    // SARH relock patch.
    // Uses shared SarhMissileMatcher logic.
    // ========================================================================
    [HarmonyPatch(typeof(SARHSeeker), "Initialize", new Type[] { typeof(Unit), typeof(GlobalPosition) })]
    public static class SARHRelockPatch
    {
        public static void Postfix(SARHSeeker __instance, Unit target)
        {
            if (!RuntimeSettings.Captured)
            {
                Log.Error("SARH relock patch ran before RuntimeSettings.Capture. This patch will be skipped.");
                return;
            }

            if (__instance == null || target == null)
                return;

            try
            {
                bool isR9 = SarhMissileMatcher.IsR9(__instance.gameObject);
                bool isRAM45 = SarhMissileMatcher.IsRAM45(__instance.gameObject);

                if (!isR9 && !isRAM45)
                    return;

                float delay;
                int attempts;

                if (isR9)
                {
                    if (!RuntimeSettings.EnableR9SARHRelock)
                        return;
                    delay = RuntimeSettings.R9SARHRelockDelay;
                    attempts = RuntimeSettings.R9SARHRelockAttempts;
                }
                else
                {
                    if (!RuntimeSettings.EnableRAM45SARHRelock)
                        return;
                    delay = RuntimeSettings.RAM45SARHRelockDelay;
                    attempts = RuntimeSettings.RAM45SARHRelockAttempts;
                }

                SARHRelockController controller = __instance.GetComponent<SARHRelockController>();
                if (controller == null)
                    controller = __instance.gameObject.AddComponent<SARHRelockController>();

                controller.Setup(__instance, delay, attempts);
            }
            catch (Exception ex)
            {
                Log.Exception("SARH relock patch", ex);
            }
        }
    }



    // ========================================================================
    // Cruise missile RCS patch.
    // ========================================================================
    [HarmonyPatch(typeof(WeaponManager), "SpawnWeapons")]
    public static class CruiseMissileRCSPatch
    {
        private sealed class RcsTarget
        {
            public string Name;
            public string Label;
            public Func<float> Value;
            public bool Applied;
        }

        private static readonly List<RcsTarget> Targets = new List<RcsTarget>
        {
            new RcsTarget { Name = "CruiseMissile1", Label = "ALM-C450", Value = () => RuntimeSettings.ALMC450RCS },
            new RcsTarget { Name = "AShM2", Label = "AGM-99", Value = () => RuntimeSettings.AGM99RCS },
            new RcsTarget { Name = "AShM1", Label = "AShM-300", Value = () => RuntimeSettings.AShM300RCS },
            new RcsTarget { Name = "CruiseMissile20kt", Label = "ALND-4 (20kt)", Value = () => RuntimeSettings.ALND420ktRCS }
        };

        private static bool allApplied;

        public static void Postfix(WeaponManager __instance)
        {
            if (!RuntimeSettings.Captured)
            {
                Log.Error("Cruise missile RCS patch ran before RuntimeSettings.Capture. This patch will be skipped.");
                return;
            }

            if (allApplied)
                return;

            try
            {
                foreach (RcsTarget target in Targets)
                {
                    if (target.Applied)
                        continue;

                    int modified = Apply(target.Name, target.Value());
                    if (modified > 0)
                    {
                        target.Applied = true;
                        Log.Info($"[Cruise RCS] Set {target.Label} ('{target.Name}') radarSize to {target.Value()}.");
                    }
                    else
                    {
                        MissingMemberLog.WarnOnce(
                            $"CruiseRCS.{target.Name}.Waiting",
                            $"[Cruise RCS] No MissileDefinition named '{target.Name}' found yet. Will retry when another WeaponManager awakens.");
                    }
                }

                allApplied = Targets.All(t => t.Applied);
            }
            catch (Exception ex)
            {
                Log.Exception("Cruise missile RCS patch", ex);
            }
        }

        private static int Apply(string cleanName, float value)
        {
            int modified = 0;

            foreach (MissileDefinition definition in Resources.FindObjectsOfTypeAll<MissileDefinition>())
            {
                if (definition == null)
                    continue;

                if (ObjectNameUtility.RemoveCloneSuffix(definition.name) != cleanName)
                    continue;

                Traverse traverse = Traverse.Create(definition);

                Traverse field = traverse.Field("radarSize");
                if (field.FieldExists())
                {
                    field.SetValue(value);
                    modified++;
                    continue;
                }

                Traverse property = traverse.Property("radarSize");
                if (property.PropertyExists())
                {
                    property.SetValue(value);
                    modified++;
                    continue;
                }

                MissingMemberLog.ErrorOnce(
                    $"MissileDefinition.radarSize.{cleanName}",
                    $"[Cruise RCS] MissileDefinition '{cleanName}' is missing field or property 'radarSize'.");
            }

            return modified;
        }
    }



    // ========================================================================
    // ARH seeker loft factor patch.
    // Applies configured loftAmount to Scythe (AAM2) and Scimitar (AAM4).
    // ========================================================================
    [HarmonyPatch(typeof(WeaponManager), "SpawnWeapons")]
    public static class ARHSeekerLoftPatch
    {
        private const string ScytheRootName = "AAM2";
        private const string ScimitarRootName = "AAM4";
        private static bool appliedScythe;
        private static bool appliedScimitar;

        public static void Postfix(WeaponManager __instance)
        {
            if (!RuntimeSettings.Captured)
            {
                Log.Error("ARH seeker loft patch ran before RuntimeSettings.Capture. This patch will be skipped.");
                return;
            }

            if (!RuntimeSettings.EnableScytheLoftFactor && !RuntimeSettings.EnableScimitarLoftFactor)
                return;

            try
            {
                if (RuntimeSettings.EnableScytheLoftFactor && !appliedScythe)
                {
                    appliedScythe = Apply(
                        "Scythe",
                        ScytheRootName,
                        RuntimeSettings.ScytheLoftFactorValue);
                }

                if (RuntimeSettings.EnableScimitarLoftFactor && !appliedScimitar)
                {
                    appliedScimitar = Apply(
                        "Scimitar",
                        ScimitarRootName,
                        RuntimeSettings.ScimitarLoftFactorValue);
                }
            }
            catch (Exception ex)
            {
                Log.Exception("ARH seeker loft patch", ex);
            }
        }

        private static bool Apply(string label, string rootName, float value)
        {
            int found = 0;
            int modified = 0;

            foreach (ARHSeeker seeker in Resources.FindObjectsOfTypeAll<ARHSeeker>())
            {
                if (seeker == null)
                    continue;

                if (!ObjectNameUtility.IsUnderNamedObject(seeker.gameObject, rootName))
                    continue;

                found++;

                Traverse traverse = Traverse.Create(seeker);

                Traverse field = traverse.Field("loftAmount");
                if (field.FieldExists())
                {
                    field.SetValue(value);
                    modified++;
                    continue;
                }

                Traverse property = traverse.Property("loftAmount");
                if (property.PropertyExists())
                {
                    property.SetValue(value);
                    modified++;
                    continue;
                }

                MissingMemberLog.ErrorOnce(
                    $"ARHSeeker.loftAmount.{label}",
                    $"[ARH Loft] {label} seeker on '{ObjectNameUtility.GetHierarchyPath(seeker.gameObject)}' is missing field or property 'loftAmount'.");
            }

            if (found == 0)
            {
                MissingMemberLog.WarnOnce(
                    $"ARHLoft.{label}.Waiting",
                    $"[ARH Loft] No ARHSeeker under '{rootName}' found yet. Will retry when another WeaponManager awakens.");
                return false;
            }

            if (modified > 0)
                Log.Info($"[ARH Loft] Set {label} ('{rootName}') loftAmount to {value} on {modified} seeker(s).");
            else
                Log.Info($"[ARH Loft] {label} ('{rootName}') seeker(s) were already processed by this mod.");

            return true;
        }
    }



    // ========================================================================
    // SAH-46 Chicane proxy gun patch.
    // Targets the gun object inside the Chicane cockpit turret:
    //   AttackHelo1/cockpit_R/cockpit_F/turretMount/turret/gun
    // ========================================================================
    [HarmonyPatch(typeof(WeaponManager), "SpawnWeapons")]
    public static class ProxyGunPatch
    {
        private const string AttackHeloRoot = "AttackHelo1";
        private const string GunChildPath = "cockpit_R/cockpit_F/turretMount/turret/gun";
        private static bool applied;

        public static void Postfix(WeaponManager __instance)
        {
            if (!RuntimeSettings.Captured)
            {
                Log.Error("Chicane proxy gun patch ran before RuntimeSettings.Capture. This patch will be skipped.");
                return;
            }

            if (!RuntimeSettings.EnableChicaneProxyGun || applied)
                return;

            try
            {
                applied = TryApplyProxyGun();
            }
            catch (Exception ex)
            {
                Log.Exception("Chicane proxy gun patch", ex);
            }
        }

        private static bool TryApplyProxyGun()
        {
            bool foundAnyAircraft = false;
            bool fixedAny = false;

            foreach (WeaponManager weaponManager in Resources.FindObjectsOfTypeAll<WeaponManager>())
            {
                if (weaponManager?.transform?.root == null)
                    continue;

                string rootName = ObjectNameUtility.RemoveCloneSuffix(weaponManager.transform.root.name);
                if (!rootName.Contains(AttackHeloRoot))
                    continue;

                foundAnyAircraft = true;

                Transform gunTransform = weaponManager.transform.root.Find(GunChildPath);
                if (gunTransform == null)
                {
                    MissingMemberLog.ErrorOnce(
                        $"ProxyGun.PathMissing|{rootName}",
                        $"[Proxy Gun] '{rootName}' does not contain gun path '{GunChildPath}'. Expected '{AttackHeloRoot}/{GunChildPath}'.");
                    continue;
                }

                if (TrySetProximityTimer(gunTransform.gameObject))
                    fixedAny = true;
            }

            if (!foundAnyAircraft)
            {
                MissingMemberLog.WarnOnce(
                    "ProxyGun.Waiting",
                    $"[Proxy Gun] No {AttackHeloRoot} WeaponManager found yet. Will retry when another WeaponManager awakens.");
                return false;
            }

            return fixedAny;
        }

        private static bool TrySetProximityTimer(GameObject gunObject)
        {
            if (gunObject == null)
                return false;

            Component gunComponent = GetGunComponent(gunObject);
            if (gunComponent == null)
            {
                MissingMemberLog.ErrorOnce(
                    $"ProxyGun.ComponentMissing|{ObjectNameUtility.GetHierarchyPath(gunObject)}",
                    $"[Proxy Gun] '{ObjectNameUtility.GetHierarchyPath(gunObject)}' has no component named 'Gun'.");
                return false;
            }

            Traverse gunTraverse = Traverse.Create(gunComponent);

            Traverse field = gunTraverse.Field("proximityTimer");
            if (field.FieldExists())
            {
                bool currentValue = field.GetValue<bool>();
                if (!currentValue)
                {
                    field.SetValue(true);
                    Log.Info($"[Proxy Gun] Set Gun.proximityTimer=true on '{ObjectNameUtility.GetHierarchyPath(gunObject)}'.");
                }
                return true;
            }

            Traverse property = gunTraverse.Property("proximityTimer");
            if (property.PropertyExists())
            {
                bool currentValue = property.GetValue<bool>();
                if (!currentValue)
                {
                    property.SetValue(true);
                    Log.Info($"[Proxy Gun] Set Gun.proximityTimer=true on '{ObjectNameUtility.GetHierarchyPath(gunObject)}'.");
                }
                return true;
            }

            MissingMemberLog.ErrorOnce(
                $"ProxyGun.proximityTimer|{ObjectNameUtility.GetHierarchyPath(gunObject)}",
                $"[Proxy Gun] The Gun component on '{ObjectNameUtility.GetHierarchyPath(gunObject)}' is missing field or property 'proximityTimer'.");
            return false;
        }

        private static Component GetGunComponent(GameObject gunObject)
        {
            if (gunObject == null)
                return null;

            foreach (Component component in gunObject.GetComponents<Component>())
            {
                if (component == null)
                    continue;

                if (component.GetType().Name == "Gun")
                    return component;
            }

            return null;
        }
    }



    // ========================================================================
    // SAH-46 Chicane bay pylon symmetry system.
    // This system uses event-driven scene sweeps and SpawnWeapons notifications.
    // Path resolution is case-insensitive and tries known pylon name variants.
    // ========================================================================
    internal static class ChicaneBayPylonSymmetrySystem
    {
        private const string AttackHeloRoot = "AttackHelo1";
        private static readonly Vector3 DesiredPylonLocalPosition = new Vector3(0f, -0.35f, -0.1f);
        private static readonly string[] PylonPathCandidates =
        {
            "weaponBay_R/weaponDoorHinge_Ra/weaponDoorHinge_Rb/pylon_bay_R",
            "weaponbay_R/weaponDoorHinge_Ra/weaponDoorHinge_Rb/pylon_bay_R",
            "weaponBay_R/weaponDoorHinge_Ra/weaponDoorHinge_Rb/pyon_bay_R",
            "weaponbay_R/weaponDoorHinge_Ra/weaponDoorHinge_Rb/pyon_bay_R"
        };

        private static bool initialized;
        private static bool sweepSucceeded;
        private static readonly HashSet<string> diagnosticLogKeys = new HashSet<string>();

        public static void Initialize(Plugin owner)
        {
            if (initialized)
                return;
            if (owner == null)
            {
                Log.Error("ChicaneBayPylonSymmetrySystem.Initialize was called with a null plugin instance.");
                return;
            }
            if (!RuntimeSettings.Captured)
            {
                Log.Error("ChicaneBayPylonSymmetrySystem.Initialize was called before RuntimeSettings.Capture. Chicane bay pylon symmetry will not run.");
                return;
            }

            initialized = true;
            if (!RuntimeSettings.EnableChicaneBayPylonSymmetryFix)
            {
                Log.Info("Chicane bay pylon symmetry system is disabled.");
                return;
            }

            SceneManager.sceneLoaded += OnSceneLoaded;
            Scene activeScene = SceneManager.GetActiveScene();
            if (activeScene.IsValid())
                ApplyAll($"Active scene '{activeScene.name}' at initialization");

            Log.Info("Chicane bay pylon symmetry system initialized using startup-cached config values. Application is event-driven and does not poll.");
        }

        public static void Shutdown()
        {
            if (!initialized)
                return;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            initialized = false;
        }

        public static void NotifyWeaponManagerSpawned(WeaponManager weaponManager)
        {
            if (!initialized)
            {
                MissingMemberLog.ErrorOnce(
                    "ChicaneBayPylon.SystemNotInitialized",
                    "[Chicane Symmetry] NotifyWeaponManagerSpawned was called before ChicaneBayPylonSymmetrySystem.Initialize.");
                return;
            }
            if (!RuntimeSettings.EnableChicaneBayPylonSymmetryFix)
                return;

            try
            {
                bool aircraftMatched;
                bool pylonFound;
                bool pylonAdjusted;
                if (TryApply(weaponManager, out aircraftMatched, out pylonFound, out pylonAdjusted) && pylonFound)
                    sweepSucceeded = true;
            }
            catch (Exception ex)
            {
                Log.Exception("Chicane bay pylon symmetry spawn notification", ex);
            }
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!initialized || !scene.IsValid() || !RuntimeSettings.EnableChicaneBayPylonSymmetryFix)
                return;
            if (scene.name == "MainMenu" || !sweepSucceeded)
                ApplyAll($"Scene '{scene.name}' loaded");
        }

        private static void ApplyAll(string reason)
        {
            if (!initialized || !RuntimeSettings.EnableChicaneBayPylonSymmetryFix)
                return;

            try
            {
                int weaponManagersScanned = 0;
                int matchedAircraft = 0;
                int pylonsFound = 0;
                int pylonsAdjusted = 0;

                foreach (WeaponManager weaponManager in Resources.FindObjectsOfTypeAll<WeaponManager>())
                {
                    if (weaponManager == null || weaponManager.transform == null)
                        continue;
                    weaponManagersScanned++;

                    bool aircraftMatched;
                    bool pylonFound;
                    bool pylonAdjusted;
                    if (!TryApply(weaponManager, out aircraftMatched, out pylonFound, out pylonAdjusted))
                        continue;
                    if (aircraftMatched)
                        matchedAircraft++;
                    if (pylonFound)
                        pylonsFound++;
                    if (pylonAdjusted)
                        pylonsAdjusted++;
                }

                if (weaponManagersScanned == 0)
                {
                    MissingMemberLog.WarnOnce(
                        "ChicaneBayPylon.NoWeaponManagersWaiting",
                        $"[Chicane Symmetry] No WeaponManager instances were found during pylon symmetry sweep. Reason='{reason}'. Will retry on a later scene load.");
                    return;
                }
                if (matchedAircraft == 0)
                {
                    MissingMemberLog.WarnOnce(
                        "ChicaneBayPylon.AttackHeloWaiting",
                        $"[Chicane Symmetry] No {AttackHeloRoot} WeaponManager was found during pylon symmetry sweep. Reason='{reason}'. Will retry on a later scene load.");
                    return;
                }
                if (pylonsFound > 0)
                {
                    sweepSucceeded = true;
                    string logKey = $"ChicaneBayPylon.Sweep|{reason}";
                    if (diagnosticLogKeys.Add(logKey))
                    {
                        Log.Info(
                            $"[Chicane Symmetry] Completed pylon symmetry sweep. " +
                            $"WeaponManagers scanned={weaponManagersScanned}, matched aircraft={matchedAircraft}, " +
                            $"pylon(s) found={pylonsFound}, adjusted={pylonsAdjusted}. Reason='{reason}'.");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Exception($"Chicane bay pylon symmetry ({reason})", ex);
            }
        }

        private static bool TryApply(WeaponManager weaponManager, out bool aircraftMatched, out bool pylonFound, out bool pylonAdjusted)
        {
            aircraftMatched = false;
            pylonFound = false;
            pylonAdjusted = false;
            if (weaponManager == null || weaponManager.transform == null)
                return false;

            Transform root = weaponManager.transform.root != null ? weaponManager.transform.root : weaponManager.transform;
            string rootName = ObjectNameUtility.GetCleanRootName(root.gameObject);
            if (string.IsNullOrEmpty(rootName) || rootName.IndexOf(AttackHeloRoot, StringComparison.OrdinalIgnoreCase) < 0)
                return false;

            aircraftMatched = true;
            Transform pylon = FindPylon(root);
            if (pylon == null)
            {
                MissingMemberLog.ErrorOnce(
                    $"ChicaneBayPylon.PathMissing|{rootName}",
                    $"[Chicane Symmetry] '{rootName}' is missing pylon path. Tried: {string.Join(" | ", PylonPathCandidates)}.");
                return true;
            }

            pylonFound = true;
            if (pylon.localPosition != DesiredPylonLocalPosition)
            {
                pylon.localPosition = DesiredPylonLocalPosition;
                pylonAdjusted = true;
                string logKey = $"ChicaneBayPylon.Centered|{rootName}";
                if (diagnosticLogKeys.Add(logKey))
                    Log.Info($"[Chicane Symmetry] Centered '{ObjectNameUtility.GetHierarchyPath(pylon.gameObject)}' on '{rootName}'.");
            }
            return true;
        }

        private static Transform FindPylon(Transform root)
        {
            foreach (string candidatePath in PylonPathCandidates)
            {
                Transform result = FindChildPathCaseInsensitive(root, candidatePath);
                if (result != null)
                    return result;
            }
            return null;
        }

        private static Transform FindChildPathCaseInsensitive(Transform root, string path)
        {
            if (root == null)
                return null;
            Transform current = root;
            foreach (string segment in path.Split('/'))
            {
                if (current == null)
                    return null;
                current = FindChildByNameCaseInsensitive(current, segment);
            }
            return current;
        }

        private static Transform FindChildByNameCaseInsensitive(Transform parent, string name)
        {
            if (parent == null || string.IsNullOrEmpty(name))
                return null;
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform child = parent.GetChild(i);
                if (child == null)
                    continue;
                string cleanName = ObjectNameUtility.RemoveCloneSuffix(child.name);
                if (string.Equals(cleanName, name, StringComparison.OrdinalIgnoreCase))
                    return child;
            }
            return null;
        }
    }



    // ========================================================================
    // SAH-46 Chicane bay pylon symmetry Harmony hook.
    // ========================================================================
    [HarmonyPatch(typeof(WeaponManager), "SpawnWeapons")]
    public static class ChicaneBayPylonSymmetryFixPatch
    {
        public static void Postfix(WeaponManager __instance)
        {
            if (!RuntimeSettings.Captured)
            {
                Log.Error("Chicane bay pylon symmetry patch ran before RuntimeSettings.Capture. This patch will be skipped.");
                return;
            }
            if (!RuntimeSettings.EnableChicaneBayPylonSymmetryFix)
                return;

            try
            {
                ChicaneBayPylonSymmetrySystem.NotifyWeaponManagerSpawned(__instance);
            }
            catch (Exception ex)
            {
                Log.Exception("Chicane bay pylon symmetry patch", ex);
            }
        }
    }



    // ========================================================================
    // EW-25 Medusa laser power draw patch.
    // ========================================================================
    [HarmonyPatch(typeof(WeaponManager), "SpawnWeapons")]
    public static class MedusaLaserPatch
    {
        private static bool applied;

        public static void Postfix(WeaponManager __instance)
        {
            if (!RuntimeSettings.Captured)
            {
                Log.Error("Medusa laser patch ran before RuntimeSettings.Capture. This patch will be skipped.");
                return;
            }

            if (!RuntimeSettings.EnableMedusaLaserBuff || applied)
                return;

            try
            {
                applied = TryApply();
            }
            catch (Exception ex)
            {
                Log.Exception("Medusa laser patch", ex);
            }
        }

        private static bool TryApply()
        {
            int laserObjects = 0;
            int powerFields = 0;
            int modified = 0;

            foreach (GameObject gameObject in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (gameObject == null || !gameObject.name.Contains("Laser_EW1"))
                    continue;

                laserObjects++;

                foreach (MonoBehaviour component in gameObject.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (component == null)
                        continue;

                    Traverse powerField = Traverse.Create(component).Field("power");
                    if (!powerField.FieldExists())
                        continue;

                    powerFields++;

                    if (component.gameObject.GetComponent<ModifiedStatsFlag>() != null)
                        continue;

                    powerField.SetValue(RuntimeSettings.MedusaLaserPowerDraw);
                    component.gameObject.AddComponent<ModifiedStatsFlag>();
                    modified++;
                }
            }

            if (laserObjects == 0)
            {
                MissingMemberLog.WarnOnce(
                    "MedusaLaser.Waiting",
                    "[Medusa Laser] No Laser_EW1 objects found yet. Will retry when another WeaponManager awakens.");
                return false;
            }

            if (powerFields == 0)
            {
                MissingMemberLog.ErrorOnce(
                    "MedusaLaser.PowerMissing",
                    "[Medusa Laser] Found Laser_EW1 object(s), but no MonoBehaviour with a 'power' field.");
                return false;
            }

            if (modified > 0)
                Log.Info($"[Medusa Laser] Set laser power draw to {RuntimeSettings.MedusaLaserPowerDraw} on {modified} component(s).");

            return true;
        }
    }



    // ========================================================================
    // AeroSentry SPAAG single magazine patch.
    // Hooks Gun.Awake to ensure magazineCapacity and magazines are set BEFORE
    // Gun.Awake calculates maxMagazines, bulletsLoaded, and ammo.
    // This guarantees correct initialization order for all SPAAG1 variants.
    // ========================================================================
    [HarmonyPatch(typeof(Gun), "Awake")]
    public static class SpaagSingleMagazinePatch
    {
        private const string SpaagNameContains = "SPAAG1";
        private static readonly HashSet<string> appliedLogKeys = new HashSet<string>();

        public static void Prefix(Gun __instance)
        {
            if (!RuntimeSettings.Captured)
            {
                Log.Error("AeroSentry SPAAG single magazine patch ran before RuntimeSettings.Capture. This patch will be skipped.");
                return;
            }

            if (!RuntimeSettings.EnableSpaagSingleMagazine)
                return;

            try
            {
                if (__instance == null || __instance.gameObject == null)
                    return;

                string rootName = ObjectNameUtility.GetCleanRootName(__instance.gameObject);

                if (rootName.IndexOf(SpaagNameContains, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    ApplyToGun(__instance, rootName);
                }
            }
            catch (Exception ex)
            {
                Log.Exception("AeroSentry SPAAG single magazine patch", ex);
            }
        }

        private static void ApplyToGun(Gun gun, string rootName)
        {
            Traverse traverse = Traverse.Create(gun);

            Traverse capField = traverse.Field("magazineCapacity");
            Traverse magsField = traverse.Field("magazines");

            if (!capField.FieldExists())
            {
                MissingMemberLog.ErrorOnce(
                    $"SpaagSingleMagazine.magazineCapacity|{rootName}",
                    $"[SPAAG Single Magazine] Gun on '{rootName}' is missing field 'magazineCapacity'.");
                return;
            }

            if (!magsField.FieldExists())
            {
                MissingMemberLog.ErrorOnce(
                    $"SpaagSingleMagazine.magazines|{rootName}",
                    $"[SPAAG Single Magazine] Gun on '{rootName}' is missing field 'magazines'.");
                return;
            }

            int currentCap = capField.GetValue<int>();
            int currentMags = magsField.GetValue<int>();
            bool changed = false;

            if (currentCap != RuntimeSettings.SpaagMagazineCapacity)
            {
                capField.SetValue(RuntimeSettings.SpaagMagazineCapacity);
                changed = true;
            }

            if (currentMags != RuntimeSettings.SpaagMagazines)
            {
                magsField.SetValue(RuntimeSettings.SpaagMagazines);
                changed = true;
            }

            if (changed)
            {
                string logKey = $"SpaagSingleMagazine.Applied|{rootName}|{RuntimeSettings.SpaagMagazineCapacity}|{RuntimeSettings.SpaagMagazines}";
                if (appliedLogKeys.Add(logKey))
                {
                    Log.Info(
                        $"[SPAAG Single Magazine] Set Gun.magazineCapacity={RuntimeSettings.SpaagMagazineCapacity} " +
                        $"and Gun.magazines={RuntimeSettings.SpaagMagazines} on '{rootName}' before Awake initialization.");
                }
            }
        }
    }



    // ========================================================================
    // Shard Class Corvette single magazine cannon patch.
    // Hooks Gun.Awake to ensure magazineCapacity and magazines are set BEFORE
    // Gun.Awake calculates maxMagazines, bulletsLoaded, and ammo.
    // Targets: Corvette1/bow1/turret_F/cannon_F
    // ========================================================================
    [HarmonyPatch(typeof(Gun), "Awake")]
    public static class CorvetteSingleMagazinePatch
    {
        private const string CorvetteRootContains = "Corvette1";
        private const string CannonPath = "bow1/turret_F/cannon_F";
        private const string CannonObjectName = "cannon_F";
        private static readonly HashSet<string> appliedLogKeys = new HashSet<string>();

        public static void Prefix(Gun __instance)
        {
            if (!RuntimeSettings.Captured)
            {
                Log.Error("Shard Class Corvette single magazine cannon patch ran before RuntimeSettings.Capture. This patch will be skipped.");
                return;
            }

            if (!RuntimeSettings.EnableCorvetteSingleMagazine)
                return;

            try
            {
                if (__instance == null || __instance.gameObject == null || __instance.transform == null)
                    return;

                string rootName = ObjectNameUtility.GetCleanRootName(__instance.gameObject);
                if (string.IsNullOrEmpty(rootName))
                    return;

                if (rootName.IndexOf(CorvetteRootContains, StringComparison.OrdinalIgnoreCase) < 0)
                    return;

                if (!IsTargetCannon(__instance, rootName))
                    return;

                ApplyToGun(__instance, rootName);
            }
            catch (Exception ex)
            {
                Log.Exception("Shard Class Corvette single magazine cannon patch", ex);
            }
        }

        private static bool IsTargetCannon(Gun gun, string rootName)
        {
            string cleanGunObjectName = ObjectNameUtility.RemoveCloneSuffix(gun.gameObject.name);
            if (cleanGunObjectName != CannonObjectName)
                return false;

            Transform root = gun.transform.root;
            if (root == null)
                return false;

            Transform cannon = root.Find(CannonPath);
            if (cannon == null)
            {
                MissingMemberLog.ErrorOnce(
                    $"CorvetteSingleMagazine.PathMissing|{rootName}",
                    $"[Corvette Single Magazine] '{rootName}' is missing cannon path '{CannonPath}'. Expected '{rootName}/{CannonPath}'.");
                return false;
            }

            if (cannon != gun.transform && !gun.transform.IsChildOf(cannon))
            {
                MissingMemberLog.ErrorOnce(
                    $"CorvetteSingleMagazine.UnexpectedCannonTransform|{rootName}",
                    $"[Corvette Single Magazine] Found '{CannonPath}' on '{rootName}', but the Gun component is not on that transform or one of its children.");
                return false;
            }

            return true;
        }

        private static void ApplyToGun(Gun gun, string rootName)
        {
            Traverse traverse = Traverse.Create(gun);

            Traverse capField = traverse.Field("magazineCapacity");
            Traverse magsField = traverse.Field("magazines");

            if (!capField.FieldExists())
            {
                MissingMemberLog.ErrorOnce(
                    $"CorvetteSingleMagazine.magazineCapacity|{rootName}",
                    $"[Corvette Single Magazine] Gun on '{rootName}/{CannonPath}' is missing field 'magazineCapacity'.");
                return;
            }

            if (!magsField.FieldExists())
            {
                MissingMemberLog.ErrorOnce(
                    $"CorvetteSingleMagazine.magazines|{rootName}",
                    $"[Corvette Single Magazine] Gun on '{rootName}/{CannonPath}' is missing field 'magazines'.");
                return;
            }

            int currentCap = capField.GetValue<int>();
            int currentMags = magsField.GetValue<int>();
            bool changed = false;

            if (currentCap != RuntimeSettings.CorvetteCannonMagazineCapacity)
            {
                capField.SetValue(RuntimeSettings.CorvetteCannonMagazineCapacity);
                changed = true;
            }

            if (currentMags != RuntimeSettings.CorvetteCannonMagazineCount)
            {
                magsField.SetValue(RuntimeSettings.CorvetteCannonMagazineCount);
                changed = true;
            }

            if (changed)
            {
                string logKey = $"CorvetteSingleMagazine.Applied|{rootName}|{RuntimeSettings.CorvetteCannonMagazineCapacity}|{RuntimeSettings.CorvetteCannonMagazineCount}";
                if (appliedLogKeys.Add(logKey))
                {
                    Log.Info(
                        $"[Corvette Single Magazine] Set Gun.magazineCapacity={RuntimeSettings.CorvetteCannonMagazineCapacity} " +
                        $"and Gun.magazines={RuntimeSettings.CorvetteCannonMagazineCount} on '{rootName}/{CannonPath}' before Awake initialization.");
                }
            }
        }
    }



    // ========================================================================
    // Dynamo Class Destroyer railgun self-destruct VFX patch.
    // Hooks Gun.Awake to ensure selfDestructEffect is set BEFORE
    // Gun.Awake initializes the weapon.
    // Targets: Destroyer1/Hull_CF/Hull_CFF/turret_F/cannon_F
    // ========================================================================
    [HarmonyPatch(typeof(Gun), "Awake")]
    public static class DynamoRailgunSelfDestructPatch
    {
        private const string DestroyerRootContains = "Destroyer1";
        private const string CannonPath = "Hull_CF/Hull_CFF/turret_F/cannon_F";
        private const string CannonObjectName = "cannon_F";
        private const string PrefabName = "explosion_10kg";
        private static readonly HashSet<string> appliedLogKeys = new HashSet<string>();
        private static GameObject cachedPrefab;

        public static void Prefix(Gun __instance)
        {
            if (!RuntimeSettings.Captured)
            {
                Log.Error("Dynamo railgun self-destruct patch ran before RuntimeSettings.Capture. This patch will be skipped.");
                return;
            }

            if (!RuntimeSettings.EnableDynamoRailgunSelfDestructVFX)
                return;

            try
            {
                if (__instance == null || __instance.gameObject == null || __instance.transform == null)
                    return;

                string rootName = ObjectNameUtility.GetCleanRootName(__instance.gameObject);
                if (string.IsNullOrEmpty(rootName))
                    return;

                if (rootName.IndexOf(DestroyerRootContains, StringComparison.OrdinalIgnoreCase) < 0)
                    return;

                if (!IsTargetCannon(__instance, rootName))
                    return;

                ApplyToGun(__instance, rootName);
            }
            catch (Exception ex)
            {
                Log.Exception("Dynamo railgun self-destruct patch", ex);
            }
        }

        private static bool IsTargetCannon(Gun gun, string rootName)
        {
            string cleanGunObjectName = ObjectNameUtility.RemoveCloneSuffix(gun.gameObject.name);
            if (cleanGunObjectName != CannonObjectName)
                return false;

            Transform root = gun.transform.root;
            if (root == null)
                return false;

            Transform cannon = root.Find(CannonPath);
            if (cannon == null)
            {
                MissingMemberLog.ErrorOnce(
                    $"DynamoRailgun.PathMissing|{rootName}",
                    $"[Dynamo Railgun] '{rootName}' is missing cannon path '{CannonPath}'. Expected '{rootName}/{CannonPath}'.");
                return false;
            }

            if (cannon != gun.transform && !gun.transform.IsChildOf(cannon))
            {
                MissingMemberLog.ErrorOnce(
                    $"DynamoRailgun.UnexpectedCannonTransform|{rootName}",
                    $"[Dynamo Railgun] Found '{CannonPath}' on '{rootName}', but the Gun component is not on that transform or one of its children.");
                return false;
            }

            return true;
        }

        private static void ApplyToGun(Gun gun, string rootName)
        {
            if (cachedPrefab == null)
            {
                foreach (GameObject go in Resources.FindObjectsOfTypeAll<GameObject>())
                {
                    if (go != null && go.name == PrefabName)
                    {
                        cachedPrefab = go;
                        break;
                    }
                }
            }

            if (cachedPrefab == null)
            {
                MissingMemberLog.ErrorOnce(
                    "DynamoRailgun.PrefabMissing",
                    $"[Dynamo Railgun] Could not find prefab '{PrefabName}' in loaded resources.");
                return;
            }

            Traverse traverse = Traverse.Create(gun);
            bool found = false;
            bool alreadySet = false;

            Traverse effectField = traverse.Field("selfDestructEffect");
            if (effectField.FieldExists())
            {
                found = true;
                object currentVal = effectField.GetValue();
                if (currentVal is GameObject currentGo && currentGo == cachedPrefab)
                    alreadySet = true;
                else
                    effectField.SetValue(cachedPrefab);
            }
            else
            {
                Traverse effectProp = traverse.Property("selfDestructEffect");
                if (effectProp.PropertyExists())
                {
                    found = true;
                    object currentVal = effectProp.GetValue();
                    if (currentVal is GameObject currentGo && currentGo == cachedPrefab)
                        alreadySet = true;
                    else
                        effectProp.SetValue(cachedPrefab);
                }
            }

            if (!found)
            {
                MissingMemberLog.ErrorOnce(
                    $"DynamoRailgun.selfDestructEffect|{rootName}",
                    $"[Dynamo Railgun] Gun on '{rootName}/{CannonPath}' is missing field or property 'selfDestructEffect'.");
                return;
            }

            if (alreadySet)
                return;

            string logKey = $"DynamoRailgun.Applied|{rootName}";
            if (appliedLogKeys.Add(logKey))
            {
                Log.Info(
                    $"[Dynamo Railgun] Set Gun.selfDestructEffect='{PrefabName}' on '{rootName}/{CannonPath}' before Awake initialization.");
            }
        }
    }



    // ========================================================================
    // Arresting cable definitions and runtime injection system.
    // This system hooks Ship.Awake to inject cables as carriers spawn.
    // ========================================================================
    internal sealed class ArrestingCableInjection
    {
        public int CableNumber;
        public string ParentPath;
        public Vector3 LocalPosition;
    }

    internal sealed class ArrestingCableCarrierDefinition
    {
        public string Label;
        public string RootName;
        public string FallbackRootContains;
        public string ValidationChildPath;
        public float? ArrestorGearDamping;
        public float? ArrestorGearSpring;
        public ArrestingCableInjection[] Injections;
    }

    internal static class ArrestingCableSystem
    {
        private const string SourceRootName = "FleetCarrier1";
        private const string SourceCablePath = "hull_R/hull_R2/arrestorCable1";
        private const string CableBaseName = "arrestorCable";
        private const string ArrestorGearComponentName = "ArrestorGear";
        private const string WireNumberMemberName = "wireNumber";
        private const string DampingMemberName = "damping";
        private const string SpringMemberName = "spring";

        private static readonly BindingFlags WireNumberBindingFlags =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        private static Plugin plugin;
        private static bool initialized;
        private static GameObject cachedSourceCable;
        private static List<ArrestingCableCarrierDefinition> definitions = new List<ArrestingCableCarrierDefinition>();
        private static HashSet<GameObject> pendingTargetRoots = new HashSet<GameObject>();
        private static readonly HashSet<string> diagnosticLogKeys = new HashSet<string>();

        public static void Initialize(Plugin owner)
        {
            if (initialized)
                return;

            if (owner == null)
            {
                Log.Error("ArrestingCableSystem.Initialize was called with a null plugin instance.");
                return;
            }

            if (!RuntimeSettings.Captured)
            {
                Log.Error("ArrestingCableSystem.Initialize was called before RuntimeSettings.Capture. Arresting cables will not run.");
                return;
            }

            plugin = owner;
            definitions.Clear();

            if (RuntimeSettings.EnableAnnexArrestingCables)
            {
                definitions.Add(new ArrestingCableCarrierDefinition
                {
                    Label = "Annex Arresting Cables",
                    RootName = "AssaultCarrier1",
                    FallbackRootContains = "AssaultCarrier1",
                    ValidationChildPath = "hull_RL",
                    Injections = new ArrestingCableInjection[]
                    {
                        new ArrestingCableInjection
                        {
                            CableNumber = 1,
                            ParentPath = "hull_RL/hull_RR/hull_wellDeck/hull_RRL",
                            LocalPosition = new Vector3(7.61999989f, 18.4200001f, -17.5900002f)
                        },
                        new ArrestingCableInjection
                        {
                            CableNumber = 2,
                            ParentPath = "hull_RL/hull_RR/hull_wellDeck/hull_RRL/deck_RR",
                            LocalPosition = new Vector3(2.8526125f, 1.57768631f, -17.1718292f)
                        },
                        new ArrestingCableInjection
                        {
                            CableNumber = 3,
                            ParentPath = "hull_RL/hull_RR/hull_wellDeck/hull_RRL/deck_RR",
                            LocalPosition = new Vector3(2.8526125f, 1.57768631f, 2.82816696f)
                        }
                    }
                });
            }

            if (RuntimeSettings.EnableCursorLFDArrestingCables)
            {
                definitions.Add(new ArrestingCableCarrierDefinition
                {
                    Label = "Cursor LFD Arresting Cables",
                    RootName = "SmallCarrier1",
                    FallbackRootContains = "SmallCarrier1",
                    ValidationChildPath = "smallCarrier1_hull_R",
                    ArrestorGearDamping = 7000f,
                    ArrestorGearSpring = 8000f,
                    Injections = new ArrestingCableInjection[]
                    {
                        new ArrestingCableInjection
                        {
                            CableNumber = 1,
                            ParentPath = "smallCarrier1_hull_R",
                            LocalPosition = new Vector3(4.53056812f, 4.13252401f, -5.5f)
                        },
                        new ArrestingCableInjection
                        {
                            CableNumber = 2,
                            ParentPath = "smallCarrier1_hull_R",
                            LocalPosition = new Vector3(4.53056812f, 4.13252401f, 9.64999962f)
                        },
                        new ArrestingCableInjection
                        {
                            CableNumber = 3,
                            ParentPath = "",
                            LocalPosition = new Vector3(4.53056812f, 4.13252401f, 4.73827934f)
                        }
                    }
                });
            }

            initialized = true;

            if (definitions.Count == 0)
            {
                Log.Info("Arresting cable system has no enabled features.");
                return;
            }

            Log.Info($"Arresting cable system initialized. Enabled feature(s): {definitions.Count}.");
        }

        public static void NotifyShipSpawned(Ship ship)
        {
            if (!initialized || ship == null || ship.transform == null)
                return;

            GameObject root = ship.transform.root?.gameObject ?? ship.gameObject;
            if (root == null)
                return;

            string cleanName = ObjectNameUtility.RemoveCloneSuffix(root.name);

            if (cleanName.Equals(SourceRootName, StringComparison.OrdinalIgnoreCase))
            {
                TryCacheSourceCable(ship.transform);
            }

            // Clean destroyed pending roots so old destroyed carriers do not keep
            // the pending set alive indefinitely.
            if (pendingTargetRoots.Count > 0)
                pendingTargetRoots.RemoveWhere(pendingRoot => pendingRoot == null);

            bool attemptedResourceSearch = false;

            foreach (ArrestingCableCarrierDefinition definition in definitions)
            {
                if (!IsTargetRoot(root, definition))
                    continue;

                // If FleetCarrier1 has not spawned, try to use a loaded FleetCarrier1
                // prefab asset or inactive scene object as the cable source.
                if (cachedSourceCable == null && !attemptedResourceSearch)
                {
                    attemptedResourceSearch = true;
                    TryCacheSourceCableFromLoadedResources();
                }

                if (cachedSourceCable != null)
                {
                    ApplyToRoot(definition, root, cachedSourceCable);
                }
                else
                {
                    pendingTargetRoots.Add(root);
                }
            }
        }

        private static bool TryCacheSourceCableFromLoadedResources()
        {
            if (cachedSourceCable != null)
                return true;

            int fleetCarrierObjects = 0;
            GameObject bestSourceRoot = null;
            Transform bestSourceCable = null;

            // Resources.FindObjectsOfTypeAll includes inactive scene objects and loaded
            // prefab assets. This is intentionally event-driven; it is only called when
            // a target carrier needs the source cable.
            foreach (GameObject candidate in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (candidate == null || candidate.transform == null)
                    continue;

                string candidateName = ObjectNameUtility.RemoveCloneSuffix(candidate.name);
                if (!string.Equals(candidateName, SourceRootName, StringComparison.OrdinalIgnoreCase))
                    continue;

                fleetCarrierObjects++;

                Transform cable = FindChildPath(candidate.transform, SourceCablePath);
                if (cable == null || cable.gameObject == null)
                    continue;

                // Prefer a prefab asset so the feature works even when no live
                // FleetCarrier1 exists in the active mission scene.
                if (ObjectNameUtility.IsPrefabAsset(candidate))
                {
                    bestSourceRoot = candidate;
                    bestSourceCable = cable;
                    break;
                }

                if (bestSourceCable == null)
                {
                    bestSourceRoot = candidate;
                    bestSourceCable = cable;
                }
            }

            if (bestSourceCable == null)
            {
                if (fleetCarrierObjects == 0)
                {
                    MissingMemberLog.WarnOnce(
                        "ArrestingCables.SourceResourceSearchWaiting",
                        "[Arresting Cables] No loaded FleetCarrier1 object was found while searching for the arresting cable source. Will retry when another target carrier spawns.");
                }
                else
                {
                    MissingMemberLog.ErrorOnce(
                        "ArrestingCables.SourceResourcePathMissing",
                        $"[Arresting Cables] Found {fleetCarrierObjects} loaded FleetCarrier1 object(s), but none contained source cable path '{SourceCablePath}'.");
                }

                return false;
            }

            cachedSourceCable = bestSourceCable.gameObject;

            if (diagnosticLogKeys.Add("ArrestingCables.SourceFoundInResources"))
            {
                Log.Info($"[Arresting Cables] Successfully cached source cable from loaded FleetCarrier1 object '{ObjectNameUtility.GetHierarchyPath(bestSourceRoot)}' (prefab asset={ObjectNameUtility.IsPrefabAsset(bestSourceRoot)}).");
            }

            ProcessPendingRoots();
            return true;
        }

        private static void TryCacheSourceCable(Transform shipRoot)
        {
            if (cachedSourceCable != null)
                return;

            Transform cable = FindChildPath(shipRoot, SourceCablePath);
            if (cable != null && cable.gameObject != null)
            {
                cachedSourceCable = cable.gameObject;
                if (diagnosticLogKeys.Add("ArrestingCables.SourceFound"))
                {
                    Log.Info("[Arresting Cables] Successfully cached source cable from FleetCarrier1 Awake.");
                }

                ProcessPendingRoots();
            }
            else
            {
                MissingMemberLog.ErrorOnce(
                    "ArrestingCables.SourceMissingOnAwake",
                    $"[Arresting Cables] FleetCarrier1 spawned, but source cable path '{SourceCablePath}' was not found in its hierarchy.");
            }
        }

        private static void ProcessPendingRoots()
        {
            if (cachedSourceCable == null || pendingTargetRoots.Count == 0)
                return;

            List<GameObject> toRemove = new List<GameObject>();
            foreach (GameObject pendingRoot in pendingTargetRoots)
            {
                if (pendingRoot == null)
                {
                    toRemove.Add(pendingRoot);
                    continue;
                }

                foreach (ArrestingCableCarrierDefinition definition in definitions)
                {
                    if (IsTargetRoot(pendingRoot, definition))
                    {
                        ApplyToRoot(definition, pendingRoot, cachedSourceCable);
                        toRemove.Add(pendingRoot);
                        break;
                    }
                }
            }

            foreach (GameObject pendingRoot in toRemove)
                pendingTargetRoots.Remove(pendingRoot);
        }

        private static bool IsTargetRoot(GameObject root, ArrestingCableCarrierDefinition definition)
        {
            if (root == null) return false;

            string cleanName = ObjectNameUtility.RemoveCloneSuffix(root.name);
            if (string.IsNullOrEmpty(cleanName)) return false;

            if (cleanName.Equals(definition.RootName, StringComparison.OrdinalIgnoreCase))
                return true;

            if (!string.IsNullOrEmpty(definition.FallbackRootContains) &&
                cleanName.IndexOf(definition.FallbackRootContains, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return IsPlausibleTargetRoot(root, definition);
            }

            return false;
        }

        private static bool IsPlausibleTargetRoot(GameObject gameObject, ArrestingCableCarrierDefinition definition)
        {
            if (gameObject == null || gameObject.transform == null)
                return false;

            if (gameObject.transform.parent == null || gameObject.transform.root == gameObject.transform)
                return true;

            if (!string.IsNullOrEmpty(definition.ValidationChildPath) &&
                FindChildPath(gameObject.transform, definition.ValidationChildPath) != null)
                return true;

            return false;
        }

        private static bool ApplyToRoot(ArrestingCableCarrierDefinition definition, GameObject carrierRoot, GameObject sourceCable)
        {
            if (carrierRoot == null || carrierRoot.transform == null)
                return false;

            bool allInjected = true;
            int injectedCount = 0;
            int existingCount = 0;

            foreach (ArrestingCableInjection injection in definition.Injections)
            {
                if (injection == null)
                {
                    Log.Error($"[{definition.Label}] Encountered a null arresting cable injection definition. This injection will be skipped.");
                    allInjected = false;
                    continue;
                }

                if (injection.CableNumber < 1)
                {
                    Log.Error($"[{definition.Label}] Injection for '{carrierRoot.name}' has invalid cable number {injection.CableNumber}. This injection will be skipped.");
                    allInjected = false;
                    continue;
                }

                Transform parent = FindChildPath(carrierRoot.transform, injection.ParentPath);
                if (parent == null)
                {
                    MissingMemberLog.ErrorOnce(
                        $"ArrestingCables.{definition.Label}.ParentMissing|{carrierRoot.name}|{injection.ParentPath}",
                        $"[{definition.Label}] '{carrierRoot.name}' is missing parent path '{injection.ParentPath}'. Expected '{carrierRoot.name}/{injection.ParentPath}'.");
                    allInjected = false;
                    continue;
                }

                string cableName = CableBaseName + injection.CableNumber;
                bool wasExisting;
                GameObject cable = EnsureCable(definition, sourceCable, parent, cableName, injection.LocalPosition, injection.CableNumber, out wasExisting);

                if (cable == null)
                {
                    allInjected = false;
                    continue;
                }

                if (wasExisting)
                    existingCount++;
                else
                    injectedCount++;
            }

            if (!allInjected)
                return false;

            string logKey = $"ArrestingCables.{definition.Label}.Root|{carrierRoot.GetInstanceID()}";
            if (diagnosticLogKeys.Add(logKey))
            {
                Log.Info($"[{definition.Label}] Ensured arresting cables on '{carrierRoot.name}'. Injected={injectedCount}, already present={existingCount}.");
            }

            return true;
        }

        private static GameObject EnsureCable(
            ArrestingCableCarrierDefinition definition,
            GameObject sourceCable,
            Transform parent,
            string cableName,
            Vector3 localPosition,
            int cableNumber,
            out bool wasExisting)
        {
            wasExisting = false;

            Transform existing = FindChildByName(parent, cableName);
            GameObject cable;

            if (existing != null)
            {
                cable = existing.gameObject;
                wasExisting = true;
            }
            else
            {
                try
                {
                    cable = UnityEngine.Object.Instantiate(sourceCable, parent, false);
                }
                catch (Exception ex)
                {
                    Log.Exception($"[{definition.Label}] Instantiating '{cableName}' under '{ObjectNameUtility.GetHierarchyPath(parent.gameObject)}'", ex);
                    return null;
                }

                if (cable == null)
                {
                    Log.Error($"[{definition.Label}] Failed to instantiate '{cableName}' under '{ObjectNameUtility.GetHierarchyPath(parent.gameObject)}'.");
                    return null;
                }

                cable.name = cableName;
            }

            if (cable.transform == null)
            {
                Log.Error($"[{definition.Label}] Cable '{cableName}' has a null transform.");
                return null;
            }

            cable.transform.SetParent(parent, false);
            cable.transform.localPosition = localPosition;
            cable.transform.localRotation = Quaternion.identity;
            cable.transform.localEulerAngles = Vector3.zero;

            if (!TryConfigureArrestorGear(definition, cable, cableNumber))
                return null;

            if (cable.GetComponent<ModifiedStatsFlag>() == null)
                cable.AddComponent<ModifiedStatsFlag>();

            cable.SetActive(true);

            return cable;
        }

        private static Transform FindChildPath(Transform root, string path)
        {
            if (root == null)
                return null;

            if (string.IsNullOrEmpty(path))
                return root;

            Transform direct = root.Find(path);
            if (direct != null)
                return direct;

            string[] segments = path.Split('/');
            Transform current = root;

            foreach (string segment in segments)
            {
                if (current == null)
                    return null;

                Transform next = FindChildByName(current, segment);
                if (next == null)
                    return null;

                current = next;
            }

            return current;
        }

        private static Transform FindChildByName(Transform parent, string name)
        {
            if (parent == null || string.IsNullOrEmpty(name))
                return null;

            for (int i = 0; i < parent.childCount; i++)
            {
                Transform child = parent.GetChild(i);
                if (child != null && ObjectNameUtility.RemoveCloneSuffix(child.name) == name)
                    return child;
            }

            return null;
        }

        private static bool TryConfigureArrestorGear(ArrestingCableCarrierDefinition definition, GameObject cable, int cableNumber)
        {
            if (cable == null)
                return false;

            bool foundArrestorGear = false;

            foreach (Component component in cable.GetComponentsInChildren<Component>(true))
            {
                if (component == null)
                    continue;

                if (component.GetType().Name != ArrestorGearComponentName)
                    continue;

                foundArrestorGear = true;

                if (!TrySetArrestorGearMember(component, WireNumberMemberName, cableNumber))
                {
                    MissingMemberLog.ErrorOnce(
                        $"ArrestingCables.{definition.Label}.wireNumberMissing|{cable.name}",
                        $"[{definition.Label}] {ArrestorGearComponentName} on '{cable.name}' is missing field or property '{WireNumberMemberName}'.");
                    return false;
                }

                if (definition.ArrestorGearDamping.HasValue &&
                    !TrySetArrestorGearMember(component, DampingMemberName, definition.ArrestorGearDamping.Value))
                {
                    MissingMemberLog.ErrorOnce(
                        $"ArrestingCables.{definition.Label}.dampingMissing|{cable.name}",
                        $"[{definition.Label}] {ArrestorGearComponentName} on '{cable.name}' is missing field or property '{DampingMemberName}'.");
                    return false;
                }

                if (definition.ArrestorGearSpring.HasValue &&
                    !TrySetArrestorGearMember(component, SpringMemberName, definition.ArrestorGearSpring.Value))
                {
                    MissingMemberLog.ErrorOnce(
                        $"ArrestingCables.{definition.Label}.springMissing|{cable.name}",
                        $"[{definition.Label}] {ArrestorGearComponentName} on '{cable.name}' is missing field or property '{SpringMemberName}'.");
                    return false;
                }
            }

            if (!foundArrestorGear)
            {
                MissingMemberLog.ErrorOnce(
                    $"ArrestingCables.{definition.Label}.ArrestorGearMissing|{cable.name}",
                    $"[{definition.Label}] '{cable.name}' has no component named '{ArrestorGearComponentName}'.");
                return false;
            }

            if ((definition.ArrestorGearDamping.HasValue || definition.ArrestorGearSpring.HasValue) &&
                diagnosticLogKeys.Add($"ArrestingCables.{definition.Label}.TuningApplied"))
            {
                Log.Info(
                    $"[{definition.Label}] Applied ArrestorGear tuning: " +
                    $"wireNumber per cable" +
                    $"{(definition.ArrestorGearDamping.HasValue ? $", damping={definition.ArrestorGearDamping.Value}" : "")}" +
                    $"{(definition.ArrestorGearSpring.HasValue ? $", spring={definition.ArrestorGearSpring.Value}" : "")}. ");
            }

            return true;
        }

        private static bool TrySetArrestorGearMember(Component component, string memberName, object value)
        {
            try
            {
                for (Type type = component.GetType(); type != null && type != typeof(object); type = type.BaseType)
                {
                    FieldInfo field = type.GetField(memberName, WireNumberBindingFlags);
                    if (field != null)
                    {
                        field.SetValue(component, Convert.ChangeType(value, field.FieldType, CultureInfo.InvariantCulture));
                        return true;
                    }

                    PropertyInfo property = type.GetProperty(memberName, WireNumberBindingFlags);
                    if (property != null && property.CanWrite)
                    {
                        property.SetValue(component, Convert.ChangeType(value, property.PropertyType, CultureInfo.InvariantCulture));
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Exception($"[{ArrestorGearComponentName}.{memberName}] Setting '{memberName}' on '{component?.gameObject?.name}'", ex);
            }

            return false;
        }
    }



    // ========================================================================
    // Harmony hook for arresting cables.
    // Hooks Ship.Awake to catch carrier spawns without polling or scene sweeps.
    // ========================================================================
    [HarmonyPatch(typeof(Ship), "Awake")]
    public static class ShipAwakeArrestingCablePatch
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        public static void Postfix(Ship __instance)
        {
            if (!RuntimeSettings.Captured)
            {
                Log.Error("Ship Awake arresting cable patch ran before RuntimeSettings.Capture. This patch will be skipped.");
                return;
            }

            if (!RuntimeSettings.EnableAnnexArrestingCables && !RuntimeSettings.EnableCursorLFDArrestingCables)
                return;

            try
            {
                ArrestingCableSystem.NotifyShipSpawned(__instance);
            }
            catch (Exception ex)
            {
                Log.Exception("Ship Awake arresting cable patch", ex);
            }
        }
    }



    // ========================================================================
    // Canopy glass visibility patch.
    // Hooks Canopy.Awake to use the component as an anchor for a localized search.
    // This avoids global scene sweeps while guaranteeing we catch the interior glass
    // meshes (which are separate from the exterior damage-decal meshes).
    // Uses startup-cached config values only. Runtime config changes are ignored.
    // ========================================================================
    [HarmonyPatch(typeof(Canopy), "Awake")]
    public static class CanopyGlassAwakePatch
    {
        private static readonly HashSet<string> infoLogKeys = new HashSet<string>();

        public static void Postfix(Canopy __instance)
        {
            if (!RuntimeSettings.Captured)
            {
                Log.Error("Canopy glass patch ran before RuntimeSettings.Capture. This patch will be skipped.");
                return;
            }

            try
            {
                Apply(__instance);
            }
            catch (Exception ex)
            {
                Log.Exception("Canopy glass patch", ex);
            }
        }

        private static void Apply(Canopy canopy)
        {
            if (canopy == null || canopy.transform == null)
                return;

            Transform rootTransform = canopy.transform.root;
            if (rootTransform == null)
                return;

            GameObject rootObject = rootTransform.gameObject;
            if (rootObject == null)
                return;

            // Avoid modifying prefab assets directly. Scene instances are the intended targets.
            if (ObjectNameUtility.IsPrefabAsset(rootObject))
                return;

            string rootName = ObjectNameUtility.RemoveCloneSuffix(rootObject.name);
            if (string.IsNullOrEmpty(rootName))
                return;

            CanopyGlassDefinition matchedDefinition = null;
            foreach (CanopyGlassDefinition definition in CanopyGlassRegistry.Definitions)
            {
                if (definition == null)
                {
                    Log.Error("CanopyGlassRegistry contains a null definition while applying canopy glass visibility.");
                    continue;
                }

                if (!definition.CachedDisabled)
                    continue;

                if (definition.RootNames == null || definition.RootNames.Length == 0)
                {
                    MissingMemberLog.ErrorOnce(
                        $"CanopyGlass.{definition.Section}.InvalidDefinition",
                        $"[Canopy Glass] Definition for '{definition.Section}' is missing RootNames.");
                    continue;
                }

                if (definition.RootNames.Any(root => string.Equals(root, rootName, StringComparison.OrdinalIgnoreCase)))
                {
                    matchedDefinition = definition;
                    break;
                }
            }

            if (matchedDefinition == null)
                return;

            if (matchedDefinition.GlassNames == null || matchedDefinition.GlassNames.Length == 0)
            {
                MissingMemberLog.ErrorOnce(
                    $"CanopyGlass.{matchedDefinition.Section}.MissingGlassNames",
                    $"[Canopy Glass] Definition for '{matchedDefinition.Section}' is missing GlassNames.");
                return;
            }

            HashSet<string> glassNameSet = new HashSet<string>(matchedDefinition.GlassNames, StringComparer.OrdinalIgnoreCase);
            HashSet<string> foundNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int found = 0;
            int disabled = 0;

            // Localized search: ONLY search within this specific aircraft root.
            // This is extremely fast and completely avoids global scene sweeps.
            Transform[] transforms = rootTransform.GetComponentsInChildren<Transform>(true);
            foreach (Transform child in transforms)
            {
                if (child == null || child.gameObject == null)
                    continue;

                string cleanName = ObjectNameUtility.RemoveCloneSuffix(child.name);
                if (string.IsNullOrEmpty(cleanName) || !glassNameSet.Contains(cleanName))
                    continue;

                found++;
                foundNames.Add(cleanName);

                if (child.gameObject.activeSelf)
                {
                    child.gameObject.SetActive(false);
                    disabled++;
                }
            }

            if (found == 0)
            {
                MissingMemberLog.ErrorOnce(
                    $"CanopyGlass.{matchedDefinition.Section}.Missing|{rootName}",
                    $"[Canopy Glass] Aircraft root '{rootName}' was found, but none of the expected glass objects were found: {string.Join(", ", matchedDefinition.GlassNames)}.");
                return;
            }

            string[] missingNames = matchedDefinition.GlassNames
                .Where(glass => !foundNames.Contains(glass))
                .ToArray();

            if (missingNames.Length > 0)
            {
                MissingMemberLog.ErrorOnce(
                    $"CanopyGlass.{matchedDefinition.Section}.PartialMissing|{rootName}",
                    $"[Canopy Glass] Aircraft root '{rootName}' is missing glass object(s): {string.Join(", ", missingNames)}.");
            }

            if (disabled > 0)
            {
                string logKey = $"CanopyGlass.Disabled|{matchedDefinition.Section}|{rootName}";
                if (infoLogKeys.Add(logKey))
                {
                    Log.Info($"[Canopy Glass] Disabled {disabled} glass object(s) for '{matchedDefinition.Section}' on '{rootName}'.");
                }
            }
            else
            {
                string logKey = $"CanopyGlass.AlreadyHidden|{matchedDefinition.Section}|{rootName}";
                if (infoLogKeys.Add(logKey))
                {
                    Log.Info($"[Canopy Glass] Glass for '{matchedDefinition.Section}' on '{rootName}' was already hidden by this mod.");
                }
            }
        }
    }



    // ========================================================================
    // Blueprint weapon spawn-time enforcement.
    // Hooks WeaponManager.SpawnWeapons Prefix to strip disabled blueprint
    // options from hardpointSets before weapons are spawned. This is a
    // safety net for saved missions or network syncs that force a disabled
    // weapon into the loadout.
    // ========================================================================
    [HarmonyPatch(typeof(WeaponManager), "SpawnWeapons")]
    public static class BlueprintWeaponManagerInitPatch
    {
        public static void Prefix(WeaponManager __instance)
        {
            if (!RuntimeSettings.Captured)
                return;
            if (__instance == null || __instance.transform == null)
                return;

            try
            {
                string rootName = ObjectNameUtility.GetCleanRootName(__instance.transform.root?.gameObject ?? __instance.gameObject);
                if (string.IsNullOrEmpty(rootName))
                    return;

                // Strip from the live instance as a safety net.
                if (__instance.hardpointSets != null)
                {
                    foreach (BlueprintWeaponRule rule in BlueprintWeaponRemovalSystem.Rules)
                    {
                        if (rule.Enabled || !rule.MatchesAircraft(rootName))
                            continue;

                        foreach (int hardpointSetIndex in rule.HardpointSets)
                        {
                            if (hardpointSetIndex < 0 || hardpointSetIndex >= __instance.hardpointSets.Length)
                                continue;

                            object hardpoint = __instance.hardpointSets[hardpointSetIndex];
                            if (BlueprintRemovalReflection.IsNull(hardpoint))
                                continue;

                            int removedCount;
                            string failure;
                            if (BlueprintRemovalReflection.TryRemoveWeaponOptions(hardpoint, rule.MatchesWeaponOption, out removedCount, out failure))
                            {
                                if (removedCount > 0)
                                {
                                    string logKey = $"BlueprintWeaponSpawnStrip.instance|{rootName}|{rule.DisplayName}|{hardpointSetIndex}";
                                    if (BlueprintWeaponRemovalSystem.DiagnosticLogKeys.Add(logKey))
                                    {
                                        Log.Info($"[Blueprints] Removed {removedCount} '{rule.DisplayName}' option(s) from {rootName} hardpoint set {hardpointSetIndex} (SpawnWeapons prefix).");
                                    }
                                }
                            }
                            else if (!string.IsNullOrEmpty(failure))
                            {
                                MissingMemberLog.ErrorOnce(
                                    $"BlueprintWeaponSpawnStrip.Failure|instance|{rootName}|{rule.DisplayName}|{hardpointSetIndex}|{failure}",
                                    $"[Blueprints] Could not remove '{rule.DisplayName}' from {rootName} hardpoint set {hardpointSetIndex} (SpawnWeapons prefix). Failure: {failure}.");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Exception("Blueprint weapon SpawnWeapons prefix", ex);
            }
        }
    }



    // ========================================================================
    // Blueprint weapon loadout enforcement.
    // Prevents disabled blueprint weapons from spawning even if they are
    // present in a saved mission, network sync, or AI fallback loadout.
    // ========================================================================
    [HarmonyPatch(typeof(WeaponManager), "LoadHardpointSet")]
    public static class BlueprintWeaponLoadPatch
    {
        public static bool Prefix(WeaponManager __instance, object hardpointSet, object weaponMount)
        {
            if (!RuntimeSettings.Captured)
                return true;

            if (weaponMount == null || hardpointSet == null)
                return true;

            try
            {
                if (BlueprintWeaponRemovalSystem.IsWeaponMountDisabled(__instance, weaponMount))
                {
                    Traverse traverse = Traverse.Create(hardpointSet);
                    traverse.Method("RemoveMounts").GetValue();

                    string mountName = weaponMount is UnityEngine.Object uObj ? uObj.name : weaponMount.ToString();
                    string logKey = $"BlueprintWeaponLoad.Blocked|{mountName}";
                    if (BlueprintWeaponRemovalSystem.DiagnosticLogKeys.Add(logKey))
                    {
                        Log.Info($"[Blueprints] Blocked disabled weapon '{mountName}' from spawning via LoadHardpointSet.");
                    }
                    return false;
                }
            }
            catch (Exception ex)
            {
                Log.Exception("Blueprint weapon LoadHardpointSet prefix", ex);
            }

            return true;
        }
    }



    // ========================================================================
    // Blueprint AI selection enforcement.
    // Postfixes SelectAIAircraftWeapons to ensure the AI never returns a
    // loadout containing disabled blueprint weapons, acting as a final safety net.
    // ========================================================================
    [HarmonyPatch(typeof(WeaponManager), "SelectAIAircraftWeapons")]
    public static class BlueprintAISelectionPatch
    {
        public static void Postfix(WeaponManager __instance, object __result)
        {
            if (!RuntimeSettings.Captured)
                return;
            if (__result == null)
                return;

            try
            {
                Traverse traverse = Traverse.Create(__result);
                Traverse weaponsField = traverse.Field("weapons");
                if (!weaponsField.FieldExists())
                    return;

                object weaponsObj = weaponsField.GetValue();
                if (weaponsObj is System.Collections.IList weaponsList)
                {
                    bool modified = false;
                    for (int i = weaponsList.Count - 1; i >= 0; i--)
                    {
                        object mountObj = weaponsList[i];
                        if (mountObj != null && BlueprintWeaponRemovalSystem.IsWeaponMountDisabled(__instance, mountObj))
                        {
                            weaponsList.RemoveAt(i);
                            modified = true;
                        }
                    }

                    if (modified)
                    {
                        string rootName = ObjectNameUtility.GetCleanRootName(__instance.gameObject);
                        string logKey = $"BlueprintAISelection.Cleaned|{rootName}";
                        if (BlueprintWeaponRemovalSystem.DiagnosticLogKeys.Add(logKey))
                        {
                            Log.Info($"[Blueprints] Removed disabled blueprint weapons from AI loadout selection for '{rootName}'.");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Exception("Blueprint AI selection postfix", ex);
            }
        }
    }
}