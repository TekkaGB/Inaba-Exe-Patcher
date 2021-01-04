using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Reloaded.Mod.Interfaces;
using System.IO;
using System.Linq;
using p4gpc.inaba.Configuration;
using System.Reflection;
using System.Buffers.Binary;
using System.Runtime.ExceptionServices;

namespace p4gpc.inaba
{
    class binMerge
    {
        private readonly ILogger mLogger;
        private readonly Config mConfig;
        private sprUtils sprUtil;

        private string exePath = @"PAKPack.exe";

        public binMerge(ILogger logger, Config config)
        {
            mLogger = logger;
            mConfig = config;
            sprUtil = new sprUtils(logger);
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
            mLogger.WriteLine($"[Aemulus]<Bin Merger> args = {args}");
            using (Process process = new Process())
            {
                process.StartInfo = startInfo;
                process.Start();

                // Add this: wait until process does its work
                process.WaitForExit();
            }
        }

        private List<string> getFileContents(string path)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.CreateNoWindow = false;
            startInfo.UseShellExecute = false;
            startInfo.RedirectStandardOutput = true;
            startInfo.FileName = $"\"{exePath}\"";
            startInfo.WindowStyle = ProcessWindowStyle.Hidden;
            startInfo.Arguments = $"list {path}";
            List<string> contents = new List<string>();
            using (Process process = new Process())
            {
                process.StartInfo = startInfo;
                process.Start();
                while (!process.StandardOutput.EndOfStream)
                {
                    string line = process.StandardOutput.ReadLine();
                    if (!line.Contains(" "))
                        contents.Add(line);
                }
                // Add this: wait until process does its work
                process.WaitForExit();
            }
            return contents;
        }
        private int commonPrefixUtil(String str1, String str2)
        {
            String result = "";
            int n1 = str1.Length,
                n2 = str2.Length;

            // Compare str1 and str2  
            for (int i = 0, j = 0;
                     i <= n1 - 1 && j <= n2 - 1;
                     i++, j++)
            {
                if (str1[i] != str2[j])
                {
                    break;
                }
                result += str1[i];
            }

            return result.Length;
        }

        private List<string> getModList(string dir)
        {
            List<string> mods = new List<string>();
            string line;
            string[] list = Directory.GetFiles(dir, "*", SearchOption.TopDirectoryOnly)
                    .Where(s => (Path.GetExtension(s).ToLower() == ".aem")).ToArray();
            if (list.Length > 0)
            {
                using (StreamReader stream = new StreamReader(list[0]))
                {
                    while ((line = stream.ReadLine()) != null)
                    {
                        mLogger.WriteLine($"[Aemulus]<Bin Merger> Adding {line} to list");
                        mods.Add(line);
                    }
                }
            }
            return mods;
        }

