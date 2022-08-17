using System;
using System.Diagnostics;
using Reloaded.Mod.Interfaces;
using System.IO;
using System.Linq;
using p4gpc.inaba.Configuration;
using System.Collections.Generic;
using Reloaded.Memory.Sources;
using Reloaded.Memory.SigScan.ReloadedII.Interfaces;
using System.Text.RegularExpressions;
using System.Text;
using Reloaded.Hooks.Definitions;
using Reloaded.Hooks.Definitions.Enums;
using System.Drawing;
using Reloaded.Memory.Sigscan.Definitions.Structs;

namespace p4gpc.inaba
{
    public class ExePatch : IDisposable
    {
        private readonly IMemory mem;
        private readonly ILogger mLogger;
        private readonly IStartupScanner mStartupScanner;
        private readonly Config mConfig;

        private readonly Process mProc;
        private readonly IntPtr mBaseAddr;
        private IReloadedHooks mHooks;
        private List<IAsmHook> mAsmHooks;

        public ExePatch(ILogger logger, IStartupScanner startupScanner, Config config, IReloadedHooks hooks)
        {
            mLogger = logger;
            mConfig = config;
            mStartupScanner = startupScanner;
            mHooks = hooks;
            mProc = Process.GetCurrentProcess();
            mBaseAddr = mProc.MainModule.BaseAddress;
            mem = new Memory();
            mAsmHooks = new List<IAsmHook>();
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
            var pattern = BitConverter.ToString(fileHeader).Replace("-", " ");
            mStartupScanner.AddMainModuleScan(pattern,
                (result) =>
                {
                    if (result.Found)
                    {
                        mem.SafeWriteRaw(mBaseAddr + result.Offset, fileContents);
                        mLogger.WriteLine($"[Inaba Exe Patcher] Successfully found and overwrote pattern in {fileName}");
                    }
                    else
                        mLogger.WriteLine($"[Inaba Exe Patcher] Couldn't find pattern to replace using {fileName}");
                });
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

                foreach (string f in exPatches)
                {
                    SinglePatchEx(f);
                }
            }
        }

        private void SinglePatchEx(string filePath)
        {
            (List<ExPatch> patches, Dictionary<string, string> constants) = ParseExPatch(filePath);
            foreach(var constant in constants)
            {
                mStartupScanner.AddMainModuleScan(constant.Value, result =>
                {
                    if(!result.Found)
                    {
                        mLogger.WriteLine($"[Inaba Exe Patcher] Couldn't find address for const {constant.Value}, it will not be replaced", Color.Red);
                        return;
                    }

                    FillInConstant(patches, constant.Key, (result.Offset + (int)mBaseAddr).ToString());
                });
            }
            foreach (var patch in patches)
            {
                mStartupScanner.AddMainModuleScan(patch.Pattern, (result) =>
                {

                    if (!result.Found)
                    {
                        mLogger.WriteLine($"[Inaba Exe Patcher] Couldn't find address for {patch.Name}, not applying it", Color.Red);
                        return;
                    }

                    if (patch.IsReplacement)
                        ReplacementPatch(patch, result, filePath);
                    else
                        FunctionPatch(patch, result, filePath);
                });
            }
        }

        private void FunctionPatch(ExPatch patch, PatternScanResult result, string filePath)
        {
            AsmHookBehaviour? order = null;
            if (patch.ExecutionOrder == "before")
            {
                order = AsmHookBehaviour.ExecuteFirst;
                mLogger.WriteLine($"[Inaba Exe Patcher] Executing {patch.Name} function before original");
            }
            else if (patch.ExecutionOrder == "after")
            {
                mLogger.WriteLine($"[Inaba Exe Patcher] Executing {patch.Name} function after original");
                order = AsmHookBehaviour.ExecuteAfter;
            }
            else if (patch.ExecutionOrder == "only" || patch.ExecutionOrder == "")
            {
                mLogger.WriteLine($"[Inaba Exe Patcher] Replacing original {patch.Name} function");
                order = AsmHookBehaviour.DoNotExecuteOriginal;
            }
            else
            {
                mLogger.WriteLine($"[Inaba Exe Patcher] Unknown execution order {patch.ExecutionOrder}, using default (only). Valid orders are before, after and only");
                order = AsmHookBehaviour.DoNotExecuteOriginal;
            }

            try
            {
                mAsmHooks.Add(mHooks.CreateAsmHook(patch.Function, (long)(mBaseAddr + result.Offset + patch.Offset), (AsmHookBehaviour)order).Activate());
            }
            catch (Exception ex)
            {
                mLogger.WriteLine($"[Inaba Exe Patcher] Error creating hook for {patch.Name}: {ex.Message}");
                mLogger.WriteLine($"[Inaba Exe Patcher] Function dump:");
                foreach (var line in patch.Function)
                    mLogger.WriteLine(line);
                return;
            }
            mLogger.WriteLine($"[Inaba Exe Patcher] Applied patch {patch.Name} from {Path.GetFileName(filePath)} at 0x{mBaseAddr + result.Offset + patch.Offset:X}");
        }

