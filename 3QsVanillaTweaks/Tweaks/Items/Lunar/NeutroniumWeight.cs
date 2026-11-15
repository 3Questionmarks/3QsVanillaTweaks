using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RoR2;
using RoR2.UI.LogBook;
using UnityEngine;
using UnityEngine.Networking;
using R = Tweaks.Items.Uncommon.UncommonConfig;
using U = Tweaks.Items.Uncommon.UncommonItemSupport;
using S = Tweaks.Items.Common.CommonItemSupport;
using A = ThreeQsVanillaTweaks.ReflectionTools;
namespace Tweaks.Items.Lunar
{
    internal static class NeutroniumWeight
    {
        static R c;
        static ItemDef consumed;
        static string vanillaDescription;
        static FieldInfo heldBuff, heldAttachment;
        static MethodInfo heldOnEnable, destroyAttachment;
        static readonly string[] stats = { "Damage", "Attack Speed", "Movement Speed", "Maximum Health", "Maximum Shield", "Regeneration", "Armour", "Critical Chance", "Jump Power", "Acceleration" };
        static readonly string[] properties = { "damage", "attackSpeed", "moveSpeed", "maxHealth", "maxShield", "regen", "armor", "crit", "jumpPower", "acceleration" };
        internal static void Init()
        {
            U.Ensure(); c = new R("Neutronium Weight", "TransferDebuffOnHit", true, "Lunar");
            c.B("Rework", true, "Replace transfers on hit/death with a held stat penalty and a permanent consumed-item bonus when removed. Vanilla: false.");
            c.B("Show Held Debuff Effect", false, "Show the native debuff icon and visual/audio effect continuously while holding the reworked item. Cosmetic only; stat penalties apply regardless. Vanilla: combat only.");
            for (int i = 0; i < stats.Length; i++) c.Pair(stats[i] + " Reduction (%)", i < 3 ? 20 : 0, i < 3 ? 10 : 0, "Held stat reduction. Rework default: 20% +10% for damage/attack speed/movement speed; other stats 0%.");
            c.N("Stats Gained On Removal (%)", 50, "Consumed-item stat bonus as a percentage of the configured reduction: default 10% +5% per stack for the three affected stats.");
            c.N("Maximum Stat Reduction (%)", 99, "Cap on held penalties, to keep maximum health and movement above zero.", 0, 99.99f);
            if (c.Enabled.Value)
            {
                consumed = ScriptableObject.CreateInstance<ItemDef>(); consumed.name = "ThreeQsNeutroniumWeightConsumed";
                consumed.nameToken = "3QS_NEUTRONIUM_CONSUMED_NAME"; consumed.pickupToken = "3QS_NEUTRONIUM_CONSUMED_PICKUP"; consumed.descriptionToken = "3QS_NEUTRONIUM_CONSUMED_DESC";
                consumed.loreToken = string.Empty;
                // Match Delicate Watch (Broken): NoTier with no ItemTierDef.
#pragma warning disable CS0618
                consumed.deprecatedTier = ItemTier.NoTier;
#pragma warning restore CS0618
                consumed.canRemove = false; consumed.isConsumed = true; consumed.hidden = false;
                consumed.tags = new[] { ItemTag.AIBlacklist, ItemTag.WorldUnique };
                R2API.ContentAddition.AddItemDef(consumed);
                U.Harmony.Patch(A.Method(typeof(LogBookController), "BuildPickupEntries"),
                    postfix: new HarmonyMethod(typeof(NeutroniumWeight), nameof(HideReleasedLogbookEntry)));
                c.Suppress("RoR2.GlobalEventManager", "ProcessHitEnemy", () => c.B("Rework"));
                c.Suppress("RoR2.GlobalEventManager", "HandleCharacterDeath", () => c.B("Rework"));
                U.Harmony.Patch(A.Method(typeof(Inventory), "GiveItemPermanent", new[] { typeof(ItemIndex), typeof(int) }), prefix: new HarmonyMethod(typeof(NeutroniumWeight), nameof(RemoveBefore)), postfix: new HarmonyMethod(typeof(NeutroniumWeight), nameof(RemoveAfter)));
                U.Harmony.Patch(A.Method(typeof(CharacterBody), "RecalculateStats"), transpiler: new HarmonyMethod(typeof(NeutroniumWeight), nameof(StatsIL)));
                // The native held behaviour is independent of the on-hit transfer code.
                Type heldType = typeof(CharacterBody).Assembly.GetType("RoR2.Items.TransferDebuffOnHitHeldBehavior", true);
                heldBuff = AccessTools.Field(heldType, "buff");
                heldAttachment = AccessTools.Field(heldType, "attachment");
                heldOnEnable = AccessTools.Method(heldType, "OnEnable");
                destroyAttachment = AccessTools.Method(heldType, "DestroyAttachment");
                U.Harmony.Patch(heldOnEnable, prefix: new HarmonyMethod(typeof(NeutroniumWeight), nameof(HeldEnable)));
                U.Harmony.Patch(AccessTools.Method(heldType, "FixedUpdate"), prefix: new HarmonyMethod(typeof(NeutroniumWeight), nameof(HeldUpdate)));
                // Block every native transfer entry point while the rework is active.
                Type transferType = typeof(CharacterBody).Assembly.GetType("RoR2.Items.TransferDebuffOnHitUtils", true);
                foreach (MethodInfo method in transferType.GetMethods(BindingFlags.Public | BindingFlags.Static))
                    if (method.Name == "FireProjectile" || method.Name == "TransferItem" || method.Name == "TransferItemToNearest")
                        U.Harmony.Patch(method, prefix: new HarmonyMethod(typeof(NeutroniumWeight), nameof(AllowNativeTransfer)));
                U.Stats += Stats;
            }
            RoR2Application.onLoad += () => {
                var original = c.Item; if (!original) return;
                vanillaDescription = Language.GetString(original.descriptionToken);
                if (consumed)
                {
                    foreach (string fieldName in new[] { "pickupIconSprite", "pickupIconSpriteReference", "pickupModelPrefab", "pickupModelReference" }) { var field = A.Field(typeof(ItemDef), fieldName); field?.SetValue(consumed, field.GetValue(original)); }
                    var pickup = PickupCatalog.GetPickupDef(PickupCatalog.FindPickupIndex(consumed.itemIndex)); var source = PickupCatalog.GetPickupDef(PickupCatalog.FindPickupIndex(original.itemIndex));
                    if (pickup != null && source != null) foreach (string fieldName in new[] { "iconSprite", "iconSpriteReference", "iconTexture", "displayPrefab", "displayPrefabReference" }) { var field = A.Field(typeof(PickupDef), fieldName); field?.SetValue(pickup, field.GetValue(source)); }
                    RecolourIcon(consumed);
                }
            };
            S.Text("3QS_NEUTRONIUM_CONSUMED_NAME", () => "Neutronium Weight (Released)");
            S.Text("3QS_NEUTRONIUM_CONSUMED_PICKUP", () => "Permanently increases damage, attack speed and movement speed.");
            S.Text("3QS_NEUTRONIUM_CONSUMED_DESC", () => BonusDescription());
            Tweaks.ModCompatibility.LookingGlassStatRegistry.RegisterItem(() => consumed ? consumed.itemIndex : ItemIndex.None,
                (master, n, text) => ReleasedStats(n), () => Reworked);
            c.ShowStat = key => c.B("Rework") && key.EndsWith(" Reduction (%)", StringComparison.Ordinal);
            c.StatValue = (key, count, value) => c.B("Rework") && key.EndsWith(" Reduction (%)", StringComparison.Ordinal)
                ? Mathf.Min(c.F("Maximum Stat Reduction (%)"),
                    DiminishingPercent(c.F(key), c.F(key + " Per Stack"), count)) : value;
            c.Text(() => c.B("Rework") ? StatDescription(false) + " " + StatDescription(true, true) : vanillaDescription, () => c.B("Rework") ? "<style=cIsHealth>Reduce stats while held.</style> Removing it permanently increases stats." : "Reduces stats in combat. Transfers to enemies on hit.");
        }
        static void HideReleasedLogbookEntry(ref Entry[] __result)
        {
            if (__result != null && Array.Exists(__result, entry => entry?.nameToken == consumed.nameToken))
                __result = Array.FindAll(__result, entry => entry?.nameToken != consumed.nameToken);
        }
        static string BonusDescription()
        {
            string description = StatDescription(true);
            return string.IsNullOrEmpty(description) ? "Grants no stat bonuses with the current settings." : description;
        }
        static string ReleasedStats(int count)
        {
            string text = "";
            foreach (string stat in stats)
            {
                float value = c.Total(stat + " Reduction (%)", count) * c.F("Stats Gained On Removal (%)") / 100;
                if (value != 0)
                    text = Tweaks.ModCompatibility.LookingGlassStatRegistry.AddValue(text, stat + " Bonus", value, "%", "#E5C962", "+");
            }
            return text;
        }
        static string StatDescription(bool bonus, bool removal = false)
        {
            float factor = bonus ? c.F("Stats Gained On Removal (%)") / 100 : 1;
            return string.Join(" ", stats.Where(s => c.F(s + " Reduction (%)") != 0 || c.F(s + " Reduction (%) Per Stack") != 0)
                .GroupBy(s => new { First = c.F(s + " Reduction (%)") * factor, Extra = c.F(s + " Reduction (%) Per Stack") * factor })
                .Select(g => {
                    string[] names = g.Select(s => s.ToLowerInvariant()).ToArray();
                    string list = names.Length < 2 ? names[0] : string.Join(", ", names.Take(names.Length - 1)) + " and " + names.Last();
                    string colour = bonus ? "cIsUtility" : "cIsHealth";
                    return (removal ? "Removing this item permanently increases " : bonus ? "Increase " : "Reduce ") + list + " by <style=" + colour + ">" + g.Key.First.ToString("0.##") + "%</style>" + (g.Key.Extra == 0 ? "" : bonus ? " <style=cStack>(+" + g.Key.Extra.ToString("0.##") + "% per stack)</style>" : " <style=cStack>(diminishing with additional stacks)</style>") + ".";
                }));
        }
        static bool Reworked => c.Active && c.B("Rework");
        static bool AllowNativeTransfer() => !Reworked;
        static bool HeldEnable() => !Reworked || c.B("Show Held Debuff Effect");
        static bool HeldUpdate(object __instance)
        {
            if (!Reworked) return true;
            if (!NetworkServer.active) return false;
            var component = __instance as Component;
            CharacterBody body = component ? component.GetComponent<CharacterBody>() : null;
            if (!body) return false;
            BuffDef buff = heldBuff?.GetValue(null) as BuffDef;
            bool show = c.B("Show Held Debuff Effect") && c.Count(body) > 0;
            if (buff)
            {
                if (show && !body.HasBuff(buff)) body.AddBuff(buff);
                else if (!show)
                    while (body.HasBuff(buff)) body.RemoveBuff(buff);
            }
            var attachment = heldAttachment?.GetValue(__instance) as UnityEngine.Object;
            if (!show && attachment)
                destroyAttachment.Invoke(__instance, null);
            else if (show && !attachment)
                heldOnEnable.Invoke(__instance, null);
            return false;
        }
        static void RemoveBefore(Inventory __instance, ItemIndex itemIndex, out int __state) { __state = NetworkServer.active && Reworked && c.Item && itemIndex == c.Item.itemIndex ? __instance.GetItemCountPermanent(itemIndex) : 0; }
        static void RemoveAfter(Inventory __instance, ItemIndex itemIndex, int __state)
        { if (__state <= 0 || !consumed) return; int removed = __state - __instance.GetItemCountPermanent(itemIndex); if (removed > 0)
            {
                __instance.GiveItemPermanent(consumed, removed);
                var master = __instance.GetComponent<CharacterMaster>();
                if (master)
                    CharacterMasterNotificationQueue.SendTransformNotification(master, c.Item.itemIndex, consumed.itemIndex, CharacterMasterNotificationQueue.TransformationType.Default);
            } }
        static readonly List<UnityEngine.Object> iconAssets = new List<UnityEngine.Object>();
        static void RecolourIcon(ItemDef item)
        {
            var spriteField = A.Field(typeof(ItemDef), "pickupIconSprite");
            var sprite = spriteField?.GetValue(item) as Sprite;
            if (sprite)
            {
                ApplyIcon(item, sprite);
                return;
            }

            var reference = A.Field(typeof(ItemDef), "pickupIconSpriteReference")?.GetValue(item) as UnityEngine.AddressableAssets.AssetReferenceT<Sprite>;
            if (reference == null || !reference.RuntimeKeyIsValid())
                return;
            if (reference.Asset is Sprite loaded)
            {
                ApplyIcon(item, loaded);
                return;
            }

            var handle = reference.OperationHandle.IsValid() ? reference.OperationHandle.Convert<Sprite>() : reference.LoadAssetAsync();
            handle.Completed += operation =>
            {
                if (operation.Status == UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationStatus.Succeeded && operation.Result)
                    ApplyIcon(item, operation.Result);
            };
        }

