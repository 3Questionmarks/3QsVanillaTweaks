namespace Tweaks.Items.Equipment
{
    internal static class GoragsOpus
    {
        static EquipmentConfig c;
        internal static void Init()
        {
            c=new EquipmentConfig("Gorags Opus","TeamWarCry");
            c.N("Buff Duration",10,"Duration of frenzy for you and your allies. Vanilla: 7 seconds.");
            c.Literal("FireTeamWarCry",7,"Buff Duration");c.Literal("FireTeamWarCry",7,"Buff Duration",occurrence:1);
            // Native WarCryBuff and TeamWarCry share their stat branches; these options add only the Opus-specific difference.
            c.N("Movement Speed (%)",50,"Movement speed bonus. Vanilla: 50%.");c.N("Attack Speed (%)",100,"Attack speed bonus. Vanilla: 100%.");
            if(c.Values.Enabled.Value)Tweaks.Items.Uncommon.UncommonItemSupport.Harmony.Patch(ThreeQsVanillaTweaks.ReflectionTools.Method(typeof(RoR2.CharacterBody),"RecalculateStats"),transpiler:new HarmonyLib.HarmonyMethod(typeof(GoragsOpus),nameof(StatsIL)));
            c.Text(()=>"All allies enter a <style=cIsDamage>frenzy</style> for "+c.V("Buff Duration"," seconds","cIsUtility")+". Increases <style=cIsUtility>movement speed</style> by "+c.V("Movement Speed (%)","%","cIsUtility")+" and <style=cIsDamage>attack speed</style> by "+c.V("Attack Speed (%)","%")+".",()=>"Empower yourself and your allies with a frenzy.");
        }
        static System.Collections.Generic.IEnumerable<HarmonyLib.CodeInstruction> StatsIL(System.Collections.Generic.IEnumerable<HarmonyLib.CodeInstruction> input)
        {
            var code=new System.Collections.Generic.List<HarmonyLib.CodeInstruction>(input);
            for(int i=0;i<code.Count;i++)if(code[i].operand is System.Reflection.FieldInfo field&&field.Name=="TeamWarCry")
                for(int j=i+1;j<System.Math.Min(i+8,code.Count);j++)if(code[j].opcode==System.Reflection.Emit.OpCodes.Ldc_R4&&((float)code[j].operand==.5f||(float)code[j].operand==1f)){
                    // Load the body explicitly: this patch does not rely on another native patch having established a thread context.
                    code.Insert(j+1,new HarmonyLib.CodeInstruction(System.Reflection.Emit.OpCodes.Ldarg_0));
                    code.Insert(j+2,new HarmonyLib.CodeInstruction(System.Reflection.Emit.OpCodes.Call,ThreeQsVanillaTweaks.ReflectionTools.Method(typeof(GoragsOpus),(float)code[j].operand==.5f?nameof(Movement):nameof(AttackSpeed))));i=j+2;break;
                }
            return code;
        }
        static float Movement(float native,RoR2.CharacterBody b)=>c.Active&&b.HasBuff(RoR2.RoR2Content.Buffs.TeamWarCry)?c.F("Movement Speed (%)")/100:native;
        static float AttackSpeed(float native,RoR2.CharacterBody b)=>c.Active&&b.HasBuff(RoR2.RoR2Content.Buffs.TeamWarCry)?c.F("Attack Speed (%)")/100:native;
    }
}
