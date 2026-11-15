using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using Mono.Cecil;
using Mono.Cecil.Cil;
using RoR2;
using C = Config.ConfigManager;
namespace Tweaks.Directors
{
    internal static class TeleporterBossVariety
    {
        static ConfigEntry<bool> enabled;
        static ILHook hook;
        static readonly Dictionary<SpawnCard,int> appearances=new Dictionary<SpawnCard,int>();
        static readonly HashSet<SpawnCard> thisStage=new HashSet<SpawnCard>();
        static Stage stage;
        static int total;
        internal static void Init()
        {
            enabled=C.BindBool("Directors - Teleporter Bosses","Increase Other Boss Weights",true,"Each different teleporter boss spawned on a stage adds 1 to every other boss's selection weight for future stages. Stage restrictions are preserved. Vanilla: false.");
            Run.onRunStartGlobal += _ => {appearances.Clear();thisStage.Clear();stage=null;total=0;};
            hook=new ILHook(AccessTools.Method(typeof(CombatDirector),"SetNextSpawnAsBoss"), il => {
                var cursor=new ILCursor(il);
                if (!cursor.TryGotoNext(MoveType.Before,i => i.Operand is MethodReference m && m.Name=="AddChoice" && m.Parameters.Count==1)) throw new InvalidOperationException("Teleporter boss choice IL not found.");
                cursor.Emit(OpCodes.Ldarg_0);
                cursor.EmitDelegate<Func<WeightedSelection<DirectorCard>.ChoiceInfo,CombatDirector,WeightedSelection<DirectorCard>.ChoiceInfo>>(Weight);
            });
            new Harmony("3Qs.TeleporterBossVariety").Patch(typeof(CombatDirector).GetMethods().Single(m => m.Name == "Spawn" && m.GetParameters()[0].ParameterType == typeof(SpawnCard)),postfix:new HarmonyMethod(typeof(TeleporterBossVariety),nameof(Spawned)));
        }
        static bool IsTeleporter(CombatDirector director) => TeleporterInteraction.instance && TeleporterInteraction.instance.bossDirector==director;
        static WeightedSelection<DirectorCard>.ChoiceInfo Weight(WeightedSelection<DirectorCard>.ChoiceInfo choice, CombatDirector director)
        {
            if(C.Get(enabled) && IsTeleporter(director)) {
                AdvanceStage();
                appearances.TryGetValue(choice.value.GetSpawnCard(),out int seen);
                choice.weight += total-seen;
            }
            return choice;
        }
        static void Spawned(CombatDirector __instance, SpawnCard spawnCard, bool __result)
        {
            if(!__result || !C.Get(enabled) || !IsTeleporter(__instance) || !spawnCard) return;
            AdvanceStage();
            thisStage.Add(spawnCard);
        }
        static void AdvanceStage()
        {
            if (ReferenceEquals(stage,Stage.instance)) return;
            foreach (var boss in thisStage) {
                appearances.TryGetValue(boss,out int seen);
                appearances[boss]=seen+1;
                total++;
            }
            thisStage.Clear();
            stage=Stage.instance;
        }
    }
}