        public void Unpack()
        {
            List<string> binPriorityList = new List<string>();

            // Add every other directory
            foreach (var dir in Directory.EnumerateDirectories(@"mods\bins"))
            {
                var name = Path.GetFileName(dir);

                binPriorityList.Add($@"mods\bins\{name}");
            }

            // Reverse order of config patch list so that the higher priorities are moved to the end
            List<string> revBinPriority = mConfig.BinFolderPriority;
            revBinPriority.Reverse();

            foreach (var dir in revBinPriority)
            {
                var name = Path.GetFileName(dir);
                if (binPriorityList.Contains($@"mods\bins\{name}", StringComparer.InvariantCultureIgnoreCase))
                {
                    binPriorityList.Remove($@"mods\bins\{name}");
                    binPriorityList.Add($@"mods\bins\{name}");
                }
            }

            foreach (var mod in binPriorityList)
            {
                foreach (var file2 in Directory.GetFiles(mod, "*", SearchOption.AllDirectories))
                {
                    if (Path.GetExtension(file2) != ".aem") // Just copy it over as a base
                    {
                        List<string> folders = new List<string>(file2.Split("\\"));
                        folders.Remove("bins");
                        folders.Remove(Path.GetFileName(mod));
                        string binPath = string.Join("\\", folders.ToArray());
                        if ((Path.GetExtension(file2).ToLower() == ".bin") ||
                            (Path.GetExtension(file2).ToLower() == ".arc") ||
                            (Path.GetExtension(file2).ToLower() == ".pak"))
                        {
                            if (File.Exists(binPath))
                                continue;
                        }
                        mLogger.WriteLine($"[Aemulus]<Bin Merger> Copying {file2} to {binPath}");
                        if (!Directory.Exists(Path.GetDirectoryName(binPath)))
                            Directory.CreateDirectory(Path.GetDirectoryName(binPath));
                        File.Copy(file2, binPath, true);
                    }
                }
                    List<string> modList = getModList(mod);
                foreach (var file in Directory.GetFiles(mod, "*", SearchOption.AllDirectories)
                    .Where(s => (Path.GetExtension(s).ToLower() == ".bin") ||
                    (Path.GetExtension(s).ToLower() == ".arc") ||
                    (Path.GetExtension(s).ToLower() == ".pak")).ToArray())
                {
                    List<string> folders = new List<string>(file.Split("\\"));
                    folders.Remove("bins");
                    folders.Remove(Path.GetFileName(mod));
                    string binPath = string.Join("\\", folders.ToArray());
                    
                    // Unpack and transfer modified parts if base already exists
                    if (File.Exists(binPath))
                    {
                        PAKPackCMD($"unpack \"{file}\"");
                        // Unpack fully before comparing to mods.aem
                        foreach (var f in Directory.GetFiles(Path.ChangeExtension(file, null), "*", SearchOption.AllDirectories))
                        {
                            if (Path.GetExtension(f) == ".bin"
                                || Path.GetExtension(f) == ".arc"
                                || Path.GetExtension(f) == ".pak")
                            {
                                PAKPackCMD($"unpack \"{f}\"");
                                foreach (var f2 in Directory.GetFiles(Path.ChangeExtension(f, null), "*", SearchOption.AllDirectories))
                                {
                                    if (Path.GetExtension(f2) == ".bin"
                                        || Path.GetExtension(f2) == ".arc"
                                        || Path.GetExtension(f2) == ".pak")
                                            PAKPackCMD($"unpack \"{f2}\"");
                                    else if (Path.GetExtension(f2) == ".spr")
                                    {
                                        string sprFolder2 = Path.ChangeExtension(f2, null);
                                        if (!Directory.Exists(sprFolder2))
                                            Directory.CreateDirectory(sprFolder2);
                                        Dictionary<string, int> tmxNames = sprUtil.getTmxNames(f2);
                                        foreach (string name in tmxNames.Keys)
                                        {
                                            //mLogger.WriteLine($"[Aemulus]<Bin Merger> Extracting {name} from {f2}");
                                            //mLogger.WriteLine($@"[Aemulus]<Bin Merger> Writing to {sprFolder2}\{name}.tmx");
                                            byte[] tmx = sprUtil.extractTmx(f2, name);
                                            File.WriteAllBytes($@"{sprFolder2}\{name}.tmx", tmx);
                                        }
                                    }
                                }
                            }
                            else if (Path.GetExtension(f) == ".spr")
                            {
                                string sprFolder = Path.ChangeExtension(f, null);
                                if (!Directory.Exists(sprFolder))
                                    Directory.CreateDirectory(sprFolder);
                                Dictionary<string, int> tmxNames = sprUtil.getTmxNames(f);
                                foreach (string name in tmxNames.Keys)
                                {
                                    //mLogger.WriteLine($"[Aemulus]<Bin Merger> Extracting {name} from {f}");
                                    //mLogger.WriteLine($@"[Aemulus]<Bin Merger> Writing to {sprFolder}\{name}.tmx");
                                    byte[] tmx = sprUtil.extractTmx(f, name);
                                    File.WriteAllBytes($@"{sprFolder}\{name}.tmx", tmx);
                                }
                            }

                            // Copy over loose files specified by mods.aem
                            foreach (var m in modList)
                            {
                                if (File.Exists($@"mods\bins\{Path.GetFileName(mod)}\{m}"))
                                {
                                    if (!Directory.Exists($@"mods\{Path.GetDirectoryName(m)}"))
                                        Directory.CreateDirectory($@"mods\{Path.GetDirectoryName(m)}");
                                    File.Copy($@"mods\bins\{Path.GetFileName(mod)}\{m}", $@"mods\{m}", true);
                                }
                            }

                        }
                    }         
                }
            }
        }