        private void ReplacementPatch(ExPatch patch, PatternScanResult result, string filePath)
        {
            if (patch.Function.Count() == 0)
            {
                mLogger.WriteLine($"[Inaba Exe Patcher] No replacement value specified for {patch.Name} replacement, skipping it");
                return;
            }
            string replacement = patch.Function.Last();
            if (patch.Function.Count() > 1)
                mLogger.WriteLine($"[Inaba Exe Patcher] Multiple replacement values specified for {patch.Name}, using last defined one ({replacement})");
            int replacementLength = 0;
            if (patch.PadNull)
                replacementLength = patch.Pattern.Replace(" ", "").Length / 2;
            WriteValue(replacement, mBaseAddr + result.Offset + patch.Offset, patch.Name, replacementLength);
            mLogger.WriteLine($"[Inaba Exe Patcher] Applied replacement {patch.Name} from {Path.GetFileName(filePath)} at 0x{mBaseAddr + result.Offset + patch.Offset:X}");
        }

        /// <summary>
        /// Parses all of the patches from an expatch file
        /// </summary>
        /// <param name="filePath">The path to the expatch file</param>
        /// <returns>A tuple containing a list of all of the found patches in the file and a dictionary of all constants that need to be scanned for</returns>
        private (List<ExPatch>, Dictionary<string, string>) ParseExPatch(string filePath)
        {
            List<ExPatch> patches = new List<ExPatch>();
            bool startPatch = false;
            bool startReplacement = false;
            List<string> currentPatch = new List<string>();
            string patchName = "";
            string pattern = "";
            string order = "";
            int offset = 0;
            bool padNull = true;
            Dictionary<string, IntPtr> variables = new();
            Dictionary<string, string> constants = new();
            Dictionary<string, string> scanConstants = new();

            foreach (var rawLine in File.ReadLines(filePath))
            {
                // Search for the start of a new patch (and its name)
                string line = RemoveComments(rawLine).Trim();
                var patchMatch = Regex.Match(line, @"\[\s*patch\s*(?:\s+(.*?))?\s*\]", RegexOptions.IgnoreCase);
                if (patchMatch.Success)
                {
                    startReplacement = false;
                    startPatch = true;
                    SaveCurrentPatch(currentPatch, patches, patchName, ref pattern, ref order, ref offset, ref padNull, false);
                    if (patchMatch.Groups.Count > 1)
                        patchName = patchMatch.Groups[1].Value;
                    else
                        patchName = "";
                    continue;
                }

                // Search for the start of a new replacement
                var replacementMatch = Regex.Match(line, @"\[\s*replacement\s*(?:\s+(.*?))?\s*\]", RegexOptions.IgnoreCase);
                if (replacementMatch.Success)
                {
                    startReplacement = true;
                    startPatch = false;
                    SaveCurrentPatch(currentPatch, patches, patchName, ref pattern, ref order, ref offset, ref padNull, true);
                    if (replacementMatch.Groups.Count > 1)
                        patchName = replacementMatch.Groups[1].Value;
                    else
                        patchName = "";
                    continue;
                }

                // Search for a variable definition
                var variableMatch = Regex.Match(line, @"^\s*var\s+([^\s(]+)(?:\(([0-9]+)\))?\s*(?:=\s*(.*))?", RegexOptions.IgnoreCase);
                if (variableMatch.Success)
                {
                    string name = variableMatch.Groups[1].Value;
                    if (variables.ContainsKey(name))
                    {
                        mLogger.WriteLine($"[Inaba Exe Patcher] Variable {name} in {Path.GetFileName(filePath)} already exists, ignoring duplicate declaration of it");
                        continue;
                    }
                    int length = 4;
                    if (variableMatch.Groups[2].Success)
                    {
                        if (!int.TryParse(variableMatch.Groups[2].Value, out length))
                            mLogger.WriteLine($"[Inaba Exe Patcher] Invalid variable length \"{variableMatch.Groups[2].Value}\" defaulting to length of 4 bytes");
                    }
                    else
                    {
                        // Automatically set the length to that of the string if none was explicitly defined
                        var match = Regex.Match(variableMatch.Groups[3].Value, "\"(.*)\"");
                        if (match.Success)
                            length = match.Groups[1].Value.Length + 1;
                    }
                    try
                    {
                        IntPtr variableAddress = mem.Allocate(length);
                        mLogger.WriteLine($"[Inaba Exe Patcher] Allocated {length} byte{(length != 1 ? "s" : "")} for {name} at 0x{variableAddress:X}");
                        if (variableMatch.Groups[3].Success)
                            WriteValue(variableMatch.Groups[3].Value, variableAddress, name, 0);
                        variables.Add(name, variableAddress);
                    }
                    catch (Exception ex)
                    {
                        mLogger.WriteLine($"[Inaba Exe Patcher] Unable to allocate variable {name}: {ex.Message}");
                    }
                    continue;
                }

                // Search for a constant definition 
                var constantMatch = Regex.Match(line, @"^\s*const\s+(\S+)\s*=\s*(.+)", RegexOptions.IgnoreCase);
                if (constantMatch.Success)
                {
                    string name = constantMatch.Groups[1].Value;
                    if (constants.ContainsKey(name) || scanConstants.ContainsKey(name))
                    {
                        mLogger.WriteLine($"[Inaba Exe Patcher] Constant {name} in {Path.GetFileName(filePath)} already exists, ignoring duplicate declaration of it");
                        continue;
                    }
                    string constValue = constantMatch.Groups[2].Value;
                    var scanMatch = Regex.Match(constValue, @"scan\((.*)\)", RegexOptions.IgnoreCase);
                    if (scanMatch.Success)
                    {
                        scanConstants.Add(name, scanMatch.Groups[1].Value);
                    }
                    else
                    {
                        constants.Add(name, constantMatch.Groups[2].Value);
                    }
                    continue;
                }

                // Don't try to add stuff if the patch hasn't actually started yet
                if (!startPatch && !startReplacement) continue;

                // Search for a patten to scan for
                var patternMatch = Regex.Match(line, @"^\s*pattern\s*=\s*(.+)", RegexOptions.IgnoreCase);
                if (patternMatch.Success)
                {
                    pattern = patternMatch.Groups[1].Value.TrimEnd();
                    continue;
                }

                // Search for an order to execute the hook in
                var orderMatch = Regex.Match(line, @"^\s*order\s*=\s*(.+)", RegexOptions.IgnoreCase);
                if (orderMatch.Success)
                {
                    order = orderMatch.Groups[1].Value;
                    continue;
                }

                // Search for an offset to make the patch/replacement on
                var offsetMatch = Regex.Match(line, @"^\s*offset\s*=\s*(([+-])?(0x|0b)?([0-9A-Fa-f]+))", RegexOptions.IgnoreCase);
                if (offsetMatch.Success)
                {
                    int offsetBase = 10;
                    if (offsetMatch.Groups[3].Success)
                    {
                        if (offsetMatch.Groups[3].Value == "0b")
                            offsetBase = 2;
                        else if (offsetMatch.Groups[3].Value == "0x")
                            offsetBase = 16;
                    }
                    try
                    {
                        offset = Convert.ToInt32(offsetMatch.Groups[4].Value, offsetBase);
                        if (offsetMatch.Groups[2].Value == "-")
                            offset *= -1;
                    }
                    catch
                    {
                        mLogger.WriteLine($"[Inaba Exe Patcher] Unable to parse offset {offsetMatch.Groups[1].Value} as an int leaving offset as 0");
                    }
                    continue;
                }

                // Search for a literal to search for
                var searchMatch = Regex.Match(line, @"^\s*search\s*=\s*(.+)", RegexOptions.IgnoreCase);
                if (searchMatch.Success)
                {
                    string value = searchMatch.Groups[1].Value;
                    if (int.TryParse(value, out int intValue))
                    {
                        var bytes = BitConverter.GetBytes(intValue);
                        pattern = BitConverter.ToString(bytes).Replace("-", " ");
                    }
                    else if (Regex.IsMatch(value, @"[0-9]+f") && float.TryParse(value, out float floatValue))
                    {
                        var bytes = BitConverter.GetBytes(floatValue);
                        pattern = BitConverter.ToString(bytes).Replace("-", " ");
                    }
                    else if (double.TryParse(value, out double doubleValue))
                    {
                        var bytes = BitConverter.GetBytes(doubleValue);
                        pattern = BitConverter.ToString(bytes).Replace("-", " ");
                    }
                    else
                    {
                        var stringValueMatch = Regex.Match(value, "\"(.*)\"");
                        if (!stringValueMatch.Success)
                        {
                            mLogger.WriteLine($"[Inaba Exe Patcher] Unable to parse {value} as an int, double, float or string not creating search pattern");
                            continue;
                        }
                        string stringValue = Regex.Unescape(stringValueMatch.Groups[1].Value);
                        var bytes = Encoding.ASCII.GetBytes(stringValue);
                        pattern = BitConverter.ToString(bytes).Replace("-", " ");
                    }
                    continue;
                }

                // Search for a replacement (the actual value to set the thing to)
                var replaceValueMatch = Regex.Match(line, @"^\s*replacement\s*=\s*(.+)", RegexOptions.IgnoreCase);
                if (replaceValueMatch.Success)
                {
                    var value = replaceValueMatch.Groups[1].Value;
                    currentPatch.Add(value);
                    continue;
                }

                var padMatch = Regex.Match(line, @"^\s*padNull\s*=\s*(.+)", RegexOptions.IgnoreCase);
                if (padMatch.Success)
                {
                    string value = padMatch.Groups[1].Value;
                    if (!bool.TryParse(value, out padNull))
                        mLogger.WriteLine($"[Inaba Exe Patcher] Unable to parse {value} to a boolean value (true or false) leaving padNull unchanged");
                }

                // Add the line as a part of the patch's function
                if (startPatch)
                    currentPatch.Add(line);
            }
            if (startReplacement || startPatch)
                SaveCurrentPatch(currentPatch, patches, patchName, ref pattern, ref order, ref offset, ref padNull, startReplacement);
            FillInVariables(patches, variables, constants);
            return (patches, scanConstants);
        }

