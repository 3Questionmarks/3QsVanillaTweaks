using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using BepInEx.Configuration;
using RoR2;
using UnityEngine.Networking;
using ModConfig = Config.ConfigManager;

namespace Tweaks.DebugStuff
{
    internal static class TestLoadout
    {
        private const string Section = "Debug - Test Loadout";
        private static readonly ConfigEntry<bool>[] enabled = new ConfigEntry<bool>[5];
        private static readonly ConfigEntry<string>[] items = new ConfigEntry<string>[5];
        private static Run lastLoadoutRun;
        private static readonly Dictionary<CharacterMaster, int> grantedLoadouts = new Dictionary<CharacterMaster, int>();

        internal static void Init()
        {
            string[] defaults =
            {
                "Mocha:40, Hopoo Feather:100, Alien Head:50, Dio's Best Friend:10, Rejuvenation Rack:10, Weeping Fungus:10, Ceremonial Dagger:10, Lens-Maker's Glasses:10, Laser Scope:10",
                "Paul's Goat Hoof:2, Hopoo Feather:100, Alien Head:50, Dio's Best Friend:10, Rejuvenation Rack:10, Weeping Fungus:10",
                "Paul's Goat Hoof:5, Hopoo Feather:5, Energy Drink:3",
                "Lens-Maker's Glasses:10, Soldier's Syringe:10, AtG Missile Mk. 1:1, Ukulele:1",
                "Tougher Times:10, Repulsion Armor Plate:5, Dio's Best Friend:3, Cautious Slug:5, Bustling Fungus:5"
            };
            string[] purposes = { "General testing", "Skill testing without damage items", "Movement testing", "Proc testing", "Defence testing" };
            for (int i = 0; i < enabled.Length; i++)
            {
                string name = "Loadout " + (i + 1);
                enabled[i] = ModConfig.BindBool(Section, name + " Enabled", true,
                    "Give each player this loadout once per run. Enabled loadouts combine.");
                items[i] = ModConfig.Bind(Section, name + " Items", defaults[i],
                    purposes[i] + ". Comma-separated Item:Count entries. Use item names or internal names. Example: Mocha:40, Feather:5.");
            }
            CharacterBody.onBodyStartGlobal += OnPlayerBodyStart;
        }

        private static void OnPlayerBodyStart(CharacterBody body)
        {
            if (!NetworkServer.active || Run.instance == null || body == null || body.master == null ||
                body.inventory == null || body.master.playerCharacterMasterController == null) return;
            if (lastLoadoutRun != Run.instance)
            {
                grantedLoadouts.Clear();
                lastLoadoutRun = Run.instance;
            }
            CharacterMaster master = body.master;
            grantedLoadouts.TryGetValue(master, out int granted);
            for (int i = 0; i < enabled.Length; i++)
            {
                int bit = 1 << i;
                if ((granted & bit) != 0 || !ModConfig.Get(enabled[i])) continue;
                foreach (var pair in ParseItems(ModConfig.Get(items[i]), i + 1))
                {
                    if (SpawnRequirements.ItemAllowed(ItemCatalog.GetItemDef(pair.Key)))
                        body.inventory.GiveItemPermanent(pair.Key, pair.Value);
                }
                granted |= bit;
                grantedLoadouts[master] = granted;
                ThreeQsVanillaTweaks.Log.Info("Debug: granted test loadout " + (i + 1) + " to " + master.name + ".");
            }
        }

        private static Dictionary<ItemIndex, int> ParseItems(string list, int loadout)
        {
            var result = new Dictionary<ItemIndex, int>();
            foreach (string entry in (list ?? "").Split(','))
            {
                if (string.IsNullOrWhiteSpace(entry)) continue;
                int separator = entry.LastIndexOf(':');
                if (separator <= 0 || !int.TryParse(entry.Substring(separator + 1).Trim(), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out int count) || count < 0)
                {
                    Warn(loadout, entry, "use Item:Count with a non-negative whole number");
                    continue;
                }
                string name = entry.Substring(0, separator).Trim();
                ItemIndex item = FindItem(name);
                if (item == ItemIndex.None) { Warn(loadout, entry, "item not found"); continue; }
                if (count == 0) continue;
                result.TryGetValue(item, out int previous);
                result[item] = (int)Math.Min(int.MaxValue, (long)previous + count);
            }
            return result;
        }

        private static ItemIndex FindItem(string name)
        {
            ItemIndex exact = ItemCatalog.FindItemIndex(name);
            if (exact != ItemIndex.None) return exact;
            string normalised = Normalise(name);
            if (normalised.Length == 0) return ItemIndex.None;
            for (int i = 0; i < ItemCatalog.itemCount; i++)
            {
                ItemDef item = ItemCatalog.GetItemDef((ItemIndex)i);
                if (item && (Normalise(item.name) == normalised || Normalise(Language.GetString(item.nameToken)) == normalised))
                    return (ItemIndex)i;
            }
            return ItemIndex.None;
        }

        private static string Normalise(string name)
        {
            var result = new StringBuilder();
            foreach (char c in name ?? "") if (char.IsLetterOrDigit(c)) result.Append(char.ToUpperInvariant(c));
            return result.ToString();
        }

        private static void Warn(int loadout, string entry, string reason) =>
            ThreeQsVanillaTweaks.Log.Warning("Debug loadout " + loadout + ": skipped '" + entry.Trim() + "' (" + reason + ").");
    }
}
