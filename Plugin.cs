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
        public const string BaseVersion = "1.3.0";

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
            Log.Info($"BVR - Starting Balance and Variety Rework v{BaseVersion}.");

            BindImportantNotices();
            BindFunctionalConfigs();
            BindBlueprinterWeapons();

            TryImportPendingConfigSeed();

            // Restart-only semantics:
            // Capture all values after seed import so no later runtime config change
            // can affect already initialized systems.
            RuntimeSettings.Capture();

            FinalizeVersionAndHash();
            RegisterHarmonyPatches();
            BlueprintWeaponToggleSystem.Initialize(this);

            Log.Info("BVR - Balance and Variety Rework Mod Loaded!");
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
                typeof(SARHLockPersistencePatch),
                typeof(SARHRelockPatch),
                typeof(CruiseMissileRCSPatch),
                typeof(ProxyGunPatch),
                typeof(ChicaneBayPylonSymmetryFixPatch),
                typeof(MedusaLaserPatch),
                typeof(BlueprintWeaponDisablePatch)
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

        public static bool EnableChicaneProxyGun;
        public static bool EnableChicaneBayPylonSymmetryFix;

        public static bool EnableMedusaLaserBuff;
        public static float MedusaLaserPowerDraw;

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

            EnableChicaneProxyGun = Plugin.EnableChicaneProxyGun.Value;
            EnableChicaneBayPylonSymmetryFix = Plugin.EnableChicaneBayPylonSymmetryFix.Value;

            EnableMedusaLaserBuff = Plugin.EnableMedusaLaserBuff.Value;
            MedusaLaserPowerDraw = SafeFloat(Plugin.MedusaLaserPowerDraw.Value, 60.0f, "Laser Power Draw Value");

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

            Captured = true;
            Log.Info("BVR runtime settings captured. Any future config changes require a full game restart.");
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
    //   3. Ensure BlueprintKeys and HardpointSets are correct.
    //   4. No additional binding code is required; Plugin.BindBlueprinterWeapons
    //      handles it automatically.
    // ========================================================================
    internal class BlueprintWeaponDefinition
    {
        public string Section, Key, Description;
        public bool DefaultValue;

        public string[] AircraftRootNames;
        public string AircraftRootContains, ExcludeRootContains;

        public int[] HardpointSets;
        public string[] BlueprintKeys;

        public ConfigEntry<bool> ConfigEntry;
        public bool CachedEnabled;
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
            new BlueprintWeaponDefinition { Section = "FS-12 Revoker Changes", Key = "Enable Revoker Kingpin x12 Triple", Description = "Enables Blueprinter Rocket2_4Podx3 on set 2", DefaultValue = true, AircraftRootNames = new[] { "Fighter1" }, AircraftRootContains = "Fighter1", ExcludeRootContains = "SmallFighter1", HardpointSets = new[] { 2 }, BlueprintKeys = new[] { "Rocket2_4Podx3" } },
            new BlueprintWeaponDefinition { Section = "FS-12 Revoker Changes", Key = "Enable Revoker Lynchpin x21 Triple", Description = "Enables Blueprinter RocketPod1_triple on set 2", DefaultValue = true, AircraftRootNames = new[] { "Fighter1" }, AircraftRootContains = "Fighter1", ExcludeRootContains = "SmallFighter1", HardpointSets = new[] { 2 }, BlueprintKeys = new[] { "RocketPod1_triple" } },
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
            new BlueprintWeaponDefinition { Section = "EW-25 Medusa Changes", Key = "Enable Medusa Kingpin x12 Triple", Description = "Enables Blueprinter Rocket2_4Podx3 on sets 3, 4", DefaultValue = true, AircraftRootNames = new[] { "EW1" }, AircraftRootContains = "EW1", HardpointSets = new[] { 3, 4 }, BlueprintKeys = new[] { "Rocket2_4Podx3" } },
            new BlueprintWeaponDefinition { Section = "EW-25 Medusa Changes", Key = "Enable Medusa Lynchpin x21 Triple", Description = "Enables Blueprinter RocketPod1_triple on sets 3, 4", DefaultValue = true, AircraftRootNames = new[] { "EW1" }, AircraftRootContains = "EW1", HardpointSets = new[] { 3, 4 }, BlueprintKeys = new[] { "RocketPod1_triple" } },
            new BlueprintWeaponDefinition { Section = "EW-25 Medusa Changes", Key = "Enable Medusa RAM-45 x3", Description = "Enables Blueprinter BVR_SAM_Radar1x3 on sets 3, 4", DefaultValue = true, AircraftRootNames = new[] { "EW1" }, AircraftRootContains = "EW1", HardpointSets = new[] { 3, 4 }, BlueprintKeys = new[] { "BVR_SAM_Radar1x3" } },
            new BlueprintWeaponDefinition { Section = "EW-25 Medusa Changes", Key = "Enable Medusa Internal RAM-45 x3", Description = "Enables Blueprinter BVR_SAM_Radar1x3_Internal on set 1", DefaultValue = true, AircraftRootNames = new[] { "EW1" }, AircraftRootContains = "EW1", HardpointSets = new[] { 1 }, BlueprintKeys = new[] { "BVR_SAM_Radar1x3_Internal" } },
            new BlueprintWeaponDefinition { Section = "EW-25 Medusa Changes", Key = "Enable Medusa R9 Stratolance x2", Description = "Enables Blueprinter BVR_SAM_Radar2x2 on sets 3, 4", DefaultValue = true, AircraftRootNames = new[] { "EW1" }, AircraftRootContains = "EW1", HardpointSets = new[] { 3, 4 }, BlueprintKeys = new[] { "BVR_SAM_Radar2x2" } },
            new BlueprintWeaponDefinition { Section = "EW-25 Medusa Changes", Key = "Enable Medusa Internal R9 Stratolance x2", Description = "Enables Blueprinter BVR_SAM_Radar2x2_Internal on set 1", DefaultValue = true, AircraftRootNames = new[] { "EW1" }, AircraftRootContains = "EW1", HardpointSets = new[] { 1 }, BlueprintKeys = new[] { "BVR_SAM_Radar2x2_Internal" } }
        };
    }



    // ========================================================================
    // Runtime blueprint rule used by the toggle system.
    // ========================================================================
    internal sealed class BlueprintWeaponRule
    {
        public string[] RootNames;
        public string RootContains, ExcludeRootContains, DisplayName;
        public bool Enabled;
        public string[] BlueprintKeys;
        public int[] HardpointSets;

        public bool MatchesAircraft(string rootName)
        {
            if (string.IsNullOrEmpty(rootName))
                return false;

            if (RootNames != null && RootNames.Any(r => string.Equals(r, rootName, StringComparison.OrdinalIgnoreCase)))
                return true;

            if (!string.IsNullOrEmpty(RootContains))
            {
                if (!string.IsNullOrEmpty(ExcludeRootContains) &&
                    rootName.IndexOf(ExcludeRootContains, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return false;
                }

                return rootName.IndexOf(RootContains, StringComparison.OrdinalIgnoreCase) >= 0;
            }

            return false;
        }

        public bool MatchesWeapon(object weaponOption)
        {
            if (BlueprintToggleReflection.IsNull(weaponOption) || BlueprintKeys == null)
                return false;

            List<string> identifiers = BlueprintToggleReflection.GetIdentifiers(weaponOption);
            return BlueprintKeys.Any(key => identifiers.Any(id => string.Equals(id, key, StringComparison.OrdinalIgnoreCase)));
        }
    }



    // ========================================================================
    // Blueprint weapon toggle system.
    // Uses cached startup values only. Runtime config changes are ignored.
    // ========================================================================
    internal static class BlueprintWeaponToggleSystem
    {
        private static Plugin plugin;
        private static bool initialized;
        private static bool awakeApplyQueued;

        private static List<BlueprintWeaponRule> rules = new List<BlueprintWeaponRule>();
        private static readonly HashSet<string> appliedLogKeys = new HashSet<string>();
        private static readonly HashSet<string> diagnosticLogKeys = new HashSet<string>();

        public static void Initialize(Plugin owner)
        {
            if (initialized)
                return;

            if (owner == null)
            {
                Log.Error("BlueprintWeaponToggleSystem.Initialize was called with a null plugin instance.");
                return;
            }

            if (!RuntimeSettings.Captured)
            {
                Log.Error("BlueprintWeaponToggleSystem.Initialize was called before RuntimeSettings.Capture. Blueprint toggles will not run.");
                return;
            }

            plugin = owner;

            rules = BlueprintWeaponRegistry.Definitions.Select(d => new BlueprintWeaponRule
            {
                RootNames = d.AircraftRootNames,
                RootContains = d.AircraftRootContains,
                ExcludeRootContains = d.ExcludeRootContains,
                DisplayName = d.Key,
                Enabled = d.CachedEnabled,
                BlueprintKeys = d.BlueprintKeys,
                HardpointSets = d.HardpointSets
            }).ToList();

            SceneManager.sceneLoaded += (scene, mode) =>
            {
                if (plugin != null)
                    plugin.StartCoroutine(DelayedApply(new[] { 0.5f, 2f, 5f }));
            };

            plugin.StartCoroutine(Poll());
            initialized = true;

            Log.Info("Blueprint weapon toggle system initialized using startup-cached config values.");
        }

        public static void NotifyWeaponManagerAwake(WeaponManager weaponManager)
        {
            if (!initialized || awakeApplyQueued)
                return;

            awakeApplyQueued = true;
            plugin.StartCoroutine(DelayedApply(new[] { 0.5f, 2f, 5f }, () => awakeApplyQueued = false));
        }

        private static IEnumerator Poll()
        {
            yield return new WaitForSecondsRealtime(2f);

            float endTime = Time.unscaledTime + 60f;
            while (Time.unscaledTime < endTime)
            {
                ApplyAll();
                yield return new WaitForSecondsRealtime(0.5f);
            }

            ApplyAll();
        }

        private static IEnumerator DelayedApply(float[] delays, Action callback = null)
        {
            foreach (float delay in delays)
            {
                yield return new WaitForSecondsRealtime(delay);
                ApplyAll();
            }

            callback?.Invoke();
        }

        private static void ApplyAll()
        {
            foreach (WeaponManager weaponManager in Resources.FindObjectsOfTypeAll<WeaponManager>())
            {
                try
                {
                    Apply(weaponManager);
                }
                catch (Exception ex)
                {
                    Log.Exception("Blueprint weapon toggle", ex);
                }
            }
        }

        private static void Apply(WeaponManager weaponManager)
        {
            if (BlueprintToggleReflection.IsNull(weaponManager) || weaponManager.transform == null)
                return;

            string rootName = ObjectNameUtility.GetCleanRootName(weaponManager.transform.root?.gameObject ?? weaponManager.gameObject);
            if (string.IsNullOrEmpty(rootName))
                return;

            foreach (BlueprintWeaponRule rule in rules)
            {
                if (!rule.MatchesAircraft(rootName))
                    continue;

                bool disable = !rule.Enabled;

                foreach (int hardpointSet in rule.HardpointSets)
                {
                    if (weaponManager.hardpointSets == null)
                    {
                        LogOnce($"HardpointArrayNull|{rootName}", $"[Blueprints] WeaponManager on '{rootName}' has null hardpointSets.");
                        continue;
                    }

                    if (hardpointSet < 0 || hardpointSet >= weaponManager.hardpointSets.Length)
                    {
                        LogOnce(
                            $"HardpointOutOfRange|{rootName}|{rule.DisplayName}|{hardpointSet}",
                            $"[Blueprints] Hardpoint set {hardpointSet} for '{rule.DisplayName}' on '{rootName}' is out of range. Count={weaponManager.hardpointSets.Length}.");
                        continue;
                    }

                    object hs = weaponManager.hardpointSets[hardpointSet];
                    if (BlueprintToggleReflection.IsNull(hs))
                    {
                        LogOnce(
                            $"HardpointNull|{rootName}|{hardpointSet}",
                            $"[Blueprints] Hardpoint set {hardpointSet} on '{rootName}' is null.");
                        continue;
                    }

                    IEnumerable options = BlueprintToggleReflection.GetWeaponOptions(hs);
                    if (options == null)
                    {
                        LogOnce(
                            $"WeaponOptionsMissing|{rootName}|{hardpointSet}",
                            $"[Blueprints] Could not find weapon options collection in hardpoint set {hardpointSet} on '{rootName}'.");
                        continue;
                    }

                    bool foundMatchingWeapon = false;

                    foreach (object option in options)
                    {
                        if (!rule.MatchesWeapon(option))
                            continue;

                        foundMatchingWeapon = true;

                        try
                        {
                            bool changed = BlueprintToggleReflection.TrySetDisabled(option, disable, out bool foundDisableMember);

                            if (changed)
                            {
                                string logKey = $"{rootName}|{rule.DisplayName}|{hardpointSet}|{disable}";
                                if (appliedLogKeys.Add(logKey))
                                {
                                    Log.Info($"[BVR] {(disable ? "Disabled" : "Enabled")} '{rule.DisplayName}' on {rootName} set {hardpointSet}.");
                                }
                            }
                            else if (!foundDisableMember)
                            {
                                LogOnce(
                                    $"DisableMemberMissing|{rootName}|{rule.DisplayName}|{hardpointSet}",
                                    $"[Blueprints] Matched '{rule.DisplayName}' on '{rootName}' set {hardpointSet}, but could not find a disable flag on the weapon option.");
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Exception($"Blueprint toggle for '{rule.DisplayName}' on '{rootName}' set {hardpointSet}", ex);
                        }
                    }

                    if (!foundMatchingWeapon)
                    {
                        LogOnce(
                            $"BlueprintWeaponMissing|{rootName}|{rule.DisplayName}|{hardpointSet}",
                            $"[Blueprints] '{rule.DisplayName}' was not found on '{rootName}' hardpoint set {hardpointSet}. If this aircraft should have it, verify BlueprintKeys and hardpoint set.");
                    }
                }
            }
        }

        private static void LogOnce(string key, string message)
        {
            if (diagnosticLogKeys.Add(key))
                Log.Warn(message);
        }
    }



    // ========================================================================
    // Reflection helper for blueprint weapon options.
    // Kept defensive because the game's internal weapon option layout may be
    // private or change between versions.
    // ========================================================================
    internal static class BlueprintToggleReflection
    {
        private const BindingFlags Flags =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        private static readonly string[] DisabledMemberNames =
            { "Disabled", "disabled", "isDisabled", "IsDisabled" };

        private static readonly string[] WeaponOptionCollectionNames =
            { "weaponOptions", "WeaponOptions", "options", "weapons" };

        private static readonly string[] IdentifierMemberNames =
            { "jsonKey", "blueprintKey", "key", "id", "name", "mountName", "weaponName" };

        private static readonly string[] NestedObjectMemberNames =
            { "mount", "weaponMount", "weaponOption", "option", "weapon" };

        private static readonly string[] PrefabMemberNames =
            { "prefab", "Prefab", "gameObject", "GameObject" };

        public static bool IsNull(object obj)
        {
            return obj == null || (obj is UnityEngine.Object unityObject && unityObject == null);
        }

        public static IEnumerable GetWeaponOptions(object hardpointSet)
        {
            if (IsNull(hardpointSet))
                return null;

            foreach (string name in WeaponOptionCollectionNames)
            {
                if (TryGet(hardpointSet, name, out object value) && value is IEnumerable enumerable && !(value is string))
                    return enumerable;
            }

            return null;
        }

        public static List<string> GetIdentifiers(object obj)
        {
            List<string> identifiers = new List<string>();

            if (IsNull(obj))
                return identifiers;

            if (obj is Component component && component.gameObject != null)
                AddIdentifier(identifiers, component.gameObject.name);

            if (obj is UnityEngine.Object unityObject)
                AddIdentifier(identifiers, unityObject.name);

            foreach (string name in IdentifierMemberNames)
            {
                if (TryGet(obj, name, out object value) && !IsNull(value))
                {
                    if (value is string s)
                        AddIdentifier(identifiers, s);
                    else if (value is UnityEngine.Object valueUnityObject)
                        AddIdentifier(identifiers, valueUnityObject.name);
                    else
                        AddIdentifier(identifiers, value.ToString());
                }
            }

            foreach (string name in PrefabMemberNames)
            {
                if (TryGet(obj, name, out object value) && !IsNull(value))
                {
                    if (value is GameObject gameObject)
                        AddIdentifier(identifiers, gameObject.name);
                    else if (value is UnityEngine.Object valueUnityObject)
                        AddIdentifier(identifiers, valueUnityObject.name);
                }
            }

            return identifiers;
        }

        public static bool TrySetDisabled(object target, bool disabled, out bool foundDisableMember)
        {
            foundDisableMember = false;

            if (IsNull(target))
                return false;

            return SetDisabledRecursive(target, disabled, 0, out foundDisableMember);
        }

        private static bool SetDisabledRecursive(object target, bool disabled, int depth, out bool foundDisableMember)
        {
            foundDisableMember = false;

            if (IsNull(target) || depth > 2)
                return false;

            if (SetBool(target, disabled, ref foundDisableMember))
                return true;

            if (foundDisableMember)
                return false;

            foreach (string nestedName in NestedObjectMemberNames)
            {
                if (TryGet(target, nestedName, out object nested) && !IsNull(nested) && !ReferenceEquals(nested, target))
                {
                    bool childFound = false;

                    if (SetDisabledRecursive(nested, disabled, depth + 1, out childFound))
                    {
                        foundDisableMember = true;
                        return true;
                    }

                    if (childFound)
                        foundDisableMember = true;
                }
            }

            return false;
        }

        private static bool SetBool(object target, bool value, ref bool foundDisableMember)
        {
            foreach (string name in DisabledMemberNames)
            {
                if (TryGet(target, name, out object currentValue))
                {
                    foundDisableMember = true;

                    if (ConvertToBool(currentValue) != value && TrySet(target, name, value))
                        return true;
                }
            }

            return false;
        }

        private static bool ConvertToBool(object value)
        {
            if (value is bool b)
                return b;

            if (value is string s)
                return s == "1" || (bool.TryParse(s, out bool parsed) && parsed);

            return false;
        }

        public static bool TryGet(object target, string memberName, out object value)
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

        public static bool TrySet(object target, string memberName, object value)
        {
            if (IsNull(target))
                return false;

            for (Type type = target.GetType(); type != null && type != typeof(object); type = type.BaseType)
            {
                FieldInfo field = type.GetField(memberName, Flags);
                if (field != null)
                {
                    field.SetValue(target, ConvertToType(value, field.FieldType));
                    return true;
                }

                PropertyInfo property = type.GetProperty(memberName, Flags);
                if (property != null && property.CanWrite)
                {
                    property.SetValue(target, ConvertToType(value, property.PropertyType));
                    return true;
                }
            }

            return false;
        }

        private static object ConvertToType(object value, Type targetType)
        {
            if (value == null)
                return targetType.IsValueType ? Activator.CreateInstance(targetType) : null;

            if (targetType.IsInstanceOfType(value))
                return value;

            return Convert.ChangeType(value, Nullable.GetUnderlyingType(targetType) ?? targetType, CultureInfo.InvariantCulture);
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
    // Harmony hook for blueprint weapon toggles.
    // ========================================================================
    [HarmonyPatch(typeof(WeaponManager), "Awake")]
    public static class BlueprintWeaponDisablePatch
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        public static void Postfix(WeaponManager __instance)
        {
            BlueprintWeaponToggleSystem.NotifyWeaponManagerAwake(__instance);
        }
    }



    // ========================================================================
    // IR missile buffs: flare count and flare rejection.
    // ========================================================================
    [HarmonyPatch(typeof(WeaponManager), "Awake")]
    public static class StatsPatch
    {
        private static bool flareSweepApplied;
        private static bool seekerSweepApplied;

        public static void Prefix()
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
    // SARH lock persistence.
    // Uses shared SarhMissileMatcher logic.
    // ========================================================================
    [HarmonyPatch(typeof(WeaponManager), "Awake")]
    public static class SARHLockPersistencePatch
    {
        private static bool appliedR9;
        private static bool appliedRAM45;

        public static void Prefix()
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
    [HarmonyPatch(typeof(WeaponManager), "Awake")]
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

        public static void Prefix()
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
    // SAH-46 Chicane proxy gun patch.
    // Targets the gun object inside the Chicane cockpit turret:
    //   AttackHelo1/cockpit_R/cockpit_F/turretMount/turret/gun
    // ========================================================================
    [HarmonyPatch(typeof(WeaponManager), "Awake")]
    public static class ProxyGunPatch
    {
    private const string AttackHeloRoot = "AttackHelo1";
    private const string GunChildPath = "cockpit_R/cockpit_F/turretMount/turret/gun";
    private static bool applied;

    public static void Prefix()
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
    // SAH-46 Chicane bay pylon symmetry fix.
    // ========================================================================
    [HarmonyPatch(typeof(WeaponManager), "Awake")]
    public static class ChicaneBayPylonSymmetryFixPatch
    {
        private const string AttackHeloRoot = "AttackHelo1";
        private const string PylonPath = "weaponBay_R/weaponDoorHinge_Ra/weaponDoorHinge_Rb/pylon_bay_R";

        private static bool applied;

        public static void Prefix()
        {
            if (!RuntimeSettings.Captured)
            {
                Log.Error("Chicane bay pylon symmetry patch ran before RuntimeSettings.Capture. This patch will be skipped.");
                return;
            }

            if (!RuntimeSettings.EnableChicaneBayPylonSymmetryFix || applied)
                return;

            try
            {
                applied = TryApply();
            }
            catch (Exception ex)
            {
                Log.Exception("Chicane bay pylon symmetry patch", ex);
            }
        }

        private static bool TryApply()
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

                Transform pylon = weaponManager.transform.root.Find(PylonPath);
                if (pylon == null)
                {
                    MissingMemberLog.ErrorOnce(
                        $"ChicaneBayPylon.PathMissing|{rootName}",
                        $"[Chicane Symmetry] '{rootName}' is missing pylon path '{PylonPath}'.");
                    continue;
                }

                Vector3 desiredPosition = new Vector3(0f, -0.35f, -0.1f);

                if (pylon.localPosition != desiredPosition)
                {
                    pylon.localPosition = desiredPosition;
                    Log.Info($"[Chicane Symmetry] Centered '{PylonPath}' on '{rootName}'.");
                }

                fixedAny = true;
            }

            if (!foundAnyAircraft)
            {
                MissingMemberLog.WarnOnce(
                    "ChicaneBayPylon.Waiting",
                    "[Chicane Symmetry] No AttackHelo1 WeaponManager found yet. Will retry when another WeaponManager awakens.");
                return false;
            }

            return fixedAny;
        }
    }



    // ========================================================================
    // EW-25 Medusa laser power draw patch.
    // ========================================================================
    [HarmonyPatch(typeof(WeaponManager), "Awake")]
    public static class MedusaLaserPatch
    {
        private static bool applied;

        public static void Prefix()
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
}