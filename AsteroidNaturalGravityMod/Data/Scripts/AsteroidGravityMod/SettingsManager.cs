using System;
using System.IO; // For TextReader, TextWriter
using Sandbox.ModAPI; // For MyAPIGateway.Utilities.WriteFileInWorldStorage etc.
using VRage.Game.Components; // For typeof(MySessionComponentBase) - for world storage file path

namespace AsteroidGravityMod
{
    public class SettingsManager
    {
        private readonly string _configFileName;
        private readonly Logger _logger;
        private const int CurrentConfigVersion = 1; // New versioning constant

        public SettingsManager(Logger logger)
        {
            this._logger = logger;
            this._configFileName = Constants.ConfigFileName; // "AsteroidGravityMod.cfg"
            MyAPIGateway.Utilities.ShowNotification($"Mod {Constants.ModName} SettingsManager Initialized!", 1500, MyFontEnum.Green);
            this._logger.Info("SettingsManager initialized. Config file: " + this._configFileName);
        }

        /// <summary>
        /// Loads settings from the custom config file.
        /// If the file doesn't exist, default settings are returned and saved.
        /// </summary>
        /// <returns>Loaded or default ModSettings.</returns>
        public ModSettings LoadSettings()
        {
            ModSettings settings = new ModSettings(); // Always start with defaults
            bool fileExists = MyAPIGateway.Utilities.FileExistsInWorldStorage(this._configFileName, typeof(AsteroidGravityModMain));
            
            if (fileExists)
            {
                try
                {
                    using (TextReader reader = MyAPIGateway.Utilities.ReadFileInWorldStorage(this._configFileName, typeof(AsteroidGravityModMain)))
                    {
                        string line;
                        bool versionFound = false;
                        while ((line = reader.ReadLine()) != null)
                        {
                            if (string.IsNullOrWhiteSpace(line))
                                continue;

                            line = line.Trim();

                            // Check for config version
                            if (line.StartsWith("ConfigVersion=", StringComparison.OrdinalIgnoreCase))
                            {
                                versionFound = true;
                                int version;
                                if (int.TryParse(line.Substring("ConfigVersion=".Length), out version))
                                {
                                    if (version != CurrentConfigVersion)
                                    {
                                        _logger.Warning("Config version mismatch detected. Expected version " 
                                            + CurrentConfigVersion + " but found version " + version 
                                            + ". Defaults will be used for missing/new settings.");
                                    }
                                }
                                continue; // Skip this line for further processing
                            }

                            // Ignore comment lines (those starting with "//")
                            if (line.StartsWith("//"))
                                continue;

                            settings.LoadFromConfigLine(line);
                        }
                    }
                    _logger.Info("Mod settings loaded successfully from file.");
                    MyAPIGateway.Utilities.ShowNotification($"Mod {Constants.ModName} Settings Loaded from file.", 2000, MyFontEnum.Green);
                }
                catch (Exception ex)
                {
                    _logger.Error("Failed to load mod settings from " + _configFileName + ". Using default settings. Exception: " + ex.Message, ex);
                    MyAPIGateway.Utilities.ShowNotification($"Mod {Constants.ModName} Settings Load Failed! Using defaults.", 3000, MyFontEnum.Red);
                }
            }
            else
            {
                _logger.Info("Config file not found. Creating default settings and saving them.");
                MyAPIGateway.Utilities.ShowNotification($"Mod {Constants.ModName} Config File Not Found! Creating defaults.", 3000, MyFontEnum.Green);
                SaveSettings(settings);
            }

            ValidateSettings(settings); // Ensure settings are within acceptable ranges
            return settings;
        }

        /// <summary>
        /// Saves the current settings to the custom config file.
        /// This overwrites the existing file.
        /// </summary>
        /// <param name="settings">The ModSettings object to save.</param>
        public void SaveSettings(ModSettings settings)
        {
            try
            {
                using (TextWriter writer = MyAPIGateway.Utilities.WriteFileInWorldStorage(this._configFileName, typeof(AsteroidGravityModMain)))
                {
                    writer.WriteLine("// Asteroid Gravity Mod Configuration");
                    writer.WriteLine("// This file is automatically generated. Edit with care.");
                    writer.WriteLine("// To reset to defaults, use /ANGM resetconfig in game.");
                    writer.WriteLine($"ConfigVersion={CurrentConfigVersion}");
                    writer.WriteLine(settings.ToConfigString());
                }
                _logger.Info("Mod settings saved successfully to file.");
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to save mod settings to " + _configFileName + ": " + ex.Message, ex);
            }
        }

        /// <summary>
        /// Validates the settings to ensure they are within acceptable ranges.
        /// If a value is out-of-range, it will be reset to a default supported value.
        /// </summary>
        /// <param name="settings">The ModSettings object to validate.</param>
        private void ValidateSettings(ModSettings settings)
        {
            // Validate interval settings (should be positive)
            if (settings.CacheRefreshIntervalSeconds <= 0)
            {
                _logger.Warning("CacheRefreshIntervalSeconds is invalid. Resetting to default 10.");
                settings.CacheRefreshIntervalSeconds = 10;
            }
            if (settings.CoGProcessIntervalSeconds <= 0)
            {
                _logger.Warning("CoGProcessIntervalSeconds is invalid. Resetting to default 2.");
                settings.CoGProcessIntervalSeconds = 2;
            }
            if (settings.EntityScanIntervalSeconds <= 0)
            {
                _logger.Warning("EntityScanIntervalSeconds is invalid. Resetting to default 5.");
                settings.EntityScanIntervalSeconds = 5;
            }
            // Validate gravitational parameters
            if (settings.GravitationalDetectionRange <= 0)
            {
                _logger.Warning("GravitationalDetectionRange is invalid. Resetting to default 2500.0.");
                settings.GravitationalDetectionRange = 2500.0;
            }
            if (settings.BaseGravityStrength <= 0)
            {
                _logger.Warning("BaseGravityStrength must be positive. Resetting to default 1.0.");
                settings.BaseGravityStrength = 1.0f;
            }
            if (settings.MinAccel_G < 0)
            {
                _logger.Warning("MinAccel_G cannot be negative. Resetting to default 0.01.");
                settings.MinAccel_G = 0.01;
            }
            if (settings.MaxAccel_G < settings.MinAccel_G)
            {
                _logger.Warning("MaxAccel_G is less than MinAccel_G. Resetting to default 0.2.");
                settings.MaxAccel_G = 0.2;
            }
            if (settings.SizeDivisor <= 0)
            {
                _logger.Warning("SizeDivisor must be positive. Resetting to default 1.0.");
                settings.SizeDivisor = 1f;
            }
            if (settings.GravityDisableThreshold_G < 0)
            {
                _logger.Warning("GravityDisableThreshold_G cannot be negative. Resetting to default 0.01.");
                settings.GravityDisableThreshold_G = 0.01f;
            }
        }
    }
}