        public void Merge()
        {
            List<string> dirs = new List<string>();
            foreach (var dir in Directory.EnumerateDirectories(@"mods"))
            {
                var name = Path.GetFileName(dir);
                if (name != "patches" || name != "bins" || name != "SND")
                    dirs.Add($@"mods\{name}");
            }
            // Find bins with loose files
            foreach (var dir in dirs)
            {
                foreach (var file in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                {
                    if (Path.GetExtension(file) == ".bin"
                        || Path.GetExtension(file) == ".arc"
                        || Path.GetExtension(file) == ".pak")
                    {
                        if (Directory.Exists(Path.ChangeExtension(file, null)))
                        {
                            string bin = file;
                            string binFolder = Path.ChangeExtension(file, null);

                            // Get contents of init_free
                            List<string> contents = getFileContents(bin);
                            foreach (var c in contents)
                                mLogger.WriteLine($"[Aemulus] {c}");

                            // Unpack init_free for future unpacking
                            string temp = $"{binFolder}_temp";
                            PAKPackCMD($"unpack \"{bin}\" \"{temp}\"");

                            foreach (var f in Directory.GetFiles(binFolder, "*", SearchOption.AllDirectories))
                            {
                                // Get bin path used for PAKPack.exe
                                int numParFolders = Path.ChangeExtension(file, null).Split("\\").Length;
                                List<string> folders = new List<string>(f.Split("\\"));
                                // TODO: get file path starting at current folder name [3..] is specific to init/init_free
                                string binPath = string.Join("/", folders.ToArray()[numParFolders..]);
                                mLogger.WriteLine($"[Aemulus] BinPath = {binPath}");
                                // Check if more unpacking needs to be done to replace
                                if (!contents.Contains(binPath))
                                {
                                    string longestPrefix = "";
                                    int longestPrefixLen = 0;
                                    foreach (var c in contents)
                                    {
                                        int prefixLen = commonPrefixUtil(c, binPath);
                                        if (prefixLen > longestPrefixLen)
                                        {
                                            longestPrefix = c;
                                            longestPrefixLen = prefixLen;
                                        }
                                    }
                                    mLogger.WriteLine($"[Aemulus] Extension = {Path.GetExtension(longestPrefix)}");
                                    // Check if we can unpack again
                                    if (Path.GetExtension(longestPrefix) == ".bin"
                                        || Path.GetExtension(longestPrefix) == ".arc"
                                        || Path.GetExtension(longestPrefix) == ".pak")
                                    {
                                        string file2 = $@"{temp}\{longestPrefix.Replace("/", "\\")}";
                                        List<string> contents2 = getFileContents(file2);

                                        List<string> split = new List<string>(binPath.Split("/"));
                                        int numPrefixFolders = longestPrefix.Split("/").Length;
                                        string binPath2 = string.Join("/", split.ToArray()[numPrefixFolders..]);

                                        if (contents2.Contains(binPath2))
                                        {
                                            PAKPackCMD($"replace \"{file2}\" {binPath2} \"{f}\" \"{file2}\"");
                                            PAKPackCMD($"replace \"{bin}\" {longestPrefix} \"{file2}\" \"{bin}\"");
                                        }
                                        else
                                        {
                                            string longestPrefix2 = "";
                                            int longestPrefixLen2 = 0;
                                            foreach (var c in contents2)
                                            {
                                                int prefixLen = commonPrefixUtil(c, binPath2);
                                                if (prefixLen > longestPrefixLen2)
                                                {
                                                    longestPrefix2 = c;
                                                    longestPrefixLen2 = prefixLen;
                                                }
                                            }
                                            mLogger.WriteLine($"[Aemulus] Extension = {Path.GetExtension(longestPrefix2)}");
                                            // Check if we can unpack again
                                            if (Path.GetExtension(longestPrefix2) == ".bin"
                                                || Path.GetExtension(longestPrefix2) == ".arc"
                                                || Path.GetExtension(longestPrefix2) == ".pak")
                                            {
                                                string file3 = $@"{temp}\{Path.ChangeExtension(longestPrefix.Replace("/", "\\"), null)}\{longestPrefix2.Replace("/", "\\")}";
                                                PAKPackCMD($"unpack \"{file2}\"");
                                                mLogger.WriteLine($"[Aemulus] Checking {file3}");
                                                List<string> contents3 = getFileContents(file3);

                                                foreach (var c in contents3)
                                                    mLogger.WriteLine($"[Aemulus] {c}");

                                                List<string> split2 = new List<string>(binPath2.Split("/"));
                                                int numPrefixFolders2 = longestPrefix2.Split("/").Length;
                                                string binPath3 = string.Join("/", split2.ToArray()[numPrefixFolders2..]);

                                                if (contents3.Contains(binPath3))
                                                {
                                                    PAKPackCMD($"replace \"{file3}\" {binPath3} \"{f}\" \"{file3}\"");
                                                    PAKPackCMD($"replace \"{file2}\" {longestPrefix2} \"{file3}\" \"{file2}\"");
                                                    PAKPackCMD($"replace \"{bin}\" {longestPrefix} \"{file2}\" \"{bin}\"");
                                                }
                                            }
                                            else if (Path.GetExtension(longestPrefix2) == ".spr" && Path.GetExtension(f) == ".tmx")
                                            {
                                                PAKPackCMD($"unpack \"{file2}\"");
                                                string sprPath = $@"{temp}\{Path.ChangeExtension(longestPrefix.Replace("/", "\\"), null)}\{longestPrefix2.Replace("/", "\\")}";
                                                sprUtil.replaceTmx(sprPath, f);
                                                PAKPackCMD($"replace \"{file2}\" {longestPrefix2} \"{sprPath}\" \"{file2}\"");
                                                PAKPackCMD($"replace \"{bin}\" {longestPrefix} \"{file2}\" \"{bin}\"");
                                            }
                                        }
                                    }
                                    else if (Path.GetExtension(longestPrefix) == ".spr" && Path.GetExtension(f) == ".tmx")
                                    {
                                        string sprPath = $@"{temp}\{longestPrefix.Replace("/", "\\")}";
                                        sprUtil.replaceTmx(sprPath, f);
                                        PAKPackCMD($"replace \"{bin}\" {longestPrefix} \"{sprPath}\" \"{bin}\"");
                                    }
                                }
                                else
                                {
                                    mLogger.WriteLine($"[Aemulus]<Bin Merger> Replacing {binPath} in init_free.bin");
                                    string args = $"replace \"{bin}\" {binPath} \"{f}\" \"{bin}\"";
                                    PAKPackCMD(args);
                                }
                            }
                            Directory.Delete(temp, true);
                        }
                    }
                }
            }

            return;
        }
    }
}
