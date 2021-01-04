using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Reloaded.Mod.Interfaces;
using System.IO;
using System.Linq;

namespace p4gpc.inaba
{
    public class tblPatch
    {
        private readonly ILogger mLogger;
        private readonly Config mConfig;

        private string exePath = @"PAKPack.exe";

        public tblPatch(ILogger logger, Config config)
        {
            mLogger = logger;
            mConfig = config;
        }

        // Use PAKPack command
        private void PAKPackCMD(string args)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.CreateNoWindow = false;
            startInfo.UseShellExecute = false;
            startInfo.FileName = $"\"{exePath}\"";
            startInfo.WindowStyle = ProcessWindowStyle.Hidden;
            startInfo.Arguments = args;
            using (Process process = new Process())
            {
                process.StartInfo = startInfo;
                process.Start();

                // Add this: wait until process does its work
                process.WaitForExit();
            }
        }
        public void Patch()
        {
            // Check if init_free exists and return if not
            string init_free = @"mods\data00004\init_free.bin";
            if (!File.Exists(init_free))
            {
                mLogger.WriteLine($"[Aemulus]<Tbl Patcher> {init_free} not found in game directory");
                return;
            }

            // Unpack init_free
            mLogger.WriteLine($"[Aemulus]<Tbl Patcher> Unpacking init_free.bin");
            PAKPackCMD($"unpack \"{init_free}\"");

            // Keep track of which tables are edited
            List<string> editedTables = new List<string>();

            List<string> patchPriorityList = new List<string>();
            // Add main directory as first entry for least priority
            patchPriorityList.Add($@"mods\patches");

            // Add every other directory
            foreach (var dir in Directory.EnumerateDirectories(@"mods\patches"))
            {
                var name = Path.GetFileName(dir);
                
                patchPriorityList.Add($@"mods\patches\{name}");
            }

            // Reverse order of config patch list so that the higher priorities are moved to the end
            List<string> revEnabledPatches = mConfig.PatchFolderPriority;
            revEnabledPatches.Reverse();

            foreach (var dir in revEnabledPatches)
            {
                var name = Path.GetFileName(dir);
                if (patchPriorityList.Contains($@"mods\patches\{name}", StringComparer.InvariantCultureIgnoreCase))
                {
                    patchPriorityList.Remove($@"mods\patches\{name}");
                    patchPriorityList.Add($@"mods\patches\{name}");
                }
            }

            // Load EnabledPatches in order
            foreach (string dir in patchPriorityList)
            {
                mLogger.WriteLine($"[Aemulus]<Tbl Patcher> Patching directory {dir}");
                string[] tbls = Directory.GetFiles(dir, "*", SearchOption.TopDirectoryOnly)
                    .Where(s => (Path.GetExtension(s).ToLower() == ".tblpatch")).ToArray();

                if (tbls.Length > 0)
                {
                    mLogger.WriteLine($"[Aemulus]<Tbl Patcher> Found tbl patches in {dir}");
                    // Loop through each tblpatch
                    foreach (string t in tbls)
                    {
                        byte[] file = File.ReadAllBytes(t);
                        string fileName = Path.GetFileName(t);
                        mLogger.WriteLine($"[Aemulus]<Tbl Patcher> Loading {fileName}");
                        if (file.Length < 12)
                        {
                            mLogger.WriteLine("[Aemulus]<Tbl Patcher> Improper .tblpatch format.");
                            continue;
                        }

                        // Name of tbl file
                        string tblName = Encoding.ASCII.GetString(file[0..3]);
                        // Offset to start overwriting at
                        byte[] byteOffset = file[3..11];
                        // Reverse endianess
                        Array.Reverse(byteOffset, 0, 8);
                        long offset = BitConverter.ToInt64(byteOffset);
                        // Contents is what to replace
                        byte[] fileContents = file[11..];

                        /*
                            * TBLS:
                            * SKILL - SKL
                            * UNIT - UNT
                            * MSG - MSG
                            * PERSONA - PSA
                            * ENCOUNT - ENC
                            * EFFECT - EFF
                            * MODEL - MDL
                            * AICALC - AIC
                            */

                        switch (tblName)
                        {
                            case "SKL":
                                tblName = "SKILL.TBL";
                                break;
                            case "UNT":
                                tblName = "UNIT.TBL";
                                break;
                            case "MSG":
                                tblName = "MSG.TBL";
                                break;
                            case "PSA":
                                tblName = "PERSONA.TBL";
                                break;
                            case "ENC":
                                tblName = "ENCOUNT.TBL";
                                break;
                            case "EFF":
                                tblName = "EFFECT.TBL";
                                break;
                            case "MDL":
                                tblName = "MODEL.TBL";
                                break;
                            case "AIC":
                                tblName = "AICALC.TBL";
                                break;
                            default:
                                mLogger.WriteLine($"[Aemulus]<Tbl Patcher> Unknown tbl name for {t}.");
                                continue;
                        }

                        if (mConfig.Debug)
                        {
                            mLogger.WriteLine($"[Aemulus]<Tbl Patcher>(Debug) TBL = {tblName}");
                            mLogger.WriteLine($"[Aemulus]<Tbl Patcher>(Debug) Offset = {offset}");
                            mLogger.WriteLine($"[Aemulus]<Tbl Patcher>(Debug) Replacement contents (in hex) = {BitConverter.ToString(fileContents).Replace("-", " ")}");
                        }

                        // Path inside init_free.bin to edit
                        string origPath = $"battle/{tblName}";
                        // Keep track of which TBL's were edited
                        if (!editedTables.Contains(origPath))
                            editedTables.Add(origPath);

                        // TBL file to edit
                        string unpackedTblPath = $@"mods\data00004\init_free\battle\{tblName}";
                        using (Stream stream = File.Open(unpackedTblPath, FileMode.Open))
                        {
                            stream.Position = offset;
                            stream.Write(fileContents, 0, fileContents.Length);
                        }

                    }
                }
                else
                {
                    mLogger.WriteLine($"[Aemulus]<Tbl Patcher> No tbl patches found in {dir}");
                }
            }

            // Replace each edited TBL's
            foreach (string u in editedTables)
            {
                mLogger.WriteLine($"[Aemulus]<Tbl Patcher> Replacing {u} in init_free.bin");
                string unpackedTblPath = $@"mods\data00004\init_free\{u}";
                string args = $"replace \"{init_free}\" {u} \"{unpackedTblPath}\" \"{init_free}\"";
                PAKPackCMD(args);
            }

            mLogger.WriteLine($"[Aemulus]<Tbl Patcher> Deleting unpacked folder and embedded resources");
            // Delete all unpacked files
            string unpacked_init_free = @"mods\data00004\init_free";
            Directory.Delete(unpacked_init_free, true);

            return;
        }
    }
}