using Reloaded.Mod.Interfaces;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Text;

namespace p4gpc.inaba
{
    public class sprUtils
    {
        private readonly ILogger mLogger;
        public sprUtils(ILogger logger)
        {
            mLogger = logger;
        }
        private int Search(byte[] src, byte[] pattern)
        {
            int c = src.Length - pattern.Length + 1;
            int j;
            for (int i = 0; i < c; i++)
            {
                if (src[i] != pattern[0]) continue;
                for (j = pattern.Length - 1; j >= 1 && src[i + j] == pattern[j]; j--) ;
                if (j == 0) return i;
            }
            return -1;
        }

        private string getTmxName(byte[] tmx)
        {
            int end = Search(tmx, new byte[] { 0x00 });
            byte[] name = tmx[0..end];
            // hardcode for ◆noiz.tmx
            if (BitConverter.ToString(name[0..2]).Replace("-", "") == "819F")
                return $"◆{Encoding.ASCII.GetString(name[2..])}";
            return Encoding.ASCII.GetString(name);
        }

        public Dictionary<string, int> getTmxNames(string spr)
        {
            Dictionary<string, int> tmxNames = new Dictionary<string, int>();
            byte[] sprBytes = File.ReadAllBytes(spr);
            byte[] pattern = Encoding.ASCII.GetBytes("TMX0");
            int offset = 0;
            int found = 0;
            while (found != -1)
            {
                //mLogger.WriteLine($"[Aemulus]<Bin Merger> Starting search at {offset}");
                // Start search after "TMX0"
                found = Search(sprBytes[offset..], pattern);
                offset =  found + offset + 4;
                //mLogger.WriteLine($"[Aemulus]<Bin Merger> Offset updated to {offset}");
                if (found != -1)
                {
                    string tmxName = getTmxName(sprBytes[(offset + 24)..]);
                    //mLogger.WriteLine($"[Aemulus]<Bin Merger> Adding {tmxName} at offset {offset - 12}");
                    tmxNames.Add(tmxName, offset - 12);
                }
            }
            return tmxNames;
        }

        private List<int> getTmxOffsets(string spr)
        {
            List<int> tmxOffsets = new List<int>();
            byte[] sprBytes = File.ReadAllBytes(spr);
            byte[] pattern = Encoding.ASCII.GetBytes("TMX0");
            int offset = 0;
            int found = 0;
            while (found != -1)
            {
                //mLogger.WriteLine($"[Aemulus]<Bin Merger> Starting search at {offset}");
                // Start search after "TMX0"
                found = Search(sprBytes[offset..], pattern);
                offset = found + offset + 4;
                //mLogger.WriteLine($"[Aemulus]<Bin Merger> Offset updated to {offset}");
                if (found != -1)
                {
                    //mLogger.WriteLine($"[Aemulus]<Bin Merger> Adding offset {offset - 12} to list");
                    tmxOffsets.Add(offset - 12);
                }
            }
            return tmxOffsets;
        }

        private int findTmx(string spr, string tmxName)
        {
            // Get all tmx names instead to prevent replacing similar names
            if (File.Exists(spr))
            {
                Dictionary<string, int> tmxNames = getTmxNames(spr);
                if (tmxNames.ContainsKey(tmxName))
                    return tmxNames[tmxName];
            }
            return -1;
        }

        public void replaceTmx(string spr, string tmx)
        {
            string tmxPattern = Path.GetFileNameWithoutExtension(tmx);
            int offset = findTmx(spr, tmxPattern);
            //mLogger.WriteLine($"[Aemulus]<Bin Merger> .spr offset = {offset}");
            if (offset > -1)
            {
                byte[] tmxBytes = File.ReadAllBytes(tmx);
                int repTmxLen = tmxBytes.Length;
                int ogTmxLen = BitConverter.ToInt32(File.ReadAllBytes(spr)[(offset + 4)..(offset + 8)]);
                //mLogger.WriteLine($"[Aemulus]<Bin Merger> Replacement tmx length = {repTmxLen}");
                //mLogger.WriteLine($"[Aemulus]<Bin Merger> Original tmx length = {ogTmxLen}");

                if (repTmxLen == ogTmxLen)
                {
                    using (Stream stream = File.Open(spr, FileMode.Open))
                    {
                        stream.Position = offset;
                        stream.Write(tmxBytes, 0, repTmxLen);
                    }
                }
                else // Insert and update offsets
                {
                    byte[] sprBytes = File.ReadAllBytes(spr);
                    byte[] newSpr = new byte[sprBytes.Length + (repTmxLen - ogTmxLen)];
                    sprBytes[0..offset].CopyTo(newSpr, 0);
                    sprBytes[(offset + ogTmxLen)..].CopyTo(newSpr, offset + repTmxLen);
                    tmxBytes.CopyTo(newSpr, offset);
                    File.WriteAllBytes(spr, newSpr);
                    updateOffsets(spr, getTmxOffsets(spr));
                }
            }
        }

        private void updateOffsets(string spr, List<int> offsets)
        {
            // Start of tmx offsets
            int pos = 36;
            using (Stream stream = File.Open(spr, FileMode.Open))
            {
                foreach (int offset in offsets)
                {
                    byte[] offsetBytes = BitConverter.GetBytes(offset);
                    stream.Position = pos;
                    stream.Write(offsetBytes, 0, 4);
                    pos += 8;
                }
            }
        }

        public byte[] extractTmx(string spr, string tmx)
        {
            string tmxPattern = Path.GetFileNameWithoutExtension(tmx);
            int offset = findTmx(spr, tmxPattern);
            //mLogger.WriteLine($"[Aemulus]<Bin Merger> .spr offset = {offset}");
            if (offset > -1)
            {
                byte[] sprBytes = File.ReadAllBytes(spr);
                int tmxLen = BitConverter.ToInt32(sprBytes[(offset + 4)..(offset + 8)]);
                return sprBytes[offset..(offset + tmxLen)];
            }
            return null;
        }
    }
}
