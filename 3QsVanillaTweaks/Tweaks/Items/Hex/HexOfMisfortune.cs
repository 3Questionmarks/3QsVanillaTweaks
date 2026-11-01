using System.Collections.Generic;
using HarmonyLib;
using RoR2;
using RoR2.Items;
using C=Config.ConfigManager;
namespace Tweaks.Items.Hex
{
    internal static class HexOfMisfortune
    {
        internal static void Init()=>HexConfigSupport.Register("RandomDebuffOnInteract",Configure);
        static void Configure(HexConfig c)
        {
            c.Number("Debuff Chance Per Stack (%)",50,"Chance per stack to apply the debuff set on interacting. Failed chance shrines always trigger it.");
            c.Values.Literal("RoR2.Items.RandomDebuffOnInteractBehaviour","RollChanceForDebuff",50,()=>100-c.Values.F("Debuff Chance Per Stack (%)"));
            c.Literal("RoR2.Items.RandomDebuffOnInteractBehaviour","ApplyDebuff",.5f,"Damage Over Time Multiplier",1);
            var pool=(List<RandomDebuffOnInteractBehaviour.BuffConfig>)AccessTools.Field(typeof(RandomDebuffOnInteractBehaviour),"_debuffPool").GetValue(null);
            for(int i=0;i<pool.Count;i++)
            {
                int index=i;var original=pool[i];var buff=BuffCatalog.GetBuffDef(original.buffIndex);
                string name=HexConfig.Words(buff.name),duration=name+" Duration",stacks=name+" Stacks";
                c.Number(duration,original.duration,"Debuff duration in seconds.");
                c.Number(stacks,original.stacks,"Number of debuff stacks applied.");
                if(!c.Values.Enabled.Value)continue;
                System.Action apply=()=>{var value=original;if(c.Values.Active){value.duration=c.Values.F(duration);value.stacks=UnityEngine.Mathf.RoundToInt(c.Values.F(stacks));}pool[index]=value;};
                apply();C.EffectiveSettingsChanged+=apply;
            }
        }
    }
}
