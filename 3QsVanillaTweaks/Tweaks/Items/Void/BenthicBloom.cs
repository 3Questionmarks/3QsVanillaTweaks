using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RoR2;
using R = Tweaks.Items.Uncommon.UncommonConfig;
using U = Tweaks.Items.Uncommon.UncommonItemSupport;
using A = ThreeQsVanillaTweaks.ReflectionTools;
namespace Tweaks.Items.Void
{
    internal static class BenthicBloom
    {
        static R c;
        internal static void Init()
        {
            U.Ensure(); c = new R("Benthic Bloom", "CloverVoid", true, "Void");
            c.B("Cannot Upgrade Into 57 Leaf Clover", true, "Exclude 57 Leaf Clover from this item's upgrade choices only. Vanilla: false.");
            c.Pair("Upgrades Per Stage", 3, 3, "Number of complete item stacks upgraded at the start of each stage. Vanilla: 3 +3 per stack.");
            if (c.Enabled.Value) U.Harmony.Patch(A.Method(typeof(CharacterMaster), "TryCloverVoidUpgrades"), transpiler: new HarmonyMethod(typeof(BenthicBloom), nameof(UpgradeIL)));
            c.Text(() => "Upgrades " + c.V("Upgrades Per Stage", "", "cIsUtility") + " random items to items of the next <style=cIsUtility>higher rarity</style> at the start of each stage." + (c.B("Cannot Upgrade Into 57 Leaf Clover") ? " Cannot upgrade into <style=cIsUtility>57 Leaf Clover</style>." : "") + " <style=cIsVoid>Corrupts all 57 Leaf Clovers</style>.", () => "Upgrade random items at the start of each stage. <style=cIsVoid>Corrupts all 57 Leaf Clovers</style>.");
        }
        static List<PickupIndex> Filter(List<PickupIndex> copy)
        { if (c.Active && c.B("Cannot Upgrade Into 57 Leaf Clover")) copy.RemoveAll(p => { var d = PickupCatalog.GetPickupDef(p); return d != null && d.itemIndex == RoR2Content.Items.Clover.itemIndex; }); return copy; }
        static int Upgrades(int count, int vanilla) => c.Active ? Math.Max(0, UnityEngine.Mathf.RoundToInt(c.Total("Upgrades Per Stage", count))) : count * vanilla;
        static IEnumerable<CodeInstruction> UpgradeIL(IEnumerable<CodeInstruction> input)
        {
            var code = input.ToList(); int lists = 0, scaled = 0;
            for (int i = 0; i < code.Count; i++)
            {
                var x = code[i];
                if (x.opcode == OpCodes.Mul && i > 0 && code[i-1].LoadsConstant(3)) { x.opcode = OpCodes.Call; x.operand = A.Method(typeof(BenthicBloom), nameof(Upgrades)); scaled++; }
                yield return x;
                if (x.opcode == OpCodes.Newobj && x.operand is ConstructorInfo ctor && ctor.DeclaringType == typeof(List<PickupIndex>)) { yield return new CodeInstruction(OpCodes.Call, A.Method(typeof(BenthicBloom), nameof(Filter))); lists++; }
            }
            if (lists != 2 || scaled != 1) throw new InvalidOperationException("Benthic Bloom upgrade anchors changed: " + lists + "/" + scaled);
        }
    }
}
