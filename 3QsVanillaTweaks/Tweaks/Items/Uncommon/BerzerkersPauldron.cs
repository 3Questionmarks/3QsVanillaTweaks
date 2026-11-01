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
    internal static class BerzerkersPauldron
    {
        static UncommonConfig c;
        static BuffDef frenzy;
        sealed class State
        {
            internal readonly Queue<float> Kills = new Queue<float>();
        }

        static readonly ConditionalWeakTable<CharacterBody, State> states = new ConditionalWeakTable<CharacterBody, State>();
        internal static void Init()
        {
            c = new UncommonConfig("Berzerker's Pauldron", "WarCryOnMultiKill", true);
            c.N("Kills Required", 2, "Enemies required to trigger. Vanilla: 4.", 1);
            c.N("Kill Window", 1, "Seconds between consecutive qualifying kills. Vanilla: 1.", 0.01f);
            c.Pair("Frenzy Duration", 6, 0, "Seconds of frenzy. Vanilla: 6 +4 per stack.");
            c.Pair("Movement Speed (%)", 15, 0, "Movement speed per frenzy stack. Vanilla: 50%.");
            c.Pair("Attack Speed (%)", 30, 0, "Attack speed per frenzy stack. Vanilla: 100%.");
            c.Pair("Damage (%)", 0, 0, "Damage per frenzy stack. Vanilla: 0%.");
            c.B("Retrigger Refreshes Duration", true, "Retriggering refreshes the remaining duration of existing frenzy stacks.");
            c.B("Buff Stacks", true, "Retriggering adds another frenzy stack, up to the configured limit.");
            c.Pair("Maximum Buff Stacks", 3, 2, "Maximum simultaneous frenzy stacks. Vanilla: 1.");
            frenzy = U.Buff("Frenzy", false, () => RoR2Content.Buffs.WarCryBuff, () => "Each stack increases attack speed by " + c.V("Attack Speed (%)", "%") + " and movement speed by " + c.V("Movement Speed (%)", "%") + ".");
            c.Suppress("RoR2.CharacterBody", "AddMultiKill");
            U.Ensure();
            U.Kill += Kill;
            U.Stats += Stats;
            c.ShowStat = key => !key.StartsWith("Kill Window") || c.F("Kills Required") > 1;
            c.Text(() => "Killing " + (c.F("Kills Required") == 1 ? "an enemy" : c.V("Kills Required", "", "cIsUtility") + " enemies within " + c.V("Kill Window", "s", "cIsUtility")) + " grants <style=cIsDamage>frenzy</style> for " + c.V("Frenzy Duration", "s", "cIsUtility") + ", increasing attack speed by " + c.V("Attack Speed (%)", "%") + " and movement speed by " + c.V("Movement Speed (%)", "%") + (c.F("Damage (%)") != 0 ? ", and damage by " + c.V("Damage (%)", "%") : "") + "." + (c.B("Buff Stacks") ? " Stacks up to " + c.V("Maximum Buff Stacks", "", "cIsUtility") + " times." : ""), () => "Rapid kills grant <style=cIsDamage>frenzy</style>, increasing attack and movement speed.");
        }

        static void Kill(DamageReport r)
        {
            var b = r.attackerBody;
            int n = c.Count(b);
            if (n == 0)
                return;
            var s = states.GetOrCreateValue(b);
            float now = Time.fixedTime;
            if (s.Kills.Count > 0 && now - Last(s.Kills) > c.F("Kill Window"))
                s.Kills.Clear();
            s.Kills.Enqueue(now);
            if (s.Kills.Count < Mathf.CeilToInt(c.F("Kills Required")))
                return;
            s.Kills.Clear();
            int old = b.GetBuffCount(frenzy), cap = c.B("Buff Stacks") ? Mathf.Max(1, Mathf.FloorToInt(c.Total("Maximum Buff Stacks", n))) : 1;
            if (c.B("Retrigger Refreshes Duration"))
            {
                b.ClearTimedBuffs(frenzy);
                for (int i = 0; i < Mathf.Min(old, cap); i++)
                    b.AddTimedBuff(frenzy, c.Total("Frenzy Duration", n));
            }

            if (old < cap)
                b.AddTimedBuff(frenzy, c.Total("Frenzy Duration", n));
        }

        static float Last(Queue<float> q)
        {
            float x = 0;
            foreach (float t in q)
                x = t;
            return x;
        }

        static void Stats(CharacterBody b)
        {
            int n = c.Count(b), buffs = b.GetBuffCount(frenzy);
            if (n == 0 || buffs == 0)
                return;
            U.Scale(b, "attackSpeed", c.Total("Attack Speed (%)", n) * buffs);
            U.Scale(b, "moveSpeed", c.Total("Movement Speed (%)", n) * buffs);
            U.Scale(b, "damage", c.Total("Damage (%)", n) * buffs);
        }
    }
}
