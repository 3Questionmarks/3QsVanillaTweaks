using System;
using System.Collections;
using System.Linq;
using System.Reflection;
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
            Definitions.Add(new Definition { Item = item, UniqueStatLabel = uniqueStatLabel, Formatter = formatter });
        }

        sealed class ContextDefinition
        {
            internal Func<ItemIndex> Item;
            internal Func<bool> Active;
            internal Func<CharacterMaster, int, string, string> Formatter;
        }

        static readonly List<ContextDefinition> ContextDefinitions = new List<ContextDefinition>();
        static readonly List<FieldInfo> definitionTables = new List<FieldInfo>();
        internal static void RegisterItem(Func<ItemIndex> item, Func<CharacterMaster, int, string, string> formatter, Func<bool> active = null)
        {
            ContextDefinitions.Add(new ContextDefinition { Item = item, Formatter = formatter, Active = active });
        }

        internal static void ConfigureContext(Assembly assembly)
        {
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException error)
            {
                types = error.Types.Where(t => t != null).ToArray();
            }

            foreach (var type in types.Where(t => t.FullName.IndexOf("ItemStats", StringComparison.OrdinalIgnoreCase) >= 0 || t.Name.IndexOf("ItemDefinitions", StringComparison.OrdinalIgnoreCase) >= 0))
                foreach (var field in type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                    if (typeof(IDictionary).IsAssignableFrom(field.FieldType))
                        definitionTables.Add(field);
        }

        internal static bool MatchesDefinition(IDictionary table, ItemIndex item, object[] arguments)
        {
            object definition = table.Contains(item) ? table[item] : table.Contains((int)item) ? table[(int)item] : null;
            return definition != null && arguments.Any(argument => ReferenceEquals(argument, definition));
        }

        internal static bool TryFormatItem(string text, object[] arguments, MethodBase method, out string result)
        {
            result = text;
            if (arguments == null || ContextDefinitions.Count == 0)
                return false;
            var master = arguments.OfType<CharacterMaster>().FirstOrDefault();
            if (!master)
            {
                var body = arguments.OfType<CharacterBody>().FirstOrDefault();
                if (body)
                    master = body.master;
            }

            int? explicitCount = null;
            var parameters = method.GetParameters();
            for (int i = 0; i < parameters.Length; i++)
                if (arguments[i] is int count && (parameters[i].Name.IndexOf("count", StringComparison.OrdinalIgnoreCase) >= 0 || parameters[i].Name.IndexOf("stack", StringComparison.OrdinalIgnoreCase) >= 0))
                    explicitCount = count;
            if (!explicitCount.HasValue)
                explicitCount = arguments.OfType<int>().Select(n => (int? )n).FirstOrDefault();
            foreach (var entry in ContextDefinitions)
            {
                if (entry.Active != null && !entry.Active())
                    continue;
                ItemIndex item = entry.Item();
                if (item == ItemIndex.None)
                    continue;
                bool matches = arguments.Any(a => a is ItemIndex index && index == item || a is ItemDef definition && definition.itemIndex == item);
                if (!matches)
                    foreach (var field in definitionTables)
                    {
                        var table = field.GetValue(null) as IDictionary;
                        if (table == null)
                            continue;
                        if (MatchesDefinition(table, item, arguments))
                        {
                            matches = true;
                            break;
                        }
                    }

                if (!matches)
                    continue;
                if (!master)
                    master = LocalUserManager.GetFirstLocalUser()?.cachedMaster;
                int stacks = explicitCount ?? (master && master.inventory ? master.inventory.GetItemCountEffective(item) : 1);
                result = entry.Formatter(master, Math.Max(0, stacks), text);
                return true;
            }

            return false;
        }

        internal static bool TryFormatKnownItem(ItemIndex item, CharacterMaster master, int count, out string stats)
        {
            foreach (var entry in ContextDefinitions)
                if ((entry.Active == null || entry.Active()) && entry.Item() == item)
                {
                    stats = entry.Formatter(master, Math.Max(0, count), "");
                    return true;
                }
            stats = null;
            return false;
        }

        internal static string Format(string originalText)
        {
            if (string.IsNullOrEmpty(originalText))
                return originalText;
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
            return Regex.Replace(text, pattern, m => m.Groups[1].Value + "<color=" + color + ">" + Number(value) + "</color>" + suffix, RegexOptions.IgnoreCase);
        }

        internal static string AddValue(string text, string label, float value, string suffix, string color, string sign = "")
        {
            // Prevent duplicates if the formatter is called more than once on a tooltip.
            if (text.IndexOf(label + ":", StringComparison.OrdinalIgnoreCase) >= 0)
                return ReplaceValue(text, label + ":", value, color);
            return text + (text.EndsWith("\n", StringComparison.Ordinal) ? "" : "\n") + label + ": <color=" + color + ">" + sign + Number(value) + suffix + "</color>";
        }

        internal static string Number(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
