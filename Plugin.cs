using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using Mirage;
using NuclearOption.Networking;
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

        // Runtime plugin instance used by static network/event helpers.
        public static Plugin Instance { get; private set; }

        // Set while a seed import is being applied so SettingChanged handlers do not spam refreshes.
        internal static bool IsApplyingConfigSeed { get; private set; }

        // Prevent recursive display/hash refreshes.
        private static bool isRefreshingVersionDisplay;

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

        [SeedIgnore]
        public static ConfigEntry<string> AppliedConfigHashDisplay;

        [SeedIgnore]
        public static ConfigEntry<string> AppliedConfigSeedDisplay;

        // Internal snapshot of the config state that is actually applied to the current game/session.
        private static string appliedNetworkSeed;
        private static string appliedNetworkVersion;

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

        // Network Handshake Entries
        [SeedIgnore]
        public static ConfigEntry<bool> EnableAutomaticSeedHandshake;

        [SeedIgnore]
        public static ConfigEntry<bool> AllowHostSeedOverwrite;

        [SeedIgnore]
        public static ConfigEntry<bool> AllowHandshakeRequestFromUnknownServers;

        [SeedIgnore]
        public static ConfigEntry<bool> MarkServerAsModded;

        [SeedIgnore]
        public static ConfigEntry<string> NetworkHandshakeStatus;

        // ================================================================================================
        // UNITY / BEPINEX LIFECYCLE
        // ================================================================================================
        private void Awake()
        {
            Instance = this;

            // 1. Bind notices FIRST so they remain pinned at the top of ConfigManager.
            BindImportantNotices();

            // 2. Bind all functional configuration entries.
            BindFunctionalConfigs();

            // 3. Bind network handshake entries.
            BindNetworkHandshakeConfigs();

            // 4. Import a pending configuration seed before hash generation and before Harmony patches run.
            // This preserves the normal workflow: paste seed -> restart -> imported settings load.
            TryImportPendingConfigSeed();

            // 5. Generate the config hash/version and update the display entries.
            FinalizeVersionAndHash();

            // 6. Initialize runtime feature management.
            // This system is responsible for applying/unapplying practical changes.
            FeatureRuntimeManager.Initialize();

            // 7. Initialize event-driven config refresh.
            ConfigSeedEvents.Initialize();

            // 8. Initialize the network handshake service.
            NetworkSeedHandshake.Initialize();

            // 9. Register all Harmony patches.
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

            AppliedConfigHashDisplay = Config.Bind(
                "Important Notices",
                "Applied Config Hash",
                "Not applied yet",
                new ConfigDescription(
                    "The hash of the configuration that is actually applied to this game session. This may differ from Current Config Hash if settings were changed and the game has not been restarted.",
                    null,
                    new ConfigurationManagerAttributes { ReadOnly = true, HideDefaultButton = true, Order = 96 }));

            AppliedConfigSeedDisplay = Config.Bind(
                "Important Notices",
                "Applied Config Seed",
                "Not applied yet",
                new ConfigDescription(
                    "The seed of the configuration that is actually applied to this game session. Network clients receive this applied state, not manually edited pending settings.",
                    null,
                    new ConfigurationManagerAttributes { ReadOnly = true, HideDefaultButton = true, Order = 95 }));

            ImportConfigSeed = Config.Bind(
                "Important Notices",
                "Import Config Seed",
                "",
                new ConfigDescription(
                    "Paste a Balance and Variety Rework configuration seed here, then restart the game to import that configuration.",
                    null,
                    new ConfigurationManagerAttributes { HideDefaultButton = true, Order = 94 }));
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

        private void BindNetworkHandshakeConfigs()
        {
            EnableAutomaticSeedHandshake = Config.Bind(
                "Network Handshake",
                "Enable Automatic Seed Handshake",
                true,
                "When enabled, this mod can request and receive the host's BVR configuration seed over the network.");

            AllowHostSeedOverwrite = Config.Bind(
                "Network Handshake",
                "Allow Host Seed To Override Local Config",
                true,
                "When enabled, a valid host seed received from the server will overwrite this client's BVR functional settings.");

            AllowHandshakeRequestFromUnknownServers = Config.Bind(
                "Network Handshake",
                "Allow Handshake Request From Unknown Servers",
                false,
                "If false, the client only requests a seed when the server is already marked as modded. " +
                "Enable this only for trusted modded servers that are not correctly marked as modded. " +
                "Sending handshake requests to vanilla servers may cause connection errors.");

            MarkServerAsModded = Config.Bind(
                "Network Handshake",
                "Mark Server As Modded",
                true,
                "When hosting, marks this server as modded so clients can identify that BVR is present.");

            NetworkHandshakeStatus = Config.Bind(
                "Network Handshake",
                "Network Handshake Status",
                "Idle",
                new ConfigDescription(
                    "Current handshake state. Diagnostic only.",
                    null,
                    new ConfigurationManagerAttributes { ReadOnly = true, HideDefaultButton = true, Order = 93 }));
        }

        private void TryImportPendingConfigSeed()
        {
            if (string.IsNullOrWhiteSpace(ImportConfigSeed.Value))
                return;

            string pendingSeed = ImportConfigSeed.Value;

            IsApplyingConfigSeed = true;

            try
            {
                if (TryImportConfigSeed(pendingSeed))
                {
                    Logger.LogInfo("BVR - Configuration seed imported successfully during startup.");
                }
                else
                {
                    Logger.LogWarning("BVR - Configuration seed import failed during startup. The seed was invalid or from an incompatible mod version.");
                }
            }
            finally
            {
                // Consume the import seed so it does not try to apply again on every launch.
                ImportConfigSeed.Value = string.Empty;
                IsApplyingConfigSeed = false;
            }
        }

        private void FinalizeVersionAndHash()
        {
            RefreshVersionAndHash();
            Config.Save();
            Logger.LogInfo($"Mod Version Loaded: {FullVersionWithHash}");
        }

        internal void RefreshVersionAndHash()
        {
            if (isRefreshingVersionDisplay)
                return;

            isRefreshingVersionDisplay = true;

            try
            {
                string configHash = GenerateConfigHash();

                FullVersionWithHash = $"{BaseVersion}-{configHash}";
                configHashDisplay.Value = configHash;
                CurrentConfigSeed.Value = GenerateConfigSeed();
            }
            finally
            {
                isRefreshingVersionDisplay = false;
            }
        }

        internal void MarkAppliedConfigState()
        {
            string hash = GenerateConfigHash();
            string seed = GenerateConfigSeed();

            appliedNetworkSeed = seed;
            appliedNetworkVersion = $"{BaseVersion}-{hash}";

            if (AppliedConfigHashDisplay != null)
                AppliedConfigHashDisplay.Value = hash;

            if (AppliedConfigSeedDisplay != null)
                AppliedConfigSeedDisplay.Value = seed;
        }

        internal string GetCurrentConfigSeed()
        {
            return GenerateConfigSeed();
        }

        internal string GetAppliedNetworkSeed()
        {
            if (string.IsNullOrEmpty(appliedNetworkSeed))
                MarkAppliedConfigState();

            return appliedNetworkSeed;
        }

        internal string GetAppliedNetworkVersion()
        {
            if (string.IsNullOrEmpty(appliedNetworkVersion))
                MarkAppliedConfigState();

            return appliedNetworkVersion;
        }

        internal bool TryApplyRuntimeConfigSeed(string seed)
        {
            if (string.IsNullOrWhiteSpace(seed))
                return false;

            IsApplyingConfigSeed = true;

            try
            {
                bool imported = TryImportConfigSeed(seed);

                if (imported)
                {
                    // Refresh current hash/seed display.
                    ConfigSeedEvents.NotifyFunctionalChange();

                    // Network seeds are authoritative and may apply/unapply practical changes immediately.
                    FeatureRuntimeManager.SyncFromNetwork();
                }

                return imported;
            }
            finally
            {
                IsApplyingConfigSeed = false;
            }
        }

        private void RegisterHarmonyPatches()
        {
            // Keep this list explicit. It documents every active patch class and avoids accidental assembly-wide patching.
            Type[] patchTypes =
            {
                // Central runtime feature trigger.
                typeof(FeatureRuntimeTriggerPatch),

                // SARH relock still needs a Harmony hook for newly initialized missiles.
                typeof(SARHRelockPatch),

                // Network handshake
                typeof(BVRServerSeedHandshakePatch),
                typeof(BVRClientSeedReceivePatch),
                typeof(BVRClientSeedRequestPatch)
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

                    FieldInfo field;
                    if (!seedFields.TryGetValue(entryParts[0], out field))
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

        internal static FieldInfo[] GetSeedConfigEntryFields()
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
    // CONFIG SEED EVENTS
    // Event-driven config change detection. Recalculates the seed/hash when functional config entries
    // are changed, without using Update loops.
    // ====================================================================================================
    internal static class ConfigSeedEvents
    {
        public static event Action FunctionalConfigChanged;

        private static bool initialized = false;
        private static bool isBroadcasting = false;

        public static void Initialize()
        {
            if (initialized)
                return;

            initialized = true;

            foreach (FieldInfo field in Plugin.GetSeedConfigEntryFields())
            {
                object entry = field.GetValue(null);

                if (entry == null)
                    continue;

                EventInfo settingChanged = field.FieldType.GetEvent(
                    "SettingChanged",
                    BindingFlags.Public | BindingFlags.Instance);

                if (settingChanged == null)
                    continue;

                MethodInfo addMethod = settingChanged.GetAddMethod(true);

                if (addMethod == null)
                    continue;

                addMethod.Invoke(entry, new object[] { new EventHandler(OnSettingChanged) });
            }
        }

        private static void OnSettingChanged(object sender, EventArgs e)
        {
            if (Plugin.IsApplyingConfigSeed || isBroadcasting)
                return;

            NotifyFunctionalChange();
        }

        public static void NotifyFunctionalChange()
        {
            if (isBroadcasting)
                return;

            isBroadcasting = true;

            try
            {
                Plugin instance = Plugin.Instance;

                if (instance != null)
                    instance.RefreshVersionAndHash();

                Action handler = FunctionalConfigChanged;

                if (handler != null)
                    handler();
            }
            finally
            {
                isBroadcasting = false;
            }
        }
    }

    // ====================================================================================================
    // NETWORK SEED HANDSHAKE SERVICE
    // Builds, parses, and applies the host config seed message.
    // Message format:
    // BVRNET1|<mod version>|<full version/hash>|<config seed>
    // ====================================================================================================
    internal static class NetworkSeedHandshake
    {
        private const string MessagePrefix = "BVRNET1";
        private static string lastAppliedMessage = null;

        public static void Initialize()
        {
            if (!IsEnabled())
            {
                SetStatus("Disabled");
                return;
            }

            SetStatus("Ready");
        }

        public static void SetStatus(string status)
        {
            if (Plugin.NetworkHandshakeStatus == null)
                return;

            if (Plugin.NetworkHandshakeStatus.Value == status)
                return;

            Plugin.NetworkHandshakeStatus.Value = status;
        }

        public static string BuildHostSeedMessage()
        {
            Plugin instance = Plugin.Instance;

            if (instance == null)
                return string.Empty;

            // IMPORTANT:
            // The server sends the APPLIED configuration snapshot, not the live ConfigManager values.
            // This prevents accidental/manual mid-match config edits from becoming network-authoritative.
            return $"{MessagePrefix}|{Plugin.BaseVersion}|{instance.GetAppliedNetworkVersion()}|{instance.GetAppliedNetworkSeed()}";
        }

        public static bool TryParseHostSeedMessage(string message, out string seed)
        {
            seed = null;

            if (string.IsNullOrWhiteSpace(message))
                return false;

            string[] parts = message.Split('|');

            if (parts.Length < 4 || parts[0] != MessagePrefix)
                return false;

            seed = parts[3];

            return !string.IsNullOrWhiteSpace(seed);
        }

        public static void OnHostSeedReceived(string message)
        {
            if (!IsEnabled())
                return;

            if (Plugin.AllowHostSeedOverwrite == null || !Plugin.AllowHostSeedOverwrite.Value)
            {
                SetStatus("Host seed overwrite disabled");
                return;
            }

            if (string.IsNullOrWhiteSpace(message) || message == lastAppliedMessage)
                return;

            string seed;

            if (!TryParseHostSeedMessage(message, out seed))
                return;

            Plugin instance = Plugin.Instance;

            if (instance == null)
                return;

            if (instance.TryApplyRuntimeConfigSeed(seed))
            {
                lastAppliedMessage = message;
                SetStatus($"Host seed applied: {Plugin.FullVersionWithHash}");
                Debug.Log("BVR - Host configuration seed applied at runtime.");
            }
            else
            {
                SetStatus("Host seed rejected");
                Debug.LogWarning("BVR - Received host configuration seed was rejected.");
            }
        }

        private static bool IsEnabled()
        {
            return Plugin.EnableAutomaticSeedHandshake != null && Plugin.EnableAutomaticSeedHandshake.Value;
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

    // Marks a class as a runtime feature that can be applied/unapplied by the handshake system.
    // Feature classes must implement:
    //   public static void Apply()
    //   public static void Unapply()
    // Optionally:
    //   public static bool IsEnabled()
    // If IsEnabled is not present, ToggleField is used to read a bool ConfigEntry from Plugin.
    [AttributeUsage(AttributeTargets.Class)]
    internal sealed class BvrFeatureAttribute : Attribute
    {
        public string Id { get; }
        public string ToggleField { get; set; }
        public int Order { get; set; }

        public BvrFeatureAttribute(string id)
        {
            Id = id;
            Order = 1000;
        }
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

    // Backup components used to make runtime changes reversible.
    public class BVRIntPairBackup : MonoBehaviour
    {
        public int value1;
        public int value2;
    }

    public class BVRFloatBackup : MonoBehaviour
    {
        public float value;
    }

    public class BVRBoolBackup : MonoBehaviour
    {
        public bool value;
    }

    public class BVRVector3Backup : MonoBehaviour
    {
        public Vector3 value;
    }

    internal static class BVRBackupUtility
    {
        public static void DestroyComponent(Component component)
        {
            if (component == null)
                return;

            if (component.gameObject != null && component.gameObject.scene.IsValid())
                UnityEngine.Object.Destroy(component);
            else
                UnityEngine.Object.DestroyImmediate(component);
        }
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
    // FEATURE RUNTIME MANAGER
    // Central authority for practical feature application.
    //
    // Manual ConfigManager changes:
    //   - update current hash/seed display
    //   - do NOT apply practical changes
    //
    // Network host seed:
    //   - imports config values
    //   - calls SyncFromNetwork()
    //   - applies/unapplies features so the client matches the host's applied state
    // ====================================================================================================
    internal static class FeatureRuntimeManager
    {
        private sealed class FeatureRegistration
        {
            public string Id;
            public int Order;
            public Type Type;
            public Func<bool> IsEnabled;
            public Action Apply;
            public Action Unapply;
            public bool Applied;
        }

        private static readonly Dictionary<string, FeatureRegistration> features =
            new Dictionary<string, FeatureRegistration>(StringComparer.Ordinal);

        private static readonly List<FeatureRegistration> orderedFeatures =
            new List<FeatureRegistration>();

        private static bool initialized = false;
        private static bool initialObjectSyncDone = false;
        private static bool networkSyncDone = false;
        private static bool isSyncing = false;

        public static bool HasCompletedInitialObjectSync
        {
            get { return initialObjectSyncDone; }
        }

        public static bool HasCompletedNetworkSync
        {
            get { return networkSyncDone; }
        }

        public static void Initialize()
        {
            if (initialized)
                return;

            initialized = true;

            Type[] types;

            try
            {
                types = Assembly.GetExecutingAssembly().GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types.Where(t => t != null).ToArray();
            }

            foreach (Type type in types)
            {
                if (type == null)
                    continue;

                BvrFeatureAttribute attr = type
                    .GetCustomAttributes(typeof(BvrFeatureAttribute), false)
                    .OfType<BvrFeatureAttribute>()
                    .FirstOrDefault();

                if (attr == null)
                    continue;

                if (features.ContainsKey(attr.Id))
                {
                    Debug.LogWarning($"[FeatureRuntimeManager] Duplicate feature ID '{attr.Id}' on type '{type.Name}'. Skipping.");
                    continue;
                }

                FeatureRegistration reg = new FeatureRegistration
                {
                    Id = attr.Id,
                    Order = attr.Order,
                    Type = type
                };

                MethodInfo isEnabledMethod = type.GetMethod(
                    "IsEnabled",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    Type.EmptyTypes,
                    null);

                if (isEnabledMethod != null && isEnabledMethod.ReturnType == typeof(bool))
                {
                    reg.IsEnabled = (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), isEnabledMethod);
                }
                else if (!string.IsNullOrEmpty(attr.ToggleField))
                {
                    string toggleField = attr.ToggleField;
                    reg.IsEnabled = () => GetPluginToggle(toggleField);
                }
                else
                {
                    reg.IsEnabled = () => false;
                }

                reg.Apply = CreateAction(type, "Apply");
                reg.Unapply = CreateAction(type, "Unapply");

                if (reg.Apply == null || reg.Unapply == null)
                {
                    Debug.LogWarning($"[FeatureRuntimeManager] Feature '{attr.Id}' on type '{type.Name}' must implement public static void Apply() and public static void Unapply(). Skipping.");
                    continue;
                }

                features.Add(attr.Id, reg);
            }

            orderedFeatures.AddRange(
                features.Values
                    .OrderBy(f => f.Order)
                    .ThenBy(f => f.Id, StringComparer.Ordinal));

            Debug.Log($"[FeatureRuntimeManager] Registered {orderedFeatures.Count} runtime feature(s).");
        }

        public static void OnWeaponManagerAwake()
        {
            // First practical application uses local config unless a network seed has already synchronized.
            if (initialObjectSyncDone)
                return;

            initialObjectSyncDone = true;

            SyncAll("initial object availability", false);

            if (Plugin.Instance != null)
                Plugin.Instance.MarkAppliedConfigState();
        }

        public static void SyncFromNetwork()
        {
            networkSyncDone = true;

            // Force-clean disabled features during network sync so host-off/client-on edge cases can be removed.
            SyncAll("network host seed", true);

            if (Plugin.Instance != null)
                Plugin.Instance.MarkAppliedConfigState();
        }

        public static bool IsFeatureEnabled(string id)
        {
            FeatureRegistration reg;

            if (!features.TryGetValue(id, out reg))
                return false;

            return reg.Applied;
        }

        private static void SyncAll(string source, bool forceCleanDisabled)
        {
            if (!initialized || isSyncing)
                return;

            isSyncing = true;

            try
            {
                List<FeatureRegistration> disabled = new List<FeatureRegistration>();
                List<FeatureRegistration> enabled = new List<FeatureRegistration>();

                foreach (FeatureRegistration reg in orderedFeatures)
                {
                    bool desired = SafeIsEnabled(reg);

                    if (desired)
                        enabled.Add(reg);
                    else
                        disabled.Add(reg);
                }

                // Unapply disabled features in reverse order.
                for (int i = disabled.Count - 1; i >= 0; i--)
                {
                    FeatureRegistration reg = disabled[i];

                    if (reg.Applied || forceCleanDisabled)
                        SafeUnapply(reg);
                }

                // Apply enabled features in forward order.
                // Always call Apply so value-only changes can refresh already-applied features.
                foreach (FeatureRegistration reg in enabled)
                {
                    SafeApply(reg);
                }

                Debug.Log($"[FeatureRuntimeManager] Feature runtime sync complete ({source}).");
            }
            finally
            {
                isSyncing = false;
            }
        }

        private static bool GetPluginToggle(string fieldName)
        {
            if (string.IsNullOrEmpty(fieldName))
                return false;

            FieldInfo field = typeof(Plugin).GetField(
                fieldName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

            if (field == null)
                return false;

            object value = field.GetValue(null);

            ConfigEntry<bool> boolEntry = value as ConfigEntry<bool>;

            if (boolEntry != null)
                return boolEntry.Value;

            return false;
        }

        private static Action CreateAction(Type type, string methodName)
        {
            MethodInfo method = type.GetMethod(
                methodName,
                BindingFlags.Public | BindingFlags.Static,
                null,
                Type.EmptyTypes,
                null);

            if (method == null)
                return null;

            return (Action)Delegate.CreateDelegate(typeof(Action), method);
        }

        private static bool SafeIsEnabled(FeatureRegistration reg)
        {
            try
            {
                return reg.IsEnabled();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FeatureRuntimeManager] Feature '{reg.Id}' failed IsEnabled check: {ex.Message}");
                return false;
            }
        }

        private static void SafeApply(FeatureRegistration reg)
        {
            try
            {
                reg.Apply();
                reg.Applied = true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FeatureRuntimeManager] Feature '{reg.Id}' failed Apply: {ex.Message}");
            }
        }

        private static void SafeUnapply(FeatureRegistration reg)
        {
            try
            {
                reg.Unapply();
                reg.Applied = false;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FeatureRuntimeManager] Feature '{reg.Id}' failed Unapply: {ex.Message}");
            }
        }
    }

    // ====================================================================================================
    // FEATURE RUNTIME TRIGGER
    // Central WeaponManager.Awake hook that starts the first local feature synchronization.
    // ====================================================================================================
    [HarmonyPatch(typeof(WeaponManager), "Awake")]
    public static class FeatureRuntimeTriggerPatch
    {
        public static void Prefix()
        {
            FeatureRuntimeManager.OnWeaponManagerAwake();
        }
    }

    // ====================================================================================================
    // FEATURE HARDPOINT INJECTION
    // Reversible hardpoint injection system.
    // Each feature records what it injected so the handshake system can remove it later if required.
    // ====================================================================================================
    internal static class FeatureHardpointInjection
    {
        private sealed class InjectionRecord
        {
            public WeaponManager manager;
            public int setIndex;
            public WeaponMount mount;
        }

        private static readonly Dictionary<string, List<InjectionRecord>> injectedRecords =
            new Dictionary<string, List<InjectionRecord>>(StringComparer.Ordinal);

        public static bool Inject(
            string featureId,
            string rootNameContains,
            WeaponMount mount,
            int[] hardpointSets,
            string logTag,
            string weaponDescription,
            string hardpointDescription,
            string excludeRootContains)
        {
            if (string.IsNullOrEmpty(featureId) ||
                string.IsNullOrEmpty(rootNameContains) ||
                mount == null ||
                hardpointSets == null ||
                hardpointSets.Length == 0)
            {
                return false;
            }

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
                        AddRecord(featureId, wm, setIndex, mount);
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

        public static void Remove(string featureId, string logTag)
        {
            List<InjectionRecord> records;

            if (!injectedRecords.TryGetValue(featureId, out records))
                return;

            if (records.Count == 0)
                return;

            int removedCount = 0;

            foreach (InjectionRecord record in records)
            {
                if (record == null)
                    continue;

                if (record.manager == null || record.mount == null)
                    continue;

                if (record.manager.hardpointSets == null)
                    continue;

                if (record.setIndex < 0 || record.setIndex >= record.manager.hardpointSets.Length)
                    continue;

                var hardpointSet = record.manager.hardpointSets[record.setIndex];

                if (hardpointSet == null || hardpointSet.weaponOptions == null)
                    continue;

                if (hardpointSet.weaponOptions.Contains(record.mount))
                {
                    hardpointSet.weaponOptions.Remove(record.mount);
                    removedCount++;
                }
            }

            records.Clear();

            if (removedCount > 0)
            {
                Debug.Log($"[{logTag}] Removed {removedCount} injected hardpoint option(s).");
            }
        }

        private static void AddRecord(string featureId, WeaponManager manager, int setIndex, WeaponMount mount)
        {
            List<InjectionRecord> records;

            if (!injectedRecords.TryGetValue(featureId, out records))
            {
                records = new List<InjectionRecord>();
                injectedRecords[featureId] = records;
            }

            records.Add(new InjectionRecord
            {
                manager = manager,
                setIndex = setIndex,
                mount = mount
            });
        }
    }

    // ====================================================================================================
    // HARDPOINT FEATURE HELPER
    // Small helper to keep individual hardpoint feature classes short and consistent.
    // ====================================================================================================
    internal static class HardpointFeatureHelper
    {
        public static void Apply(
            string featureId,
            Func<WeaponMount> mountFactory,
            string rootContains,
            int[] hardpointSets,
            string logTag,
            string weaponDescription,
            string hardpointDescription,
            string excludeRootContains = null)
        {
            if (mountFactory == null)
                return;

            WeaponMount mount = mountFactory();

            if (mount == null)
                return;

            FeatureHardpointInjection.Inject(
                featureId,
                rootContains,
                mount,
                hardpointSets,
                logTag,
                weaponDescription,
                hardpointDescription,
                excludeRootContains);
        }

        public static void Unapply(string featureId, string logTag)
        {
            FeatureHardpointInjection.Remove(featureId, logTag);
        }

        public static WeaponMount GetExistingMount(string jsonKey)
        {
            return Resources.FindObjectsOfTypeAll<WeaponMount>()
                .FirstOrDefault(m => m != null && m.jsonKey == jsonKey);
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

    // ====================================================================================================
    // SARH RELOCK HELPER
    // ====================================================================================================
    internal static class SARHRelockHelper
    {
        private sealed class RelockSettings
        {
            public float delay;
            public int attempts;
        }

        private static readonly Dictionary<string, RelockSettings> appliedSettings =
            new Dictionary<string, RelockSettings>(StringComparer.Ordinal);

        public static void Apply(string featureId, string targetName, float delay, int attempts, string logTag)
        {
            appliedSettings[featureId] = new RelockSettings
            {
                delay = delay,
                attempts = attempts
            };

            int count = 0;

            SARHSeeker[] seekers = Resources.FindObjectsOfTypeAll<SARHSeeker>();

            foreach (SARHSeeker seeker in seekers)
            {
                if (seeker == null || seeker.gameObject == null)
                    continue;

                // Only attach controllers to active scene objects.
                // Future missiles are handled by SARHRelockPatch.
                if (!seeker.gameObject.activeInHierarchy)
                    continue;

                if (!ObjectNameUtility.IsUnderNamedObject(seeker.gameObject, targetName))
                    continue;

                Setup(seeker, delay, attempts);
                count++;
            }

            if (count > 0)
                Debug.Log($"[{logTag}] Applied SARH relock controller(s) to {count} active seeker(s) under {targetName}.");
        }

        public static void Unapply(string featureId, string targetName, string logTag)
        {
            appliedSettings.Remove(featureId);

            int count = 0;

            SARHSeeker[] seekers = Resources.FindObjectsOfTypeAll<SARHSeeker>();

            foreach (SARHSeeker seeker in seekers)
            {
                if (seeker == null || seeker.gameObject == null)
                    continue;

                if (!ObjectNameUtility.IsUnderNamedObject(seeker.gameObject, targetName))
                    continue;

                SARHRelockController controller = seeker.GetComponent<SARHRelockController>();

                if (controller != null)
                {
                    BVRBackupUtility.DestroyComponent(controller);
                    count++;
                }
            }

            if (count > 0)
                Debug.Log($"[{logTag}] Removed SARH relock controller(s) from {count} seeker(s) under {targetName}.");
        }

        public static void Setup(SARHSeeker seeker, float delay, int attempts)
        {
            if (seeker == null)
                return;

            SARHRelockController controller = seeker.GetComponent<SARHRelockController>();

            if (controller == null)
                controller = seeker.gameObject.AddComponent<SARHRelockController>();

            controller.Setup(seeker, delay, attempts);
        }

        public static bool TrySetupIfFeatureEnabled(SARHSeeker seeker)
        {
            if (seeker == null)
                return false;

            string rootName = ObjectNameUtility.GetCleanRootName(seeker.gameObject);

            RelockSettings settings;

            if (rootName.Contains("SAM_Radar2") &&
                FeatureRuntimeManager.IsFeatureEnabled("R9SARHRelock") &&
                appliedSettings.TryGetValue("R9SARHRelock", out settings))
            {
                Setup(seeker, settings.delay, settings.attempts);
                return true;
            }

            if (rootName.Contains("SAM_Radar1") &&
                FeatureRuntimeManager.IsFeatureEnabled("RAM45SARHRelock") &&
                appliedSettings.TryGetValue("RAM45SARHRelock", out settings))
            {
                Setup(seeker, settings.delay, settings.attempts);
                return true;
            }

            return false;
        }
    }

    // ====================================================================================================
    // SARH RELOCK FEATURES
    // ====================================================================================================
    [BvrFeature("R9SARHRelock", ToggleField = nameof(Plugin.EnableR9SARHRelock), Order = 30)]
    public static class R9SARHRelockFeature
    {
        private const string FeatureId = "R9SARHRelock";

        public static void Apply()
        {
            SARHRelockHelper.Apply(
                FeatureId,
                "SAM_Radar2",
                Mathf.Max(0f, Plugin.R9SARHRelockDelay.Value),
                Mathf.Max(0, Plugin.R9SARHRelockAttempts.Value),
                FeatureId);
        }

        public static void Unapply()
        {
            SARHRelockHelper.Unapply(
                FeatureId,
                "SAM_Radar2",
                FeatureId);
        }
    }

    [BvrFeature("RAM45SARHRelock", ToggleField = nameof(Plugin.EnableRAM45SARHRelock), Order = 31)]
    public static class RAM45SARHRelockFeature
    {
        private const string FeatureId = "RAM45SARHRelock";

        public static void Apply()
        {
            SARHRelockHelper.Apply(
                FeatureId,
                "SAM_Radar1",
                Mathf.Max(0f, Plugin.RAM45SARHRelockDelay.Value),
                Mathf.Max(0, Plugin.RAM45SARHRelockAttempts.Value),
                FeatureId);
        }

        public static void Unapply()
        {
            SARHRelockHelper.Unapply(
                FeatureId,
                "SAM_Radar1",
                FeatureId);
        }
    }

    // ====================================================================================================
    // SARH RELOCK PATCH
    // Still required for newly initialized missiles after the feature has been applied.
    // ====================================================================================================
    [HarmonyPatch(typeof(SARHSeeker), "Initialize", new Type[] { typeof(Unit), typeof(GlobalPosition) })]
    public static class SARHRelockPatch
    {
        public static void Postfix(SARHSeeker __instance, Unit target)
        {
            if (__instance == null || target == null)
                return;

            SARHRelockHelper.TrySetupIfFeatureEnabled(__instance);
        }
    }

    // ====================================================================================================
    // IR MISSILES BUFF FEATURE
    // ====================================================================================================
    [BvrFeature("IRMissilesBuff", ToggleField = nameof(Plugin.EnableIRMissilesBuff), Order = 10)]
    public static class IRMissilesBuffFeature
    {
        public static void Apply()
        {
            FlareEjector[] allFlares = Resources.FindObjectsOfTypeAll<FlareEjector>();

            foreach (FlareEjector flare in allFlares)
            {
                ApplyFlare(flare);
            }

            IRSeeker[] allSeekers = Resources.FindObjectsOfTypeAll<IRSeeker>();

            foreach (IRSeeker seeker in allSeekers)
            {
                ApplySeeker(seeker);
            }

            Debug.Log("[IRMissileBuff] Feature applied.");
        }

        public static void Unapply()
        {
            FlareEjector[] allFlares = Resources.FindObjectsOfTypeAll<FlareEjector>();

            foreach (FlareEjector flare in allFlares)
            {
                RestoreFlare(flare);
            }

            IRSeeker[] allSeekers = Resources.FindObjectsOfTypeAll<IRSeeker>();

            foreach (IRSeeker seeker in allSeekers)
            {
                RestoreSeeker(seeker);
            }

            Debug.Log("[IRMissileBuff] Feature unapplied.");
        }

        private static void ApplyFlare(FlareEjector flare)
        {
            if (flare == null)
                return;

            Traverse traverse = Traverse.Create(flare);

            Traverse maxAmmoField = traverse.Field("maxAmmo");
            Traverse ammoField = traverse.Field("ammo");

            if (!maxAmmoField.FieldExists() || !ammoField.FieldExists())
                return;

            BVRIntPairBackup backup = flare.GetComponent<BVRIntPairBackup>();

            if (backup == null)
            {
                backup = flare.gameObject.AddComponent<BVRIntPairBackup>();
                backup.value1 = maxAmmoField.GetValue<int>();
                backup.value2 = ammoField.GetValue<int>();
            }

            float multiplier = Plugin.FlareCountMultiplier.Value;

            maxAmmoField.SetValue(Mathf.RoundToInt(backup.value1 * multiplier));
            ammoField.SetValue(Mathf.RoundToInt(backup.value2 * multiplier));
        }

        private static void RestoreFlare(FlareEjector flare)
        {
            if (flare == null)
                return;

            BVRIntPairBackup backup = flare.GetComponent<BVRIntPairBackup>();

            if (backup == null)
                return;

            Traverse traverse = Traverse.Create(flare);

            Traverse maxAmmoField = traverse.Field("maxAmmo");
            Traverse ammoField = traverse.Field("ammo");

            if (!maxAmmoField.FieldExists() || !ammoField.FieldExists())
                return;

            maxAmmoField.SetValue(backup.value1);
            ammoField.SetValue(backup.value2);

            BVRBackupUtility.DestroyComponent(backup);
        }

        private static void ApplySeeker(IRSeeker seeker)
        {
            if (seeker == null)
                return;

            Traverse traverse = Traverse.Create(seeker);

            Traverse rejectionField = traverse.Field("flareRejection");

            if (!rejectionField.FieldExists())
                return;

            BVRFloatBackup backup = seeker.GetComponent<BVRFloatBackup>();

            if (backup == null)
            {
                backup = seeker.gameObject.AddComponent<BVRFloatBackup>();
                backup.value = rejectionField.GetValue<float>();
            }

            float multiplier = Plugin.FlareRejectionMultiplier.Value;

            rejectionField.SetValue(backup.value * multiplier);
        }

        private static void RestoreSeeker(IRSeeker seeker)
        {
            if (seeker == null)
                return;

            BVRFloatBackup backup = seeker.GetComponent<BVRFloatBackup>();

            if (backup == null)
                return;

            Traverse traverse = Traverse.Create(seeker);

            Traverse rejectionField = traverse.Field("flareRejection");

            if (!rejectionField.FieldExists())
                return;

            rejectionField.SetValue(backup.value);

            BVRBackupUtility.DestroyComponent(backup);
        }
    }

    // ====================================================================================================
    // SARH LOCK PERSISTENCE HELPER
    // ====================================================================================================
    internal static class SARHLockPersistenceHelper
    {
        public static void Apply(string targetName, float lockPersistence, string logTag)
        {
            int patchedCount = 0;

            SARHSeeker[] seekers = Resources.FindObjectsOfTypeAll<SARHSeeker>();

            foreach (SARHSeeker seeker in seekers)
            {
                if (seeker == null)
                    continue;

                if (!ObjectNameUtility.IsUnderNamedObject(seeker.gameObject, targetName))
                    continue;

                float originalValue;

                if (!TryGetLockPersistence(seeker, out originalValue))
                    continue;

                BVRFloatBackup backup = seeker.GetComponent<BVRFloatBackup>();

                if (backup == null)
                {
                    backup = seeker.gameObject.AddComponent<BVRFloatBackup>();
                    backup.value = originalValue;
                }

                if (SetLockPersistence(seeker, lockPersistence))
                    patchedCount++;
            }

            if (patchedCount <= 0)
                Debug.LogWarning($"[{logTag}] Could not find SARHSeeker.lockPersistence on {targetName}.");
            else
                Debug.Log($"[{logTag}] Set lockPersistence={lockPersistence} on {patchedCount} seeker(s) under {targetName}.");
        }

        public static void Unapply(string targetName, string logTag)
        {
            int restoredCount = 0;

            SARHSeeker[] seekers = Resources.FindObjectsOfTypeAll<SARHSeeker>();

            foreach (SARHSeeker seeker in seekers)
            {
                if (seeker == null)
                    continue;

                if (!ObjectNameUtility.IsUnderNamedObject(seeker.gameObject, targetName))
                    continue;

                BVRFloatBackup backup = seeker.GetComponent<BVRFloatBackup>();

                if (backup == null)
                    continue;

                if (SetLockPersistence(seeker, backup.value))
                {
                    BVRBackupUtility.DestroyComponent(backup);
                    restoredCount++;
                }
            }

            if (restoredCount > 0)
                Debug.Log($"[{logTag}] Restored lockPersistence on {restoredCount} seeker(s) under {targetName}.");
        }

        private static bool TryGetLockPersistence(SARHSeeker seeker, out float value)
        {
            value = 0f;

            Traverse traverse = Traverse.Create(seeker);

            Traverse field = traverse.Field("lockPersistence");

            if (field.FieldExists())
            {
                value = field.GetValue<float>();
                return true;
            }

            Traverse property = traverse.Property("lockPersistence");

            if (property.PropertyExists())
            {
                value = property.GetValue<float>();
                return true;
            }

            return false;
        }

        private static bool SetLockPersistence(SARHSeeker seeker, float value)
        {
            Traverse traverse = Traverse.Create(seeker);

            Traverse field = traverse.Field("lockPersistence");

            if (field.FieldExists())
            {
                field.SetValue(value);
                return true;
            }

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
    // SARH LOCK PERSISTENCE FEATURES
    // ====================================================================================================
    [BvrFeature("R9LockPersistence", ToggleField = nameof(Plugin.EnableR9LockPersistenceBuff), Order = 20)]
    public static class R9LockPersistenceFeature
    {
        private const string FeatureId = "R9LockPersistence";

        public static void Apply()
        {
            SARHLockPersistenceHelper.Apply(
                "SAM_Radar2",
                Plugin.R9LockPersistenceValue.Value,
                FeatureId);
        }

        public static void Unapply()
        {
            SARHLockPersistenceHelper.Unapply(
                "SAM_Radar2",
                FeatureId);
        }
    }

    [BvrFeature("RAM45LockPersistence", ToggleField = nameof(Plugin.EnableRAM45LockPersistenceBuff), Order = 21)]
    public static class RAM45LockPersistenceFeature
    {
        private const string FeatureId = "RAM45LockPersistence";

        public static void Apply()
        {
            SARHLockPersistenceHelper.Apply(
                "SAM_Radar1",
                Plugin.RAM45LockPersistenceValue.Value,
                FeatureId);
        }

        public static void Unapply()
        {
            SARHLockPersistenceHelper.Unapply(
                "SAM_Radar1",
                FeatureId);
        }
    }

    // ====================================================================================================
    // CI-22 CRICKET HARDPOINT FEATURES
    // ====================================================================================================
    [BvrFeature("CricketLynchpinx14Double", ToggleField = nameof(Plugin.EnableCricketLynchpinx14Double), Order = 100)]
    public static class CricketLynchpinx14DoubleFeature
    {
        private const string FeatureId = "CricketLynchpinx14Double";

        public static void Apply()
        {
            HardpointFeatureHelper.Apply(
                FeatureId,
                CustomWeaponsReusedAssets.GetExternalLynchpinx14Double,
                "COIN",
                new[] { 2, 3 },
                FeatureId,
                "double rockets",
                "hardpoint sets 2 and 3");
        }

        public static void Unapply()
        {
            HardpointFeatureHelper.Unapply(FeatureId, FeatureId);
        }
    }

    [BvrFeature("CricketKingpinx8Double", ToggleField = nameof(Plugin.EnableCricketKingpinx8Double), Order = 101)]
    public static class CricketKingpinx8DoubleFeature
    {
        private const string FeatureId = "CricketKingpinx8Double";

        public static void Apply()
        {
            HardpointFeatureHelper.Apply(
                FeatureId,
                CustomWeaponsReusedAssets.GetExternalKingpinx8Double,
                "COIN",
                new[] { 2, 3 },
                FeatureId,
                "double rockets",
                "hardpoint sets 2 and 3");
        }

        public static void Unapply()
        {
            HardpointFeatureHelper.Unapply(FeatureId, FeatureId);
        }
    }

    // ====================================================================================================
    // T/A-30 COMPASS HARDPOINT FEATURES
    // ====================================================================================================
    [BvrFeature("CompassLynchpinx14Double", ToggleField = nameof(Plugin.EnableCompassLynchpinx14Double), Order = 110)]
    public static class CompassLynchpinx14DoubleFeature
    {
        private const string FeatureId = "CompassLynchpinx14Double";

        public static void Apply()
        {
            HardpointFeatureHelper.Apply(
                FeatureId,
                CustomWeaponsReusedAssets.GetExternalLynchpinx14Double,
                "trainer",
                new[] { 1 },
                FeatureId,
                "double rockets",
                "hardpoint set 1");
        }

        public static void Unapply()
        {
            HardpointFeatureHelper.Unapply(FeatureId, FeatureId);
        }
    }

    [BvrFeature("CompassKingpinx8Double", ToggleField = nameof(Plugin.EnableCompassKingpinx8Double), Order = 111)]
    public static class CompassKingpinx8DoubleFeature
    {
        private const string FeatureId = "CompassKingpinx8Double";

        public static void Apply()
        {
            HardpointFeatureHelper.Apply(
                FeatureId,
                CustomWeaponsReusedAssets.GetExternalKingpinx8Double,
                "trainer",
                new[] { 1 },
                FeatureId,
                "double rockets",
                "hardpoint set 1");
        }

        public static void Unapply()
        {
            HardpointFeatureHelper.Unapply(FeatureId, FeatureId);
        }
    }

    // ====================================================================================================
    // VT-7 VAGRANT HARDPOINT FEATURES
    // ====================================================================================================
    [BvrFeature("VagrantLynchpinx14Double", ToggleField = nameof(Plugin.EnableVagrantLynchpinx14Double), Order = 120)]
    public static class VagrantLynchpinx14DoubleFeature
    {
        private const string FeatureId = "VagrantLynchpinx14Double";

        public static void Apply()
        {
            HardpointFeatureHelper.Apply(
                FeatureId,
                CustomWeaponsReusedAssets.GetExternalLynchpinx14Double,
                "VTOLTrainer1",
                new[] { 3 },
                FeatureId,
                "double rockets",
                "hardpoint set 3");
        }

        public static void Unapply()
        {
            HardpointFeatureHelper.Unapply(FeatureId, FeatureId);
        }
    }

    [BvrFeature("VagrantKingpinx8Double", ToggleField = nameof(Plugin.EnableVagrantKingpinx8Double), Order = 121)]
    public static class VagrantKingpinx8DoubleFeature
    {
        private const string FeatureId = "VagrantKingpinx8Double";

        public static void Apply()
        {
            HardpointFeatureHelper.Apply(
                FeatureId,
                CustomWeaponsReusedAssets.GetExternalKingpinx8Double,
                "VTOLTrainer1",
                new[] { 3 },
                FeatureId,
                "double rockets",
                "hardpoint set 3");
        }

        public static void Unapply()
        {
            HardpointFeatureHelper.Unapply(FeatureId, FeatureId);
        }
    }

    // ====================================================================================================
    // UH-90 IBIS HARDPOINT FEATURES
    // ====================================================================================================
    [BvrFeature("IbisLynchpinx14Double", ToggleField = nameof(Plugin.EnableIbisLynchpinx14Double), Order = 130)]
    public static class IbisLynchpinx14DoubleFeature
    {
        private const string FeatureId = "IbisLynchpinx14Double";

        public static void Apply()
        {
            HardpointFeatureHelper.Apply(
                FeatureId,
                CustomWeaponsReusedAssets.GetExternalLynchpinx14Double,
                "UtilityHelo1",
                new[] { 0, 1 },
                FeatureId,
                "double rockets",
                "hardpoint sets 0 and 1");
        }

        public static void Unapply()
        {
            HardpointFeatureHelper.Unapply(FeatureId, FeatureId);
        }
    }

    [BvrFeature("IbisKingpinx8Double", ToggleField = nameof(Plugin.EnableIbisKingpinx8Double), Order = 131)]
    public static class IbisKingpinx8DoubleFeature
    {
        private const string FeatureId = "IbisKingpinx8Double";

        public static void Apply()
        {
            HardpointFeatureHelper.Apply(
                FeatureId,
                CustomWeaponsReusedAssets.GetExternalKingpinx8Double,
                "UtilityHelo1",
                new[] { 0, 1 },
                FeatureId,
                "double rockets",
                "hardpoint sets 0 and 1");
        }

        public static void Unapply()
        {
            HardpointFeatureHelper.Unapply(FeatureId, FeatureId);
        }
    }

    // ====================================================================================================
    // SAH-46 CHICANE HARDPOINT / INTERNAL BAY FEATURES
    // ====================================================================================================
    [BvrFeature("ChicaneScytheSingle", ToggleField = nameof(Plugin.EnableChicaneScythesSingle), Order = 140)]
    public static class ChicaneScytheSingleFeature
    {
        private const string FeatureId = "ChicaneScytheSingle";

        public static void Apply()
        {
            HardpointFeatureHelper.Apply(
                FeatureId,
                () => HardpointFeatureHelper.GetExistingMount("AAM2_single"),
                "AttackHelo1",
                new[] { 2 },
                FeatureId,
                "AAM-24 Scythe x1",
                "inner stub pylons");
        }

        public static void Unapply()
        {
            HardpointFeatureHelper.Unapply(FeatureId, FeatureId);
        }
    }

    [BvrFeature("ChicaneScytheDouble", ToggleField = nameof(Plugin.EnableChicaneScythesDouble), Order = 141)]
    public static class ChicaneScytheDoubleFeature
    {
        private const string FeatureId = "ChicaneScytheDouble";

        public static void Apply()
        {
            HardpointFeatureHelper.Apply(
                FeatureId,
                () => HardpointFeatureHelper.GetExistingMount("AAM2_double"),
                "AttackHelo1",
                new[] { 2 },
                FeatureId,
                "AAM-24 Scythe x2",
                "inner stub pylons");
        }

        public static void Unapply()
        {
            HardpointFeatureHelper.Unapply(FeatureId, FeatureId);
        }
    }

    [BvrFeature("ChicaneInternalLynchpinx14", ToggleField = nameof(Plugin.EnableChicaneInternalLynchpinx14), Order = 142)]
    public static class ChicaneInternalLynchpinx14Feature
    {
        private const string FeatureId = "ChicaneInternalLynchpinx14";

        public static void Apply()
        {
            HardpointFeatureHelper.Apply(
                FeatureId,
                CustomWeaponsReusedAssets.GetInternalLynchpinx14Double,
                "AttackHelo1",
                new[] { 1 },
                FeatureId,
                "double rockets",
                "internal bays");
        }

        public static void Unapply()
        {
            HardpointFeatureHelper.Unapply(FeatureId, FeatureId);
        }
    }

    [BvrFeature("ChicaneInternalKingpinx8", ToggleField = nameof(Plugin.EnableChicaneInternalKingpinx8), Order = 143)]
    public static class ChicaneInternalKingpinx8Feature
    {
        private const string FeatureId = "ChicaneInternalKingpinx8";

        public static void Apply()
        {
            HardpointFeatureHelper.Apply(
                FeatureId,
                CustomWeaponsReusedAssets.GetInternalKingpinx8Double,
                "AttackHelo1",
                new[] { 1 },
                FeatureId,
                "double rockets",
                "internal bays");
        }

        public static void Unapply()
        {
            HardpointFeatureHelper.Unapply(FeatureId, FeatureId);
        }
    }

    // ====================================================================================================
    // CHICANE PROXIMITY FUSE NOSEGUN FEATURE
    // ====================================================================================================
    [BvrFeature("ChicaneProxyGun", ToggleField = nameof(Plugin.EnableChicaneProxyGun), Order = 150)]
    public static class ChicaneProxyGunFeature
    {
        private sealed class Record
        {
            public object weapon;
            public bool original;
        }

        private static readonly List<Record> records = new List<Record>();

        public static void Apply()
        {
            WeaponManager[] allWeaponManagers = Resources.FindObjectsOfTypeAll<WeaponManager>();

            foreach (WeaponManager wm in allWeaponManagers)
            {
                if (wm == null || wm.transform == null || wm.transform.root == null)
                    continue;

                if (!wm.transform.root.name.Contains("AttackHelo1"))
                    continue;

                TryApplyProxyGun(wm.transform.root.gameObject);
            }

            Debug.Log("[ChicaneProxyGun] Feature applied.");
        }

        public static void Unapply()
        {
            for (int i = records.Count - 1; i >= 0; i--)
            {
                Record record = records[i];

                if (!IsDead(record.weapon))
                {
                    Traverse weaponTraverse = Traverse.Create(record.weapon);
                    Traverse proxyTimerField = weaponTraverse.Field("proximityTimer");

                    if (proxyTimerField.FieldExists())
                        proxyTimerField.SetValue(record.original);
                }

                records.RemoveAt(i);
            }

            Debug.Log("[ChicaneProxyGun] Feature unapplied.");
        }

        private static bool TryApplyProxyGun(GameObject rootVehicle)
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

                if (!proxyTimerField.FieldExists())
                    continue;

                bool currentValue = proxyTimerField.GetValue<bool>();

                Record existing;

                if (!TryGetRecord(firstWeapon, out existing))
                {
                    records.Add(new Record
                    {
                        weapon = firstWeapon,
                        original = currentValue
                    });
                }

                proxyTimerField.SetValue(true);

                return true;
            }

            return false;
        }

        private static bool TryGetRecord(object weapon, out Record record)
        {
            for (int i = records.Count - 1; i >= 0; i--)
            {
                if (IsDead(records[i].weapon))
                    records.RemoveAt(i);
            }

            foreach (Record r in records)
            {
                if (object.ReferenceEquals(r.weapon, weapon))
                {
                    record = r;
                    return true;
                }
            }

            record = null;
            return false;
        }

        private static bool IsDead(object target)
        {
            if (target == null)
                return true;

            if (!(target is UnityEngine.Object))
                return false;

            return (UnityEngine.Object)target == null;
        }
    }

    // ====================================================================================================
    // CHICANE BAY PYLON SYMMETRY FIX FEATURE
    // ====================================================================================================
    [BvrFeature("ChicaneBayPylonSymmetryFix", ToggleField = nameof(Plugin.EnableChicaneBayPylonSymmetryFix), Order = 151)]
    public static class ChicaneBayPylonSymmetryFixFeature
    {
        private const string BayPylonPath = "weaponBay_R/weaponDoorHinge_Ra/weaponDoorHinge_Rb/pylon_bay_R";
        private static readonly Vector3 BayPylonLocalPosition = new Vector3(0f, -0.35f, -0.1f);

        public static void Apply()
        {
            WeaponManager[] allWeaponManagers = Resources.FindObjectsOfTypeAll<WeaponManager>();

            foreach (WeaponManager wm in allWeaponManagers)
            {
                if (wm == null || wm.transform == null || wm.transform.root == null)
                    continue;

                if (!wm.transform.root.name.Contains("AttackHelo1"))
                    continue;

                TryApply(wm.transform.root.gameObject);
            }

            Debug.Log("[ChicaneBayPylonSymmetryFix] Feature applied.");
        }

        public static void Unapply()
        {
            WeaponManager[] allWeaponManagers = Resources.FindObjectsOfTypeAll<WeaponManager>();

            foreach (WeaponManager wm in allWeaponManagers)
            {
                if (wm == null || wm.transform == null || wm.transform.root == null)
                    continue;

                if (!wm.transform.root.name.Contains("AttackHelo1"))
                    continue;

                TryRestore(wm.transform.root.gameObject);
            }

            Debug.Log("[ChicaneBayPylonSymmetryFix] Feature unapplied.");
        }

        private static bool TryApply(GameObject rootVehicle)
        {
            if (rootVehicle == null)
                return false;

            Transform pylon = rootVehicle.transform.Find(BayPylonPath);

            if (pylon == null)
                return false;

            BVRVector3Backup backup = pylon.GetComponent<BVRVector3Backup>();

            if (backup == null)
            {
                backup = pylon.gameObject.AddComponent<BVRVector3Backup>();
                backup.value = pylon.localPosition;
            }

            pylon.localPosition = BayPylonLocalPosition;

            return true;
        }

        private static bool TryRestore(GameObject rootVehicle)
        {
            if (rootVehicle == null)
                return false;

            Transform pylon = rootVehicle.transform.Find(BayPylonPath);

            if (pylon == null)
                return false;

            BVRVector3Backup backup = pylon.GetComponent<BVRVector3Backup>();

            if (backup == null)
                return false;

            pylon.localPosition = backup.value;

            BVRBackupUtility.DestroyComponent(backup);

            return true;
        }
    }

    // ====================================================================================================
    // FS-12 REVOKER HARDPOINT FEATURES
    // ====================================================================================================
    [BvrFeature("RevokerLynchpinx14Double", ToggleField = nameof(Plugin.EnableRevokerLynchpinx14Double), Order = 160)]
    public static class RevokerLynchpinx14DoubleFeature
    {
        private const string FeatureId = "RevokerLynchpinx14Double";

        public static void Apply()
        {
            HardpointFeatureHelper.Apply(
                FeatureId,
                CustomWeaponsReusedAssets.GetExternalLynchpinx14Double,
                "Fighter1",
                new[] { 2 },
                FeatureId,
                "double rockets",
                "hardpoint set 2",
                "SmallFighter1");
        }

        public static void Unapply()
        {
            HardpointFeatureHelper.Unapply(FeatureId, FeatureId);
        }
    }

    [BvrFeature("RevokerKingpinx8Double", ToggleField = nameof(Plugin.EnableRevokerKingpinx8Double), Order = 161)]
    public static class RevokerKingpinx8DoubleFeature
    {
        private const string FeatureId = "RevokerKingpinx8Double";

        public static void Apply()
        {
            HardpointFeatureHelper.Apply(
                FeatureId,
                CustomWeaponsReusedAssets.GetExternalKingpinx8Double,
                "Fighter1",
                new[] { 2 },
                FeatureId,
                "double rockets",
                "hardpoint set 2",
                "SmallFighter1");
        }

        public static void Unapply()
        {
            HardpointFeatureHelper.Unapply(FeatureId, FeatureId);
        }
    }

    // ====================================================================================================
    // FS-20 VORTEX HARDPOINT FEATURES
    // ====================================================================================================
    [BvrFeature("VortexLynchpinx14Double", ToggleField = nameof(Plugin.EnableVortexLynchpinx14Double), Order = 170)]
    public static class VortexLynchpinx14DoubleFeature
    {
        private const string FeatureId = "VortexLynchpinx14Double";

        public static void Apply()
        {
            HardpointFeatureHelper.Apply(
                FeatureId,
                CustomWeaponsReusedAssets.GetExternalLynchpinx14Double,
                "SmallFighter1",
                new[] { 3 },
                FeatureId,
                "double rockets",
                "hardpoint set 3");
        }

        public static void Unapply()
        {
            HardpointFeatureHelper.Unapply(FeatureId, FeatureId);
        }
    }

    [BvrFeature("VortexKingpinx8Double", ToggleField = nameof(Plugin.EnableVortexKingpinx8Double), Order = 171)]
    public static class VortexKingpinx8DoubleFeature
    {
        private const string FeatureId = "VortexKingpinx8Double";

        public static void Apply()
        {
            HardpointFeatureHelper.Apply(
                FeatureId,
                CustomWeaponsReusedAssets.GetExternalKingpinx8Double,
                "SmallFighter1",
                new[] { 3 },
                FeatureId,
                "double rockets",
                "hardpoint set 3");
        }

        public static void Unapply()
        {
            HardpointFeatureHelper.Unapply(FeatureId, FeatureId);
        }
    }

    // ====================================================================================================
    // VL-49 TARANTULA HARDPOINT FEATURES
    // ====================================================================================================
    [BvrFeature("TarantulaLynchpinx14Double", ToggleField = nameof(Plugin.EnableTarantulaLynchpinx14Double), Order = 180)]
    public static class TarantulaLynchpinx14DoubleFeature
    {
        private const string FeatureId = "TarantulaLynchpinx14Double";

        public static void Apply()
        {
            HardpointFeatureHelper.Apply(
                FeatureId,
                CustomWeaponsReusedAssets.GetExternalLynchpinx14Double,
                "QuadVTOL1",
                new[] { 4, 5 },
                FeatureId,
                "double rockets",
                "hardpoint sets 4 and 5");
        }

        public static void Unapply()
        {
            HardpointFeatureHelper.Unapply(FeatureId, FeatureId);
        }
    }

    [BvrFeature("TarantulaKingpinx8Double", ToggleField = nameof(Plugin.EnableTarantulaKingpinx8Double), Order = 181)]
    public static class TarantulaKingpinx8DoubleFeature
    {
        private const string FeatureId = "TarantulaKingpinx8Double";

        public static void Apply()
        {
            HardpointFeatureHelper.Apply(
                FeatureId,
                CustomWeaponsReusedAssets.GetExternalKingpinx8Double,
                "QuadVTOL1",
                new[] { 4, 5 },
                FeatureId,
                "double rockets",
                "hardpoint sets 4 and 5");
        }

        public static void Unapply()
        {
            HardpointFeatureHelper.Unapply(FeatureId, FeatureId);
        }
    }

    // ====================================================================================================
    // KR-67 IFRIT HARDPOINT FEATURES
    // ====================================================================================================
    [BvrFeature("IfritLynchpinx14Double", ToggleField = nameof(Plugin.EnableIfritLynchpinx14Double), Order = 190)]
    public static class IfritLynchpinx14DoubleFeature
    {
        private const string FeatureId = "IfritLynchpinx14Double";

        public static void Apply()
        {
            HardpointFeatureHelper.Apply(
                FeatureId,
                CustomWeaponsReusedAssets.GetExternalLynchpinx14Double,
                "Multirole1",
                new[] { 4, 5 },
                FeatureId,
                "double rockets",
                "hardpoint sets 4 and 5");
        }

        public static void Unapply()
        {
            HardpointFeatureHelper.Unapply(FeatureId, FeatureId);
        }
    }

    [BvrFeature("IfritKingpinx8Double", ToggleField = nameof(Plugin.EnableIfritKingpinx8Double), Order = 191)]
    public static class IfritKingpinx8DoubleFeature
    {
        private const string FeatureId = "IfritKingpinx8Double";

        public static void Apply()
        {
            HardpointFeatureHelper.Apply(
                FeatureId,
                CustomWeaponsReusedAssets.GetExternalKingpinx8Double,
                "Multirole1",
                new[] { 4, 5 },
                FeatureId,
                "double rockets",
                "hardpoint sets 4 and 5");
        }

        public static void Unapply()
        {
            HardpointFeatureHelper.Unapply(FeatureId, FeatureId);
        }
    }

    // ====================================================================================================
    // EW-25 MEDUSA LASER FEATURE
    // ====================================================================================================
    [BvrFeature("MedusaLaserBuff", ToggleField = nameof(Plugin.EnableMedusaLaserBuff), Order = 200)]
    public static class MedusaLaserFeature
    {
        public static void Apply()
        {
            GameObject[] allGameObjects = Resources.FindObjectsOfTypeAll<GameObject>();

            foreach (GameObject go in allGameObjects)
            {
                if (go == null)
                    continue;

                if (!go.name.Contains("Laser_EW1"))
                    continue;

                TryApplyLaser(go);
            }

            Debug.Log("[MedusaLaserBuff] Feature applied.");
        }

        public static void Unapply()
        {
            GameObject[] allGameObjects = Resources.FindObjectsOfTypeAll<GameObject>();

            foreach (GameObject go in allGameObjects)
            {
                if (go == null)
                    continue;

                if (!go.name.Contains("Laser_EW1"))
                    continue;

                TryRestoreLaser(go);
            }

            Debug.Log("[MedusaLaserBuff] Feature unapplied.");
        }

        private static bool TryApplyLaser(GameObject laserRoot)
        {
            bool success = false;

            MonoBehaviour[] allComponents = laserRoot.GetComponentsInChildren<MonoBehaviour>(true);

            foreach (MonoBehaviour comp in allComponents)
            {
                if (comp == null)
                    continue;

                if (!comp.gameObject.name.Contains("Laser") && !comp.GetType().Name.Contains("Laser"))
                    continue;

                Traverse compTraverse = Traverse.Create(comp);

                Traverse powerField = compTraverse.Field("power");

                if (!powerField.FieldExists())
                    continue;

                BVRFloatBackup backup = comp.GetComponent<BVRFloatBackup>();

                if (backup == null)
                {
                    backup = comp.gameObject.AddComponent<BVRFloatBackup>();
                    backup.value = powerField.GetValue<float>();
                }

                powerField.SetValue(Plugin.MedusaLaserPowerDraw.Value);
                success = true;
            }

            return success;
        }

        private static bool TryRestoreLaser(GameObject laserRoot)
        {
            bool success = false;

            MonoBehaviour[] allComponents = laserRoot.GetComponentsInChildren<MonoBehaviour>(true);

            foreach (MonoBehaviour comp in allComponents)
            {
                if (comp == null)
                    continue;

                BVRFloatBackup backup = comp.GetComponent<BVRFloatBackup>();

                if (backup == null)
                    continue;

                Traverse compTraverse = Traverse.Create(comp);

                Traverse powerField = compTraverse.Field("power");

                if (!powerField.FieldExists())
                    continue;

                powerField.SetValue(backup.value);

                BVRBackupUtility.DestroyComponent(backup);

                success = true;
            }

            return success;
        }
    }

    // ====================================================================================================
    // EW-25 MEDUSA HARDPOINT FEATURES
    // ====================================================================================================
    [BvrFeature("MedusaLynchpinx14Double", ToggleField = nameof(Plugin.EnableMedusaLynchpinx14Double), Order = 210)]
    public static class MedusaLynchpinx14DoubleFeature
    {
        private const string FeatureId = "MedusaLynchpinx14Double";

        public static void Apply()
        {
            HardpointFeatureHelper.Apply(
                FeatureId,
                CustomWeaponsReusedAssets.GetExternalLynchpinx14Double,
                "EW1",
                new[] { 3 },
                FeatureId,
                "double rockets",
                "hardpoint set 3");
        }

        public static void Unapply()
        {
            HardpointFeatureHelper.Unapply(FeatureId, FeatureId);
        }
    }

    [BvrFeature("MedusaKingpinx8Double", ToggleField = nameof(Plugin.EnableMedusaKingpinx8Double), Order = 211)]
    public static class MedusaKingpinx8DoubleFeature
    {
        private const string FeatureId = "MedusaKingpinx8Double";

        public static void Apply()
        {
            HardpointFeatureHelper.Apply(
                FeatureId,
                CustomWeaponsReusedAssets.GetExternalKingpinx8Double,
                "EW1",
                new[] { 3 },
                FeatureId,
                "double rockets",
                "hardpoint set 3");
        }

        public static void Unapply()
        {
            HardpointFeatureHelper.Unapply(FeatureId, FeatureId);
        }
    }

    [BvrFeature("MedusaSAMRadar2Single", ToggleField = nameof(Plugin.EnableMedusaSAMRadar2Single), Order = 212)]
    public static class MedusaSAMRadar2SingleFeature
    {
        private const string FeatureId = "MedusaSAMRadar2Single";

        public static void Apply()
        {
            HardpointFeatureHelper.Apply(
                FeatureId,
                CustomWeaponsReusedAssets.GetExternalSAMRadar2Single,
                "EW1",
                new[] { 3, 4 },
                FeatureId,
                "R9 Stratolance x1",
                "hardpoint sets 3 and 4");
        }

        public static void Unapply()
        {
            HardpointFeatureHelper.Unapply(FeatureId, FeatureId);
        }
    }

    [BvrFeature("MedusaSAMRadar2Double", ToggleField = nameof(Plugin.EnableMedusaSAMRadar2Double), Order = 213)]
    public static class MedusaSAMRadar2DoubleFeature
    {
        private const string FeatureId = "MedusaSAMRadar2Double";

        public static void Apply()
        {
            HardpointFeatureHelper.Apply(
                FeatureId,
                CustomWeaponsReusedAssets.GetExternalSAMRadar2Double,
                "EW1",
                new[] { 4 },
                FeatureId,
                "R9 Stratolance x2",
                "hardpoint set 4");
        }

        public static void Unapply()
        {
            HardpointFeatureHelper.Unapply(FeatureId, FeatureId);
        }
    }

    // ====================================================================================================
    // NETWORK SEED HANDSHAKE - MIRAGE HOOKS
    // Uses Mirage message handlers and NetworkManagerNuclearOption callbacks.
    // No Update loops are used.
    // ====================================================================================================
    [NetworkMessage]
    public struct BvrSeedRequestMessage
    {
        public byte ProtocolVersion;
    }

    [NetworkMessage]
    public struct BvrSeedMessage
    {
        public string Payload;
    }

    // ====================================================================================================
    // SERVER SIDE
    // Registers the seed request handler when the server starts.
    // ====================================================================================================
    [HarmonyPatch(typeof(NetworkManagerNuclearOption), "OnServerStarted")]
    public static class BVRServerSeedHandshakePatch
    {
        public static void Postfix(NetworkManagerNuclearOption __instance)
        {
            if (__instance == null)
                return;

            if (Plugin.MarkServerAsModded != null && Plugin.MarkServerAsModded.Value)
            {
                __instance.SetModdedServer(true);
            }

            if (__instance.Server == null || __instance.Server.MessageHandler == null)
                return;

            if (Plugin.EnableAutomaticSeedHandshake == null || !Plugin.EnableAutomaticSeedHandshake.Value)
                return;

            __instance.Server.MessageHandler.RegisterHandler<BvrSeedRequestMessage>(
                new MessageDelegateWithPlayer<BvrSeedRequestMessage>(HandleSeedRequest),
                true);

            NetworkSeedHandshake.SetStatus("Server handshake ready");
        }

        private static void HandleSeedRequest(INetworkPlayer player, BvrSeedRequestMessage msg)
        {
            if (Plugin.EnableAutomaticSeedHandshake == null || !Plugin.EnableAutomaticSeedHandshake.Value)
                return;

            if (player == null || !player.IsConnected || !player.IsAuthenticated)
                return;

            // The host does not need to receive its own seed.
            if (player.IsHost)
                return;

            string payload = NetworkSeedHandshake.BuildHostSeedMessage();

            if (string.IsNullOrEmpty(payload))
                return;

            player.Send<BvrSeedMessage>(
                new BvrSeedMessage
                {
                    Payload = payload
                },
                Channel.Reliable);

            NetworkSeedHandshake.SetStatus("Host seed sent to client");
        }
    }

    // ====================================================================================================
    // CLIENT SIDE - RECEIVE SEED
    // Registers the client handler for the host seed message.
    // ====================================================================================================
    [HarmonyPatch(typeof(NetworkManagerNuclearOption), "ClientStarted")]
    public static class BVRClientSeedReceivePatch
    {
        public static void Postfix(NetworkManagerNuclearOption __instance)
        {
            if (__instance == null)
                return;

            if (__instance.Client == null || __instance.Client.MessageHandler == null)
                return;

            if (Plugin.EnableAutomaticSeedHandshake == null || !Plugin.EnableAutomaticSeedHandshake.Value)
                return;

            __instance.Client.MessageHandler.RegisterHandler<BvrSeedMessage>(
                new MessageDelegateWithPlayer<BvrSeedMessage>(HandleHostSeedMessage),
                true);

            NetworkSeedHandshake.SetStatus("Client handshake ready");
        }

        private static void HandleHostSeedMessage(INetworkPlayer player, BvrSeedMessage msg)
        {
            NetworkSeedHandshake.OnHostSeedReceived(msg.Payload);
        }
    }

    // ====================================================================================================
    // CLIENT SIDE - REQUEST SEED
    // Sends a seed request after the client is authenticated.
    // ====================================================================================================
    [HarmonyPatch(typeof(NetworkManagerNuclearOption), "OnClientAuthenticated")]
    public static class BVRClientSeedRequestPatch
    {
        public static void Postfix(NetworkManagerNuclearOption __instance, INetworkPlayer player)
        {
            if (Plugin.EnableAutomaticSeedHandshake == null || !Plugin.EnableAutomaticSeedHandshake.Value)
                return;

            if (player == null || player.IsHost)
                return;

            if (__instance == null || __instance.Client == null || !__instance.Client.IsConnected)
                return;

            bool knownModdedServer = NetworkManagerNuclearOption.ModdedServer == true;

            bool allowUnknownServers =
                Plugin.AllowHandshakeRequestFromUnknownServers != null &&
                Plugin.AllowHandshakeRequestFromUnknownServers.Value;

            if (!knownModdedServer && !allowUnknownServers)
            {
                NetworkSeedHandshake.SetStatus("Seed request skipped: server not marked modded");
                return;
            }

            __instance.Client.Send<BvrSeedRequestMessage>(
                new BvrSeedRequestMessage
                {
                    ProtocolVersion = 1
                },
                Channel.Reliable);

            NetworkSeedHandshake.SetStatus("Host seed requested");
        }
    }
}