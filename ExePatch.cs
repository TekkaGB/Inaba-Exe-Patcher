using System;
using System.Diagnostics;
using Reloaded.Memory.Sigscan;
using Reloaded.Memory.Sigscan.Structs;
using Reloaded.Mod.Interfaces;
using System.IO;
using System.Linq;
using p4gpc.inaba.Configuration;
using System.Collections.Generic;
using Reloaded.Memory.Sources;
using System.Text.RegularExpressions;
using System.Text;
using Reloaded.Hooks.Definitions;
using Reloaded.Hooks.Definitions.Enums;

namespace p4gpc.inaba
{
    public class ExePatch : IDisposable
    {
        private readonly IMemory mem;
        private readonly ILogger mLogger;
        private readonly Config mConfig;

        private readonly Process mProc;
        private readonly IntPtr mBaseAddr;
        private Scanner scanner;
        private IReloadedHooks mHooks;

        public ExePatch(ILogger logger, Config config, IReloadedHooks hooks)
        {
            mLogger = logger;
            mConfig = config;
            mHooks = hooks;
            mProc = Process.GetCurrentProcess();
            mBaseAddr = mProc.MainModule.BaseAddress;
            mem = new Memory();
            scanner = new Scanner(mProc, mProc.MainModule);
        }

        private void SinglePatch(string filePath)
        {
            byte[] file;
            uint fileLen;
            string fileName = Path.GetFileName(filePath);

            file = File.ReadAllBytes(filePath);
            mLogger.WriteLine($"[Inaba Exe Patcher] Loading {fileName}");

            if (file.Length < 2)
            {
                mLogger.WriteLine("[Inaba Exe Patcher] Improper .patch format.");
                return;
            }

            // Length of line to search is reversed in hex.
            byte[] fileHeaderLenBytes = file[0..2];
            Array.Reverse(fileHeaderLenBytes, 0, 2);
            int fileHeaderLen = BitConverter.ToInt16(fileHeaderLenBytes);

            if (file.Length < 2 + fileHeaderLen)
            {
                mLogger.WriteLine("[Inaba Exe Patcher] Improper .patch format.");
                return;
            }

            // Header is line to search for in exe
            byte[] fileHeader = file[2..(fileHeaderLen + 2)];
            // Contents is what to replace
            byte[] fileContents = file[(fileHeaderLen + 2)..];
            fileLen = Convert.ToUInt32(fileContents.Length);

            // Debug
            if (mConfig.Debug)
            {
                mLogger.WriteLine($"[Inaba Exe Patcher] (Debug) Length of Search Pattern = {fileHeaderLen}");
                mLogger.WriteLine($"[Inaba Exe Patcher] (Debug) Search Pattern (in hex) = {BitConverter.ToString(fileHeader).Replace("-", " ")}");
                mLogger.WriteLine($"[Inaba Exe Patcher] (Debug) Replacement Content (in hex) = {BitConverter.ToString(fileContents).Replace("-", " ")}");
            }

            var result = scanner.FindPattern(BitConverter.ToString(fileHeader).Replace("-", " "));
        
            if (result.Found)
            {
                mem.SafeWriteRaw(mBaseAddr + result.Offset, fileContents);
                mLogger.WriteLine($"[Inaba Exe Patcher] Successfully found and overwrote pattern in {fileName}");
            }
            else
                mLogger.WriteLine($"[Inaba Exe Patcher] Couldn't find pattern to replace using {fileName}");
        }
        
        public void Patch()
        {
            List<string> patchPriorityList = new List<string>();
            // Add main directory as last entry for least priority
            patchPriorityList.Add($@"mods/patches");

            // Add every other directory
            foreach (var dir in Directory.EnumerateDirectories(@"mods/patches"))
            {
                var name = Path.GetFileName(dir);

                patchPriorityList.Add($@"mods/patches/{name}");
            }

            // Reverse order of config patch list so that the higher priorities are moved to the end
            List<string> revEnabledPatches = mConfig.PatchFolderPriority;
            revEnabledPatches.Reverse();

            foreach (var dir in revEnabledPatches)
            {
                var name = Path.GetFileName(dir);
                if (patchPriorityList.Contains($@"mods/patches/{name}", StringComparer.InvariantCultureIgnoreCase))
                {
                    patchPriorityList.Remove($@"mods/patches/{name}");
                    patchPriorityList.Add($@"mods/patches/{name}");
                }
            }

            // Load EnabledPatches in order
            foreach (string dir in patchPriorityList)
            {
                string[] allFiles = Directory.GetFiles(dir, "*", SearchOption.TopDirectoryOnly);
                    
                string[] patches = allFiles.Where(s => Path.GetExtension(s).ToLower() == ".patch").ToArray();
                string[] exPatches = allFiles.Where(s => Path.GetExtension(s).ToLower() == ".expatch").ToArray();

                if (patches.Length == 0 && exPatches.Length == 0)
                {
                    mLogger.WriteLine($"[Inaba Exe Patcher] No patches found in {dir}");
                    return;
                }

                mLogger.WriteLine($"[Inaba Exe Patcher] Found patches in {dir}");
                foreach (string f in patches)
                {
                    SinglePatch(f);
                }

                foreach(string f in exPatches)
                {
                    SinglePatchEx(f);
                }
            }
        }