        static void ApplyIcon(ItemDef item, Sprite source)
        {
            RenderTexture temporary = null;
            RenderTexture previous = RenderTexture.active;
            Texture2D texture = null;
            try
            {
                var rect = source.textureRect;
                temporary = RenderTexture.GetTemporary(source.texture.width, source.texture.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                Graphics.Blit(source.texture, temporary);
                RenderTexture.active = temporary;
                texture = new Texture2D((int)rect.width, (int)rect.height, TextureFormat.RGBA32, false);
                texture.ReadPixels(rect, 0, 0);
                var pixels = texture.GetPixels();
                for (int i = 0; i < pixels.Length; i++)
                {
                    Color pixel = pixels[i];
                    float grey = pixel.grayscale * .7f;
                    pixels[i] = new Color(grey, grey, grey, pixel.a);
                }

                texture.SetPixels(pixels);
                texture.Apply();
                texture.name = "3QsNeutroniumWeightReleasedIcon";
                var replacement = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(.5f, .5f), source.pixelsPerUnit);
                replacement.name = texture.name;
                iconAssets.Add(texture);
                iconAssets.Add(replacement);
                A.Field(typeof(ItemDef), "pickupIconSprite")?.SetValue(item, replacement);
                // Invalid addressable keys make the standard UI use the direct sprite.
                var referenceField = A.Field(typeof(ItemDef), "pickupIconSpriteReference");
                if (referenceField != null)
                    referenceField.SetValue(item, Activator.CreateInstance(referenceField.FieldType, new object[] { "" }));
                var pickup = PickupCatalog.GetPickupDef(PickupCatalog.FindPickupIndex(item.itemIndex));
                if (pickup != null)
                {
                    A.Field(typeof(PickupDef), "iconTexture")?.SetValue(pickup, texture);
                    A.Field(typeof(PickupDef), "iconSprite")?.SetValue(pickup, replacement);
                    var field = A.Field(typeof(PickupDef), "iconSpriteReference");
                    if (field != null)
                        field.SetValue(pickup, Activator.CreateInstance(field.FieldType, new object[] { "" }));
                }
            }
            catch (Exception error)
            {
                if (texture)
                    UnityEngine.Object.Destroy(texture);
                ThreeQsVanillaTweaks.Log.Warning("Released Neutronium Weight icon creation failed: " + error);
            }
            finally
            {
                RenderTexture.active = previous;
                if (temporary)
                    RenderTexture.ReleaseTemporary(temporary);
            }
        }

