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

        public ExPatch(string name, string pattern, string[] function, string executionOrder)
        {
            Name = name;
            Pattern = pattern;
            Function = function;
            ExecutionOrder = executionOrder;
        }
    }
}
