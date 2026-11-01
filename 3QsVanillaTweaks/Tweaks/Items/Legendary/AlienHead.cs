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
    internal static class AlienHead
    {
        static R c;
        internal static void Init()
        {
            c = RareItemSupport.Config("Alien Head", "AlienHead");
            c.Pair("Cooldown Reduction (%)", 15, 15, "Multiplicative skill cooldown reduction. Vanilla: 25% per stack.");
            c.B("Change Rarity To Uncommon", true, "Move Alien Head into uncommon loot pools. Restart to apply. Vanilla: false.");
            c.StatValue = (key, n, value) => key == "Cooldown Reduction (%)" ? 100 * (1 - Remaining(n)) : value;
            c.Literal("RoR2.CharacterBody", "RecalculateStats", .75f, () => Factor());
            RoR2Application.onLoad += () =>
            {
                if (c.Active && c.B("Change Rarity To Uncommon"))
                {
                    c.Item._itemTierDef = ItemTierCatalog.GetItemTierDef(ItemTier.Tier2);
                    var pickup = PickupCatalog.GetPickupDef(PickupCatalog.FindPickupIndex(c.Item.itemIndex));
                    var tier = ItemTierCatalog.GetItemTierDef(ItemTier.Tier2);
                    if (pickup != null)
                    {
                        pickup.itemTier = ItemTier.Tier2;
                        pickup.baseColor = ColorCatalog.GetColor(tier.colorIndex);
                        pickup.darkColor = ColorCatalog.GetColor(tier.darkColorIndex);
                        pickup.dropletDisplayPrefab = tier.dropletDisplayPrefab;
                    }

                    RecolourIcon(c.Item);
                    PickupTransmutationManager.RebuildPickupGroups();
                }
            };
            c.Text(() => "Reduce skill cooldowns by " + c.V("Cooldown Reduction (%)", "%", "cIsUtility") + ".", () => "Reduce skill cooldowns.");
        }

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
                Color.RGBToHSV(ColorCatalog.GetColor(ItemTierCatalog.GetItemTierDef(ItemTier.Tier2).colorIndex), out float greenHue, out _, out _);
                for (int i = 0; i < pixels.Length; i++)
                {
                    var pixel = pixels[i];
                    Color.RGBToHSV(pixel, out float hue, out float saturation, out float value);
                    if (pixel.a <= 0 || saturation < .35f || pixel.r < pixel.g * 1.25f || pixel.r < pixel.b * 1.15f || (hue > .12f && hue < .95f))
                        continue;
                    Color colour = Color.HSVToRGB(greenHue, saturation, value);
                    colour.a = pixel.a;
                    pixels[i] = colour;
                }

                texture.SetPixels(pixels);
                texture.Apply();
                texture.name = "3QsAlienHeadUncommonIcon";
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
                ThreeQsVanillaTweaks.Log.Warning("Alien Head icon recolour failed: " + error);
            }
            finally
            {
                RenderTexture.active = previous;
                if (temporary)
                    RenderTexture.ReleaseTemporary(temporary);
            }
        }

        static float Remaining(int n) => n <= 0 ? 1 : Mathf.Clamp01(1 - c.F("Cooldown Reduction (%)") / 100f) * Mathf.Pow(Mathf.Clamp01(1 - c.F("Cooldown Reduction (%) Per Stack") / 100f), n - 1);
        static float Factor()
        {
            int n = Math.Max(1, c.Count(N.Body));
            return Mathf.Pow(Remaining(n), 1f / n);
        }
    }
}
