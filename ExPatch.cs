using System;
using System.Collections.Generic;
using System.Text;

namespace p4gpc.inaba
{
    public class ExPatch
    {
        /// <summary>
        /// The name of the patch
        /// </summary>
        public string Name { get; set; }
        /// <summary>
        /// The pattern to sig scan for
        /// </summary>
        public string Pattern { get; set; }
        /// <summary>
        /// The function to add in the hook
        /// </summary>
        public string[] Function { get; set; }
        /// <summary>
        /// When to execute the function (first, after, or only)
        /// </summary>
        public string ExecutionOrder { get; set; }
        /// <summary>
        /// The offset to add to the address of the hook
        /// </summary>
        public int Offset { get; set; }
        /// <summary>
        /// If true this is a replacement instead of a code patch
        /// </summary>
        public bool IsReplacement { get; set; }
        /// <summary>
        /// If true when replacing values with strings the value will be padded with null characters up to the length of the search pattern
        /// </summary>
        public bool PadNull { get; set; }
        /// <summary>
        /// A list of indices to replace/patch at
        /// This will cause Inaba to scan multiple times and replace/patch each duplicate of it at these indices
        /// </summary>
        public List<int> Indices { get; set; }
        /// <summary>
        /// If true all occurences of the pattern will be replaced/patched
        /// </summary>
        public bool AllIndices { get; set; }

        public ExPatch(string name, string pattern, string[] function, string executionOrder, int offset, bool isReplacement, bool padNull, List<int> indices, bool allIndices)
        {
            Name = name;
            Pattern = pattern;
            Function = function;
            ExecutionOrder = executionOrder;
            Offset = offset;
            IsReplacement = isReplacement;
            PadNull = padNull;
            Indices = indices;
            AllIndices = allIndices;
        }
    }
}