        private void SaveCurrentPatch(List<string> currentPatch, List<ExPatch> patches, string patchName, ref string pattern, ref string order, ref int offset, ref bool padNull, bool isReplacement)
        {
            if (currentPatch.Count > 0)
            {
                if (!isReplacement)
                    currentPatch.Insert(0, "use32");
                patches.Add(new ExPatch(patchName, pattern, currentPatch.ToArray(), order, offset, isReplacement, padNull));
            }
            currentPatch.Clear();
            pattern = "";
            order = "";
            offset = 0;
            padNull = true;
        }

        /// <summary>
        /// Replaces any constant definitions with their value
        /// </summary>
        /// <param name="patches">A list patches to replace the constant in</param>
        /// <param name="name">The name of the constant</param>
        /// <param name="value">The value of the constant</param>
        private void FillInConstant(List<ExPatch> patches, string name, string value)
        {
            foreach(var patch in patches)
            {
                for(int i = 0; i < patch.Function.Length; i++)
                {
                    patch.Function[i] = patch.Function[i].Replace($"{{{name}}}", value);
                }
            }
        }

        /// <summary>
        /// Replaces any variable and constant declarations in functions (such as {variableName}) with their actual addresses
        /// </summary>
        /// <param name="patches">A list of patches to replace the variables in</param>
        /// <param name="variables">A Dictionary where the key is the variable name and the value is the variable address</param>
        private void FillInVariables(List<ExPatch> patches, Dictionary<string, IntPtr> variables, Dictionary<string, string> constants)
        {
            if (variables.Count == 0 && constants.Count == 0)
                return;
            foreach (var patch in patches)
                for (int i = 0; i < patch.Function.Length; i++)
                {
                    foreach (var variable in variables)
                        patch.Function[i] = patch.Function[i].Replace($"{{{variable.Key}}}", variable.Value.ToString());
                    foreach (var constant in constants)
                        patch.Function[i] = patch.Function[i].Replace($"{{{constant.Key}}}", constant.Value);
                    patch.Function[i] = patch.Function[i].Replace("{pushCaller}", mHooks.Utilities.PushCdeclCallerSavedRegisters());
                    patch.Function[i] = patch.Function[i].Replace("{popCaller}", mHooks.Utilities.PopCdeclCallerSavedRegisters());
                    patch.Function[i] = patch.Function[i].Replace("{pushXmm}", Utils.PushXmm());
                    patch.Function[i] = patch.Function[i].Replace("{popXmm}", Utils.PopXmm());
                    var xmmMatch = Regex.Match(patch.Function[i], @"{pushXmm([0-9]+)}", RegexOptions.IgnoreCase);
                    if (xmmMatch.Success)
                        patch.Function[i] = patch.Function[i].Replace(xmmMatch.Groups[0].Value, Utils.PushXmm(int.Parse(xmmMatch.Groups[1].Value)));
                    xmmMatch = Regex.Match(patch.Function[i], @"{popXmm([0-9]+)}", RegexOptions.IgnoreCase);
                    if (xmmMatch.Success)
                        patch.Function[i] = patch.Function[i].Replace(xmmMatch.Groups[0].Value, Utils.PopXmm(int.Parse(xmmMatch.Groups[1].Value)));

                }
        }

