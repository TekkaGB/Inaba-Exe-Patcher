﻿using p4gpc.inaba.Configuration;
using p4gpc.inaba.Template;
using Reloaded.Hooks.ReloadedII.Interfaces;
using Reloaded.Memory.SigScan.ReloadedII.Interfaces;
using Reloaded.Mod.Interfaces;
using Reloaded.Mod.Interfaces.Internal;
using System.Diagnostics;
using System.Drawing;

namespace p4gpc.inaba
{
    /// <summary>
    /// Your mod logic goes here.
    /// </summary>
    public class Mod : ModBase // <= Do not Remove.
    {
        /// <summary>
        /// Provides access to the mod loader API.
        /// </summary>
        private readonly IModLoader _modLoader;

        /// <summary>
        /// Provides access to the Reloaded.Hooks API.
        /// </summary>
        /// <remarks>This is null if you remove dependency on Reloaded.SharedLib.Hooks in your mod.</remarks>
        private readonly IReloadedHooks? _hooks;

        /// <summary>
        /// Provides access to the Reloaded logger.
        /// </summary>
        private readonly ILogger _logger;

        /// <summary>
        /// Entry point into the mod, instance that created this class.
        /// </summary>
        private readonly IMod _owner;

        /// <summary>
        /// Provides access to this mod's configuration.
        /// </summary>
        private Config _configuration;

        /// <summary>
        /// The configuration of the currently executing mod.
        /// </summary>
        private readonly IModConfig _modConfig;

        private ExePatch? _exePatcher;

        public Mod(ModContext context)
        {
            _modLoader = context.ModLoader;
            _hooks = context.Hooks;
            _logger = context.Logger;
            _owner = context.Owner;
            _configuration = context.Configuration;
            _modConfig = context.ModConfig;

            // For more information about this template, please see
            // https://reloaded-project.github.io/Reloaded-II/ModTemplate/

            // If you want to implement e.g. unload support in your mod,
            // and some other neat features, override the methods in ModBase.

            var startupScannerController = _modLoader.GetController<IStartupScanner>();
            if (startupScannerController == null || !startupScannerController.TryGetTarget(out var startupScanner))
            {
                _logger.WriteLine($"[Inaba Exe Patcher] Unable to get controller for Reloaded SigScan Library, aborting initialisation", Color.Red);
                return;
            }

            _exePatcher = new ExePatch(_logger, startupScanner, _configuration, _hooks);

            string patchPath = $"mods{Path.DirectorySeparatorChar}patches";

            if (Directory.Exists(patchPath))
            {
                _exePatcher.Patch(patchPath);
            }

            _modLoader.ModLoading += ModLoading;
            _modLoader.OnModLoaderInitialized += ModLoaderInitialised;
        }

        private void ModLoaderInitialised()
        {
            _modLoader.ModLoaded -= ModLoading;
        }

        private void ModLoading(IModV1 mod, IModConfigV1 modConfig)
        {
            if(modConfig.ModDependencies.Contains(_modConfig.ModId))
            {
                string modDir = _modLoader.GetDirectoryForModId(modConfig.ModId);
                if (Directory.Exists($"{modDir}{Path.DirectorySeparatorChar}InabaPatches"))
                    _exePatcher!.Patch($"{modDir}{Path.DirectorySeparatorChar}InabaPatches");
            }
        }

        

        #region Standard Overrides
        public override void ConfigurationUpdated(Config configuration)
        {
            // Apply settings from configuration.
            // ... your code here.
            _configuration = configuration;
            _logger.WriteLine($"[{_modConfig.ModId}] Config Updated: Applying");
        }
        #endregion

        #region For Exports, Serialization etc.
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        public Mod() { }
#pragma warning restore CS8618
        #endregion
    }
}