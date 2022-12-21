using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace p4gpc.inaba
{
    internal class Utils
    {        
        // Pushes the value of an xmm register to the stack, saving it so it can be restored with PopXmm
        public static string PushXmm(int xmmNum)
        {
            string rsp = Environment.Is64BitProcess ? "rsp" : "esp";
            return // Save an xmm register 
                $"sub {rsp}, 16\n" + // allocate space on stack
                $"movdqu dqword [{rsp}], xmm{xmmNum}\n";
        }

        // Pushes all xmm registers (0-7) to the stack, saving them to be restored with PopXmm
        public static string PushXmm()
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < 8; i++)
            {
                sb.Append(PushXmm(i));
            }
            return sb.ToString();
        }

        // Pops the value of an xmm register to the stack, restoring it after being saved with PushXmm
        public static string PopXmm(int xmmNum)
        {
            string rsp = Environment.Is64BitProcess ? "rsp" : "esp";
            return                 //Pop back the value from stack to xmm
                $"movdqu xmm{xmmNum}, dqword [{rsp}]\n" +
                $"add {rsp}, 16\n"; // re-align the stack
        }

        // Pops all xmm registers (0-7) from the stack, restoring them after being saved with PushXmm
        public static string PopXmm()
        {
            StringBuilder sb = new StringBuilder();
            for (int i = Environment.Is64BitProcess ? 15 : 7; i >= 0; i--)
            {
                sb.Append(PopXmm(i));
            }
            return sb.ToString();
        }

        public static bool EvaluateExpression(string expression, out long sum)
        {
            sum = 0;
            var matches = Regex.Matches(expression, @"([+-])?\s*(0x|0b)?([0-9a-f]+)");
            bool success = false;
            foreach(Match match in matches)
            {
                int offsetBase = 10;
                if (match.Groups[2].Success)
                {
                    if (match.Groups[2].Value == "0b")
                        offsetBase = 2;
                    else if (match.Groups[2].Value == "0x")
                        offsetBase = 16;
                }

                if (long.TryParse(match.Groups[3].Value, out var value))
                {
                    if (match.Groups[1].Success && match.Groups[1].Value == "-")
                        value *= -1;
                    sum += value;
                    success = true;
                }
            }
            return success;
        }
    }
}
