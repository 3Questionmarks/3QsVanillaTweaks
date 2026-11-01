using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using RoR2;
using R = Tweaks.Items.Uncommon.UncommonConfig;
namespace Tweaks.Items.Lunar
{
    internal static class LunarDescriptionColours
    {
        // Preserve existing colours outside the selected downside. Flatten nested
        // style spans so an inner healing/utility style cannot override the red.
        internal static string Highlight(string source, string pattern)
        {
            var plain = new StringBuilder(); var styles = new List<string>(); var stack = new Stack<string>();
            for (int i = 0; i < source.Length;)
            {
                if (source[i] == '<')
                {
                    int end = source.IndexOf('>', i);
                    if (end >= 0)
                    {
                        string tag = source.Substring(i, end - i + 1);
                        if (tag.StartsWith("<style=")) { stack.Push(tag.Substring(7, tag.Length - 8)); i = end + 1; continue; }
                        if (tag == "</style>") { if (stack.Count > 0) stack.Pop(); i = end + 1; continue; }
                    }
                }
                plain.Append(source[i++]); styles.Add(stack.Count == 0 ? null : stack.Peek());
            }
            string text = plain.ToString(); var matches = Regex.Matches(text, pattern, RegexOptions.IgnoreCase);
            if (matches.Count == 0) return source;
            foreach (Match match in matches) for (int i = match.Index; i < match.Index + match.Length; i++) styles[i] = "cIsHealth";
            var output = new StringBuilder(); string active = null;
            for (int i = 0; i < text.Length; i++)
            {
                if (active != styles[i]) { if (active != null) output.Append("</style>"); active = styles[i]; if (active != null) output.Append("<style=" + active + ">"); }
                output.Append(text[i]);
            }
            if (active != null) output.Append("</style>");
            return output.ToString();
        }
    }
}
