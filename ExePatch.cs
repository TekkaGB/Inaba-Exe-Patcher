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

namespace p4gpc.inaba
{
    public class exePatch
    {
        private readonly IMemory mem;
        private readonly ILogger mLogger;
        private readonly Config mConfig;

        private readonly Process mProc;
        private readonly IntPtr mBaseAddr;
        private readonly IntPtr mHnd;
        private Scanner scanner;

        public exePatch(ILogger logger, Config config)
        {
            mLogger = logger;
            mConfig = config;
            mProc = Process.GetCurrentProcess();
            mBaseAddr = mProc.MainModule.BaseAddress;
            mHnd = mProc.Handle;
            mem = new ExternalMemory(mProc.Handle);
        }

        private void singlePatch(string filePath)
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

            var pattern = new CompiledScanPattern(BitConverter.ToString(fileHeader).Replace("-", " "));
            var result = scanner.CompiledFindPattern(pattern, 0);

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
            scanner = new Scanner(mProc, mProc.MainModule);

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
                string[] files;

                files = Directory.GetFiles(dir, "*", SearchOption.TopDirectoryOnly)
                    .Where(s => (Path.GetExtension(s).ToLower() == ".patch")).ToArray();

                if (files.Length == 0)
                {
                    mLogger.WriteLine($"[Inaba Exe Patcher] No patches found in {dir}");
                    return;
                }

                mLogger.WriteLine($"[Inaba Exe Patcher] Found patches in {dir}");
                foreach (string f in files)
                {
                    singlePatch(f);
                }
            }
            return;
        }
    }
}