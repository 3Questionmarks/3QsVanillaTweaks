using System;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using UnityEngine;
using C = Config.ConfigManager;
namespace Tweaks.Interactables
{
    internal static class BarrelRewards
    {
        internal enum RewardScaling { SmallChestCost, Linear }
        sealed class Settings
        {
            internal ConfigEntry<bool> Enabled, Multiplayer;
            internal ConfigEntry<int> Gold, XP;
            internal ConfigEntry<RewardScaling> Scaling;
            internal Settings(string name,int gold,int xp) {
                string s="Interactables - "+name;
                Enabled=C.BindBool(s,"Enabled",true,"Enable configurable rewards.");
                Gold=C.Bind(s,"Gold",gold,new ConfigDescription("Base gold reward before difficulty scaling.",new AcceptableValueRange<int>(0,1000000)));
                XP=C.Bind(s,"XP",xp,new ConfigDescription("Base experience reward before difficulty scaling.",new AcceptableValueRange<int>(0,1000000)));
                Scaling=C.Bind(s,"Scaling",RewardScaling.SmallChestCost,"SmallChestCost scales rewards like the cost of a small chest. Linear uses vanilla difficulty-coefficient scaling.");
                Multiplayer=C.BindBool(s,"Multiplayer Scaling",true,"Include multiplayer difficulty scaling when using SmallChestCost. Vanilla linear scaling always includes multiplayer difficulty.");
            }
        }
        static Settings barrels,stalks;
        internal static void Init()
        {
            barrels=new Settings("Barrels",8,4);
            stalks=new Settings("Stalks",25,0);
            new Harmony("3Qs.BarrelRewards").Patch(AccessTools.Method(typeof(BarrelInteraction),"Start"),postfix:new HarmonyMethod(typeof(BarrelRewards),nameof(After)));
        }
        static void After(BarrelInteraction __instance)
        {
            string name=__instance.name+" "+__instance.displayNameToken;
            bool stalk=name.IndexOf("VoidCoinBarrel",StringComparison.OrdinalIgnoreCase)>=0 || name.IndexOf("stalk",StringComparison.OrdinalIgnoreCase)>=0;
            if (!stalk && !__instance.name.StartsWith("Barrel1", StringComparison.OrdinalIgnoreCase)) return;
            var settings=stalk?stalks:barrels;
            if(!C.Get(settings.Enabled) || !Run.instance) return;
            float difficulty=Run.instance.difficultyCoefficient;
            if(C.Get(settings.Scaling)==RewardScaling.SmallChestCost && !C.Get(settings.Multiplayer)) {
                float players=Math.Max(1,Run.instance.participatingPlayerCount);
                float stage=Mathf.Pow(1.15f,Run.instance.effectiveStageClearCount);
                difficulty=stage*(1f+(difficulty/stage-(0.7f+players*0.3f))/Mathf.Pow(players,0.2f));
            }
            float scale=C.Get(settings.Scaling)==RewardScaling.SmallChestCost?Mathf.Pow(difficulty,1.25f):difficulty;
            __instance.goldReward=(int)Math.Min(int.MaxValue,Math.Floor(C.Get(settings.Gold)*(double)scale));
            __instance.expReward=(uint)Math.Min(uint.MaxValue,Math.Floor(C.Get(settings.XP)*(double)scale));
        }
    }
}
