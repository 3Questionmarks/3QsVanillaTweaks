using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;
using C = Config.ConfigManager;
using S = Tweaks.Items.Common.CommonItemSupport;
using A = ThreeQsVanillaTweaks.ReflectionTools;

namespace Tweaks.Items.Uncommon
{
    internal sealed class UncommonConfig
    {
        internal readonly string Name, ItemName, Section;
        internal readonly ConfigEntry<bool> Enabled;
        internal readonly Dictionary<string, ConfigEntry<float>> Numbers = new Dictionary<string, ConfigEntry<float>>();
        internal Func<string, bool> ShowStat;
        internal Func<string, int, float, float> StatValue;
        internal readonly Dictionary<string, ConfigEntry<bool>> Flags = new Dictionary<string, ConfigEntry<bool>>();
        internal ItemDef Item => ItemCatalog.GetItemDef(ItemCatalog.FindItemIndex(ItemName));
        internal bool Active => C.Get(Enabled);

        internal UncommonConfig(string name, string item, bool enabled, string category = "02 Uncommon")
        {
            Name = name;
            ItemName = item;
            Section = "Items - " + category + " - " + name.Replace("'", "").Replace("\"", "");
            Enabled = C.BindStartupBool(Section, "Enabled", enabled, "Enable this item's configurable changes. Restart to apply.");
            UncommonItemSupport.Configs.Add(this);
            if (enabled)
                S.Ensure();
        }

        internal void N(string key, float value, string vanilla, float min = 0, float max = 100000) => Numbers[key] = S.Num(Section, key, value, vanilla, min, max);
        internal void B(string key, bool value, string description) => Flags[key] = C.BindBool(Section, key, value, description);
        internal float F(string key) => C.Get(Numbers[key]);
        internal bool B(string key) => C.Get(Flags[key]);
        internal float Total(string key, int count) => S.Linear(count, F(key), Numbers.ContainsKey(key + " Per Stack") ? F(key + " Per Stack") : 0);
        internal int Count(CharacterBody b) => Active && b && b.inventory ? b.inventory.GetItemCountEffective(Item) : 0;
        internal void Pair(string key, float first, float extra, string vanilla, float min = 0)
        {
            N(key, first, vanilla, min);
            N(key + " Per Stack", extra, "Additional amount per extra item stack. " + vanilla, min);
        }

        internal string V(string key, string unit = "", string style = "cIsDamage") => "<style=" + style + ">" + F(key).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + unit + "</style>" + (Numbers.ContainsKey(key + " Per Stack") && F(key + " Per Stack") != 0 ? " <style=cStack>(" + (F(key + " Per Stack") > 0 ? "+" : "") + F(key + " Per Stack").ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + unit + " per stack)</style>" : "");
        internal void Text(Func<string> description, Func<string> pickup)
        {
            if (!Enabled.Value)
                return;
            RoR2Application.onLoad += () =>
            {
                var item = Item;
                if (!item)
                    return;
                string nativeDesc = item.descriptionToken, nativePickup = item.pickupToken;
                string oldDesc = Language.GetString(nativeDesc), oldPickup = Language.GetString(nativePickup);
                string desc = "3QS_" + ItemName.ToUpperInvariant() + "_DESC", pick = "3QS_" + ItemName.ToUpperInvariant() + "_PICKUP";
                S.Text(desc, () => Active ? description() : oldDesc);
                S.Text(pick, () => Active ? pickup() : oldPickup);
                S.Text(nativeDesc, () => Active ? description() : oldDesc);
                S.Text(nativePickup, () => Active ? pickup() : oldPickup);
                item.descriptionToken = desc;
                item.pickupToken = pick;
            };
            Tweaks.ModCompatibility.LookingGlassStatRegistry.RegisterItem(() => Item ? Item.itemIndex : ItemIndex.None, (master, n, text) =>
            {
                string result = "";
                foreach (var entry in Numbers)
                {
                    string key = entry.Key;
                    if (ShowStat != null && !ShowStat(key))
                        continue;
                    if (key.EndsWith(" Per Stack") || key.IndexOf("Proc Coefficient", StringComparison.OrdinalIgnoreCase) >= 0)
                        continue;
                    float value = Total(key, n);
                    if (StatValue != null)
                        value = StatValue(key, n, value);
                    if (value == 0 || value == -1)
                        continue;
                    result = Tweaks.ModCompatibility.LookingGlassStatRegistry.AddValue(result, key.Replace(" (%)", ""), value, key.EndsWith(" (%)") ? "%" : "", "#E5C962");
                }

                return result;
            }, () => Active);
        }

