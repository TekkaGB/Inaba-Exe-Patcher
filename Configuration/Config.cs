using System.Collections.Generic;
using System.ComponentModel;
using p4gpc.inaba.Configuration.Implementation;

namespace p4gpc.inaba.Configuration
{
    public class Config : Configurable<Config>
    {
        /*
            User Properties:
                - Please put all of your configurable properties here.
                - Tip: Consider using the various available attributes https://stackoverflow.com/a/15051390/11106111
        
            By default, configuration saves as "Config.json" in mod folder.    
            Need more config files/classes? See Configuration.cs
        */

        [DisplayName("Patch Folder Priority")]
        [Description("List of patch folders that should be loaded in order (first one takes priority)")]
        public List<string> PatchFolderPriority { get; set; } = new List<string>();

        [DisplayName("Debug")]
        [Description("Enable to print more info grabbed from patch files.")]
        public bool Debug { get; set; } = false;
    }
}
