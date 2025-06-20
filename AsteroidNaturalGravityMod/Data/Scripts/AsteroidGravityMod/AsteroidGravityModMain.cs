// AsteroidGravityModMain.cs
// The main session component for the Asteroid Natural Gravity Mod.
// This is the entry point for the mod within Space Engineers.

using System; // For Exception, StringComparison
using System.Collections.Generic; // For Dictionary, HashSet, List
using System.Collections.Concurrent; // For ConcurrentQueue
using VRage.Game.Components; // For MySessionComponentDescriptor, MySessionComponentBase
using VRage.Game.ModAPI; // For IMyCharacter, IMyCubeGrid, IMyFloatingObject
using VRageMath; // For Vector3D, Vector4
using Sandbox.ModAPI; // For MyAPIGateway
using VRage.Utils; // For MyLog, MyFontEnum
using VRage.Input; // For MyKeys
using VRageRender; // For MySimpleObjectDraw (used by DebugRenderer indirectly via class instantiation)
using Sandbox.Engine.Physics; // For MyPhysics (used by ApplyGravityToDynamicEntities)
using ParallelTasks; // Required for MyAPIGateway.Parallel.Start

// All classes are in the same namespace and folder, so implicit visibility should work.

namespace AsteroidGravityMod
{
    [MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation)]
    public class AsteroidGravityModMain : MySessionComponentBase
    {
        private ModSettings _settings; // All configurable settings
        private AsteroidCache _asteroidCache;
        private GravityCalculator _gravityCalculator;
        private DebugRenderer _debugRenderer;
        private Logger _logger; // Our custom logger (now uses MyAPIGateway.Utilities.WriteFileInWorldStorage)
        private SettingsManager _settingsManager; // Our custom settings manager (uses custom file)

        private IMyCharacter _localCharacter;
        private int _tickCounter = 0;
        private int _entityScanTimer = 0;
        private int _cacheRefreshTimer = 0; // New timer for cache refreshing
        private int _coGProcessTimer = 0; // New timer for CoG processing

        private Dictionary<long, IMyEntity> _trackedDynamicEntities = new Dictionary<long, IMyEntity>();
        private HashSet<IMyEntity> _scanResultsCache = new HashSet<IMyEntity>();
        private List<long> _entitiesToRemoveFromTracked = new List<long>();

        // Queues for asynchronous CoG calculation
        private ConcurrentQueue<long> _asteroidsNeedingCoGQueue = new ConcurrentQueue<long>(); // Asteroids waiting for a job
        private ConcurrentQueue<CoGResult> _completedCoGResultsQueue = new ConcurrentQueue<CoGResult>(); // Results from completed jobs

        private bool _initialScanCompleted = false;

        // No longer using MyObjectBuilder_SessionComponent for direct settings persistence.
        // We manage settings via SettingsManager. Remove the GetObjectBuilder override.
        public override MyObjectBuilder_SessionComponent GetObjectBuilder()
        {
            // For mods managing their own external config, this method can return null or base.GetObjectBuilder()
            // if no other data needs to be saved via the session component system.
            return null;
        }

        public override void Init(MyObjectBuilder_SessionComponent sessionComponent)
        {
            base.Init(sessionComponent);

            // Add an immediate notification to confirm Mod Init started.
            MyAPIGateway.Utilities.ShowNotification($"Mod {Constants.ModName} Init Started!", 2000, MyFontEnum.Green);

            try
            {
                // Initialize Logger (needs settings to determine debug status).
                // First, create a default settings instance to pass to Logger,
                // then load the actual settings from file.
                this._settings = new ModSettings();
                this._logger = new Logger(Constants.ModName, this._settings); // Pass the default settings to the logger constructor
                this._logger.Info("Asteroid Gravity Mod Initialization begins...");

                // Now initialize SettingsManager, which will load/save the real settings.
                this._settingsManager = new SettingsManager(this._logger);
                ModSettings loadedSettings = this._settingsManager.LoadSettings();
                
                // Assign the loaded settings back to _settings, and update the logger's settings reference.
                // This ensures the logger correctly uses the loaded DebugMode setting.
                this._settings = loadedSettings;
                this._logger.UpdateSettingsReference(this._settings); 

                // Initialize other components with necessary dependencies
                this._asteroidCache = new AsteroidCache(this._settings, null, this._asteroidsNeedingCoGQueue, this._logger); // Pass the queue
                this._gravityCalculator = new GravityCalculator(this._settings, this._asteroidCache, this._logger);
                this._debugRenderer = new DebugRenderer(this._settings, this._asteroidCache, this._logger);

                this._logger.Info("Asteroid Gravity Mod Loaded (v1.5.5 - With Precise CoG & Custom Config)");
                MyAPIGateway.Utilities.ShowNotification("Asteroid Gravity Mod Loaded (v1.5.5 - With Precise CoG & Custom Config)", 5000);
                MyAPIGateway.Utilities.MessageEntered += this.HandleChatCommand;
            }
            catch (Exception ex)
            {
                // Log critical errors during initialization.
                // We attempt to use the logger, but also MyLog.Default as a fallback if logger itself failed.
                string errorMessage = $"AsteroidGravityMod: Critical Error during Init: {ex.Message}\n{ex.StackTrace}";
                
                if (this._logger != null)
                {
                    this._logger.Error(errorMessage);
                }
                else
                {
                    MyLog.Default.WriteLine(errorMessage);
                }
                MyAPIGateway.Utilities.ShowNotification($"MOD INIT FAILED: {ex.Message}", 8000, MyFontEnum.Red);
            }
        }

        protected override void UnloadData()
        {
            MyAPIGateway.Utilities.MessageEntered -= this.HandleChatCommand;
            
            // Clear queues and null out references to allow GC to clean up
            long dummyId;
            while (this._asteroidsNeedingCoGQueue.TryDequeue(out dummyId)) { } // C# 6 compatible

            CoGResult dummyResult;
            while (this._completedCoGResultsQueue.TryDequeue(out dummyResult)) { } // C# 6 compatible

            // Save settings on unload to ensure changes persist
            this._settingsManager?.SaveSettings(this._settings); 

            // Log unload, then nullify
            if (this._logger != null) // Check if logger is initialized before using
            {
                this._logger.Info("Unloading Asteroid Gravity Mod.");
            }

            this._settings = null;
            this._asteroidCache = null;
            this._gravityCalculator = null;
            this._debugRenderer = null;
            this._localCharacter = null;
            this._trackedDynamicEntities.Clear();
            this._scanResultsCache.Clear();
            this._entitiesToRemoveFromTracked.Clear();
            this._initialScanCompleted = false;
            this._logger = null; // Nullify logger last
            this._settingsManager = null; // Nullify settings manager
        }

        // --- Private Helper Methods for UpdateBeforeSimulation ---

        private void HandleChatCommand(string messageText, ref bool sendToOthers)
        {
            // Always consume the command if it starts with "/ANGM"
            if (!messageText.StartsWith("/ANGM", StringComparison.OrdinalIgnoreCase))
            {
                return; // Not our command
            }
            sendToOthers = false; // Prevent command from showing in chat

            string[] parts = messageText.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length < 2)
            {
                MyAPIGateway.Utilities.ShowNotification("Invalid ANGM command. Type '/ANGM help' for a list.", 3000, MyFontEnum.Red);
                if (this._logger.IsDebugEnabled) // Conditional debug log
                    this._logger.Debug("Invalid ANGM command: too few parts.");
                return;
            }

            string category = parts[1].ToLowerInvariant(); // Lowercase for easier comparison

            switch (category)
            {
                case "help":
                    this.DisplayHelpMessage();
                    if (this._logger.IsDebugEnabled) // Conditional debug log
                        this._logger.Debug("Displayed help message.");
                    break;

                case "debug":
                    this.ToggleDebugCommand(parts);
                    break;

                case "cog": // Re-enabled CoG command
                    this.ToggleCoGCommand(parts);
                    break;

                case "gravity":
                    this.HandleGravityCommands(parts);
                    break;

                case "perf":
                    this.HandlePerformanceCommands(parts);
                    break;

                case "player": // This category will now show no commands or an error if subcommands are invalid
                    this.HandlePlayerCommands(parts);
                    break;

                case "saveconfig":
                    this.SaveConfigCommand();
                    break;

                case "loadconfig":
                    this.LoadConfigCommand();
                    break;

                case "resetconfig":
                    this.ResetConfigCommand();
                    break;

                default:
                    MyAPIGateway.Utilities.ShowNotification($"Unknown ANGM command '{category}'. Type '/ANGM help' for a list.", 3000, MyFontEnum.Red);
                    this._logger.Warning($"Unknown ANGM command: {category}.");
                    break;
            }
        }

        private void DisplayHelpMessage()
        {
            string helpMessage =
                "--- Asteroid Gravity Mod Commands (v1.5.5 - With Precise CoG & Custom Config) ---\n" +
                "General:\n" +
                "  /ANGM help              : Displays this list of commands.\n" +
                "  /ANGM debug [on/off]    : Toggles debug visuals and notifications.\n" +
                "  /ANGM saveconfig        : Saves current settings to " + Constants.ConfigFileName + ".\n" +
                "  /ANGM loadconfig        : Reloads settings from " + Constants.ConfigFileName + ".\n" +
                "  /ANGM resetconfig       : Resets all settings to defaults and saves them.\n" +
                "\n" +
                "Gravity Settings:\n" +
                "  /ANGM CoG [on/off]      : Toggles precise Center of Gravity calculation.\n" +
                "  /ANGM gravity strength <float> : Adjusts base gravity multiplier (e.g., 1.0).\n" +
                "  /ANGM gravity range <double>   : Max gravity detection distance in meters (e.g., 2500).\n" +
                "  /ANGM gravity minaccel <G>     : Min acceleration in Gs (e.g., 0.01).\n" +
                "  /ANGM gravity maxaccel <G>     : Max acceleration in Gs (e.g., 0.2).\n" +
                "  /ANGM gravity sizediv <float>  : Adjusts size influence on gravity (e.g., 1.0).\n" +
                "  /ANGM gravity threshold <G>    : Min G force for gravity/alignment (e.g., 0.01).\n" +
                "\n" +
                "Performance & Update Frequencies (values in seconds):\n" +
                "  /ANGM perf cache <int>         : Asteroid scan interval (e.g., 10s).\n" +
                "  /ANGM perf coginterval <int>   : Precise CoG calculation interval for one asteroid (e.g., 2s).\n" +
                "  /ANGM perf entityscan <int>    : Dynamic entity scan interval (e.g., 5s).\n" +
                "\n" +
                "Player Orientation & Raycast:\n" +
                "  NOTE: Player alignment feature has been removed.\n" +
                "----------------------------------------------------------------------\n" +
                "Note: All commands and arguments are case-insensitive. Values are applied immediately.";

            MyAPIGateway.Utilities.ShowMessage("Asteroid Gravity Mod Help", helpMessage);
        }

        private void ToggleDebugCommand(string[] parts)
        {
            if (parts.Length == 3)
            {
                string arg = parts[2].ToLowerInvariant();
                bool newState;
                if (arg == "on") newState = true;
                else if (arg == "off") newState = false;
                else
                {
                    MyAPIGateway.Utilities.ShowNotification("Usage: /ANGM debug [on/off]", 3000, MyFontEnum.Red);
                    if (this._logger.IsDebugEnabled) // Conditional debug log
                        this._logger.Debug("Invalid /ANGM debug command usage.");
                    return;
                }

                if (this._settings.DebugMode != newState)
                {
                    this._settings.DebugMode = newState;
                    MyAPIGateway.Utilities.ShowNotification("Debug Mode is now: " + (this._settings.DebugMode ? "Enabled" : "Disabled"), 2000);
                    this._logger.Info("Debug Mode set to: " + this._settings.DebugMode);
                }
                else
                {
                    MyAPIGateway.Utilities.ShowNotification("Debug Mode is already: " + (this._settings.DebugMode ? "Enabled" : "Disabled"), 2000);
                    if (this._logger.IsDebugEnabled) // Conditional debug log
                        this._logger.Debug("Debug Mode command: state unchanged.");
                }
            }
            else
            {
                MyAPIGateway.Utilities.ShowNotification("Usage: /ANGM debug [on/off]", 3000, MyFontEnum.Red);
                this._logger.Warning("Invalid /ANGM debug command format.");
            }
        }

        private void ToggleCoGCommand(string[] parts)
        {
            if (parts.Length == 3)
            {
                string arg = parts[2].ToLowerInvariant();
                bool newState;
                if (arg == "on") newState = true;
                else if (arg == "off") newState = false;
                else
                {
                    MyAPIGateway.Utilities.ShowNotification("Usage: /ANGM CoG [on/off]", 3000, MyFontEnum.Red);
                    this._logger.Warning("Invalid /ANGM CoG command usage.");
                    return;
                }

                if (this._settings.EnablePreciseCoG != newState)
                {
                    this._settings.EnablePreciseCoG = newState;
                    MyAPIGateway.Utilities.ShowNotification("Precise CoG calculation is now: " + (this._settings.EnablePreciseCoG ? "Enabled" : "Disabled"), 3000);
                    this._logger.Info("Precise CoG calculation set to: " + this._settings.EnablePreciseCoG);
                }
                else
                {
                    MyAPIGateway.Utilities.ShowNotification("Precise CoG calculation is already: " + (this._settings.EnablePreciseCoG ? "Enabled" : "Disabled"), 2000);
                    if (this._logger.IsDebugEnabled) // Conditional debug log
                        this._logger.Debug("Precise CoG command: state unchanged.");
                }
            }
            else
            {
                MyAPIGateway.Utilities.ShowNotification("Usage: /ANGM CoG [on/off]", 3000, MyFontEnum.Red);
                this._logger.Warning("Invalid /ANGM CoG command format.");
            }
        }

        private void HandleGravityCommands(string[] parts)
        {
            if (parts.Length < 3)
            {
                MyAPIGateway.Utilities.ShowNotification("Usage: /ANGM gravity [setting] <value>", 3000, MyFontEnum.Red);
                if (this._logger.IsDebugEnabled) // Conditional debug log
                    this._logger.Debug("Invalid /ANGM gravity command: too few parts.");
                return;
            }

            string setting = parts[2].ToLowerInvariant();
            if (parts.Length < 4)
            {
                MyAPIGateway.Utilities.ShowNotification("Missing value for /ANGM gravity " + setting + ". See /ANGM help.", 3000, MyFontEnum.Red);
                if (this._logger.IsDebugEnabled) // Conditional debug log
                    this._logger.Debug("Missing value for /ANGM gravity " + setting + ".");
                return;
            }

            float floatVal; // C# 6 compatible declaration
            double doubleVal; // C# 6 compatible declaration

            bool updated = false;
            if (float.TryParse(parts[3], out floatVal))
            {
                switch (setting)
                {
                    case "strength":
                        this._settings.BaseGravityStrength = floatVal; updated = true; break;
                    case "sizediv":
                        this._settings.SizeDivisor = floatVal; updated = true; break;
                    case "threshold":
                        this._settings.GravityDisableThreshold_G = floatVal; updated = true; break;
                }
            }
            // Use else if for double.TryParse
            else if (double.TryParse(parts[3], out doubleVal))
            {
                switch (setting)
                {
                    case "range":
                        this._settings.GravitationalDetectionRange = doubleVal; updated = true; break;
                    case "minaccel":
                        this._settings.MinAccel_G = doubleVal; updated = true; break;
                    case "maxaccel":
                        this._settings.MaxAccel_G = doubleVal; updated = true; break;
                }
            }

            if (updated)
            {
                MyAPIGateway.Utilities.ShowNotification("Gravity " + setting + " set to " + parts[3] + ". Current: " + this._settings.BaseGravityStrength.ToString("0.0") + " strength, " + this._settings.GravitationalDetectionRange.ToString("0") + "m range, " + this._settings.MinAccel_G.ToString("0.00") + "G minaccel, " + this._settings.MaxAccel_G.ToString("0.00") + "G maxaccel, " + this._settings.SizeDivisor.ToString("0.0") + " sizediv, " + this._settings.GravityDisableThreshold_G.ToString("0.00") + "G threshold", 4000);
                this._logger.Info("Gravity setting '" + setting + "' updated to " + parts[3] + ".");
            }
            else
            {
                MyAPIGateway.Utilities.ShowNotification("Invalid value or setting for /ANGM gravity " + setting + ". See /ANGM help.", 3000, MyFontEnum.Red);
                this._logger.Warning("Invalid value or setting for /ANGM gravity " + setting + ": " + parts[3] + ".");
            }
        }

        private void HandlePerformanceCommands(string[] parts)
        {
            if (parts.Length < 3)
            {
                MyAPIGateway.Utilities.ShowNotification("Usage: /ANGM perf [setting] <seconds>", 3000, MyFontEnum.Red);
                if (this._logger.IsDebugEnabled) // Conditional debug log
                    this._logger.Debug("Invalid /ANGM perf command: too few parts.");
                return;
            }

            string setting = parts[2].ToLowerInvariant();
            
            int intVal; // C# 6 compatible declaration
            if (parts.Length < 4 || !int.TryParse(parts[3], out intVal) || intVal <= 0)
            {
                MyAPIGateway.Utilities.ShowNotification("Invalid value for /ANGM perf " + setting + ". Must be a positive integer in seconds. See /ANGM help.", 3000, MyFontEnum.Red);
                if (this._logger.IsDebugEnabled) // Conditional debug log
                    this._logger.Debug("Invalid value for /ANGM perf " + setting + ": " + parts[3] + ". Must be positive integer.");
                return;
            }

            bool updated = false;
            switch (setting)
            {
                case "cache":
                    this._settings.CacheRefreshIntervalSeconds = intVal; updated = true; break;
                case "coginterval": // Re-enabled coginterval
                    this._settings.CoGProcessIntervalSeconds = intVal; updated = true; break;
                case "entityscan":
                    this._settings.EntityScanIntervalSeconds = intVal; updated = true; break;
            }

            if (updated)
            {
                MyAPIGateway.Utilities.ShowNotification("Performance " + setting + " set to " + intVal + " seconds. Current: " + this._settings.CacheRefreshIntervalSeconds + "s cache, " + this._settings.CoGProcessIntervalSeconds + "s cog, " + this._settings.EntityScanIntervalSeconds + "s entity scan", 4000);
                this._logger.Info("Performance setting '" + setting + "' updated to " + intVal + " seconds.");
            }
            else
            {
                MyAPIGateway.Utilities.ShowNotification("Unknown setting for /ANGM perf " + setting + ". See /ANGM help.", 3000, MyFontEnum.Red);
                this._logger.Warning("Unknown setting for /ANGM perf " + setting + ": " + parts[2] + ".");
            }
        }

        private void HandlePlayerCommands(string[] parts)
        {
            MyAPIGateway.Utilities.ShowNotification("Player alignment features have been removed. No commands available in this category.", 3000);
            this._logger.Info("Player commands invoked, but feature removed.");
        }

        private void SaveConfigCommand()
        {
            this._settingsManager.SaveSettings(this._settings);
            MyAPIGateway.Utilities.ShowNotification("Mod settings saved to file: " + Constants.ConfigFileName, 3000);
            this._logger.Info("Mod settings saved via command.");
        }

        private void LoadConfigCommand()
        {
            this._settings = this._settingsManager.LoadSettings();
            // Important: Update logger's internal settings reference after loading new settings
            this._logger.UpdateSettingsReference(this._settings);
            MyAPIGateway.Utilities.ShowNotification("Mod settings loaded from file: " + Constants.ConfigFileName, 4000);
            MyAPIGateway.Utilities.ShowNotification("Changes will apply to currently active settings.", 4000);
            this._logger.Info("Mod settings loaded via command.");
        }

        private void ResetConfigCommand()
        {
            this._settings.ResetToDefaults();
            // Important: Update logger's internal settings reference after resetting to defaults
            this._logger.UpdateSettingsReference(this._settings);
            this._settingsManager.SaveSettings(this._settings); // Save defaults immediately
            MyAPIGateway.Utilities.ShowNotification("All mod settings reset to default values and saved.", 3000);
            this._logger.Info("All mod settings reset to default values and saved.");
        }

        // --- Helper Methods ---

        private void PerformInitialAsteroidScan()
        {
            if (this._localCharacter == null && MyAPIGateway.Session.LocalHumanPlayer != null)
            {
                this._localCharacter = MyAPIGateway.Session.LocalHumanPlayer.Character;
            }

            if (this._localCharacter != null)
            {
                this._asteroidCache.CacheAsteroids(this._localCharacter.GetPosition());
                this._initialScanCompleted = true;
                this._logger.Info("Initial asteroid scan completed.");
                MyAPIGateway.Utilities.ShowNotification("Initial asteroid scan completed.", 2000);
            }
            else
            {
                if (this._logger.IsDebugEnabled) // Conditional debug log
                    this._logger.Debug("Initial asteroid scan skipped: local character not available.");
            }
        }

        private void RefreshAsteroidCache()
        {
            this._cacheRefreshTimer++;
            if (this._cacheRefreshTimer >= this._settings.CacheRefreshTicks)
            {
                if (this._localCharacter != null)
                {
                    this._asteroidCache.CacheAsteroids(this._localCharacter.GetPosition());
                }
                this._cacheRefreshTimer = 0;
            }
        }

        // This method now handles queuing and processing of Precise CoG calculations.
        private void ProcessCoGCalculations()
        {
            if (!this._settings.EnablePreciseCoG)
            {
                // If precise CoG is disabled, clear any pending jobs
                long dummyId; // C# 6 compatible
                while (this._asteroidsNeedingCoGQueue.TryDequeue(out dummyId)) { }
                return;
            }

            this._coGProcessTimer++;
            if (this._coGProcessTimer >= this._settings.CoGProcessIntervalTicks)
            {
                this._coGProcessTimer = 0; // Reset timer regardless of whether a job is started

                // C# 6 compatible out variable declaration
                long asteroidId;
                // Try to dequeue an asteroid ID that needs CoG calculation
                if (this._asteroidsNeedingCoGQueue.TryDequeue(out asteroidId))
                {
                    IMyVoxelMap voxelMap = MyAPIGateway.Entities.GetEntityById(asteroidId) as IMyVoxelMap;
                    if (voxelMap?.Storage != null)
                    {
                        // Start a new asynchronous job for CoG calculation
                        // Create a new instance of the job and pass its DoWork method as an Action
                        MyAPIGateway.Parallel.Start(new PreciseCoGCalculationJob(asteroidId, voxelMap, this._completedCoGResultsQueue, this._logger).DoWork);
                        if (this._logger.IsDebugEnabled) // Conditional debug log
                            this._logger.Debug("Queued CoG job for asteroid " + asteroidId + ".");
                    }
                    else
                    {
                        // If voxel map is invalid, mark as calculated with geometric center
                        // This handles cases where an asteroid might be removed or invalid before its CoG is calculated.
                        this._asteroidCache.UpdateAsteroidCoGResult(new CoGResult { EntityId = asteroidId, CoGPosition = Vector3D.Zero, Success = false });
                        if (this._logger.IsDebugEnabled) // Conditional debug log
                            this._logger.Debug("Skipped CoG for " + asteroidId + ": invalid voxel map.");
                        if (this._settings.DebugMode) // Conditional debug notification
                            MyAPIGateway.Utilities.ShowNotification("DEBUG: Skipped CoG for " + asteroidId + ": invalid voxel map.", 2000, MyFontEnum.Debug);
                    }
                }
            }
        }

        // New method to process completed CoG results
        private void ProcessCompletedCoGResults()
        {
            CoGResult result; // C# 6 compatible
            while (this._completedCoGResultsQueue.TryDequeue(out result))
            {
                // Update the AsteroidCache with the completed result on the main thread
                this._asteroidCache.UpdateAsteroidCoGResult(result);
            }
        }

        private void ApplyGravityToLocalCharacter()
        {
            this._localCharacter = MyAPIGateway.Session.LocalHumanPlayer?.Character;
            if (this._localCharacter?.Physics == null) return;

            Vector3D playerPos = this._localCharacter.GetPosition();
            Vector3D totalGravity = this._gravityCalculator.ComputeTotalGravity(playerPos);

            if (totalGravity.LengthSquared() > this._settings.GravityDisableThresholdSq)
            {
                this._localCharacter.Physics.LinearVelocity += totalGravity * Constants.TickSeconds;
            }
        }

        private void ApplyGravityToDynamicEntities()
        {
            this._entityScanTimer++;
            if (this._entityScanTimer >= this._settings.EntityScanTicks)
            {
                this._scanResultsCache.Clear();
                // Get grids and floating objects that have physics and are active
                MyAPIGateway.Entities.GetEntities(this._scanResultsCache, e =>
                    (e is IMyCubeGrid || e is IMyFloatingObject) && e.Physics != null && e.Physics.Enabled && e != this._localCharacter);
                this._entityScanTimer = 0;

                Vector3D playerPosForScan = this._localCharacter?.GetPosition() ?? Vector3D.Zero;

                foreach (IMyEntity entity in this._scanResultsCache)
                {
                    // Track entities if local player is null (server) or if within a broader range
                    if (this._localCharacter == null || (entity.GetPosition() - playerPosForScan).LengthSquared() < this._settings.GravitationalDetectionRangeSq * 2.0)
                    {
                        if (!this._trackedDynamicEntities.ContainsKey(entity.EntityId))
                        {
                            this._trackedDynamicEntities.Add(entity.EntityId, entity);
                        }
                    }
                }
            }

            this._entitiesToRemoveFromTracked.Clear();
            foreach (var kvp in this._trackedDynamicEntities)
            {
                IMyEntity entity = kvp.Value;
                
                // Critical conditions for removal.
                // We no longer remove an entity *solely* because its physics are inactive before applying gravity,
                // as applying gravity is intended to wake it up.
                // If it's MarkedForClose or Physics is null, it's truly invalid for gravity application.
                if (entity == null || entity.MarkedForClose || entity.Physics == null)
                {
                    this._entitiesToRemoveFromTracked.Add(kvp.Key);
                    continue;
                }

                if (this._localCharacter != null)
                {
                    Vector3D entityPos = entity.GetPosition();
                    // If the entity is too far from the player, mark it for removal and skip gravity.
                    // This prevents applying gravity to distant objects that are about to be untracked.
                    if ((entityPos - this._localCharacter.GetPosition()).LengthSquared() >= this._settings.GravitationalDetectionRangeSq * 2.5)
                    {
                        this._entitiesToRemoveFromTracked.Add(kvp.Key);
                        continue;
                    }
                }

                // If we haven't 'continued', the entity is considered valid and in range for gravity application.
                // Apply gravity. This should wake up sleeping entities if they have physics enabled.
                Vector3D gravityForce = this._gravityCalculator.ComputeTotalGravity(entity.GetPosition());

                if (gravityForce.LengthSquared() > this._settings.GravityDisableThresholdSq)
                {
                    entity.Physics.LinearVelocity += gravityForce * Constants.TickSeconds;
                }
            }

            foreach (long id in this._entitiesToRemoveFromTracked)
            {
                this._trackedDynamicEntities.Remove(id);
            }
        }

        // --- Main Update Method ---

        public override void UpdateBeforeSimulation()
        {
            try
            {
                this._tickCounter++;

                // Debug toggle via 'T' key still works
                if (MyAPIGateway.Input.IsNewKeyPressed(MyKeys.T))
                {
                    this._settings.DebugMode = !this._settings.DebugMode;
                    MyAPIGateway.Utilities.ShowNotification("Debug Mode: " + (this._settings.DebugMode ? "Enabled" : "Disabled"), 2000);
                    this._logger.Info("Debug Mode toggled via T-key: " + this._settings.DebugMode);
                }

                if (!this._initialScanCompleted)
                {
                    this.PerformInitialAsteroidScan();
                    if (!this._initialScanCompleted) return;
                }

                this.RefreshAsteroidCache();
                this.ProcessCoGCalculations(); // Queues jobs
                this.ProcessCompletedCoGResults(); // Processes results from completed jobs

                this.ApplyGravityToLocalCharacter();
                this.ApplyGravityToDynamicEntities();

                this._debugRenderer.DrawDebugVisuals();
            }
            catch (Exception ex)
            {
                // Log critical errors to our custom logger and game's default log
                this._logger.Error("AsteroidGravityMod Critical Error: " + ex.Message, ex);
                MyAPIGateway.Utilities.ShowNotification("Gravity Mod Error: " + ex.Message, 3000, MyFontEnum.Red);
            }
        }
    }
}
