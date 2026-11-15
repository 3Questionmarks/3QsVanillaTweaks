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
    internal static class HuntersHarpoon
    {
        static UncommonConfig c;
        static BuffDef speed, clock;
        static readonly List<UnityEngine.Object> iconAssets = new List<UnityEngine.Object>();
        sealed class State
        {
            internal float Remaining;
        }

        static readonly ConditionalWeakTable<CharacterBody, State> states = new ConditionalWeakTable<CharacterBody, State>();
        internal static void Init()
        {
            c = new UncommonConfig("Hunter's Harpoon", "MoveSpeedOnKill", true);
            c.B("Rework", true, "Critical hits reduce skill cooldowns instead of granting speed on kill.");
            c.B("Change Rarity To Legendary", true, "Move Hunter's Harpoon into Legendary loot pools. Restart to apply. Vanilla: false.");
            c.Pair("Critical Chance (%)", 10, 0, "Rework critical chance. Vanilla: 0%.");
            c.Pair("Cooldown Reduction", 1f, 1f, "Seconds removed from skill cooldowns on a critical hit, multiplied by the triggering attack's proc coefficient. Vanilla: 0.");
            c.Pair("Movement Speed (%)", 50, 0, "Movement speed after a kill in normal mode. Vanilla: 125%.");
            c.B("Fade Speed Over Duration", false, "Speed falls as the remaining duration decreases. Vanilla: true.");
            c.Pair("Duration", 1, 1, "Seconds gained per kill in normal mode. Vanilla: 1 +0.5 per stack.");
            c.B("Duration Stacks", true, "Kills add duration to the remaining speed buff. Vanilla: true.");
            c.Pair("Maximum Duration", 25, 5, "Maximum seconds of speed. Vanilla: no practical cap.");
            c.Pair("Trigger Chance (%)", 100, 0, "Chance on a qualifying critical hit or kill. Vanilla: 100%.");
            c.Suppress("RoR2.GlobalEventManager", "OnCharacterDeath");
            speed = U.Buff("Harpoon Haste", false, () => DLC1Content.Buffs.KillMoveSpeed, () => "Increased <style=cIsUtility>movement speed</style>.");
            clock = U.Buff("Harpoon Duration", false, () => DLC1Content.Buffs.KillMoveSpeed, () => "Remaining harpoon haste duration.");
            clock.isHidden = true;
            U.Ensure();
            U.Hit += Hit;
            U.Kill += Kill;
            U.Tick += Tick;
            U.Stats += Stats;
            RoR2Application.onLoad += ChangeRarity;
            c.ShowStat = key => c.B("Rework") ? key.StartsWith("Critical Chance") || key.StartsWith("Cooldown Reduction") || key.StartsWith("Trigger Chance") : !key.StartsWith("Critical Chance") && !key.StartsWith("Cooldown Reduction");
            c.Text(() => c.B("Rework") ? "Gain " + c.V("Critical Chance (%)", "%") + " <style=cIsDamage>critical chance</style>. Critical hits reduce <style=cIsUtility>skill cooldowns</style> by " + c.V("Cooldown Reduction", "s", "cIsUtility") + "." : "Kills increase <style=cIsUtility>movement speed</style> by " + c.V("Movement Speed (%)", "%", "cIsUtility") + " for " + c.V("Duration", "s", "cIsUtility") + "." + (c.B("Duration Stacks") ? " Additional kills extend the duration, up to " + c.V("Maximum Duration", "s", "cIsUtility") + "." : ""), () => c.B("Rework") ? "Critical strikes reduce <style=cIsUtility>skill cooldowns</style>. Gain critical chance." : "Kills grant a burst of <style=cIsUtility>movement speed</style>.");
        }

        static void ChangeRarity()
        {
            if (!c.Active || !c.B("Change Rarity To Legendary") || !c.Item)
                return;
            ItemTierDef tier = ItemTierCatalog.GetItemTierDef(ItemTier.Tier3);
            c.Item._itemTierDef = tier;
            PickupDef pickup = PickupCatalog.GetPickupDef(PickupCatalog.FindPickupIndex(c.Item.itemIndex));
            if (pickup != null)
            {
                pickup.itemTier = ItemTier.Tier3;
                pickup.baseColor = ColorCatalog.GetColor(tier.colorIndex);
                pickup.darkColor = ColorCatalog.GetColor(tier.darkColorIndex);
                pickup.dropletDisplayPrefab = tier.dropletDisplayPrefab;
            }
            RecolourIcon(c.Item, tier);
            PickupTransmutationManager.RebuildPickupGroups();
        }

        static void RecolourIcon(ItemDef item, ItemTierDef tier)
        {
            var spriteField = A.Field(typeof(ItemDef), "pickupIconSprite");
            Sprite sprite = spriteField?.GetValue(item) as Sprite;
            if (sprite)
            {
                ApplyIcon(item, tier, sprite);
                return;
            }
            var reference = A.Field(typeof(ItemDef), "pickupIconSpriteReference")?.GetValue(item) as
                UnityEngine.AddressableAssets.AssetReferenceT<Sprite>;
            if (reference == null || !reference.RuntimeKeyIsValid()) return;
            if (reference.Asset is Sprite loaded)
            {
                ApplyIcon(item, tier, loaded);
                return;
            }
            var handle = reference.OperationHandle.IsValid() ? reference.OperationHandle.Convert<Sprite>() : reference.LoadAssetAsync();
            handle.Completed += operation =>
            {
                if (operation.Status == UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationStatus.Succeeded && operation.Result)
                    ApplyIcon(item, tier, operation.Result);
            };
        }

        static void ApplyIcon(ItemDef item, ItemTierDef tier, Sprite source)
        {
            var spriteField = A.Field(typeof(ItemDef), "pickupIconSprite");
            RenderTexture temporary = null;
            RenderTexture previous = RenderTexture.active;
            Texture2D texture = null;
            try
            {
                Rect rect = source.textureRect;
                temporary = RenderTexture.GetTemporary(source.texture.width, source.texture.height, 0,
                    RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                Graphics.Blit(source.texture, temporary);
                RenderTexture.active = temporary;
                texture = new Texture2D((int)rect.width, (int)rect.height, TextureFormat.RGBA32, false);
                texture.ReadPixels(rect, 0, 0);
                Color[] pixels = texture.GetPixels();
                Color.RGBToHSV(ColorCatalog.GetColor(tier.colorIndex), out float redHue, out _, out _);
                for (int i = 0; i < pixels.Length; i++)
                {
                    Color pixel = pixels[i];
                    Color.RGBToHSV(pixel, out float hue, out float saturation, out float value);
                    bool uncommonGreen = saturation >= .35f && hue >= .16f && hue <= .48f &&
                        pixel.g > pixel.r * 1.08f && pixel.g > pixel.b * .95f;
                    if (pixel.a <= 0f || !uncommonGreen) continue;
                    Color colour = Color.HSVToRGB(redHue, saturation, value);
                    colour.a = pixel.a;
                    pixels[i] = colour;
                }
                texture.SetPixels(pixels);
                texture.Apply();
                texture.name = "3QsHuntersHarpoonLegendaryIcon";
                Sprite replacement = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
                    new Vector2(.5f, .5f), source.pixelsPerUnit);
                replacement.name = texture.name;
                iconAssets.Add(texture);
                iconAssets.Add(replacement);
                spriteField?.SetValue(item, replacement);
                var referenceField = A.Field(typeof(ItemDef), "pickupIconSpriteReference");
                if (referenceField != null)
                    referenceField.SetValue(item, Activator.CreateInstance(referenceField.FieldType, new object[] { "" }));
                PickupDef pickup = PickupCatalog.GetPickupDef(PickupCatalog.FindPickupIndex(item.itemIndex));
                if (pickup != null)
                {
                    A.Field(typeof(PickupDef), "iconTexture")?.SetValue(pickup, texture);
                    A.Field(typeof(PickupDef), "iconSprite")?.SetValue(pickup, replacement);
                    var pickupReference = A.Field(typeof(PickupDef), "iconSpriteReference");
                    if (pickupReference != null)
                        pickupReference.SetValue(pickup, Activator.CreateInstance(pickupReference.FieldType, new object[] { "" }));
                }
            }
            catch (Exception error)
            {
                if (texture) UnityEngine.Object.Destroy(texture);
                ThreeQsVanillaTweaks.Log.Warning("Hunter's Harpoon icon recolour failed: " + error);
            }
            finally
            {
                RenderTexture.active = previous;
                if (temporary) RenderTexture.ReleaseTemporary(temporary);
            }
        }

        static void Hit(DamageReport r)
        {
            var b = r.attackerBody;
            int n = c.Count(b);
            if (n > 0 && c.B("Rework") && r.damageInfo.crit && r.damageInfo.procCoefficient > 0 && U.Roll(c.Total("Trigger Chance (%)", n), b))
                U.Cooldowns(b, c.Total("Cooldown Reduction", n) * r.damageInfo.procCoefficient);
        }

        static void Kill(DamageReport r)
        {
            var b = r.attackerBody;
            int n = c.Count(b);
            if (n == 0 || c.B("Rework") || !U.Roll(c.Total("Trigger Chance (%)", n), b))
                return;
            var s = states.GetOrCreateValue(b);
            s.Remaining = Mathf.Min(c.Total("Maximum Duration", n), (c.B("Duration Stacks") ? s.Remaining : 0) + c.Total("Duration", n));
            b.SetBuffCount(speed.buffIndex, s.Remaining > 0 ? 1 : 0);
            b.SetBuffCount(clock.buffIndex, c.B("Fade Speed Over Duration") ? Mathf.CeilToInt(s.Remaining * 10) : 0);
        }

        static void Tick(CharacterBody b)
        {
            if (!states.TryGetValue(b, out var s) || s.Remaining <= 0)
                return;
            s.Remaining = Mathf.Max(0, s.Remaining - Time.fixedDeltaTime);
            if (c.Count(b) == 0 || c.B("Rework"))
                s.Remaining = 0;
            if (s.Remaining == 0)
                b.SetBuffCount(speed.buffIndex, 0);
            b.SetBuffCount(clock.buffIndex, c.B("Fade Speed Over Duration") ? Mathf.CeilToInt(s.Remaining * 10) : 0);
            if (c.B("Fade Speed Over Duration"))
                b.MarkAllStatsDirty();
        }

        static void Stats(CharacterBody b)
        {
            int n = c.Count(b);
            if (n == 0)
                return;
            if (c.B("Rework"))
            {
                U.Add(b, "crit", c.Total("Critical Chance (%)", n));
                return;
            }

            if (!b.HasBuff(speed))
                return;
            float factor = 1;
            if (c.B("Fade Speed Over Duration"))
                factor = Mathf.Clamp01((states.TryGetValue(b, out var s) ? s.Remaining : b.GetBuffCount(clock) / 10f) / Mathf.Max(.01f, c.Total("Duration", n)));
            U.Scale(b, "moveSpeed", c.Total("Movement Speed (%)", n) * factor);
        }
    }
}
