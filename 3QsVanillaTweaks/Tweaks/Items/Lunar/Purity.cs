using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RoR2;
using UnityEngine;
using A=ThreeQsVanillaTweaks.ReflectionTools;
using U=Tweaks.Items.Uncommon.UncommonItemSupport;
using N=Tweaks.Items.Uncommon.UncommonNative;
namespace Tweaks.Items.Lunar
{
    // Native item ID: LunarBadLuck. Configuration and hooks for this item only.
    internal static class Purity
    {
        const string Body="RoR2.CharacterBody",Health="RoR2.HealthComponent",Events="RoR2.GlobalEventManager";
        internal static void Init()=>LunarConfigSupport.Register("LunarBadLuck",Configure);
        static void Configure(LunarConfig c)
        {
            var v=c.Values;
            c.Pair("Cooldown Reduction",2,1,"Flat skill cooldown reduction, in seconds.");c.Pair("Luck Penalty",1,1,"Number of extra rolls for an unfavourable outcome.");
            if(v.Enabled.Value){U.Ensure();U.Stats+=b=>{int n=v.Count(b);if(n<=0)return;if(b.master)b.master.luck-=v.Total("Luck Penalty",n)-n;float delta=v.Total("Cooldown Reduction",n)-(n+1);foreach(var skill in b.GetComponents<GenericSkill>())skill.flatCooldownReduction+=delta;};}
        }
    }
}