        private void SinglePatchEx(string filePath)
        {
            List<ExPatch> patches = ParseExPatch(filePath);
            foreach(var patch in patches)
            {
                var result = scanner.FindPattern(patch.Pattern);
                AsmHookBehaviour? order = null;
                if (patch.ExecutionOrder == "before")
                    order = AsmHookBehaviour.ExecuteFirst;
                else if (patch.ExecutionOrder == "after")
                    order = AsmHookBehaviour.ExecuteAfter;
                else if (patch.ExecutionOrder == "only")
                    order = AsmHookBehaviour.DoNotExecuteOriginal;
                if(order != null)
                    mHooks.CreateAsmHook(patch.Function, (long)(mBaseAddr + result.Offset), (AsmHookBehaviour)order).Activate();
                else 
                    mHooks.CreateAsmHook(patch.Function, (long)(mBaseAddr + result.Offset)).Activate();
                mLogger.WriteLine($"[Inaba Exe Patcher] Applied patch {patch.Name} from {Path.GetFileName(filePath)}");
            }
        }

        /// <summary>
        /// Parses all of the patches from an expatch file
        /// </summary>
        /// <param name="filePath">The path to the expatch file</param>
        /// <returns>A list of all of the found patches in the file</returns>
        private List<ExPatch> ParseExPatch(string filePath)
        {
            List<ExPatch> patches = new List<ExPatch>();
            bool startPatch = false;
            List<string> currentPatch = new List<string>();
            string patchName = "";
            string pattern = "";
            string order = "";

            foreach (var line in File.ReadLines(filePath))
            {
                // Search for the start of a new patch (and its name)
                var patchMatch = Regex.Match(line, @"\[\s*patch\s*(?:\s+(.*?))?\s*\]");
                if (patchMatch.Success)
                {
                    startPatch = true;
                    if (currentPatch.Count > 0)
                    {
                        patches.Add(new ExPatch(patchName, pattern, currentPatch.ToArray(), order));
                    }
                    currentPatch.Clear();
                    if (patchMatch.Groups.Count > 1)
                        patchName = RemoveComments(patchMatch.Groups[1].Value);
                    else
                        patchName = "";
                    pattern = "";
                    order = "";
                    continue;
                }

                // Don't try to add stuff if the patch hasn't actually started yet
                if (!startPatch) continue;

                // Search for a patten to scan for
                var patternMatch = Regex.Match(line, @"pattern\s*=\s*(.+)");
                if (patternMatch.Success)
                {
                    pattern = RemoveComments(patternMatch.Groups[1].Value);
                    continue;
                }

                // Search for an order to execute the hook in
                var orderMatch = Regex.Match(line, @"\s*order\s*=\s*(.+)");
                if (orderMatch.Success)
                {
                    order = RemoveComments(orderMatch.Groups[1].Value);
                    continue;
                }

                // Add the line as a part of the patch's function
                currentPatch.Add(RemoveComments(line));
            }
            if (currentPatch.Count > 0)
            {
                patches.Add(new ExPatch(patchName, pattern, currentPatch.ToArray(), order));
            }
            return patches;
        }

        /// <summary>
        /// Searches for "//" in a string and removes it and anything after it
        /// </summary>
        /// <returns>A copy of the string with comments removed (or a copy of the string with no changes if there were no comments)</returns>
        private string RemoveComments(string text)
        {
            return Regex.Replace(text, @"\/\/.*", "");
        }

        /// <inheritdoc />
        public void Dispose()
        {
            mProc?.Dispose();
            scanner?.Dispose();
        }
    }
}