        internal void Assign(string type, string method, string member, Func<float> value, string anchor = null)
        {
            if (Enabled.Value)
                UncommonNative.AddAssign(this, type, method, member, value, anchor);
        }

        internal void Field(string type, string method, string field, Func<float> value)
        {
            if (Enabled.Value)
                UncommonNative.AddField(this, type, method, field, value);
        }

        internal void Literal(string type, string method, float native, Func<float> value, string anchor = null, int occurrence = 0, int window = int.MaxValue)
        {
            if (Enabled.Value)
                UncommonNative.AddLiteral(this, type, method, native, value, anchor, occurrence, window);
        }

        internal void NativeCount(string type, string method, float native, string key, float unit = 1, string item = null)
        {
            if (Enabled.Value)
                UncommonNative.AddCount(this, type, method, item ?? ItemName, native, Numbers[key], Numbers[key + " Per Stack"], unit);
        }

        internal void Suppress(string type, string method, Func<bool> condition = null) => UncommonItemSupport.Suppress(this, type, method, condition);
    }

    internal static class UncommonItemSupport
    {
        internal static readonly List<UncommonConfig> Configs = new List<UncommonConfig>();
        internal static readonly Harmony Harmony = new Harmony("3Questionmarks.3QsVanillaTweaks.Uncommon");
        internal static event Action<DamageReport> Hit, Kill;
        internal static event Action<CharacterBody> Stats, Tick;
        internal static event Action<HealthComponent, DamageInfo> BeforeDamage;
        static bool installed;
        internal static void Ensure()
        {
            if (installed)
                return;
            installed = true;
            S.Ensure();
            RoR2Application.onLoad += InstallBlocks;
            S.Hit += r => Hit?.Invoke(r);
            S.Stats += b => Stats?.Invoke(b);
            var deathEvent = typeof(GlobalEventManager).GetEvent("onDeathProcServer", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static) ?? typeof(GlobalEventManager).GetEvent("onCharacterDeathGlobal", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static) ?? typeof(GlobalEventManager).GetEvent("onCharacterDeathServer", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (deathEvent != null)
                deathEvent.AddEventHandler(null, (Action<DamageReport>)OnDeath);
            else
                Harmony.Patch(A.Method(typeof(GlobalEventManager), "OnCharacterDeath"), postfix: new HarmonyMethod(typeof(UncommonItemSupport), nameof(DeathFallback)));
            Harmony.Patch(A.Method(typeof(HealthComponent), "TakeDamageProcess"), prefix: new HarmonyMethod(typeof(UncommonItemSupport), nameof(Damage)));
            Harmony.Patch(A.Method(typeof(CharacterBody), "FixedUpdate"), postfix: new HarmonyMethod(typeof(UncommonItemSupport), nameof(Update)));
        }

        static void OnDeath(DamageReport report)
        {
            if (NetworkServer.active)
                Kill?.Invoke(report);
        }

        static void DeathFallback(DamageReport damageReport) => OnDeath(damageReport);
        static void Damage(HealthComponent __instance, DamageInfo damageInfo)
        {
            if (NetworkServer.active && damageInfo != null && !damageInfo.rejected)
                BeforeDamage?.Invoke(__instance, damageInfo);
        }

        static void Update(CharacterBody __instance)
        {
            if (NetworkServer.active)
                Tick?.Invoke(__instance);
        }

        internal static void Patch(Type type, string method, Type owner, string prefix = null, string postfix = null) => S.Patch(Harmony, type, method, owner, prefix, postfix);
        sealed class Block
        {
            internal UncommonConfig Config;
            internal Func<bool> When;
        }

        static readonly Dictionary<MethodBase, Dictionary<string, Block>> blocks = new Dictionary<MethodBase, Dictionary<string, Block>>();
        static readonly List<Block> ids = new List<Block>();
        internal static void Suppress(UncommonConfig config, string type, string method, Func<bool> when)
        {
            if (!config.Enabled.Value)
                return;
            Ensure();
            var candidates = typeof(CharacterBody).Assembly.GetType(type, true).GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).Where(m => m.Name == method).ToArray();
            var target = candidates.FirstOrDefault(m => PatchProcessor.GetOriginalInstructions(m).Any(i => i.operand is FieldInfo f && f.Name == config.ItemName)) ?? candidates.FirstOrDefault();
            if (target == null)
                throw new MissingMethodException(type, method);
            if (!blocks.TryGetValue(target, out var map))
                blocks[target] = map = new Dictionary<string, Block>();
            map[config.ItemName] = new Block
            {
                Config = config,
                When = when
            };
        }

