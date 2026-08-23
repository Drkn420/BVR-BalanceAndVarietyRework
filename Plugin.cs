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



namespace BalanceAndVarietyRework
{



    // ====================================================================================================
    // PLUGIN CORE
    // Central plugin class. Owns config binding, config seed export/import, version hashing, and Harmony registration.
    // ====================================================================================================



    [BepInPlugin("com.Draken0015.BVR", "Balance and Variety Rework", BaseVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string BaseVersion = "1.1.5";

        // Expose the dynamically generated version hash for multiplayer desync checks.
        public static string FullVersionWithHash { get; private set; }

        // Local display entry for the generated config hash. Intentionally not seed-exported.
        private ConfigEntry<string> configHashDisplay;

        // Cached reflection result for the seed system. This avoids repeated reflection passes.
        private static FieldInfo[] cachedSeedFields;

        // ================================================================================================
        // CONFIGURATION ENTRY DEFINITIONS
        // These static fields are the canonical config entries exposed to BepInEx.
        // Keep names stable: the config seed system uses field names as part of the serialized payload.
        // ================================================================================================

        // Important notice / seed display entries
        [SeedIgnore]
        public static ConfigEntry<string> ModVersionDisplay;

        [SeedIgnore]
        public static ConfigEntry<string> CurrentConfigSeed;

        [SeedIgnore]
        public static ConfigEntry<string> ImportConfigSeed;

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



        // ================================================================================================
        // UNITY / BEPINEX LIFECYCLE
        // ================================================================================================



        private void Awake()
        {
            // 1. Bind notices FIRST so they remain pinned at the top of ConfigManager.
            BindImportantNotices();

            // 2. Bind all functional configuration entries.
            BindFunctionalConfigs();

            // 3. Import a pending configuration seed before hash generation and before Harmony patches run.
            // This preserves the normal workflow: paste seed -> restart -> imported settings load.
            TryImportPendingConfigSeed();

            // 4. Generate the config hash/version and update the display entries.
            FinalizeVersionAndHash();

            // 5. Register all Harmony patches.
            RegisterHarmonyPatches();

            Logger.LogInfo("BVR - Balance and Variety Rework Mod Loaded!");
        }



        // ================================================================================================
        // CONFIG BINDING
        // ================================================================================================



        private void BindImportantNotices()
        {
            Config.Bind(
                "Important Notices",
                "Restart Required",
                "Changes made here require a full game restart to apply.",
                new ConfigDescription(
                    "Please restart the game after changing any settings for them to take effect.",
                    null,
                    new ConfigurationManagerAttributes { ReadOnly = true, HideDefaultButton = true, Order = 100 }));

            ModVersionDisplay = Config.Bind(
                "Important Notices",
                "Mod Version",
                $"v{BaseVersion}",
                new ConfigDescription(
                    "The currently installed version of Balance and Variety Rework.",
                    null,
                    new ConfigurationManagerAttributes { ReadOnly = true, HideDefaultButton = true, Order = 99 }));

            ModVersionDisplay.Value = $"v{BaseVersion}";

            configHashDisplay = Config.Bind(
                "Important Notices",
                "Current Config Hash",
                "Calculating...",
                new ConfigDescription(
                    "Compare this 6-character hash with other players to ensure your settings match for multiplayer. (Updates on game restart)",
                    null,
                    new ConfigurationManagerAttributes { ReadOnly = true, HideDefaultButton = true, Order = 98 }));

            CurrentConfigSeed = Config.Bind(
                "Important Notices",
                "Current Config Seed",
                "Calculating...",
                new ConfigDescription(
                    "Copy this seed to share your exact Balance and Variety Rework configuration with other players. Restart the game before copying this seed to make sure it accurately reflects your active settings.",
                    null,
                    new ConfigurationManagerAttributes { ReadOnly = true, HideDefaultButton = true, Order = 97 }));

            ImportConfigSeed = Config.Bind(
                "Important Notices",
                "Import Config Seed",
                "",
                new ConfigDescription(
                    "Paste a Balance and Variety Rework configuration seed here, then restart the game to import that configuration.",
                    null,
                    new ConfigurationManagerAttributes { HideDefaultButton = true, Order = 96 }));
        }



        private void BindFunctionalConfigs()
        {
            // Missile Balance Changes
            // IR Missiles Buff
            EnableIRMissilesBuff = Config.Bind("Missile Balance Changes", "Enable IR Missiles Buff", true, "Master toggle to enable the custom flare rejection and flare count multipliers.");
            FlareCountMultiplier = Config.Bind("Missile Balance Changes", "Flare Count Multiplier", 2.0f, "Multiplies the total number of flares on all aircraft (e.g., 2.0 = double flares, 0.5 = half flares).");
            FlareRejectionMultiplier = Config.Bind("Missile Balance Changes", "Flare Rejection Multiplier", 2.0f, "Multiplies the flare rejection stat on all IR missiles. Higher values make them harder to decoy (e.g., 2.0 = double rejection).");

            // R9 Lock Persistence Change
            EnableR9LockPersistenceBuff = Config.Bind("Missile Balance Changes", "Enable R9 Lock Persistence Buff", true, "Master toggle to enable the custom R9 lock persistence value.");
            R9LockPersistenceValue = Config.Bind("Missile Balance Changes", "R9 Lock Persistence Value", 3.0f, "Sets the lock persistence duration for the R9's SARH seeker, measured in seconds. Higher values keep the lock active for longer after the target successfully jams or is obscured. 600 effectively makes it relock infinitely.");

            // RAM45 Lock Persistence Change
            EnableRAM45LockPersistenceBuff = Config.Bind("Missile Balance Changes", "Enable RAM45 Lock Persistence Buff", true, "Master toggle to enable the custom RAM45 lock persistence value.");
            RAM45LockPersistenceValue = Config.Bind("Missile Balance Changes", "RAM45 Lock Persistence Value", 3.0f, "Sets the lock persistence duration for the RAM45's SARH seeker, measured in seconds. Higher values keep the lock active for longer after the target successfully jams or is obscured. 600 effectively makes it relock infinitely.");

            // R9 SARH Relock Change
            EnableR9SARHRelock = Config.Bind("Missile Balance Changes", "Enable R9 SARH Relock", true, "Master toggle to enable automatic R9 SARH relock attempts after lockPersistence allows the seeker to drop its target.");
            R9SARHRelockDelay = Config.Bind("Missile Balance Changes", "R9 SARH Relock Delay", 3.0f, "Seconds the R9 waits after it is left without a lock before attempting to relock. This timer starts after lockPersistence expires.");
            R9SARHRelockAttempts = Config.Bind("Missile Balance Changes", "R9 SARH Relock Attempts", 0, "Number of R9 relock attempts. 0 = infinite attempts.");

            // RAM45 SARH Relock Change
            EnableRAM45SARHRelock = Config.Bind("Missile Balance Changes", "Enable RAM45 SARH Relock", true, "Master toggle to enable automatic RAM45 SARH relock attempts after lockPersistence allows the seeker to drop its target.");
            RAM45SARHRelockDelay = Config.Bind("Missile Balance Changes", "RAM45 SARH Relock Delay", 3.0f, "Seconds the RAM45 waits after it is left without a lock before attempting to relock. This timer starts after lockPersistence expires.");
            RAM45SARHRelockAttempts = Config.Bind("Missile Balance Changes", "RAM45 SARH Relock Attempts", 0, "Number of RAM45 relock attempts. 0 = infinite attempts.");

            // Cricket Changes
            EnableCricketLynchpinx14Double = Config.Bind("CI-22 Cricket Changes", "Enable Cricket Lynchpin x14 Double", true, "Enables the AGR-18 Lynchpin x14 double rocket pod on the Cricket's hardpoint sets 2 and 3.");
            EnableCricketKingpinx8Double = Config.Bind("CI-22 Cricket Changes", "Enable Cricket Kingpin x8 Double", true, "Enables the AGR-24 Kingpin x8 double rocket pod on the Cricket's hardpoint sets 2 and 3.");

            // Compass Changes
            EnableCompassLynchpinx14Double = Config.Bind("T/A-30 Compass Changes", "Enable Compass Lynchpin x14 Double", true, "Enables the AGR-18 Lynchpin x14 double rocket pod on the Compass's hardpoint set 1.");
            EnableCompassKingpinx8Double = Config.Bind("T/A-30 Compass Changes", "Enable Compass Kingpin x8 Double", true, "Enables the AGR-24 Kingpin x8 double rocket pod on the Compass's hardpoint set 1.");

            // Vagrant Changes
            EnableVagrantLynchpinx14Double = Config.Bind("VT-7 Vagrant Changes", "Enable Vagrant Lynchpin x14 Double", true, "Enables the AGR-18 Lynchpin x14 double rocket pod on the Vagrant's hardpoint set 3.");
            EnableVagrantKingpinx8Double = Config.Bind("VT-7 Vagrant Changes", "Enable Vagrant Kingpin x8 Double", true, "Enables the AGR-24 Kingpin x8 double rocket pod on the Vagrant's hardpoint set 3.");

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
        }



        private void TryImportPendingConfigSeed()
        {
            if (string.IsNullOrWhiteSpace(ImportConfigSeed.Value))
                return;

            string pendingSeed = ImportConfigSeed.Value;

            if (TryImportConfigSeed(pendingSeed))
            {
                Logger.LogInfo("BVR - Configuration seed imported successfully during startup.");
            }
            else
            {
                Logger.LogWarning("BVR - Configuration seed import failed during startup. The seed was invalid or from an incompatible mod version.");
            }

            // Consume the import seed so it does not try to apply again on every launch.
            ImportConfigSeed.Value = string.Empty;
        }



        private void FinalizeVersionAndHash()
        {
            string configHash = GenerateConfigHash();
            FullVersionWithHash = $"{BaseVersion}-{configHash}";

            configHashDisplay.Value = configHash;
            CurrentConfigSeed.Value = GenerateConfigSeed();

            Config.Save();
            Logger.LogInfo($"Mod Version Loaded: {FullVersionWithHash}");
        }



        private void RegisterHarmonyPatches()
        {
            // Keep this list explicit. It documents every active patch class and avoids accidental assembly-wide patching.
            Type[] patchTypes =
            {
                // Missile balance changes
                typeof(StatsPatch),
                typeof(SARHLockPersistencePatch),
                typeof(SARHRelockPatch),

                // Cricket changes
                typeof(CricketLynchpinx14DoublePatch),
                typeof(CricketKingpinx8DoublePatch),

                // Compass changes
                typeof(CompassLynchpinx14DoublePatch),
                typeof(CompassKingpinx8DoublePatch),

                // Vagrant changes
                typeof(VagrantLynchpinx14DoublePatch),
                typeof(VagrantKingpinx8DoublePatch),

                // Ibis changes
                typeof(IbisLynchpinx14DoublePatch),
                typeof(IbisKingpinx8DoublePatch),

                // Chicane changes
                typeof(ProxyGunPatch),
                typeof(ChicaneScythePatch),
                typeof(ChicaneInternalLynchpinx14Patch),
                typeof(ChicaneInternalKingpinx8Patch),
                typeof(ChicaneBayPylonSymmetryFixPatch),

                // Revoker changes
                typeof(RevokerLynchpinx14DoublePatch),
                typeof(RevokerKingpinx8DoublePatch),

                // Vortex changes
                typeof(VortexLynchpinx14DoublePatch),
                typeof(VortexKingpinx8DoublePatch),

                // Tarantula changes
                typeof(TarantulaLynchpinx14DoublePatch),
                typeof(TarantulaKingpinx8DoublePatch),

                // Ifrit changes
                typeof(IfritLynchpinx14DoublePatch),
                typeof(IfritKingpinx8DoublePatch),

                // Medusa changes
                typeof(MedusaLaserPatch),
                typeof(MedusaLynchpinx14DoublePatch),
                typeof(MedusaKingpinx8DoublePatch),
                typeof(MedusaSAMRadar2SinglePatch),
                typeof(MedusaSAMRadar2DoublePatch)
            };

            foreach (Type patchType in patchTypes)
            {
                Harmony.CreateAndPatchAll(patchType);
            }
        }



        // ================================================================================================
        // CONFIG SEED EXPORT / IMPORT
        // Seed format:
        // BVR1-<url-safe base64 payload>
        // Payload:
        // BVRSEED|<mod version>|<FieldName>:<type>:<escaped value>|...
        // ================================================================================================



        private const string SeedFormatPrefix = "BVR1";
        private const string SeedPayloadPrefix = "BVRSEED";



        private string GenerateConfigHash()
        {
            string combinedConfigs = GenerateSeedPayload(false);

            using (var md5 = MD5.Create())
            {
                byte[] hashBytes = md5.ComputeHash(Encoding.UTF8.GetBytes(combinedConfigs));
                return BitConverter.ToString(hashBytes).Replace("-", "").Substring(0, 6);
            }
        }



        private string GenerateConfigSeed()
        {
            return $"{SeedFormatPrefix}-{ToUrlSafeBase64(GenerateSeedPayload(true))}";
        }



        private string GenerateSeedPayload(bool includeSeedMetadata)
        {
            StringBuilder payload = new StringBuilder();

            if (includeSeedMetadata)
                payload.Append(SeedPayloadPrefix).Append('|').Append(BaseVersion);
            else
                payload.Append("BVRHASH");

            foreach (FieldInfo field in GetSeedConfigEntryFields())
            {
                object entry = field.GetValue(null);
                if (entry == null)
                    continue;

                PropertyInfo valueProperty = field.FieldType.GetProperty("Value");
                if (valueProperty == null || !valueProperty.CanRead)
                    continue;

                object value = valueProperty.GetValue(entry);
                Type valueType = field.FieldType.GetGenericArguments()[0];

                payload.Append('|')
                    .Append(field.Name)
                    .Append(':')
                    .Append(GetTypeKey(valueType))
                    .Append(':')
                    .Append(Uri.EscapeDataString(ConvertValueToString(value, valueType)));
            }

            return payload.ToString();
        }



        private bool TryImportConfigSeed(string seed)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(seed))
                    return false;

                seed = seed.Trim();

                if (seed.StartsWith(SeedFormatPrefix + "-", StringComparison.OrdinalIgnoreCase))
                    seed = seed.Substring(SeedFormatPrefix.Length + 1);

                string payload = FromUrlSafeBase64(seed);
                if (string.IsNullOrEmpty(payload))
                    return false;

                string[] parts = payload.Split('|');
                if (parts.Length < 2 || parts[0] != SeedPayloadPrefix)
                    return false;

                if (parts[1] != BaseVersion)
                {
                    Logger.LogWarning($"BVR - Importing configuration seed from version {parts[1]}. Current version is {BaseVersion}.");
                }

                Dictionary<string, FieldInfo> seedFields = GetSeedConfigEntryFields()
                    .ToDictionary(f => f.Name, StringComparer.Ordinal);

                int importedCount = 0;

                for (int i = 2; i < parts.Length; i++)
                {
                    string[] entryParts = parts[i].Split(':');
                    if (entryParts.Length != 3)
                        continue;

                    if (!seedFields.TryGetValue(entryParts[0], out FieldInfo field))
                        continue;

                    try
                    {
                        object entry = field.GetValue(null);
                        if (entry == null)
                            continue;

                        PropertyInfo valueProperty = field.FieldType.GetProperty("Value");
                        if (valueProperty == null || !valueProperty.CanWrite)
                            continue;

                        Type targetType = field.FieldType.GetGenericArguments()[0];
                        string rawValue = Uri.UnescapeDataString(entryParts[2]);
                        object newValue = ConvertStringToValue(rawValue, targetType);

                        valueProperty.SetValue(entry, newValue);
                        importedCount++;
                    }
                    catch (Exception fieldEx)
                    {
                        Logger.LogWarning($"BVR - Could not import config seed field '{entryParts[0]}': {fieldEx.Message}");
                    }
                }

                if (importedCount <= 0)
                    return false;

                Config.Save();
                return true;
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"BVR - Config seed parse error: {ex.Message}");
                return false;
            }
        }



        private static FieldInfo[] GetSeedConfigEntryFields()
        {
            // Cache the reflected field list. Config entry fields are static and do not change at runtime.
            if (cachedSeedFields == null)
            {
                cachedSeedFields = typeof(Plugin)
                    .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                    .Where(f => f.FieldType.IsGenericType && f.FieldType.GetGenericTypeDefinition() == typeof(ConfigEntry<>))
                    .Where(f => !f.IsDefined(typeof(SeedIgnoreAttribute), false))
                    .OrderBy(f => f.Name, StringComparer.Ordinal)
                    .ToArray();
            }

            return cachedSeedFields;
        }



        private static string GetTypeKey(Type type)
        {
            if (type == typeof(bool)) return "bool";
            if (type == typeof(int)) return "int";
            if (type == typeof(float)) return "float";
            if (type == typeof(double)) return "double";
            if (type == typeof(long)) return "long";
            if (type == typeof(string)) return "string";
            if (type.IsEnum) return "enum";

            return type.Name.ToLowerInvariant();
        }



        private static string ConvertValueToString(object value, Type targetType)
        {
            if (value == null)
                return string.Empty;

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

            if (value is Enum)
                return value.ToString();

            return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        }



        private static object ConvertStringToValue(string raw, Type targetType)
        {
            Type underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;

            if (underlying == typeof(string))
                return raw ?? string.Empty;

            if (string.IsNullOrEmpty(raw))
            {
                if (underlying.IsValueType)
                    return Activator.CreateInstance(underlying);

                return null;
            }

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

            if (underlying.IsEnum)
                return Enum.Parse(underlying, raw, true);

            return Convert.ChangeType(raw, underlying, CultureInfo.InvariantCulture);
        }



        private static string ToUrlSafeBase64(string text)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);

            return Convert.ToBase64String(bytes, Base64FormattingOptions.None)
                .Replace('+', '-')
                .Replace('/', '_')
                .TrimEnd('=');
        }



        private static string FromUrlSafeBase64(string data)
        {
            if (string.IsNullOrWhiteSpace(data))
                return null;

            data = new string(data.Trim().Where(c => !char.IsWhiteSpace(c)).ToArray());
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



    // ====================================================================================================
    // SHARED SUPPORT TYPES
    // Small shared attributes and helper classes used across the mod.
    // ====================================================================================================



    // Marks ConfigEntry fields that should not be exported/imported by the config seed system.
    [AttributeUsage(AttributeTargets.Field)]
    internal sealed class SeedIgnoreAttribute : Attribute
    {
    }



    // BepInEx ConfigurationManager attributes (duck-typed).
    // This type is intentionally internal and field-based. Configuration Manager reads it by convention.
#pragma warning disable CS0169, CS0414, CS0649
    internal sealed class ConfigurationManagerAttributes
    {
        public bool? ReadOnly;
        public bool? HideDefaultButton;
        public int? Order;
    }
#pragma warning restore CS0169, CS0414, CS0649



    // Marker component used to prevent double-modification of prefab/component stats.
    public class ModifiedStatsFlag : MonoBehaviour
    {
    }



    // Shared name helpers. Nuclear Option object names often include "(Clone)" at runtime.
    internal static class ObjectNameUtility
    {
        public static string RemoveCloneSuffix(string name)
        {
            if (string.IsNullOrEmpty(name))
                return string.Empty;

            return name.Replace("(Clone)", string.Empty);
        }

        public static string GetCleanRootName(GameObject obj)
        {
            if (obj == null)
                return string.Empty;

            Transform root = obj.transform != null ? obj.transform.root : null;
            GameObject rootObject = root != null ? root.gameObject : obj;

            return RemoveCloneSuffix(rootObject != null ? rootObject.name : string.Empty);
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

                // HideAndDontSave completely hides the vault from RUE and the scene hierarchy.
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

        // Info assets are loaded once and cached. They should not change during a game session.
        private static readonly Dictionary<string, UnityEngine.Object> infoAssetCache = new Dictionary<string, UnityEngine.Object>();



        // ================================================================================================
        // PUBLIC MOUNT FACTORIES
        // ================================================================================================



        public static WeaponMount GetExternalSAMRadar2Single()
        {
            if (externalSAMRadar2SingleMount != null)
                return externalSAMRadar2SingleMount;

            WeaponMount sourceMount = FindSourceMount("AAM4_single", "AAM4_single", null);
            if (sourceMount == null)
                return null;

            GameObject singlePrefab = ClonePrefabToVault(sourceMount.prefab, "SAM_Radar2_single");

            GameObject originalSamPrefab = FindOriginalSamRadar2Prefab();
            if (originalSamPrefab != null)
            {
                Transform pylon = singlePrefab.transform.Find("pylon");
                Transform missileChild = pylon != null ? pylon.Find("aam4") : null;

                if (missileChild != null)
                {
                    missileChild.name = "sam_radar2";
                    SwapMissileVisualsAndCollider(missileChild, originalSamPrefab, false, false);
                }
            }
            else
            {
                Debug.LogWarning("[ExternalSAMRadar2Single] Could not find original 'SAM_Radar2' prefab in resources to copy mesh and collider!");
            }

            AssignWeaponInfo(singlePrefab, "info_SAM_Radar2", "MountedMissile");

            externalSAMRadar2SingleMount = CreateConfiguredMount(sourceMount, singlePrefab, "R9 Stratolance x1", "SAM_Radar2_single");
            Debug.Log("[ExternalSAMRadar2Single] Custom R9 Stratolance x1 prefab and mount generation complete!");

            return externalSAMRadar2SingleMount;
        }



        public static WeaponMount GetExternalSAMRadar2Double()
        {
            if (externalSAMRadar2DoubleMount != null)
                return externalSAMRadar2DoubleMount;

            WeaponMount sourceMount = FindSourceMount("ARM1_double", "ARM1_double", null);
            if (sourceMount == null)
                return null;

            GameObject doublePrefab = ClonePrefabToVault(sourceMount.prefab, "SAM_Radar2_double");

            GameObject originalSamPrefab = FindOriginalSamRadar2Prefab();
            if (originalSamPrefab != null)
            {
                int swappedMissiles = 0;
                Transform[] allChildren = doublePrefab.GetComponentsInChildren<Transform>(true);

                foreach (Transform child in allChildren)
                {
                    if (child == null || child == doublePrefab.transform)
                        continue;

                    // Only process the two direct pylons. This intentionally ignores "pylon (1)".
                    if (child.parent != doublePrefab.transform)
                        continue;

                    if (child.name != "pylon")
                        continue;

                    Transform missileChild = child.Find("ARM1");
                    if (missileChild == null)
                        continue;

                    missileChild.name = "sam_radar2";
                    SwapMissileVisualsAndCollider(missileChild, originalSamPrefab, true, true);
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

            AssignWeaponInfo(doublePrefab, "info_SAM_Radar2", "MountedMissile");

            externalSAMRadar2DoubleMount = CreateConfiguredMount(sourceMount, doublePrefab, "R9 Stratolance x2", "SAM_Radar2_double");
            Debug.Log("[ExternalSAMRadar2Double] Custom R9 Stratolance x2 prefab and mount generation complete!");

            return externalSAMRadar2DoubleMount;
        }



        public static WeaponMount GetExternalLynchpinx14Double()
        {
            if (externalLynchpinx14DoubleMount != null)
                return externalLynchpinx14DoubleMount;

            WeaponMount sourceMount = FindSourceMount(null, "RocketPod1_single", null);
            if (sourceMount == null)
                return null;

            GameObject doublePrefab = ClonePrefabToVault(sourceMount.prefab, "RocketPod1_double");
            doublePrefab.transform.localPosition = Vector3.zero;

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

            externalLynchpinx14DoubleMount = CreateConfiguredMount(sourceMount, doublePrefab, "AGR-18 Lynchpin x14", "RocketPod1_double");
            Debug.Log("[ExternalLynchpinx14Double] Custom double Lynchpin prefab and mount generation complete!");

            return externalLynchpinx14DoubleMount;
        }



        public static WeaponMount GetExternalKingpinx8Double()
        {
            if (externalKingpinx8DoubleMount != null)
                return externalKingpinx8DoubleMount;

            WeaponMount sourceMount = FindSourceMount(null, "Rocket2_4Pod", "Rocket2_4Pod");
            if (sourceMount == null)
                return null;

            GameObject doublePrefab = ClonePrefabToVault(sourceMount.prefab, "Rocket2_4Podx2");
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

            // Reposition the pylon child to correct alignment.
            Transform pylon = doublePrefab.transform.Find("pylon");
            if (pylon != null)
            {
                pylon.localPosition = new Vector3(0f, 0.038f, 0f);
            }

            externalKingpinx8DoubleMount = CreateConfiguredMount(sourceMount, doublePrefab, "AGR-24 Kingpin x8", "Rocket2_4Podx2");
            Debug.Log("[ExternalKingpinx8Double] Custom double Kingpin prefab and mount generation complete!");

            return externalKingpinx8DoubleMount;
        }



        public static WeaponMount GetInternalLynchpinx14Double()
        {
            if (internalLynchpinx14DoubleMount != null)
                return internalLynchpinx14DoubleMount;

            WeaponMount sourceMount = FindSourceMount(null, "RocketPod1_single", null);
            if (sourceMount == null)
                return null;

            GameObject doublePrefab = ClonePrefabToVault(sourceMount.prefab, "RocketPod1_double_internal");
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

            // Force MountedMissile.railDelay on all rockets inside both pods.
            // This affects the prefab itself, so later spawned clones inherit the modified value.
            SetMountedMissileRailDelay(doublePrefab, InternalLynchpinRailDelay);

            internalLynchpinx14DoubleMount = CreateConfiguredMount(sourceMount, doublePrefab, "AGR-18 Lynchpin x14", "RocketPod1_double_internal");
            EnableMissileBay(internalLynchpinx14DoubleMount);

            Debug.Log("[InternalLynchpinx14Double] Custom internal double Lynchpin prefab and mount generation complete!");

            return internalLynchpinx14DoubleMount;
        }



        public static WeaponMount GetInternalKingpinx8Double()
        {
            if (internalKingpinx8DoubleMount != null)
                return internalKingpinx8DoubleMount;

            WeaponMount sourceMount = FindSourceMount(null, "Rocket2_4Pod", "Rocket2_4Pod");
            if (sourceMount == null)
                return null;

            GameObject doublePrefab = ClonePrefabToVault(sourceMount.prefab, "Rocket2_4Podx2_internal");
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

            // Reposition the pylon child to correct internal bay alignment.
            Transform pylon = doublePrefab.transform.Find("pylon");
            if (pylon != null)
            {
                pylon.localPosition = new Vector3(0f, 0.038f, 0f);
            }

            // Force MountedMissile.railDelay on all rockets inside both pods.
            // The Kingpin pod contains rocket1 through rocket4, but this sweep catches all MountedMissile components automatically.
            SetMountedMissileRailDelay(doublePrefab, InternalKingpinRailDelay);

            internalKingpinx8DoubleMount = CreateConfiguredMount(sourceMount, doublePrefab, "AGR-24 Kingpin x8", "Rocket2_4Podx2_internal");
            EnableMissileBay(internalKingpinx8DoubleMount);

            Debug.Log("[InternalKingpinx8Double] Custom internal double Kingpin prefab and mount generation complete!");

            return internalKingpinx8DoubleMount;
        }



        // ================================================================================================
        // MOUNT / PREFAB CREATION HELPERS
        // ================================================================================================



        private static WeaponMount FindSourceMount(string jsonKey, string name, string fallbackPrefabName)
        {
            WeaponMount[] allMounts = Resources.FindObjectsOfTypeAll<WeaponMount>();
            WeaponMount mount = null;

            if (!string.IsNullOrEmpty(jsonKey))
                mount = allMounts.FirstOrDefault(m => m != null && m.jsonKey == jsonKey);

            if (mount == null && !string.IsNullOrEmpty(name))
                mount = allMounts.FirstOrDefault(m => m != null && m.name == name);

            if (mount == null && !string.IsNullOrEmpty(fallbackPrefabName))
                mount = allMounts.FirstOrDefault(m => m != null && m.prefab != null && m.prefab.name == fallbackPrefabName);

            return mount != null && mount.prefab != null ? mount : null;
        }



        private static GameObject ClonePrefabToVault(GameObject sourcePrefab, string prefabName)
        {
            if (sourcePrefab == null)
                return null;

            // activeSelf = true, but activeInHierarchy = false because the vault root is inactive.
            // This keeps spawned weapons visible while preventing the prefab from floating in the world.
            GameObject prefab = UnityEngine.Object.Instantiate(sourcePrefab, PrefabVault.Get().transform);
            prefab.name = prefabName;
            prefab.SetActive(true);

            return prefab;
        }



        private static WeaponMount CreateConfiguredMount(WeaponMount sourceMount, GameObject prefab, string mountName, string jsonKey)
        {
            if (sourceMount == null || prefab == null)
                return null;

            WeaponMount newMount = UnityEngine.Object.Instantiate(sourceMount);
            newMount.name = jsonKey;
            newMount.prefab = prefab;

            Traverse traverseMount = Traverse.Create(newMount);
            SetMountField(traverseMount, "mountName", mountName);
            SetMountField(traverseMount, "jsonKey", jsonKey);
            SetNetworkLookupIndex(traverseMount, jsonKey);

            return newMount;
        }



        private static void SetMountField(Traverse traverseMount, string fieldName, string value)
        {
            Traverse field = traverseMount.Field(fieldName);

            if (field.FieldExists())
                field.SetValue(value);
        }



        private static void EnableMissileBay(WeaponMount mount)
        {
            if (mount == null)
                return;

            Traverse traverse = Traverse.Create(mount);

            Traverse field = traverse.Field("missileBay");
            if (field.FieldExists())
            {
                field.SetValue(true);
                return;
            }

            Traverse property = traverse.Property("missileBay");
            if (property.PropertyExists())
            {
                property.SetValue(true);
            }
        }



        private static void SetNetworkLookupIndex(Traverse traverseMount, string networkKey)
        {
            // Fix for Network Lookup Index conflict destroying the ghost duplicate.
            // A deterministic hash is used so all players generate the same lookup ID for the same key.
            int customNetworkId = GetStablePositiveHash(networkKey);

            Traverse backingField = traverseMount.Field("<INetworkDefinition.LookupIndex>k__BackingField");
            if (backingField.FieldExists())
                backingField.SetValue(customNetworkId);

            Traverse interfaceProperty = traverseMount.Property("INetworkDefinition.LookupIndex");
            if (interfaceProperty.PropertyExists())
                interfaceProperty.SetValue(customNetworkId);
        }



        private static int GetStablePositiveHash(string text)
        {
            unchecked
            {
                uint hash = 2166136261;

                if (!string.IsNullOrEmpty(text))
                {
                    foreach (char c in text)
                    {
                        hash ^= c;
                        hash *= 16777619;
                    }
                }

                return (int)(hash & 0x7fffffff);
            }
        }



        private static GameObject FindOriginalSamRadar2Prefab()
        {
            GameObject[] allGameObjects = Resources.FindObjectsOfTypeAll<GameObject>();

            return allGameObjects.FirstOrDefault(go =>
                go != null &&
                go.name == "SAM_Radar2" &&
                go.transform.parent == null);
        }



        private static void SwapMissileVisualsAndCollider(Transform missileChild, GameObject originalSamPrefab, bool addColliderIfMissing, bool copyColliderTrigger)
        {
            if (missileChild == null || originalSamPrefab == null)
                return;

            // Swap MeshFilter (the 3D geometry).
            MeshFilter origMf = originalSamPrefab.GetComponent<MeshFilter>();
            MeshFilter newMf = missileChild.GetComponent<MeshFilter>();
            if (origMf != null && newMf != null)
                newMf.sharedMesh = origMf.sharedMesh;

            // Swap MeshRenderer (textures/materials).
            MeshRenderer origMr = originalSamPrefab.GetComponent<MeshRenderer>();
            MeshRenderer newMr = missileChild.GetComponent<MeshRenderer>();
            if (origMr != null && newMr != null)
                newMr.sharedMaterials = origMr.sharedMaterials;

            // Neutralize LODGroup.
            // If left enabled, LODs can continue rendering the original missile meshes at some distances
            // because internal renderer references have not been updated.
            LODGroup newLod = missileChild.GetComponent<LODGroup>();
            if (newLod != null)
                newLod.enabled = false;

            // Swap CapsuleCollider (physical hitbox).
            CapsuleCollider origCol = originalSamPrefab.GetComponent<CapsuleCollider>();
            if (origCol == null)
                return;

            CapsuleCollider newCol = missileChild.GetComponent<CapsuleCollider>();
            if (newCol == null)
            {
                if (!addColliderIfMissing)
                    return;

                newCol = missileChild.gameObject.AddComponent<CapsuleCollider>();
            }

            if (newCol == null)
                return;

            newCol.center = origCol.center;
            newCol.radius = origCol.radius;
            newCol.height = origCol.height;
            newCol.direction = origCol.direction;

            if (copyColliderTrigger)
                newCol.isTrigger = origCol.isTrigger;
        }



        private static void AssignWeaponInfo(GameObject prefabRoot, string infoAssetName, string componentName)
        {
            if (prefabRoot == null)
                return;

            UnityEngine.Object infoAsset = GetCachedInfoAsset(infoAssetName);
            if (infoAsset == null)
            {
                Debug.LogWarning($"[CustomWeaponsReusedAssets] Could not find info asset '{infoAssetName}'.");
                return;
            }

            foreach (Component comp in prefabRoot.GetComponentsInChildren<Component>(true))
            {
                if (comp == null)
                    continue;

                Type type = comp.GetType();
                if (type == null || type.Name != componentName)
                    continue;

                Traverse traverseComp = Traverse.Create(comp);

                Traverse infoField = traverseComp.Field("info");
                if (infoField.FieldExists())
                {
                    infoField.SetValue(infoAsset);
                    continue;
                }

                Traverse infoProperty = traverseComp.Property("info");
                if (infoProperty.PropertyExists())
                    infoProperty.SetValue(infoAsset);
            }
        }



        private static UnityEngine.Object GetCachedInfoAsset(string assetName)
        {
            if (string.IsNullOrEmpty(assetName))
                return null;

            UnityEngine.Object cached;
            if (infoAssetCache.TryGetValue(assetName, out cached))
                return cached;

            UnityEngine.Object asset = Resources.FindObjectsOfTypeAll<UnityEngine.Object>()
                .FirstOrDefault(o => o != null && o.name == assetName);

            infoAssetCache[assetName] = asset;
            return asset;
        }



        private static void SetMountedMissileRailDelay(GameObject prefabRoot, float delay)
        {
            if (prefabRoot == null)
                return;

            int patchedCount = 0;

            // Scan the entire custom prefab. This catches both pods and all rocket children:
            // RocketPod1_double_internal/pod/rocket1 ... rocket7
            // Rocket2_4Podx2_internal/pod/rocket1 ... rocket4
            foreach (Component component in prefabRoot.GetComponentsInChildren<Component>(true))
            {
                if (component == null)
                    continue;

                Type type = component.GetType();
                if (type == null || type.Name != "MountedMissile")
                    continue;

                if (TrySetRailDelay(component, delay))
                    patchedCount++;
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
            Traverse traverse = Traverse.Create(target);

            Traverse field = traverse.Field("railDelay");
            if (field.FieldExists())
            {
                field.SetValue(delay);
                return true;
            }

            Traverse property = traverse.Property("railDelay");
            if (property.PropertyExists())
            {
                property.SetValue(delay);
                return true;
            }

            return false;
        }



        // ================================================================================================
        // CLONED POD LIVERY FIX / COLORABLE MOUNT MERGE
        // Ensures duplicated pod renderers are included in the root ColorableMount so liveries apply correctly.
        // ================================================================================================



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

            Traverse rootTraverse = Traverse.Create(rootMount);
            Traverse colorField = rootTraverse.Field("colorableRenderers");
            Traverse skinField = rootTraverse.Field("skinnableRenderers");

            if (!colorField.FieldExists() || !skinField.FieldExists())
            {
                Debug.LogWarning($"[CustomWeaponsReusedAssets] Could not access ColorableMount renderer arrays on {prefabRoot.name}.");
                return;
            }

            List<Renderer> colorList = new List<Renderer>(colorField.GetValue<Renderer[]>() ?? new Renderer[0]);
            List<Renderer> skinList = new List<Renderer>(skinField.GetValue<Renderer[]>() ?? new Renderer[0]);

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

            Traverse rootTraverse = Traverse.Create(rootMount);
            Traverse rootColorField = rootTraverse.Field("colorableRenderers");
            Traverse rootSkinField = rootTraverse.Field("skinnableRenderers");

            if (!rootColorField.FieldExists() || !rootSkinField.FieldExists())
                return rootMount;

            List<Renderer> colorList = new List<Renderer>(rootColorField.GetValue<Renderer[]>() ?? new Renderer[0]);
            List<Renderer> skinList = new List<Renderer>(rootSkinField.GetValue<Renderer[]>() ?? new Renderer[0]);

            foreach (ColorableMount mount in existingMounts)
            {
                if (mount == null || mount == rootMount)
                    continue;

                Traverse mountTraverse = Traverse.Create(mount);

                Traverse childColorField = mountTraverse.Field("colorableRenderers");
                if (childColorField.FieldExists())
                {
                    Renderer[] childColors = childColorField.GetValue<Renderer[]>() ?? new Renderer[0];
                    foreach (Renderer r in childColors)
                        AddUniqueRenderer(colorList, r);
                }

                Traverse childSkinField = mountTraverse.Field("skinnableRenderers");
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
    }



    // ====================================================================================================
    // SHARED HARDPOINT INJECTION
    // Common helper for adding WeaponMounts to aircraft hardpoint sets.
    // This removes repeated Resource scans and duplicated hardpoint validation logic from every patch.
    // ====================================================================================================



    internal static class HardpointInjection
    {
        // The injection only occurs if ALL requested hardpoint set indices exist on the target WeaponManager.
        // This preserves the original behavior of checks like "hardpointSets.Length > 3" for sets 2 and 3.
        public static bool InjectWeaponMount(
            string rootNameContains,
            WeaponMount mount,
            int[] hardpointSets,
            string logTag,
            string weaponDescription,
            string hardpointDescription,
            string excludeRootContains)
        {
            if (string.IsNullOrEmpty(rootNameContains) || mount == null || hardpointSets == null || hardpointSets.Length == 0)
                return false;

            bool injectedAny = false;
            int maxRequestedSet = hardpointSets.Max();

            WeaponManager[] allWeaponManagers = Resources.FindObjectsOfTypeAll<WeaponManager>();

            foreach (WeaponManager wm in allWeaponManagers)
            {
                if (wm == null || wm.transform == null || wm.transform.root == null)
                    continue;

                string rootName = wm.transform.root.name;
                if (!rootName.Contains(rootNameContains))
                    continue;

                if (!string.IsNullOrEmpty(excludeRootContains) && rootName.Contains(excludeRootContains))
                    continue;

                if (wm.hardpointSets == null || wm.hardpointSets.Length <= maxRequestedSet)
                    continue;

                bool updated = false;

                foreach (int setIndex in hardpointSets)
                {
                    if (setIndex < 0 || setIndex >= wm.hardpointSets.Length)
                        continue;

                    var hardpointSet = wm.hardpointSets[setIndex];
                    if (hardpointSet == null || hardpointSet.weaponOptions == null)
                        continue;

                    if (!hardpointSet.weaponOptions.Contains(mount))
                    {
                        hardpointSet.weaponOptions.Add(mount);
                        updated = true;
                    }
                }

                if (updated)
                {
                    Debug.Log($"[{logTag}] Successfully injected {weaponDescription} into {wm.gameObject.name} {hardpointDescription}.");
                    injectedAny = true;
                }
            }

            return injectedAny;
        }
    }



    // ====================================================================================================
    // MISSILE BALANCE CHANGES
    // Config Category: Missile Balance Changes
    // ====================================================================================================



    // ====================================================================================================
    // IR MISSILES BUFF
    // ====================================================================================================



    [HarmonyPatch(typeof(WeaponManager), "Awake")]
    public static class StatsPatch
    {
        private static bool hasSweptStats = false;

        public static void Prefix()
        {
            if (!Plugin.EnableIRMissilesBuff.Value)
                return;

            if (hasSweptStats)
                return;

            FlareEjector[] allFlares = Resources.FindObjectsOfTypeAll<FlareEjector>();
            foreach (FlareEjector flare in allFlares)
            {
                ApplyFlareMultiplier(flare);
            }

            IRSeeker[] allSeekers = Resources.FindObjectsOfTypeAll<IRSeeker>();
            foreach (IRSeeker seeker in allSeekers)
            {
                ApplySeekerMultiplier(seeker);
            }

            hasSweptStats = true;
            Debug.Log("[IRMissileBuff] Successfully swept and multiplied all FlareEjector and IRSeeker blueprints!");
        }

        private static void ApplyFlareMultiplier(FlareEjector flare)
        {
            if (flare == null || flare.GetComponent<ModifiedStatsFlag>() != null)
                return;

            Traverse traverse = Traverse.Create(flare);
            Traverse maxAmmoField = traverse.Field("maxAmmo");
            Traverse ammoField = traverse.Field("ammo");

            if (!maxAmmoField.FieldExists() || !ammoField.FieldExists())
                return;

            int currentMax = maxAmmoField.GetValue<int>();
            int currentAmmo = ammoField.GetValue<int>();
            float multiplier = Plugin.FlareCountMultiplier.Value;

            maxAmmoField.SetValue(Mathf.RoundToInt(currentMax * multiplier));
            ammoField.SetValue(Mathf.RoundToInt(currentAmmo * multiplier));

            flare.gameObject.AddComponent<ModifiedStatsFlag>();
        }

        private static void ApplySeekerMultiplier(IRSeeker seeker)
        {
            if (seeker == null || seeker.GetComponent<ModifiedStatsFlag>() != null)
                return;

            Traverse traverse = Traverse.Create(seeker);
            Traverse rejectionField = traverse.Field("flareRejection");

            if (!rejectionField.FieldExists())
                return;

            float currentRejection = rejectionField.GetValue<float>();
            float multiplier = Plugin.FlareRejectionMultiplier.Value;

            rejectionField.SetValue(currentRejection * multiplier);
            seeker.gameObject.AddComponent<ModifiedStatsFlag>();
        }
    }



    // ====================================================================================================
    // SARH LOCK PERSISTENCE
    // ====================================================================================================



    [HarmonyPatch(typeof(WeaponManager), "Awake")]
    public static class SARHLockPersistencePatch
    {
        private static bool hasPatchedR9LockPersistence = false;
        private static bool hasPatchedRAM45LockPersistence = false;

        public static void Prefix()
        {
            if (!Plugin.EnableR9LockPersistenceBuff.Value && !Plugin.EnableRAM45LockPersistenceBuff.Value)
                return;

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

            // Searching directly for SARHSeeker components is faster than scanning every GameObject and every component.
            SARHSeeker[] seekers = Resources.FindObjectsOfTypeAll<SARHSeeker>();

            foreach (SARHSeeker seeker in seekers)
            {
                if (seeker == null)
                    continue;

                // Handles both prefab assets and scene instances.
                if (!ObjectNameUtility.IsUnderNamedObject(seeker.gameObject, targetName))
                    continue;

                if (seeker.GetComponent<ModifiedStatsFlag>() != null)
                {
                    success = true;
                    continue;
                }

                if (TrySetLockPersistence(seeker, lockPersistence))
                {
                    seeker.gameObject.AddComponent<ModifiedStatsFlag>();
                    success = true;
                    Debug.Log($"[{logTag}] Successfully set lockPersistence={lockPersistence} on {seeker.gameObject.name}.");
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
            Traverse traverse = Traverse.Create(target);

            // Try field first.
            Traverse field = traverse.Field("lockPersistence");
            if (field.FieldExists())
            {
                field.SetValue(value);
                return true;
            }

            // Fallback if lockPersistence is exposed as a property.
            Traverse property = traverse.Property("lockPersistence");
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
    // ====================================================================================================



    public class SARHRelockController : MonoBehaviour
    {
        private SARHSeeker seeker;
        private Traverse seekerTraverse;

        // Cached Traverse member accessors. This avoids repeated field/property lookups inside Update.
        private Traverse targetTransformField;
        private Traverse targetUnitField;
        private Traverse timeWithoutTrackField;
        private Traverse lastTrackingCheckField;
        private Traverse jamAccumulationField;
        private Traverse jamToleranceField;
        private Traverse missileField;

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
            relockDelay = Mathf.Max(0f, delay);
            maxAttempts = Mathf.Max(0, attempts);
            attemptsUsed = 0;
            waitingForRelock = false;
            relockTimer = 0f;
            initialized = false;
            cachedMissile = null;

            if (seeker != null)
            {
                seekerTraverse = Traverse.Create(seeker);
                InitializeFieldAccessors();
            }
        }

        private void InitializeFieldAccessors()
        {
            targetTransformField = CreateFieldOrProperty("targetTransform");
            targetUnitField = CreateFieldOrProperty("targetUnit");
            timeWithoutTrackField = CreateFieldOrProperty("timeWithoutTrack");
            lastTrackingCheckField = CreateFieldOrProperty("lastTrackingCheck");
            jamAccumulationField = CreateFieldOrProperty("jamAccumulation");
            jamToleranceField = CreateFieldOrProperty("jamTolerance");
            missileField = CreateFieldOrProperty("missile");
        }

        private Traverse CreateFieldOrProperty(string memberName)
        {
            if (seekerTraverse == null)
                return null;

            Traverse field = seekerTraverse.Field(memberName);
            if (field.FieldExists())
                return field;

            Traverse property = seekerTraverse.Property(memberName);
            if (property.PropertyExists())
                return property;

            return null;
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
                InitializeFieldAccessors();
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

            Transform currentTargetTransform = GetFieldValue<Transform>(targetTransformField);
            Unit targetUnit = GetFieldValue<Unit>(targetUnitField);

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

            Unit targetUnit = GetFieldValue<Unit>(targetUnitField);
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

            SetFieldValue(targetTransformField, newTargetTransform);
            SetFieldValue(timeWithoutTrackField, 0f);
            SetFieldValue(lastTrackingCheckField, 0f);

            TryResubscribeJamEvent();
            waitingForRelock = false;
        }

        private void DecayJam(float deltaTime)
        {
            float jam = GetFieldValue<float>(jamAccumulationField);

            if (jam <= 0f)
            {
                if (jam != 0f)
                    SetFieldValue(jamAccumulationField, 0f);

                return;
            }

            float tolerance = GetFieldValue<float>(jamToleranceField);
            jam -= Mathf.Max(jam, 0.2f) * Mathf.Max(tolerance, 0.1f) * deltaTime;

            SetFieldValue(jamAccumulationField, Mathf.Clamp01(jam));
        }

        private Missile GetMissile()
        {
            if (cachedMissile != null)
                return cachedMissile;

            if (missileField != null)
                cachedMissile = missileField.GetValue<Missile>();

            return cachedMissile;
        }

        private T GetFieldValue<T>(Traverse member)
        {
            if (member == null)
                return default(T);

            return member.GetValue<T>();
        }

        private void SetFieldValue<T>(Traverse member, T value)
        {
            if (member == null)
                return;

            member.SetValue(value);
        }

        private void TryResubscribeJamEvent()
        {
            try
            {
                Missile missile = GetMissile();
                if (missile == null || seeker == null)
                    return;

                MethodInfo method = AccessTools.Method(typeof(SARHSeeker), "SARHSeeker_OnJam", new Type[] { typeof(Unit.JamEventArgs) });
                if (method == null)
                    return;

                // Use standard reflection to find the event.
                EventInfo eventInfo = missile.GetType().GetEvent(
                    "onJam",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                if (eventInfo != null)
                {
                    Delegate handler = Delegate.CreateDelegate(eventInfo.EventHandlerType, seeker, method);
                    eventInfo.RemoveEventHandler(missile, handler);
                    eventInfo.AddEventHandler(missile, handler);
                    return;
                }

                // Fallback: If 'onJam' is exposed as a public Delegate field instead of a C# event.
                FieldInfo field = AccessTools.Field(missile.GetType(), "onJam");
                if (field != null && typeof(Delegate).IsAssignableFrom(field.FieldType))
                {
                    Delegate currentDelegate = field.GetValue(missile) as Delegate;
                    Delegate handler = Delegate.CreateDelegate(field.FieldType, seeker, method);

                    if (currentDelegate != null)
                        currentDelegate = Delegate.Remove(currentDelegate, handler);

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
            if (__instance == null || target == null)
                return;

            if (!TryGetRelockSettings(__instance, out float delay, out int attempts))
                return;

            SARHRelockController controller = __instance.GetComponent<SARHRelockController>();
            if (controller == null)
                controller = __instance.gameObject.AddComponent<SARHRelockController>();

            controller.Setup(__instance, delay, attempts);
        }

        private static bool TryGetRelockSettings(SARHSeeker seeker, out float delay, out int attempts)
        {
            delay = 0f;
            attempts = 0;

            if (seeker == null)
                return false;

            string rootName = ObjectNameUtility.GetCleanRootName(seeker.gameObject);

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
    // CI-22 CRICKET CHANGES (COIN)
    // Config Category: CI-22 Cricket Changes
    // ====================================================================================================



    [HarmonyPatch(typeof(WeaponManager), "Awake")]
    public static class CricketLynchpinx14DoublePatch
    {
        private static bool hasPatched = false;

        public static void Prefix()
        {
            if (!Plugin.EnableCricketLynchpinx14Double.Value || hasPatched)
                return;

            WeaponMount doubleMount = CustomWeaponsReusedAssets.GetExternalLynchpinx14Double();
            if (doubleMount == null)
                return;

            HardpointInjection.InjectWeaponMount(
                "COIN",
                doubleMount,
                new[] { 2, 3 },
                "CricketLynchpinx14Double",
                "double rockets",
                "hardpoint sets 2 and 3",
                null);

            hasPatched = true;
            Debug.Log("[CricketLynchpinx14Double] Master Prefab injection complete!");
        }
    }



    [HarmonyPatch(typeof(WeaponManager), "Awake")]
    public static class CricketKingpinx8DoublePatch
    {
        private static bool hasPatched = false;

        public static void Prefix()
        {
            if (!Plugin.EnableCricketKingpinx8Double.Value || hasPatched)
                return;

            WeaponMount doubleMount = CustomWeaponsReusedAssets.GetExternalKingpinx8Double();
            if (doubleMount == null)
                return;

            HardpointInjection.InjectWeaponMount(
                "COIN",
                doubleMount,
                new[] { 2, 3 },
                "CricketKingpinx8Double",
                "double rockets",
                "hardpoint sets 2 and 3",
                null);

            hasPatched = true;
            Debug.Log("[CricketKingpinx8Double] Master Prefab injection complete!");
        }
    }



    // ====================================================================================================
    // T/A-30 COMPASS CHANGES (trainer)
    // Config Category: T/A-30 Compass Changes
    // ====================================================================================================



    [HarmonyPatch(typeof(WeaponManager), "Awake")]
    public static class CompassLynchpinx14DoublePatch
    {
        private static bool hasPatched = false;

        public static void Prefix()
        {
            if (!Plugin.EnableCompassLynchpinx14Double.Value || hasPatched)
                return;

            WeaponMount doubleMount = CustomWeaponsReusedAssets.GetExternalLynchpinx14Double();
            if (doubleMount == null)
                return;

            HardpointInjection.InjectWeaponMount(
                "trainer",
                doubleMount,
                new[] { 1 },
                "CompassLynchpinx14Double",
                "double rockets",
                "hardpoint set 1",
                null);

            hasPatched = true;
            Debug.Log("[CompassLynchpinx14Double] Master Prefab injection complete!");
        }
    }



    [HarmonyPatch(typeof(WeaponManager), "Awake")]
    public static class CompassKingpinx8DoublePatch
    {
        private static bool hasPatched = false;

        public static void Prefix()
        {
            if (!Plugin.EnableCompassKingpinx8Double.Value || hasPatched)
                return;

            WeaponMount doubleMount = CustomWeaponsReusedAssets.GetExternalKingpinx8Double();
            if (doubleMount == null)
                return;

            HardpointInjection.InjectWeaponMount(
                "trainer",
                doubleMount,
                new[] { 1 },
                "CompassKingpinx8Double",
                "double rockets",
                "hardpoint set 1",
                null);

            hasPatched = true;
            Debug.Log("[CompassKingpinx8Double] Master Prefab injection complete!");
        }
    }



    // ====================================================================================================
    // VT-7 VAGRANT CHANGES (VTOLTrainer1)
    // Config Category: VT-7 Vagrant Changes
    // ====================================================================================================



    [HarmonyPatch(typeof(WeaponManager), "Awake")]
    public static class VagrantLynchpinx14DoublePatch
    {
        private static bool hasPatched = false;

        public static void Prefix()
        {
            if (!Plugin.EnableVagrantLynchpinx14Double.Value || hasPatched)
                return;

            WeaponMount doubleMount = CustomWeaponsReusedAssets.GetExternalLynchpinx14Double();
            if (doubleMount == null)
                return;

            HardpointInjection.InjectWeaponMount(
                "VTOLTrainer1",
                doubleMount,
                new[] { 3 },
                "VagrantLynchpinx14Double",
                "double rockets",
                "hardpoint set 3",
                null);

            hasPatched = true;
            Debug.Log("[VagrantLynchpinx14Double] Master Prefab injection complete!");
        }
    }



    [HarmonyPatch(typeof(WeaponManager), "Awake")]
    public static class VagrantKingpinx8DoublePatch
    {
        private static bool hasPatched = false;

        public static void Prefix()
        {
            if (!Plugin.EnableVagrantKingpinx8Double.Value || hasPatched)
                return;

            WeaponMount doubleMount = CustomWeaponsReusedAssets.GetExternalKingpinx8Double();
            if (doubleMount == null)
                return;

            HardpointInjection.InjectWeaponMount(
                "VTOLTrainer1",
                doubleMount,
                new[] { 3 },
                "VagrantKingpinx8Double",
                "double rockets",
                "hardpoint set 3",
                null);

            hasPatched = true;
            Debug.Log("[VagrantKingpinx8Double] Master Prefab injection complete!");
        }
    }



    // ====================================================================================================
    // UH-90 IBIS CHANGES (UtilityHelo1)
    // Config Category: UH-90 Ibis Changes
    // ====================================================================================================



    [HarmonyPatch(typeof(WeaponManager), "Awake")]
    public static class IbisLynchpinx14DoublePatch
    {
        private static bool hasPatched = false;

        public static void Prefix()
        {
            if (!Plugin.EnableIbisLynchpinx14Double.Value || hasPatched)
                return;

            WeaponMount doubleMount = CustomWeaponsReusedAssets.GetExternalLynchpinx14Double();
            if (doubleMount == null)
                return;

            HardpointInjection.InjectWeaponMount(
                "UtilityHelo1",
                doubleMount,
                new[] { 0, 1 },
                "IbisLynchpinx14Double",
                "double rockets",
                "hardpoint sets 0 and 1",
                null);

            hasPatched = true;
            Debug.Log("[IbisLynchpinx14Double] Master Prefab injection complete!");
        }
    }



    [HarmonyPatch(typeof(WeaponManager), "Awake")]
    public static class IbisKingpinx8DoublePatch
    {
        private static bool hasPatched = false;

        public static void Prefix()
        {
            if (!Plugin.EnableIbisKingpinx8Double.Value || hasPatched)
                return;

            WeaponMount doubleMount = CustomWeaponsReusedAssets.GetExternalKingpinx8Double();
            if (doubleMount == null)
                return;

            HardpointInjection.InjectWeaponMount(
                "UtilityHelo1",
                doubleMount,
                new[] { 0, 1 },
                "IbisKingpinx8Double",
                "double rockets",
                "hardpoint sets 0 and 1",
                null);

            hasPatched = true;
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
            if (!Plugin.EnableChicaneProxyGun.Value)
                return;

            if (hasPatchedGun)
                return;

            WeaponManager[] allWeaponManagers = Resources.FindObjectsOfTypeAll<WeaponManager>();

            foreach (WeaponManager wm in allWeaponManagers)
            {
                if (wm == null || wm.transform == null || wm.transform.root == null)
                    continue;

                if (!wm.transform.root.name.Contains("AttackHelo1"))
                    continue;

                bool success = TryPatchProxyGun(wm.transform.root.gameObject);
                if (success)
                {
                    Debug.Log($"[ChicaneProxyGun] Successfully enabled proxy timer on: {wm.gameObject.name}");
                }
            }

            hasPatchedGun = true;
            Debug.Log("[ChicaneProxyGun] Master Prefab sweep complete!");
        }

        private static bool TryPatchProxyGun(GameObject rootVehicle)
        {
            MonoBehaviour[] allComponents = rootVehicle.GetComponentsInChildren<MonoBehaviour>(true);

            foreach (MonoBehaviour comp in allComponents)
            {
                Traverse compTraverse = Traverse.Create(comp);
                Traverse stationsField = compTraverse.Field("weaponStations");

                if (!stationsField.FieldExists())
                    continue;

                IList stationsList = stationsField.GetValue<IList>();
                if (stationsList == null || stationsList.Count <= 0)
                    continue;

                object firstStation = stationsList[0];
                Traverse stationTraverse = Traverse.Create(firstStation);
                Traverse weaponsField = stationTraverse.Field("Weapons");

                if (!weaponsField.FieldExists())
                    continue;

                IList weaponsList = weaponsField.GetValue<IList>();
                if (weaponsList == null || weaponsList.Count <= 0)
                    continue;

                object firstWeapon = weaponsList[0];
                Traverse weaponTraverse = Traverse.Create(firstWeapon);
                Traverse proxyTimerField = weaponTraverse.Field("proximityTimer");

                if (proxyTimerField.FieldExists())
                {
                    proxyTimerField.SetValue(true);
                    return true;
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

        public static void Prefix()
        {
            if (!Plugin.EnableChicaneScythesSingle.Value && !Plugin.EnableChicaneScythesDouble.Value)
                return;

            if (hasPatchedPrefab)
                return;

            WeaponMount[] allMounts = Resources.FindObjectsOfTypeAll<WeaponMount>();
            WeaponMount aam2Single = allMounts.FirstOrDefault(w => w != null && w.jsonKey == "AAM2_single");
            WeaponMount aam2Double = allMounts.FirstOrDefault(w => w != null && w.jsonKey == "AAM2_double");

            if (aam2Single == null || aam2Double == null)
                return;

            WeaponManager[] allWeaponManagers = Resources.FindObjectsOfTypeAll<WeaponManager>();

            foreach (WeaponManager wm in allWeaponManagers)
            {
                if (wm == null || wm.transform == null || wm.transform.root == null)
                    continue;

                if (!wm.transform.root.name.Contains("AttackHelo1"))
                    continue;

                if (wm.hardpointSets == null || wm.hardpointSets.Length <= 2)
                    continue;

                var stubPylons = wm.hardpointSets[2];
                if (stubPylons == null || stubPylons.weaponOptions == null)
                    continue;

                bool updated = false;

                if (Plugin.EnableChicaneScythesSingle.Value &&
                    !stubPylons.weaponOptions.Any(w => w != null && w.jsonKey == "AAM2_single"))
                {
                    stubPylons.weaponOptions.Add(aam2Single);
                    updated = true;
                }

                if (Plugin.EnableChicaneScythesDouble.Value &&
                    !stubPylons.weaponOptions.Any(w => w != null && w.jsonKey == "AAM2_double"))
                {
                    stubPylons.weaponOptions.Add(aam2Double);
                    updated = true;
                }

                if (updated)
                {
                    Debug.Log($"[ChicaneScythe] Successfully dynamically injected AAM-24 mounts into: {wm.gameObject.name}");
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
            if (!Plugin.EnableChicaneInternalLynchpinx14.Value || hasPatchedInternalLynchpinx14)
                return;

            WeaponMount doubleMount = CustomWeaponsReusedAssets.GetInternalLynchpinx14Double();
            if (doubleMount == null)
                return;

            HardpointInjection.InjectWeaponMount(
                "AttackHelo1",
                doubleMount,
                new[] { 1 },
                "ChicaneInternalLynchpinx14",
                "double rockets",
                "internal bays",
                null);

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
            if (!Plugin.EnableChicaneInternalKingpinx8.Value || hasPatchedInternalKingpinx8)
                return;

            WeaponMount doubleMount = CustomWeaponsReusedAssets.GetInternalKingpinx8Double();
            if (doubleMount == null)
                return;

            HardpointInjection.InjectWeaponMount(
                "AttackHelo1",
                doubleMount,
                new[] { 1 },
                "ChicaneInternalKingpinx8",
                "double rockets",
                "internal bays",
                null);

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
            if (!Plugin.EnableChicaneBayPylonSymmetryFix.Value)
                return;

            if (hasPatchedBayPylon)
                return;

            bool patchedAny = false;
            WeaponManager[] allWeaponManagers = Resources.FindObjectsOfTypeAll<WeaponManager>();

            foreach (WeaponManager wm in allWeaponManagers)
            {
                if (wm == null || wm.transform == null || wm.transform.root == null)
                    continue;

                if (!wm.transform.root.name.Contains("AttackHelo1"))
                    continue;

                bool success = TryFixBayPylon(wm.transform.root.gameObject);
                if (success)
                {
                    Debug.Log($"[ChicaneBayPylonSymmetryFix] Successfully centered bay pylon on: {wm.gameObject.name}");
                    patchedAny = true;
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
            if (rootVehicle == null)
                return false;

            Transform pylon = rootVehicle.transform.Find(BayPylonPath);
            if (pylon == null)
                return false;

            pylon.localPosition = BayPylonLocalPosition;
            return true;
        }
    }



    // ====================================================================================================
    // FS-12 REVOKER CHANGES (Fighter1)
    // Config Category: FS-12 Revoker Changes
    // ====================================================================================================



    [HarmonyPatch(typeof(WeaponManager), "Awake")]
    public static class RevokerLynchpinx14DoublePatch
    {
        private static bool hasPatched = false;

        public static void Prefix()
        {
            if (!Plugin.EnableRevokerLynchpinx14Double.Value || hasPatched)
                return;

            WeaponMount doubleMount = CustomWeaponsReusedAssets.GetExternalLynchpinx14Double();
            if (doubleMount == null)
                return;

            HardpointInjection.InjectWeaponMount(
                "Fighter1",
                doubleMount,
                new[] { 2 },
                "RevokerLynchpinx14Double",
                "double rockets",
                "hardpoint set 2",
                "SmallFighter1");

            hasPatched = true;
            Debug.Log("[RevokerLynchpinx14Double] Master Prefab injection complete!");
        }
    }



    [HarmonyPatch(typeof(WeaponManager), "Awake")]
    public static class RevokerKingpinx8DoublePatch
    {
        private static bool hasPatched = false;

        public static void Prefix()
        {
            if (!Plugin.EnableRevokerKingpinx8Double.Value || hasPatched)
                return;

            WeaponMount doubleMount = CustomWeaponsReusedAssets.GetExternalKingpinx8Double();
            if (doubleMount == null)
                return;

            HardpointInjection.InjectWeaponMount(
                "Fighter1",
                doubleMount,
                new[] { 2 },
                "RevokerKingpinx8Double",
                "double rockets",
                "hardpoint set 2",
                "SmallFighter1");

            hasPatched = true;
            Debug.Log("[RevokerKingpinx8Double] Master Prefab injection complete!");
        }
    }



    // ====================================================================================================
    // FS-20 VORTEX CHANGES (SmallFighter1)
    // Config Category: FS-20 Vortex Changes
    // ====================================================================================================



    [HarmonyPatch(typeof(WeaponManager), "Awake")]
    public static class VortexLynchpinx14DoublePatch
    {
        private static bool hasPatched = false;

        public static void Prefix()
        {
            if (!Plugin.EnableVortexLynchpinx14Double.Value || hasPatched)
                return;

            WeaponMount doubleMount = CustomWeaponsReusedAssets.GetExternalLynchpinx14Double();
            if (doubleMount == null)
                return;

            HardpointInjection.InjectWeaponMount(
                "SmallFighter1",
                doubleMount,
                new[] { 3 },
                "VortexLynchpinx14Double",
                "double rockets",
                "hardpoint set 3",
                null);

            hasPatched = true;
            Debug.Log("[VortexLynchpinx14Double] Master Prefab injection complete!");
        }
    }



    [HarmonyPatch(typeof(WeaponManager), "Awake")]
    public static class VortexKingpinx8DoublePatch
    {
        private static bool hasPatched = false;

        public static void Prefix()
        {
            if (!Plugin.EnableVortexKingpinx8Double.Value || hasPatched)
                return;

            WeaponMount doubleMount = CustomWeaponsReusedAssets.GetExternalKingpinx8Double();
            if (doubleMount == null)
                return;

            HardpointInjection.InjectWeaponMount(
                "SmallFighter1",
                doubleMount,
                new[] { 3 },
                "VortexKingpinx8Double",
                "double rockets",
                "hardpoint set 3",
                null);

            hasPatched = true;
            Debug.Log("[VortexKingpinx8Double] Master Prefab injection complete!");
        }
    }



    // ====================================================================================================
    // VL-49 TARANTULA CHANGES (QuadVTOL1)
    // Config Category: VL-49 Tarantula Changes
    // ====================================================================================================



    [HarmonyPatch(typeof(WeaponManager), "Awake")]
    public static class TarantulaLynchpinx14DoublePatch
    {
        private static bool hasPatched = false;

        public static void Prefix()
        {
            if (!Plugin.EnableTarantulaLynchpinx14Double.Value || hasPatched)
                return;

            WeaponMount doubleMount = CustomWeaponsReusedAssets.GetExternalLynchpinx14Double();
            if (doubleMount == null)
                return;

            HardpointInjection.InjectWeaponMount(
                "QuadVTOL1",
                doubleMount,
                new[] { 4, 5 },
                "TarantulaLynchpinx14Double",
                "double rockets",
                "hardpoint sets 4 and 5",
                null);

            hasPatched = true;
            Debug.Log("[TarantulaLynchpinx14Double] Master Prefab injection complete!");
        }
    }



    [HarmonyPatch(typeof(WeaponManager), "Awake")]
    public static class TarantulaKingpinx8DoublePatch
    {
        private static bool hasPatched = false;

        public static void Prefix()
        {
            if (!Plugin.EnableTarantulaKingpinx8Double.Value || hasPatched)
                return;

            WeaponMount doubleMount = CustomWeaponsReusedAssets.GetExternalKingpinx8Double();
            if (doubleMount == null)
                return;

            HardpointInjection.InjectWeaponMount(
                "QuadVTOL1",
                doubleMount,
                new[] { 4, 5 },
                "TarantulaKingpinx8Double",
                "double rockets",
                "hardpoint sets 4 and 5",
                null);

            hasPatched = true;
            Debug.Log("[TarantulaKingpinx8Double] Master Prefab injection complete!");
        }
    }



    // ====================================================================================================
    // KR-67 IFRIT CHANGES (Multirole1)
    // Config Category: KR-67 Ifrit Changes
    // ====================================================================================================



    [HarmonyPatch(typeof(WeaponManager), "Awake")]
    public static class IfritLynchpinx14DoublePatch
    {
        private static bool hasPatched = false;

        public static void Prefix()
        {
            if (!Plugin.EnableIfritLynchpinx14Double.Value || hasPatched)
                return;

            WeaponMount doubleMount = CustomWeaponsReusedAssets.GetExternalLynchpinx14Double();
            if (doubleMount == null)
                return;

            HardpointInjection.InjectWeaponMount(
                "Multirole1",
                doubleMount,
                new[] { 4, 5 },
                "IfritLynchpinx14Double",
                "double rockets",
                "hardpoint sets 4 and 5",
                null);

            hasPatched = true;
            Debug.Log("[IfritLynchpinx14Double] Master Prefab injection complete!");
        }
    }



    [HarmonyPatch(typeof(WeaponManager), "Awake")]
    public static class IfritKingpinx8DoublePatch
    {
        private static bool hasPatched = false;

        public static void Prefix()
        {
            if (!Plugin.EnableIfritKingpinx8Double.Value || hasPatched)
                return;

            WeaponMount doubleMount = CustomWeaponsReusedAssets.GetExternalKingpinx8Double();
            if (doubleMount == null)
                return;

            HardpointInjection.InjectWeaponMount(
                "Multirole1",
                doubleMount,
                new[] { 4, 5 },
                "IfritKingpinx8Double",
                "double rockets",
                "hardpoint sets 4 and 5",
                null);

            hasPatched = true;
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
            if (!Plugin.EnableMedusaLaserBuff.Value)
                return;

            if (hasPatchedLaser)
                return;

            bool patchedAny = false;
            GameObject[] allGameObjects = Resources.FindObjectsOfTypeAll<GameObject>();

            foreach (GameObject go in allGameObjects)
            {
                if (go == null)
                    continue;

                if (!go.name.Contains("Laser_EW1"))
                    continue;

                bool success = TryPatchMedusaLaser(go);
                if (success)
                {
                    Debug.Log($"[MedusaLaserBuff] Successfully modified laser power draw on: {go.name}");
                    patchedAny = true;
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
            MonoBehaviour[] allComponents = laserRoot.GetComponentsInChildren<MonoBehaviour>(true);

            foreach (MonoBehaviour comp in allComponents)
            {
                if (comp == null)
                    continue;

                if (!comp.gameObject.name.Contains("Laser") && !comp.GetType().Name.Contains("Laser"))
                    continue;

                if (comp.gameObject.GetComponent<ModifiedStatsFlag>() != null)
                    continue;

                Traverse compTraverse = Traverse.Create(comp);
                Traverse powerField = compTraverse.Field("power");

                if (powerField.FieldExists())
                {
                    powerField.SetValue(Plugin.MedusaLaserPowerDraw.Value);
                    comp.gameObject.AddComponent<ModifiedStatsFlag>();
                    success = true;
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
        private static bool hasPatched = false;

        public static void Prefix()
        {
            if (!Plugin.EnableMedusaLynchpinx14Double.Value || hasPatched)
                return;

            WeaponMount doubleMount = CustomWeaponsReusedAssets.GetExternalLynchpinx14Double();
            if (doubleMount == null)
                return;

            HardpointInjection.InjectWeaponMount(
                "EW1",
                doubleMount,
                new[] { 3 },
                "MedusaLynchpinx14Double",
                "double rockets",
                "hardpoint set 3",
                null);

            hasPatched = true;
            Debug.Log("[MedusaLynchpinx14Double] Master Prefab injection complete!");
        }
    }



    // ====================================================================================================
    // MEDUSA KINGPIN X8 DOUBLE
    // ====================================================================================================



    [HarmonyPatch(typeof(WeaponManager), "Awake")]
    public static class MedusaKingpinx8DoublePatch
    {
        private static bool hasPatched = false;

        public static void Prefix()
        {
            if (!Plugin.EnableMedusaKingpinx8Double.Value || hasPatched)
                return;

            WeaponMount doubleMount = CustomWeaponsReusedAssets.GetExternalKingpinx8Double();
            if (doubleMount == null)
                return;

            HardpointInjection.InjectWeaponMount(
                "EW1",
                doubleMount,
                new[] { 3 },
                "MedusaKingpinx8Double",
                "double rockets",
                "hardpoint set 3",
                null);

            hasPatched = true;
            Debug.Log("[MedusaKingpinx8Double] Master Prefab injection complete!");
        }
    }



    // ====================================================================================================
    // MEDUSA SAM_RADAR2 SINGLE (R9 STRATOLANCE x1)
    // ====================================================================================================



    [HarmonyPatch(typeof(WeaponManager), "Awake")]
    public static class MedusaSAMRadar2SinglePatch
    {
        private static bool hasPatched = false;

        public static void Prefix()
        {
            if (!Plugin.EnableMedusaSAMRadar2Single.Value || hasPatched)
                return;

            WeaponMount singleMount = CustomWeaponsReusedAssets.GetExternalSAMRadar2Single();
            if (singleMount == null)
                return;

            HardpointInjection.InjectWeaponMount(
                "EW1",
                singleMount,
                new[] { 3, 4 },
                "MedusaSAMRadar2Single",
                "R9 Stratolance x1",
                "hardpoint sets 3 and 4",
                null);

            hasPatched = true;
            Debug.Log("[MedusaSAMRadar2Single] Master Prefab injection complete!");
        }
    }



    // ====================================================================================================
    // MEDUSA SAM_RADAR2 DOUBLE (R9 STRATOLANCE x2)
    // ====================================================================================================



    [HarmonyPatch(typeof(WeaponManager), "Awake")]
    public static class MedusaSAMRadar2DoublePatch
    {
        private static bool hasPatched = false;

        public static void Prefix()
        {
            if (!Plugin.EnableMedusaSAMRadar2Double.Value || hasPatched)
                return;

            WeaponMount doubleMount = CustomWeaponsReusedAssets.GetExternalSAMRadar2Double();
            if (doubleMount == null)
                return;

            HardpointInjection.InjectWeaponMount(
                "EW1",
                doubleMount,
                new[] { 4 },
                "MedusaSAMRadar2Double",
                "R9 Stratolance x2",
                "hardpoint set 4",
                null);

            hasPatched = true;
            Debug.Log("[MedusaSAMRadar2Double] Master Prefab injection complete!");
        }
    }



}