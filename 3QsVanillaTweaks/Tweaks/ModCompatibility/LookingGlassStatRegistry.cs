using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using RoR2;

namespace Tweaks.ModCompatibility
{
    // Item tweaks register here without importing or referencing LookingGlass.
    // The registry is inert when LookingGlass is not installed.
    internal static class LookingGlassStatRegistry
    {
        private sealed class Definition
        {
            internal Func<ItemIndex> Item;
            internal string UniqueStatLabel;
            internal Func<int, string, string> Formatter;
        }

        private static readonly List<Definition> Definitions = new List<Definition>();

        internal static void Register(Func<ItemIndex> item, string uniqueStatLabel, Func<int, string, string> formatter)
        {
            if (item == null || string.IsNullOrEmpty(uniqueStatLabel) || formatter == null)
                throw new ArgumentException("LookingGlass stat definition is incomplete.");

            Definitions.RemoveAll(d => d.UniqueStatLabel == uniqueStatLabel);
            Definitions.Add(new Definition
            {
                Item = item,
                UniqueStatLabel = uniqueStatLabel,
                Formatter = formatter
            });
        }

        internal static string Format(string originalText)
        {
            if (string.IsNullOrEmpty(originalText)) return originalText;

            foreach (Definition definition in Definitions)
            {
                if (originalText.IndexOf(definition.UniqueStatLabel, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                // LookingGlass's formatter doesn't expose ItemIndex. Its unique existing
                // stat label identifies the entry; the ItemIndex is used for stack counts.
                int stacks = 1;
                LocalUser local = LocalUserManager.GetFirstLocalUser();
                Inventory inventory = local != null && local.cachedBody != null ? local.cachedBody.inventory : null;
                if (inventory != null)
                {
                    ItemIndex item = definition.Item();
                    if (item != ItemIndex.None)
                        stacks = Math.Max(1, inventory.GetItemCountPermanent(item));
                }

                return definition.Formatter(stacks, originalText);
            }
            return originalText;
        }

        internal static string ReplaceValue(string text, string label, float value, string color, string suffix = "")
        {
            string pattern = "(" + Regex.Escape(label) + @"[ \t]*(?:<[^>]+>)*[+\-]?)\d+(?:\.\d+)?";
            return Regex.Replace(text, pattern,
                m => m.Groups[1].Value + "<color=" + color + ">" + Number(value) + "</color>" + suffix,
                RegexOptions.IgnoreCase);
        }

        internal static string AddValue(string text, string label, float value, string suffix, string color, string sign = "")
        {
            // Prevent duplicates if the formatter is called more than once on a tooltip.
            if (text.IndexOf(label + ":", StringComparison.OrdinalIgnoreCase) >= 0)
                return ReplaceValue(text, label + ":", value, color);

            return text + (text.EndsWith("\n", StringComparison.Ordinal) ? "" : "\n") + label +
                ": <color=" + color + ">" + sign + Number(value) + suffix + "</color>";
        }

        internal static string Number(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
