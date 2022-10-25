using p4gpc.inaba.Template.Configuration;
using System.ComponentModel;

namespace p4gpc.inaba.Configuration
{
    public class Config : Configurable<Config>
    {
        /*
            User Properties:
                - Please put all of your configurable properties here.

            By default, configuration saves as "Config.json" in mod user config folder.    
            Need more config files/classes? See Configuration.cs

            Available Attributes:
            - Category
            - DisplayName
            - Description
            - DefaultValue

            // Technically Supported but not Useful
            - Browsable
            - Localizable

            The `DefaultValue` attribute is used as part of the `Reset` button in Reloaded-Launcher.
        */

        [DisplayName("Patch Folder Priority")]
        [Description("List of patch folders that should be loaded in order (first one takes priority)")]
        public List<string> PatchFolderPriority { get; set; } = new List<string>();

        [DisplayName("Debug")]
        [Description("Enable to print more info grabbed from patch files.")]
        public bool Debug { get; set; } = false;

    }

    /// <summary>
    /// Allows you to override certain aspects of the configuration creation process (e.g. create multiple configurations).
    /// Override elements in <see cref="ConfiguratorMixinBase"/> for finer control.
    /// </summary>
    public class ConfiguratorMixin : ConfiguratorMixinBase
    {
        public override void Migrate(string oldDirectory, string newDirectory)
        {
            // Replace Config.json with your original config file name.
            TryMoveFile("Config.json");

#pragma warning disable CS8321
            void TryMoveFile(string fileName)
            {
                try { File.Move(Path.Combine(oldDirectory, fileName), Path.Combine(newDirectory, fileName)); }
                catch (Exception) { /* Ignored */ }
            }
#pragma warning restore CS8321
        }
    }
}