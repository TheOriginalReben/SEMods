// Logger.cs
// Provides custom logging functionality for the mod by writing to a file in world storage.

using System;
using System.IO; // Used for TextReader/TextWriter, but implicitly via MyAPIGateway.Utilities.
using Sandbox.ModAPI; // For MyAPIGateway.Utilities.WriteFileInWorldStorage, ShowNotification
using VRage.Utils; // For MyLog, MyFontEnum
using VRage.Game.Components; // For typeof(MySessionComponentBase) - for world storage file path

namespace AsteroidGravityMod
{
    public class Logger
    {
        private readonly string _modName;
        private readonly string _logFileName;
        private static bool _firstWriteThisSession = true; // Flag to indicate if it's the very first write to the file this game session
        private ModSettings _settings; // Reference to ModSettings to check DebugMode

        // Public property to check if debug logging is enabled
        public bool IsDebugEnabled => _settings != null && _settings.DebugMode;

        // Constructor now accepts ModSettings to read debug mode state
        public Logger(string modName, ModSettings settings)
        {
            _modName = modName;
            _logFileName = Constants.LogFileName; // "AsteroidGravityMod.log"
            _settings = settings; // Assign settings reference

            // On first initialization of the logger for this game session, clear the log file.
            if (_firstWriteThisSession)
            {
                ClearLogFile();
                _firstWriteThisSession = false;
            }
            // Add a notification to confirm Logger initialization
            MyAPIGateway.Utilities.ShowNotification($"Mod {Constants.ModName} Logger Initialized!", 1500, MyFontEnum.Green);
            Info("Logger initialized.");
        }

        /// <summary>
        /// Updates the internal reference to ModSettings. This is called after settings are loaded from file.
        /// </summary>
        /// <param name="newSettings">The newly loaded ModSettings object.</param>
        public void UpdateSettingsReference(ModSettings newSettings)
        {
            _settings = newSettings;
            Info("Logger's settings reference updated.");
        }

        // Clears the log file by opening it in overwrite mode and immediately closing.
        private void ClearLogFile()
        {
            try
            {
                // WriteFileInWorldStorage defaults to overwrite, so simply opening and closing clears it.
                // Pass typeof(AsteroidGravityModMain) to ensure it's stored under your mod's folder.
                TextWriter writer = MyAPIGateway.Utilities.WriteFileInWorldStorage(_logFileName, typeof(AsteroidGravityModMain));
                writer.Close(); // Close immediately to clear content
                MyLog.Default.WriteLine($"[{_modName}] [INFO] Log file '{_logFileName}' cleared in world storage.");
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLine($"[{_modName}] [ERROR] Failed to clear log file '{_logFileName}' in world storage: {ex.Message}");
            }
        }

        // Writes a message to the custom log file by reading existing content, appending, and rewriting.
        // This simulates appending in a sandboxed environment where true append mode might not be available.
        private void WriteToWorldStorageFile(string level, string message, bool prependTimestamp = true)
        {
            try
            {
                string existingContent = string.Empty;
                // Check if file exists before trying to read it
                if (MyAPIGateway.Utilities.FileExistsInWorldStorage(_logFileName, typeof(AsteroidGravityModMain)))
                {
                    TextReader reader = MyAPIGateway.Utilities.ReadFileInWorldStorage(_logFileName, typeof(AsteroidGravityModMain));
                    existingContent = reader.ReadToEnd();
                    reader.Close();
                }

                // Create a new TextWriter which will overwrite the file
                TextWriter writer = MyAPIGateway.Utilities.WriteFileInWorldStorage(_logFileName, typeof(AsteroidGravityModMain));
                writer.Write(existingContent); // Write back old content

                string formattedMessage = prependTimestamp ?
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{_modName}] [{level}] {message}" :
                    $"[{_modName}] [{level}] {message}";
                writer.WriteLine(formattedMessage); // Write new content
                writer.Close(); // Close to flush and save
            }
            catch (Exception ex)
            {
                // Fallback to MyLog.Default if writing to world storage fails
                MyLog.Default.WriteLine($"[{_modName}] [FATAL_LOG_ERROR] Failed to write to mod log file: {ex.Message}");
            }
        }

        public void Debug(string message)
        {
            // Only write debug messages to file and MyLog.Default if debug mode is enabled in settings
            if (IsDebugEnabled)
            {
                WriteToWorldStorageFile("DEBUG", message);
                MyLog.Default.WriteLine($"[{_modName}] [DEBUG] {message}");
            }
        }

        public void Info(string message)
        {
            WriteToWorldStorageFile("INFO", message);
            MyLog.Default.WriteLine($"[{_modName}] [INFO] {message}");
        }

        public void Warning(string message)
        {
            WriteToWorldStorageFile("WARNING", message);
            MyAPIGateway.Utilities.ShowNotification($"MOD WARNING: {message}", 3000, MyFontEnum.Debug); // Using MyFontEnum.Debug
            MyLog.Default.WriteLine($"[{_modName}] [WARNING] {message}");
        }

        public void Error(string message, Exception ex = null)
        {
            string fullMessage = message;
            if (ex != null)
            {
                fullMessage += $"\nException: {ex.Message}\nStackTrace:\n{ex.StackTrace}";
            }
            WriteToWorldStorageFile("ERROR", fullMessage);
            MyAPIGateway.Utilities.ShowNotification($"MOD ERROR: {message}", 5000, MyFontEnum.Red);
            MyLog.Default.WriteLine($"[{_modName}] [ERROR] {fullMessage}"); // Always write critical errors to game's log
        }
    }
}