        // Install once after every item has registered, so transpilers compose.
        static void InstallBlocks()
        {
            foreach (var pair in blocks)
                Harmony.Patch(pair.Key, transpiler: new HarmonyMethod(typeof(UncommonItemSupport), nameof(BlockIL)));
        }

        static int Filter(int count, int id) => ids[id].Config.Active && (ids[id].When == null || ids[id].When()) ? 0 : count;
        static IEnumerable<CodeInstruction> BlockIL(IEnumerable<CodeInstruction> input, MethodBase __originalMethod)
        {
            var code = new List<CodeInstruction>(input);
            var map = blocks[__originalMethod];
            for (int i = 0; i < code.Count; i++)
            {
                if (!(code[i].operand is FieldInfo f) || !map.TryGetValue(f.Name, out var block))
                    continue;
                int end = -1;
                for (int j = i + 1; j < Math.Min(code.Count, i + 12); j++)
                {
                    if (code[j].operand is MethodInfo m && m.Name.StartsWith("GetItemCount"))
                    {
                        end = j;
                        break;
                    }

                    if (code[j].opcode == OpCodes.Stloc || code[j].opcode == OpCodes.Stloc_S || code[j].opcode == OpCodes.Stloc_0 || code[j].opcode == OpCodes.Stloc_1 || code[j].opcode == OpCodes.Stloc_2 || code[j].opcode == OpCodes.Stloc_3)
                        break;
                }

                if (end < 0)
                    continue;
                int id = ids.Count;
                ids.Add(block);
                code.Insert(end + 1, new CodeInstruction(OpCodes.Ldc_I4, id));
                code.Insert(end + 2, new CodeInstruction(OpCodes.Call, A.Method(typeof(UncommonItemSupport), nameof(Filter))));
                i = end + 2;
            }

            return code;
        }

        internal static float Linear(int n, float a, float b) => S.Linear(n, a, b);
        internal static void Add(CharacterBody b, string stat, float value)
        {
            if (value != 0)
                S.Set(b, stat, S.GetStat(b, stat) + value);
        }

        internal static void Scale(CharacterBody b, string stat, float percent)
        {
            if (percent != 0)
                S.Set(b, stat, S.GetStat(b, stat) * (1 + percent / 100));
        }

        internal static void Cooldowns(CharacterBody b, float seconds)
        {
            if (!b || !b.skillLocator)
                return;
            foreach (var skill in b.skillLocator.allSkills)
                if (skill)
                    skill.RunRecharge(seconds);
        }

        internal static bool Roll(float percent, CharacterBody body) => Util.CheckRoll(percent, body ? body.master : null);
        internal static int Count(CharacterBody b, string name) => b && b.inventory ? b.inventory.GetItemCountEffective(ItemCatalog.FindItemIndex(name)) : 0;
        internal static void Blast(CharacterBody source, Vector3 position, float radius, float damage, float proc, ProcType mask)
        {
            var chain = default(ProcChainMask);
            chain.AddProc(mask);
            new BlastAttack
            {
                attacker = source.gameObject,
                teamIndex = source.teamComponent.teamIndex,
                position = position,
                radius = radius,
                baseDamage = damage,
                crit = source.RollCrit(),
                procCoefficient = proc,
                procChainMask = chain,
                damageColorIndex = DamageColorIndex.Item,
                falloffModel = BlastAttack.FalloffModel.SweetSpot,
                attackerFiltering = AttackerFiltering.NeverHitSelf
            }.Fire();
        }

        internal static BuffDef Buff(string name, bool debuff, Func<BuffDef> icon, Func<string> text)
        {
            var buff = ScriptableObject.CreateInstance<BuffDef>();
            buff.name = "3QS_" + name;
            buff.isDebuff = debuff;
            buff.canStack = true;
            buff.buffColor = debuff ? Color.yellow : Color.white;
            R2API.ContentAddition.AddBuffDef(buff);
            S.BuffTexts[buff.name] = new S.BuffText
            {
                Title = name,
                Text = i => text()
            };
            RoR2Application.onLoad += () =>
            {
                var original = icon();
                if (original)
                    buff.iconSprite = original.iconSprite;
            };
            return buff;
        }
    }
}
