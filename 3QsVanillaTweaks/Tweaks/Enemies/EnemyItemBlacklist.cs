using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BepInEx.Configuration;
using RoR2;
using C = Config.ConfigManager;

namespace Tweaks.Enemies
{
    internal static class EnemyItemBlacklist
    {
        private const string Section = "Enemies - Item Blacklist";
        private const string DefaultItems =
            "Tougher Times, Safer Spaces, Shuriken, Shipping Request Form, Regenerating Scrap, " +
            "Shield Booster, Tesla Coil, N'kuhana's Opinion, Frost Relic, Spare Drone Parts, Empathy Cores";

        private static ConfigEntry<bool> enabled;
        private static ConfigEntry<string> items;

        internal static void Init()
        {
            enabled = C.BindBool(Section, "Enable Additional Blacklist", true,
                "Keep the listed items out of enemy item rolls. Existing game restrictions remain in place. Restart to apply.");
            items = C.Bind(Section, "Additional Blacklisted Items", DefaultItems,
                "Comma-separated item names or internal names. Applies to enemy item rolls such as Evolution, Void Fields and Simulacrum. Restart to apply.");
            RoR2Application.onLoad += Apply;
        }

        private static void Apply()
        {
            if (!C.Get(enabled)) return;
            var seen = new HashSet<ItemIndex>();
            foreach (string entry in (C.Get(items) ?? "").Split(','))
            {
                string name = entry.Trim();
                if (name.Length == 0) continue;
                ItemIndex index = FindItem(name);
                if (index == ItemIndex.None || !seen.Add(index)) continue;
                ItemDef item = ItemCatalog.GetItemDef(index);
                if (!item) continue;
                var tags = new List<ItemTag>(item.tags ?? Array.Empty<ItemTag>());
                if (tags.Contains(ItemTag.AIBlacklist)) continue;
                tags.Add(ItemTag.AIBlacklist);
                item.tags = tags.ToArray();
            }
        }

        private static ItemIndex FindItem(string name)
        {
            // Accept the names players commonly use for these items as well as their catalog names.
            switch (Normalize(name))
            {
                case "TESLACOIL": name = "ShockNearby"; break;
                case "SHIELDBOOSTER": name = "ShieldBooster"; break;
            }
            ItemIndex index = ItemCatalog.FindItemIndex(name);
            if (index != ItemIndex.None) return index;
            string normalized = Normalize(name);
            for (int i = 0; i < ItemCatalog.itemCount; i++)
            {
                ItemDef item = ItemCatalog.GetItemDef((ItemIndex)i);
                if (item && (Normalize(item.name) == normalized ||
                             Normalize(Language.GetString(item.nameToken)) == normalized))
                    return (ItemIndex)i;
            }
            return ItemIndex.None; // An uninstalled DLC or mod item is safely ignored.
        }

        private static string Normalize(string value)
        {
            var result = new StringBuilder();
            foreach (char c in value ?? "")
                if (char.IsLetterOrDigit(c)) result.Append(char.ToUpperInvariant(c));
            return result.ToString();
        }
    }
}