        static void Apply(CharacterBody body, bool capacities)
        {
            if (!Reworked || !body.inventory) return;
            int held = c.Count(body), released = consumed ? body.inventory.GetItemCountEffective(consumed) : 0;
            for (int i = 0; i < stats.Length; i++)
            {
                if ((i == 3 || i == 4) != capacities) continue;
                string key = stats[i] + " Reduction (%)";
                float loss = Mathf.Min(c.F("Maximum Stat Reduction (%)"), DiminishingPercent(c.F(key), c.F(key + " Per Stack"), held));
                float gain = c.Total(stats[i] + " Reduction (%)", released) * c.F("Stats Gained On Removal (%)") / 100;
                U.Scale(body, properties[i], -loss); U.Scale(body, properties[i], gain);
            }
        }
        static float DiminishingPercent(float first, float extra, int count)
        {
            if (count <= 0) return 0f;
            double a = Math.Max(0d, Math.Min(99.99d, first));
            double b = Math.Max(0d, Math.Min(99.99d, extra));
            double odds = a / (100d - a) + (count - 1d) * b / (100d - b);
            return (float)(100d * odds / (1d + odds));
        }
        static void Stats(CharacterBody body) => Apply(body, false);
        static void Capacities(CharacterBody body) => Apply(body, true);
        static bool NativePenalty(bool native) => Reworked ? false : native;
        static IEnumerable<CodeInstruction> StatsIL(IEnumerable<CodeInstruction> input)
        {
            var code = input.ToList(); int capacity = 0, buff = 0;
            for (int i = 6; i < code.Count; i++)
            {
                if (!(code[i].operand is MethodInfo setter) || setter.Name != "set_maxBarrier" || code[i - 1].opcode != OpCodes.Add || !(code[i - 2].operand is MethodInfo sh) || sh.Name != "get_maxShield" || !(code[i - 4].operand is MethodInfo hp) || hp.Name != "get_maxHealth") continue;
                var load = new CodeInstruction(OpCodes.Ldarg_0); load.labels.AddRange(code[i - 6].labels); code[i - 6].labels.Clear(); load.blocks.AddRange(code[i - 6].blocks); code[i - 6].blocks.Clear();
                code.InsertRange(i - 6, new[] { load, new CodeInstruction(OpCodes.Call, A.Method(typeof(NeutroniumWeight), nameof(Capacities))) }); capacity++; i += 2;
            }
            bool pending = false;
            for (int i = 0; i < code.Count; i++)
            {
                if (code[i].operand is FieldInfo f && f.Name == "TransferDebuffOnHit" && f.DeclaringType.Name == "Buffs") pending = true;
                if (pending && code[i].operand is MethodInfo m && m.Name == "HasBuff") { code.Insert(i + 1, new CodeInstruction(OpCodes.Call, A.Method(typeof(NeutroniumWeight), nameof(NativePenalty)))); buff++; pending = false; i++; }
            }
            if (capacity != 1 || buff != 1) throw new InvalidOperationException("Neutronium stat anchors changed: " + capacity + "/" + buff);
            return code;
        }
    }
}
