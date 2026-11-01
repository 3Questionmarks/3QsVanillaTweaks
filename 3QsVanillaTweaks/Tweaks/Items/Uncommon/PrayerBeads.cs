using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using RoR2;
using RoR2.Orbs;
using UnityEngine;
using UnityEngine.Networking;
using C = Config.ConfigManager;
using S = Tweaks.Items.Common.CommonItemSupport;
using U = Tweaks.Items.Uncommon.UncommonItemSupport;
using A = ThreeQsVanillaTweaks.ReflectionTools;

namespace Tweaks.Items.Uncommon
{
    internal static class PrayerBeads
    {
        static UncommonConfig c;
        internal static void Init()
        {
            c = new UncommonConfig("Prayer Beads", "ExtraStatsOnLevelUp", true);
            c.N("Experience Modifier Per Stack (%)", 25, "Extra bead experience per additional stack. Vanilla: 25%.");
            c.Pair("Stats Stored (%)", 20, 10, "Percentage of level-up health, shield, regeneration and damage stored. Vanilla: 20 +5 per stack.");
            c.N("Experience Reduction Per Consumed Buff (%)", 0, "Experience reduction for each previously consumed bead level. Vanilla: 2.5%.", 0, 100);
            c.B("Prioritize At Printers", false, "Consume Prayer Beads before other eligible green items when a printer would otherwise pick randomly. Vanilla: false.");
            if (c.Enabled.Value)
            {
                U.Patch(typeof(CharacterMaster), "TrackBeadExperience", typeof(PrayerBeads), prefix: nameof(Experience));
            }

            c.Literal("RoR2.CharacterMaster", "TrackBeadExperience", .25f, () => 0);
            c.Literal("RoR2.CharacterMaster", "TrackBeadExperience", .025f, () => 0);
            RoR2Application.onLoad += () =>
            {
                if (!c.Enabled.Value || !c.Item)
                    return;
                var tags = new List<ItemTag>(c.Item.tags);
                tags.Remove(ItemTag.PriorityScrap);
                if (c.B("Prioritize At Printers"))
                {
                    if (!tags.Contains(ItemTag.Scrap))
                        tags.Add(ItemTag.Scrap);
                }
                else
                    tags.Remove(ItemTag.Scrap);
                c.Item.tags = tags.ToArray();
            };
            // The compiler-generated stat application method is patched by name.
            if (c.Enabled.Value)
            {
                foreach (var method in typeof(CharacterBody).GetMethods(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic))
                    if (method.Name.Contains("CalculateAppliedBeadStats"))
                    {
                        UncommonNative.AddLiteral(c, "RoR2.CharacterBody", method.Name, .2f, () => c.F("Stats Stored (%)") / 100, null, 0, int.MaxValue);
                        UncommonNative.AddLiteral(c, "RoR2.CharacterBody", method.Name, .05f, () => c.F("Stats Stored (%) Per Stack") / 100, null, 0, int.MaxValue);
                    }
            }

            c.Text(() => "Absorb experience to store " + c.V("Stats Stored (%)", "%", "cIsUtility") + " of level-up stats. Gain all stored <style=cIsHealth>health and regeneration</style> and <style=cIsDamage>damage</style> when the beads are removed.", () => "Store <style=cIsUtility>stats</style> as you gain experience. Removing the beads grants the stored stats.");
        }

        static void Experience(CharacterMaster __instance, ref ulong amount)
        {
            if (!c.Active || !__instance.inventory)
                return;
            int n = __instance.inventory.GetItemCountEffective(c.Item);
            if (n <= 0)
                return;
            float modifier = 1 + c.F("Experience Modifier Per Stack (%)") * (n - 1) / 100;
            int consumed = (int)A.Field(typeof(CharacterMaster), "numberOfBeadStatsGained_XPGainNerf").GetValue(__instance);
            modifier = Mathf.Max(.2f, modifier - consumed * c.F("Experience Reduction Per Consumed Buff (%)") / 100);
            amount = (ulong)Math.Min(ulong.MaxValue, (double)amount * modifier);
        }
    }
}
