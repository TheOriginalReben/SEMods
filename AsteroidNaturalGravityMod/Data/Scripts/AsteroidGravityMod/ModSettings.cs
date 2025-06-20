// ModSettings.cs
// Defines the configurable parameters for the Asteroid Gravity Mod.
// Manually parsed/written by SettingsManager for external .cfg file.

using System; // Required for [Serializable]
using ProtoBuf; // Still used for compatibility if the game engine tries to protobuf this.
using System.Xml.Serialization; // XmlIgnore needed for derived properties.

// All classes are in the same namespace and folder, so implicit visibility should work.

namespace AsteroidGravityMod
{
    [ProtoContract]
    [Serializable] // Keep for general .NET compatibility; SettingsManager will do custom text parsing.
    public class ModSettings
    {
        // Gravity Control
        [ProtoMember(1)]
        public float BaseGravityStrength = 1.0f; // Base gravity multiplier

        [ProtoMember(2)]
        public double GravitationalDetectionRange = 2500.0; // Max distance in meters for gravity detection

        [ProtoMember(3)]
        public double MinAccel_G = 0.01; // Minimum acceleration in Gs (0.01g)

        [ProtoMember(4)]
        public double MaxAccel_G = 0.2;  // Maximum acceleration in Gs (0.2g)

        [ProtoMember(5)]
        public float SizeDivisor = 1f; // Adjusts how much asteroid size influences gravity

        [ProtoMember(6)]
        public float GravityDisableThreshold_G = 0.01f; // Minimum G force to apply gravity / enable alignment

        // Performance & Update Frequencies (in seconds, will be converted to ticks)
        [ProtoMember(7)]
        public int CacheRefreshIntervalSeconds = 10; // How often to scan for new asteroids to cache

        [ProtoMember(8)]
        public int CoGProcessIntervalSeconds = 2;   // 2 seconds (120 ticks) - For precise CoG processing

        [ProtoMember(9)]
        public int EntityScanIntervalSeconds = 5;   // 5 seconds (300 ticks)

        // Mod Functionality Toggles
        [ProtoMember(13)]
        public bool EnablePreciseCoG = true; // Defaulted to true - Re-enabled as precise CoG is possible

        [ProtoMember(14)]
        public bool DebugMode = false; // Debug mode toggle

        // Derived properties for convenience/performance (not serialized)
        [XmlIgnore]
        public double GravitationalDetectionRangeSq => GravitationalDetectionRange * GravitationalDetectionRange;

        [XmlIgnore]
        public float GravityDisableThresholdSq => (float)(GravityDisableThreshold_G * Constants.G * GravityDisableThreshold_G * Constants.G);

        [XmlIgnore]
        public int CacheRefreshTicks => CacheRefreshIntervalSeconds * 60;

        [XmlIgnore]
        public int CoGProcessIntervalTicks => CoGProcessIntervalSeconds * 60; 

        [XmlIgnore]
        public int EntityScanTicks => EntityScanIntervalSeconds * 60;

        [XmlIgnore]
        public double MinAccel_ms2 => MinAccel_G * Constants.G; // In m/s^2

        [XmlIgnore]
        public double MaxAccel_ms2 => MaxAccel_G * Constants.G; // In m/s^2

        public ModSettings() { } // Required for external parsing

        public void ResetToDefaults()
        {
            // Reset all properties to their original default values
            BaseGravityStrength = 1.0f;
            GravitationalDetectionRange = 2500.0;
            MinAccel_G = 0.01;
            MaxAccel_G = 0.2;
            SizeDivisor = 1f;
            GravityDisableThreshold_G = 0.01f;

            CacheRefreshIntervalSeconds = 10;
            CoGProcessIntervalSeconds = 2;
            EntityScanIntervalSeconds = 5;

            EnablePreciseCoG = true; // Reset to true
            DebugMode = false;
        }

        /// <summary>
        /// Converts the current settings to a string format suitable for the config file.
        /// </summary>
        public string ToConfigString()
        {
            return "BaseGravityStrength=" + BaseGravityStrength.ToString() + "\n" +
                   "GravitationalDetectionRange=" + GravitationalDetectionRange.ToString() + "\n" +
                   "MinAccel_G=" + MinAccel_G.ToString() + "\n" +
                   "MaxAccel_G=" + MaxAccel_G.ToString() + "\n" +
                   "SizeDivisor=" + SizeDivisor.ToString() + "\n" +
                   "GravityDisableThreshold_G=" + GravityDisableThreshold_G.ToString() + "\n" +
                   "CacheRefreshIntervalSeconds=" + CacheRefreshIntervalSeconds.ToString() + "\n" +
                   "CoGProcessIntervalSeconds=" + CoGProcessIntervalSeconds.ToString() + "\n" +
                   "EntityScanIntervalSeconds=" + EntityScanIntervalSeconds.ToString() + "\n" +
                   "EnablePreciseCoG=" + EnablePreciseCoG.ToString() + "\n" +
                   "DebugMode=" + DebugMode.ToString();
        }

        /// <summary>
        /// Loads a single setting from a line in the config file.
        /// </summary>
        /// <param name="line">A line from the config file, e.g., "SettingName=Value".</param>
        public void LoadFromConfigLine(string line)
        {
            int equalsIndex = line.IndexOf('=');
            if (equalsIndex > 0)
            {
                string key = line.Substring(0, equalsIndex);
                string value = line.Substring(equalsIndex + 1);

                float floatVal;
                double doubleVal;
                int intVal;
                bool boolVal;

                switch (key)
                {
                    case "BaseGravityStrength": if (float.TryParse(value, out floatVal)) BaseGravityStrength = floatVal; break;
                    case "GravitationalDetectionRange": if (double.TryParse(value, out doubleVal)) GravitationalDetectionRange = doubleVal; break;
                    case "MinAccel_G": if (double.TryParse(value, out doubleVal)) MinAccel_G = doubleVal; break;
                    case "MaxAccel_G": if (double.TryParse(value, out doubleVal)) MaxAccel_G = doubleVal; break;
                    case "SizeDivisor": if (float.TryParse(value, out floatVal)) SizeDivisor = floatVal; break;
                    case "GravityDisableThreshold_G": if (float.TryParse(value, out floatVal)) GravityDisableThreshold_G = floatVal; break;
                    case "CacheRefreshIntervalSeconds": if (int.TryParse(value, out intVal)) CacheRefreshIntervalSeconds = intVal; break;
                    case "CoGProcessIntervalSeconds": if (int.TryParse(value, out intVal)) CoGProcessIntervalSeconds = intVal; break;
                    case "EntityScanIntervalSeconds": if (int.TryParse(value, out intVal)) EntityScanIntervalSeconds = intVal; break;
                    case "EnablePreciseCoG": if (bool.TryParse(value, out boolVal)) EnablePreciseCoG = boolVal; break;
                    case "DebugMode": if (bool.TryParse(value, out boolVal)) DebugMode = boolVal; break;
                }
            }
        }
    }
}