        /// <summary>
        /// Writes the value to an address based on a string attempting to parse the string to an int, float or double, defaulting to writing it as a string if these fail
        /// </summary>
        /// <param name="value">The string to interpret and write</param>
        /// <param name="address">The address to write to</param>
        /// <param name="name">The name of the variable this is for</param>
        /// <param name="stringLength">The length of the string that should be written, if <paramref name="value"/> is shorter than this it will be padded with null characters. This has no effect if <paramref name="value"/> is not written as a string</param>
        private void WriteValue(string value, IntPtr address, string name, int stringLength)
        {
            Match match;
            match = Regex.Match(value, @"^([+-])?(0x|0b)?([0-9A-Fa-f]+)(u)?$");
            if (match.Success)
            {
                int offsetBase = 10;
                if (match.Groups[2].Success)
                {
                    if (match.Groups[2].Value == "0b")
                        offsetBase = 2;
                    else if (match.Groups[2].Value == "0x")
                        offsetBase = 16;
                }
                try
                {
                    if (match.Groups[4].Success)
                    {
                        uint intValue = Convert.ToUInt32(match.Groups[3].Value, offsetBase);
                        mem.SafeWrite(address, intValue);
                        mLogger.WriteLine($"[Inaba Exe Patcher] Wrote uint {intValue} as value of {name} at 0x{address:X}");
                    }
                    else
                    {
                        int intValue = Convert.ToInt32(match.Groups[3].Value, offsetBase);
                        if (match.Groups[1].Value == "-")
                            intValue *= -1;
                        mem.SafeWrite(address, intValue);
                        mLogger.WriteLine($"[Inaba Exe Patcher] Wrote int {intValue} as value of {name} at 0x{address:X}");
                    }
                    return;
                }
                catch { }
            }
            match = Regex.Match(value, @"^([+-])?(0x|0b)?([0-9A-Fa-f]+)([su])b$");
            if (match.Success)
            {
                int offsetBase = 10;
                if (match.Groups[2].Success)
                {
                    if (match.Groups[2].Value == "0b")
                        offsetBase = 2;
                    else if (match.Groups[2].Value == "0x")
                        offsetBase = 16;
                }
                try
                {
                    if (match.Groups[4].Value == "s")
                    {
                        sbyte byteValue = Convert.ToSByte(match.Groups[3].Value, offsetBase);
                        if (match.Groups[1].Value == "-")
                            byteValue *= -1;
                        mem.SafeWrite(address, byteValue);
                        mLogger.WriteLine($"[Inaba Exe Patcher] Wrote sbyte {byteValue} as value of {name} at 0x{address:X}");
                    }
                    else
                    {
                        byte byteValue = Convert.ToByte(match.Groups[3].Value, offsetBase);
                        mem.SafeWrite(address, byteValue);
                        mLogger.WriteLine($"[Inaba Exe Patcher] Wrote ubyte {byteValue} as value of {name} at 0x{address:X}");
                    }
                    return;
                }
                catch { }
            }
            match = Regex.Match(value, @"^([+-])?(0x|0b)?([0-9A-Fa-f]+)(u)?l$");
            if (match.Success)
            {
                int offsetBase = 10;
                if (match.Groups[2].Success)
                {
                    if (match.Groups[2].Value == "0b")
                        offsetBase = 2;
                    else if (match.Groups[2].Value == "0x")
                        offsetBase = 16;
                }
                try
                {
                    if (match.Groups[4].Success)
                    {
                        ulong longValue = Convert.ToUInt64(match.Groups[3].Value, offsetBase);
                        mem.SafeWrite(address, longValue);
                        mLogger.WriteLine($"[Inaba Exe Patcher] Wrote ulong {longValue} as value of {name} at 0x{address:X}");
                    }
                    else
                    {
                        long longValue = Convert.ToInt64(match.Groups[3].Value, offsetBase);
                        if (match.Groups[1].Value == "-")
                            longValue *= -1;
                        mem.SafeWrite(address, longValue);
                        mLogger.WriteLine($"[Inaba Exe Patcher] Wrote long {longValue} as value of {name} at 0x{address:X}");
                    }
                    return;
                }
                catch { }
            }
            match = Regex.Match(value, @"^([+-])?(0x|0b)?([0-9A-Fa-f]+)(u)?s$");
            if (match.Success)
            {
                int offsetBase = 10;
                if (match.Groups[2].Success)
                {
                    if (match.Groups[2].Value == "0b")
                        offsetBase = 2;
                    else if (match.Groups[2].Value == "0x")
                        offsetBase = 16;
                }
                try
                {
                    if (match.Groups[4].Success)
                    {
                        ushort shortValue = Convert.ToUInt16(match.Groups[3].Value, offsetBase);
                        mem.SafeWrite(address, shortValue);
                        mLogger.WriteLine($"[Inaba Exe Patcher] Wrote ushort {shortValue} as value of {name} at 0x{address:X}");
                    }
                    else
                    {
                        short shortValue = Convert.ToInt16(match.Groups[3].Value, offsetBase);
                        if (match.Groups[1].Value == "-")
                            shortValue *= -1;
                        mem.SafeWrite(address, shortValue);
                        mLogger.WriteLine($"[Inaba Exe Patcher] Wrote short {shortValue} as value of {name} at 0x{address:X}");
                    }
                    return;
                }
                catch { }
            }
            match = Regex.Match(value, @"^([+-])?([0-9]+(?:\.[0-9]+)?)f$");
            if (match.Success && float.TryParse(match.Groups[2].Value, out float floatValue))
            {
                if (match.Groups[1].Success)
                    floatValue *= -1;
                mem.SafeWrite(address, floatValue);
                mLogger.WriteLine($"[Inaba Exe Patcher] Wrote float {floatValue} as value of {name} at 0x{address:X}");
                return;
            }
            if (double.TryParse(value.Replace("d", ""), out double doubleValue))
            {
                mem.SafeWrite(address, doubleValue);
                mLogger.WriteLine($"[Inaba Exe Patcher] Wrote double {doubleValue} as value of {name} at 0x{address:X}");
            }
            else
            {
                var stringValueMatch = Regex.Match(value, "\"(.*)\"");
                if (!stringValueMatch.Success)
                {
                    mLogger.WriteLine($"[Inaba Exe Patcher] Unable to parse {value} as an int, double, float or string not writing a value for {name}");
                    return;
                }
                string stringValue = Regex.Unescape(stringValueMatch.Groups[1].Value);
                var stringBytes = Encoding.ASCII.GetBytes(stringValue);
                if (stringBytes.Length < stringLength)
                {
                    List<byte> byteList = stringBytes.ToList();
                    while (byteList.Count < stringLength)
                        byteList.Add(0);
                    stringBytes = byteList.ToArray();
                }
                mem.SafeWrite(address, stringBytes);
                mLogger.WriteLine($"[Inaba Exe Patcher] Wrote string \"{stringValue}\" as value of {name} at 0x{address:X}");
            }
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
        }
    }
}