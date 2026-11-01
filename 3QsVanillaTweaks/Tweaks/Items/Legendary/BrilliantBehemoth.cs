using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using RoR2.Projectile;
using UnityEngine;
using UnityEngine.Networking;
using C = Config.ConfigManager;
using S = Tweaks.Items.Common.CommonItemSupport;
using U = Tweaks.Items.Uncommon.UncommonItemSupport;
using R = Tweaks.Items.Uncommon.UncommonConfig;
using N = Tweaks.Items.Uncommon.UncommonNative;
using A = ThreeQsVanillaTweaks.ReflectionTools;

namespace Tweaks.Items.Legendary
{
    internal static class BrilliantBehemoth
    {
        static R c;
        internal static void Init()
        {
            c = RareItemSupport.Config("Brilliant Behemoth", "Behemoth");
            c.Pair("Damage (%)", 60, 20, "Explosion damage relative to the triggering hit. Vanilla: 60%, no damage stacking.");
            c.Pair("Radius", 4, 2.5f, "Explosion radius in metres, scaled by the triggering proc coefficient. Vanilla: 4 +2.5 per stack.");
            c.Pair("Trigger Chance (%)", 100, 0, "Chance for an attack to explode. Vanilla: 100%.");
            c.Suppress("RoR2.GlobalEventManager", "OnHitAllProcess", () => c.Count(N.Source) > 0 && !U.Roll(c.Total("Trigger Chance (%)", c.Count(N.Source)), N.Source));
            c.Pair("Proc Coefficient", 0, 0, "Explosion proc coefficient. Vanilla: 0.");
            c.Literal("RoR2.GlobalEventManager", "OnHitAllProcess", .6f, () => c.Total("Damage (%)", c.Count(N.Source)) / 100f, "Behemoth");
            c.Literal("RoR2.GlobalEventManager", "OnHitAllProcess", 1.5f, () => c.F("Radius") - c.F("Radius Per Stack"), "Behemoth");
            c.Literal("RoR2.GlobalEventManager", "OnHitAllProcess", 2.5f, () => c.F("Radius Per Stack"), "Behemoth");
            c.Assign("RoR2.GlobalEventManager", "OnHitAllProcess", "procCoefficient", () => c.Total("Proc Coefficient", c.Count(N.Source)), "Behemoth");
            if (c.Enabled.Value)
                U.Harmony.Patch(A.Method(typeof(GlobalEventManager), "OnHitAllProcess"), transpiler: new HarmonyMethod(typeof(BrilliantBehemoth), nameof(ChainIL)));
            c.Text(() => "All attacks explode in a " + c.V("Radius", "m", "cIsUtility") + " radius for " + c.V("Damage (%)", "%") + " of their damage.", () => "All attacks explode for bonus damage.");
        }

        static IEnumerable<CodeInstruction> ChainIL(IEnumerable<CodeInstruction> input)
        {
            bool area = false, done = false;
            foreach (var i in input)
            {
                if (area && !done && i.operand is FieldInfo f && f.Name == "procChainMask" && i.opcode == System.Reflection.Emit.OpCodes.Stfld)
                {
                    done = true;
                    yield return new CodeInstruction(System.Reflection.Emit.OpCodes.Call, A.Method(typeof(BrilliantBehemoth), nameof(Mask)));
                }

                yield return i;
                if (i.operand is FieldInfo item && item.Name == "Behemoth")
                    area = true;
            }

            if (!done)
                throw new InvalidOperationException("Behemoth proc-chain assignment missing.");
        }

        static ProcChainMask Mask(ProcChainMask mask)
        {
            if (c.Active)
                mask.AddProc(ProcType.Behemoth);
            return mask;
        }
    }
}
