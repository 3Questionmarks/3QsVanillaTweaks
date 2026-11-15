using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RoR2.Projectile;
using BepInEx.Configuration;
using EntityStates;
using RoR2;
using RoR2.Skills;
using UnityEngine;
using UnityEngine.Networking;
using C = Config.ConfigManager;

namespace Tweaks.Others.RemoteOperation
{
    // Player-controlled FlameDroneBodyRemoteOp only. The vanilla AI body is untouched.
    internal static class RemoteIncineratorDrone
    {
        private const string Root = "Remote Operation - Incinerator Drone";
        private const string PrimaryMachine = "3QSIncineratorPrimary";
        private const string SecondaryMachine = "3QSIncineratorSecondary";
        private const string UtilityMachine = "3QSIncineratorUtility";
        private const string SpecialMachine = "3QSIncineratorSpecial";
        internal static readonly Type[] States = { typeof(Flamethrower), typeof(FuelGlaze), typeof(FlameJet), typeof(Inferno), typeof(Detonate), typeof(OverheatedLockout) };
        private static ConfigEntry<bool> enabled, useArtificerFlames;
        private static ConfigEntry<int> fuelStocks, fuelRestock, fireTicks, glazeCount;
        private static ConfigEntry<float> windup, fuelCycle, fuelDelay, fireDamage, fireBurn, fireBurnDuration, fireProc, fireRange;
        private static ConfigEntry<float> glazeDamage, glazeProc, glazeWeak, glazeOil, glazeCooldown, glazeRange, glazeSpread;
        private static ConfigEntry<float> jetDamage, jetRange, jetAngle, jetForce, jetSelfForce, jetProc, jetCooldown, jetHotCooldown, jetHotMultiplier;
        private static ConfigEntry<float> infernoCharge, infernoDamage, infernoRadius, infernoProc, infernoOverheat, infernoCooldown;
        private static SkillDef primary, secondary, utility, special, hotUtility, detonatePrimary, lockedPrimary, lockedSpecial;

        internal static void Bind()
        {
            enabled = C.BindStartupBool(Root, "Changes Enabled", true, "Replace the player-controlled Incinerator Drone's skills. AI drones are unchanged. Restart to apply.");
            string p = Root + " - Flamethrower";
            fuelStocks = RemoteDroneSkills.Number(p, "Maximum Fuel", 40, "Maximum fuel charges.");
            fuelRestock = RemoteDroneSkills.Number(p, "Fuel Restored", 40, "Fuel charges restored after the recharge delay.");
            useArtificerFlames = C.BindStartupBool(p, "Use Artificer Flamethrower Visuals", false, "Use Artificer's timed flamethrower effect instead of the drone's continuous lightweight flame stream. Does not change damage. The artificer one is harcoded to last ~6s so it will cutout every now and then temporarilty.");
            windup = RemoteDroneSkills.Number(p, "Wind-up (s)", .6f, "Time before the flamethrower starts.");
            fuelCycle = RemoteDroneSkills.Number(p, "Fuel Cycle (s)", .5f, "Duration of each fuel charge's firing cycle.");
            fuelDelay = RemoteDroneSkills.Number(p, "Fuel Recharge Delay (s)", 8f, "Time after the last firing cycle before fuel replenishes.");
            fireTicks = RemoteDroneSkills.Number(p, "Base Hits per Fuel", 5, "Hits per fuel charge at 100% attack speed. Attack speed increases hit frequency.");
            fireDamage = RemoteDroneSkills.Number(p, "Damage per Hit (%)", 25f, "Damage of each flame hit.");
            fireBurn = RemoteDroneSkills.Number(p, "Ignite Chance (%)", 50f, "Chance for a flame hit to Ignite.");
            fireBurnDuration = RemoteDroneSkills.Number(Root, "Ignite Duration (s)", 5f, "Duration of Burn applied by Flamethrower, Flame Jet and Inferno.");
            fireProc = RemoteDroneSkills.Number(p, "Proc Coefficient", .2f, "Proc coefficient per flame hit.");
            fireRange = RemoteDroneSkills.Number(p, "Range (m)", 30f, "Maximum flame range. Vanilla: 12m.");
            string s = Root + " - Fuel Glaze";
            glazeCount = RemoteDroneSkills.Number(s, "Projectiles", 7, "Number of fuel globs fired.");
            glazeDamage = RemoteDroneSkills.Number(s, "Damage per Glob (%)", 150f, "Damage dealt by each fuel glob.");
            glazeProc = RemoteDroneSkills.Number(s, "Proc Coefficient", 1f, "Proc coefficient per glob.");
            glazeWeak = RemoteDroneSkills.Number(s, "Weak Duration (s)", 5f, "Duration of Weak on hit.");
            glazeOil = RemoteDroneSkills.Number(s, "Oiled Duration (s)", 5f, "Duration of Oiled on hit.");
            glazeCooldown = RemoteDroneSkills.Number(s, "Cooldown (s)", 10f, "Fuel Glaze cooldown.");
            glazeRange = RemoteDroneSkills.Number(s, "Range (m)", 35f, "Maximum glob travel distance.");
            glazeSpread = RemoteDroneSkills.Number(s, "Spread (degrees)", 24f, "Total cone width for the projectile spread.");
            s = Root + " - Flame Jet";
            jetDamage = RemoteDroneSkills.Number(s, "Damage (%)", 400f, "Damage of the fire cone.");
            jetRange = RemoteDroneSkills.Number(s, "Range (m)", 12f, "Fire cone range.");
            jetAngle = RemoteDroneSkills.Number(s, "Cone Angle (degrees)", 75f, "Full cone angle.");
            jetForce = RemoteDroneSkills.Number(s, "Enemy Push Force", 35f, "Knockback applied to enemies.");
            jetSelfForce = RemoteDroneSkills.Number(s, "Self Propulsion", 2400f, "Recoil impulse applied to the drone.");
            jetProc = RemoteDroneSkills.Number(s, "Proc Coefficient", 1f, "Proc coefficient per target hit.");
            jetCooldown = RemoteDroneSkills.Number(s, "Cooldown (s)", 7f, "Recharge time per charge.");
            jetHotCooldown = RemoteDroneSkills.Number(s, "Overheated Cooldown (s)", 5f, "Recharge of the mobility-only Flame Jet.");
            jetHotMultiplier = RemoteDroneSkills.Number(s, "Overheated Propulsion (%)", 60f, "Recoil strength of the mobility-only Flame Jet as a persent of the base one.");
            s = Root + " - Inferno";
            infernoCharge = RemoteDroneSkills.Number(s, "Charge Time (s)", 2f, "Time to prepare Inferno, divided by attack speed.");
            infernoDamage = RemoteDroneSkills.Number(s, "Explosion Damage (%)", 3000f, "Damage dealt to each enemy within Inferno's radius.");
            infernoRadius = RemoteDroneSkills.Number(s, "Explosion Radius (m)", 40f, "Inferno radius.");
            infernoProc = RemoteDroneSkills.Number(s, "Proc Coefficient", 1f, "Proc coefficient per enemy hit.");
            infernoOverheat = RemoteDroneSkills.Number(s, "Overheat Duration (s)", 5f, "Duration that the flamethrower is disabled after detonation.");
            infernoCooldown = RemoteDroneSkills.Number(s, "Cooldown After Overheat (s)", 30f, "Inferno cooldown begins when the drone finishes overheating.");
        }

        internal static void Register()
        {
            if (enabled == null || !enabled.Value) return;
            primary = RemoteDroneSkills.MakeSkill("IncineratorFlamethrower", "Flamethrower", "Ignite. Spray flames for 5x25% damage per fuel charge.", typeof(Flamethrower), PrimaryMachine, 0f, null);
            secondary = RemoteDroneSkills.MakeSkill("IncineratorGlaze", "Fuel Glaze", "Fire 7 fuel globs for 150% damage each, inflicting Weak and Oiled.", typeof(FuelGlaze), SecondaryMachine, 10f, null);
            utility = RemoteDroneSkills.MakeSkill("IncineratorJet", "Flame Jet", "Ignite. Blast enemies for 400% damage and launch yourself backwards.", typeof(FlameJet), UtilityMachine, 7f, null);
            special = RemoteDroneSkills.MakeSkill("IncineratorInferno", "Inferno", "Charge a 3000% damage explosion. Press Primary to detonate, then overheat.", typeof(Inferno), SpecialMachine, 30f, null);
            hotUtility = UnityEngine.Object.Instantiate(utility);
            ((ScriptableObject)hotUtility).name = hotUtility.skillName = "3QSRemoteIncineratorJetOverheated";
            hotUtility.skillNameToken = "3QS_INCINERATOR_HOT_JET_NAME";
            hotUtility.skillDescriptionToken = "3QS_INCINERATOR_HOT_JET_DESCRIPTION";
            hotUtility.fullRestockOnAssign = false;
            R2API.LanguageAPI.Add(hotUtility.skillNameToken, "Overheated Flame Jet");
            R2API.LanguageAPI.Add(hotUtility.skillDescriptionToken, "Release a <style=cIsUtility>weaker blast</style> to propel yourself backwards while overheated. The <style=cIsUtility>last charge provides extra propulsion</style>.");
            RemoteDroneSkills.Definitions.Add(hotUtility);
            detonatePrimary = UnityEngine.Object.Instantiate(primary);
            ((ScriptableObject)detonatePrimary).name = detonatePrimary.skillName = "3QSRemoteIncineratorDetonate";
            detonatePrimary.skillNameToken = "3QS_INCINERATOR_DETONATE_NAME";
            detonatePrimary.skillDescriptionToken = "3QS_INCINERATOR_DETONATE_DESCRIPTION";
            detonatePrimary.activationState = new SerializableEntityStateType(typeof(Detonate));
            detonatePrimary.fullRestockOnAssign = false;
            detonatePrimary.baseMaxStock = 1;
            detonatePrimary.baseRechargeInterval = 0f;
            detonatePrimary.stockToConsume = 1;
            detonatePrimary.mustKeyPress = true;
            R2API.LanguageAPI.Add(detonatePrimary.skillNameToken, "Detonate Inferno");
            R2API.LanguageAPI.Add(detonatePrimary.skillDescriptionToken, "Detonate Inferno, dealing massive damage and Igniting all nearby enemies.");
            RemoteDroneSkills.Definitions.Add(detonatePrimary);
            // Railgunner BaseCharged uses contextual skill overrides to change the actual
            // displayed skill icon. Use the same approach for the overheat lockout.
            lockedPrimary = CreateLockedSkill(primary, "3QSIncineratorPrimaryOverheated", "Overheated Flamethrower");
            lockedSpecial = CreateLockedSkill(special, "3QSIncineratorInfernoOverheated", "Overheated Inferno");
            Refresh();
        }

        // Find the actual Railgunner offline/reboot icon when its DLC assets are loaded.
        // Do not treat Railgunner's normal primary icon as its disconnected icon.
        private static Sprite FindRailgunnerDisconnectedIcon()
        {
            foreach (Sprite icon in Resources.FindObjectsOfTypeAll<Sprite>())
            {
                if (!icon) continue;
                string name = icon.name.ToLowerInvariant();
                if ((name.Contains("railgunner") || name.Contains("railgun")) &&
                    (name.Contains("offline") || name.Contains("reboot") || name.Contains("disabled")))
                    return icon;
            }
            return null;
        }

        private static SkillDef CreateLockedSkill(SkillDef source, string name, string title)
        {
            SkillDef def = UnityEngine.Object.Instantiate(source);
            ((ScriptableObject)def).name = def.skillName = name;
            def.skillNameToken = name.ToUpperInvariant() + "_NAME";
            def.skillDescriptionToken = name.ToUpperInvariant() + "_DESCRIPTION";
            def.activationState = new SerializableEntityStateType(typeof(OverheatedLockout));
            def.fullRestockOnAssign = false;
            def.mustKeyPress = true;
            def.baseMaxStock = 1;
            // Keep the lockout at full visible stock, but permanently below its
            // required stock. This avoids the invalid/negative recharge timer.
            def.requiredStock = 2;
            def.rechargeStock = 0;
            def.baseRechargeInterval = 999999f;
            def.stockToConsume = 0;
            R2API.LanguageAPI.Add(def.skillNameToken, title);
            R2API.LanguageAPI.Add(def.skillDescriptionToken, "Unavailable while the Incinerator Drone is overheated.");
            RemoteDroneSkills.Definitions.Add(def);
            return def;
        }

        internal static void Refresh()
        {
            if (!primary) return;
            primary.baseMaxStock = Mathf.Max(1, C.Get(fuelStocks));
            primary.baseRechargeInterval = Mathf.Max(.05f, C.Get(fuelDelay)); // HUD counts down to full reservoir replenishment.
            primary.rechargeStock = 1; // Native recharge enables the HUD overlay; the controller still completes the full tank refill.
            primary.mustKeyPress = false;
            primary.stockToConsume = 0; // The Flamethrower state pays one stock at the start of each firing cycle.
            primary.requiredStock = 1;
            primary.beginSkillCooldownOnSkillEnd = true; // Freeze fuel recharge HUD while firing.
            utility.mustKeyPress = false;
            hotUtility.mustKeyPress = false;
            detonatePrimary.mustKeyPress = true;
            secondary.baseRechargeInterval = Mathf.Max(.05f, C.Get(glazeCooldown));
            secondary.stockToConsume = 1;
            secondary.requiredStock = 1;
            utility.baseRechargeInterval = Mathf.Max(.05f, C.Get(jetCooldown));
            utility.baseMaxStock = 2;
            hotUtility.baseMaxStock = 2;
            hotUtility.baseRechargeInterval = Mathf.Max(.05f, C.Get(jetHotCooldown));
            special.baseRechargeInterval = Mathf.Max(.05f, C.Get(infernoCooldown));
            special.beginSkillCooldownOnSkillEnd = false; // Visible recharge progresses during Inferno charging.
            special.stockToConsume = 1;
            special.requiredStock = 1;
            // Show spare Special stocks during overheat without allowing activation.
            lockedSpecial.baseMaxStock = Mathf.Max(1, special.baseMaxStock);
            lockedSpecial.requiredStock = 999999; // Must stay unavailable even with many Lysate Cell stacks.
            Tweaks.ModCompatibility.LookingGlassSkillStats.Register(primary,
                () => $"\nFlame Range: {C.Get(fireRange):0.##}m\nFuel Cycle: {C.Get(fuelCycle):0.##}s\nFull Reload: {C.Get(fuelDelay):0.##}s", () => C.Get(fireProc));
            Tweaks.ModCompatibility.LookingGlassSkillStats.Register(secondary,
                () => $"\nGlobs: {C.Get(glazeCount)}\nWeak Duration: {C.Get(glazeWeak):0.##}s\nOil Duration: {C.Get(glazeOil):0.##}s", () => C.Get(glazeProc));
            Tweaks.ModCompatibility.LookingGlassSkillStats.Register(utility,
                () => $"\nBlast Range: {C.Get(jetRange):0.##}m\nKnockback Force: {C.Get(jetForce):0.##}", () => C.Get(jetProc));
            Tweaks.ModCompatibility.LookingGlassSkillStats.Register(hotUtility,
                () => $"\nBlast Range: {C.Get(jetRange):0.##}m\nSelf Propulsion: {C.Get(jetSelfForce) * C.Get(jetHotMultiplier) / 100f:0.##}\nLast Stock Propulsion: +25%", () => 0f);
            Tweaks.ModCompatibility.LookingGlassSkillStats.Register(special,
                () => $"\nExplosion Radius: {C.Get(infernoRadius):0.##}m\nCharge Time: {C.Get(infernoCharge):0.##}s\nOverheat: {C.Get(infernoOverheat):0.##}s", () => C.Get(infernoProc));
            Tweaks.Survivors.SurvivorSettings.SetSkillText(hotUtility, () =>
                "Release a <style=cIsUtility>weaker blast</style> to propel yourself backwards while overheated. The <style=cIsUtility>last charge provides extra propulsion</style>.");
            Tweaks.Survivors.SurvivorSettings.SetSkillText(primary, () =>
                $"<style=cIsDamage>Ignite.</style> Spray flames for <style=cIsDamage>{C.Get(fireTicks)}x{C.Get(fireDamage):0.##}% damage</style> per fuel charge. Attack speed increases hits per charge.");
            Tweaks.Survivors.SurvivorSettings.SetSkillText(secondary, () =>
                $"Launch <style=cIsDamage>{C.Get(glazeCount)} globs of volatile fuel</style> for <style=cIsDamage>{C.Get(glazeDamage):0.##}% damage</style> each, inflicting <style=cIsUtility>Weak</style> and <style=cIsUtility>Oiled</style>. Fuel left on the ground can be <style=cIsDamage>Ignited</style>, damaging enemies standing within it.");
            Tweaks.Survivors.SurvivorSettings.SetSkillText(utility, () =>
                $"<style=cIsDamage>Ignite.</style> Unleash a fiery blast for <style=cIsDamage>{C.Get(jetDamage):0.##}% damage</style>, knocking enemies back and <style=cIsUtility>propelling yourself backwards</style>. The <style=cIsUtility>last charge provides extra propulsion</style>.");
            Tweaks.Survivors.SurvivorSettings.SetSkillText(special, () =>
                $"Charge a devastating explosion, dealing <style=cIsDamage>{C.Get(infernoDamage):0.##}% damage</style> and <style=cIsDamage>Igniting</style> enemies within <style=cIsUtility>{C.Get(infernoRadius):0.##}m</style>. On detonation, the drone <style=cIsUtility>Overheats</style> for <style=cIsUtility>{C.Get(infernoOverheat):0.##} seconds</style>, temporarily disabling its fire abilities. Once Overheat ends, Flamethrower is fully refuelled.");
        }

        // Recolour a vanilla icon without modifying the shared Sprite used by other survivors.
        // Copy via a RenderTexture: most game atlas textures are not CPU-readable.
        private static Sprite TintVanillaIcon(Sprite source, bool inferno, bool locked = false, bool blue = false)
        {
            if (!source) return null;
            RenderTexture render = null;
            RenderTexture previous = RenderTexture.active;
            try
            {
                Texture2D sourceTexture = source.texture;
                if (!sourceTexture) return source;
                Rect rect = source.textureRect;
                int width = Mathf.Max(1, Mathf.RoundToInt(rect.width));
                int height = Mathf.Max(1, Mathf.RoundToInt(rect.height));
                render = RenderTexture.GetTemporary(sourceTexture.width, sourceTexture.height, 0,
                    RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
                Graphics.Blit(sourceTexture, render);
                RenderTexture.active = render;
                var copy = new Texture2D(width, height, TextureFormat.RGBA32, false);
                copy.ReadPixels(new Rect(rect.x, rect.y, width, height), 0, 0);
                copy.Apply();
                Color32[] pixels = copy.GetPixels32();
                for (int i = 0; i < pixels.Length; ++i)
                {
                    Color32 p = pixels[i];
                    if (p.a == 0) continue;
                    float brightness = Mathf.Clamp01((p.r * .299f + p.g * .587f + p.b * .114f) / 255f);
                    // Preserve the linework and transparency while replacing the palette.
                    float glow = Mathf.Pow(brightness, .8f);
                    Color colour = blue
                        ? Color.Lerp(new Color(.015f, .11f, .29f), new Color(.35f, .87f, 1f), glow)
                        : locked
                        ? Color.Lerp(new Color(.055f, .055f, .06f), new Color(.42f, .42f, .44f), glow)
                        : inferno
                            ? Color.Lerp(new Color(.42f, .025f, .01f), new Color(1f, .67f, .18f), glow)
                            : Color.Lerp(new Color(.4f, .095f, .015f), new Color(1f, .74f, .24f), glow);
                    pixels[i] = new Color32((byte)(colour.r * 255f), (byte)(colour.g * 255f),
                        (byte)(colour.b * 255f), p.a);
                }
                copy.SetPixels32(pixels);
                copy.Apply();
                copy.wrapMode = TextureWrapMode.Clamp;
                copy.filterMode = FilterMode.Bilinear;
                Vector2 pivot = new Vector2(source.pivot.x / source.rect.width,
                    source.pivot.y / source.rect.height);
                Sprite tinted = Sprite.Create(copy, new Rect(0, 0, width, height), pivot, source.pixelsPerUnit);
                tinted.name = source.name + (blue ? "_OverheatedBlue" : locked ? "_Overheated" : inferno ? "_Inferno" : "_Orange");
                return tinted;
            }
            catch (Exception error)
            {
                Debug.LogWarning("[3QsVanillaTweaks] Incinerator icon tint failed: " + error.Message);
                return source;
            }
            finally
            {
                RenderTexture.active = previous;
                if (render != null) RenderTexture.ReleaseTemporary(render);
            }
        }

        internal static void Install(GameObject prefab)
        {
            if (!prefab || enabled == null || !enabled.Value) return;
            Sprite fallback = RemoteDroneSkills.VanillaIcon("FlameDroneBody", "Primary", null);
            Sprite railgunnerOffline = FindRailgunnerDisconnectedIcon();
            if (!railgunnerOffline)
                ThreeQsVanillaTweaks.Log.Warning("Incinerator: Railgunner disconnected skill sprite not found in loaded assets; using fallback locked icons.");
            if (primary) {
                primary.icon = fallback;
                lockedPrimary.icon = TintVanillaIcon(railgunnerOffline ? railgunnerOffline : fallback, false, true);
                RemoteDroneSkills.AddMachine(prefab, PrimaryMachine);
                RemoteDroneSkills.SetSkill(prefab, "Primary", primary);
            }
            if (secondary) {
                secondary.icon = RemoteDroneSkills.VanillaIcon("ChefBody", "Special", fallback);
                RemoteDroneSkills.AddMachine(prefab, SecondaryMachine);
                RemoteDroneSkills.SetSkill(prefab, "Secondary", secondary);
            }
            if (utility) {
                // REX's Utility artwork, recoloured to a warm orange.
                utility.icon = TintVanillaIcon(RemoteDroneSkills.VanillaIcon("TreebotBody", "Utility", fallback), false);
                hotUtility.icon = TintVanillaIcon(RemoteDroneSkills.VanillaIcon("TreebotBody", "Utility", fallback), false, false, true);
                RemoteDroneSkills.AddMachine(prefab, UtilityMachine);
                RemoteDroneSkills.SetSkill(prefab, "Utility", utility);
            }
            if (special) {
                // Artificer's Flamethrower is a better thematic match than Railgunner's rifle.
                special.icon = TintVanillaIcon(RemoteDroneSkills.VanillaIcon("MageBody", "Special", fallback), true);
                detonatePrimary.icon = special.icon;
                lockedSpecial.icon = TintVanillaIcon(railgunnerOffline ? railgunnerOffline : special.icon, true, true);
                RemoteDroneSkills.AddMachine(prefab, SpecialMachine);
                RemoteDroneSkills.SetSkill(prefab, "Special", special);
            }
            if (!prefab.GetComponent<IncineratorController>()) prefab.AddComponent<IncineratorController>();
        }

        private sealed class IncineratorController : MonoBehaviour
        {
            internal CharacterBody Body;
            internal SkillLocator Slots;
            internal float LastFire;
            internal float OverheatUntil;
            internal bool Ready;
            private float specialUsedAt = -10000f;
            private int specialStockAfterUse;
            private float specialRechargeAfterUse;
            internal void MarkSpecialUsed()
            {
                specialUsedAt = Time.time;
                // OnEnter runs after the skill system pays its one-stock cost.
                // Preserve spare Lysate Cell charges while the special is locked.
                specialStockAfterUse = Slots && Slots.special ? Slots.special.stock : 0;
                specialRechargeAfterUse = Slots && Slots.special ? Slots.special.rechargeStopwatch : 0f;
            }
            private float lastDetonation = -1000f;
            internal bool TryDetonate()
            {
                if (!Ready || Overheated || Time.time - lastDetonation < .5f) return false;
                lastDetonation = Time.time;
                StartOverheat();
                return true;
            }
            private bool lastOverheated;
            private bool lockoutApplied;
            private bool lastReady;
            private int storedUtilityStock;
            private float storedUtilityRecharge;
            private int hotStock = 2;
            private float hotRecharge;
            private int storedPrimaryStock;
            private float storedPrimaryRecharge;
            private int overheatPrimaryStock;
            private float overheatPrimaryRecharge;
            private void Awake() { Body = GetComponent<CharacterBody>(); Slots = GetComponent<SkillLocator>(); LastFire = -1000f; }
            internal bool Overheated => Time.time < OverheatUntil;
            internal bool CanFire => !Ready && !Overheated;
            internal void StartOverheat()
            {
                Ready = false;
                OverheatUntil = Time.time + Mathf.Max(0f, C.Get(infernoOverheat));
                // The cooldown begins only after the overheat lockout finishes.
                // Immediately switch to the mobility-only jet rather than waiting for the next physics tick.
                if (!lastOverheated) { SwapUtility(true); lastOverheated = true; }
            }
            private void FixedUpdate()
            {
                if (!Body || !Slots) return;
                bool overheated = Overheated;
                // Never erase spare Lysate Cell stocks when Inferno becomes ready.
                // The active Inferno state already blocks another cast.
                if (lastOverheated != overheated)
                {
                    SwapUtility(overheated);
                    lastOverheated = overheated;
                }
                if (lastReady != Ready)
                {
                    if (Slots.primary)
                    {
                        if (Ready)
                        {
                            storedPrimaryStock = Slots.primary.stock;
                            storedPrimaryRecharge = Slots.primary.rechargeStopwatch;
                            Slots.primary.SetSkillOverride(gameObject, detonatePrimary, GenericSkill.SkillOverridePriority.Contextual);
                            Slots.primary.stock = 1; // Detonation must remain usable even with empty fuel.
                        }
                        else
                        {
                            Slots.primary.UnsetSkillOverride(gameObject, detonatePrimary, GenericSkill.SkillOverridePriority.Contextual);
                            Slots.primary.stock = Mathf.Clamp(storedPrimaryStock, 0, Slots.primary.maxStock);
                            Slots.primary.rechargeStopwatch = storedPrimaryRecharge;
                        }
                    }
                    lastReady = Ready;
                }
                // Match Railgunner's BaseCharged: contextual SkillDef overrides
                // replace the HUD icon, not just the stock count. The lockout
                // state's activation never performs an attack.
                if (lockoutApplied != overheated)
                {
                    if (overheated)
                    {
                        if (Slots.primary)
                        {
                            overheatPrimaryStock = Slots.primary.stock;
                            overheatPrimaryRecharge = Slots.primary.rechargeStopwatch;
                            Slots.primary.SetSkillOverride(gameObject, lockedPrimary, GenericSkill.SkillOverridePriority.Contextual);
                            Slots.primary.stock = 1;
                            Slots.primary.rechargeStopwatch = 0f;
                        }
                        if (Slots.special)
                        {
                            Slots.special.SetSkillOverride(gameObject, lockedSpecial, GenericSkill.SkillOverridePriority.Contextual);
                            Slots.special.stock = Mathf.Clamp(specialStockAfterUse, 0, Slots.special.maxStock);
                            Slots.special.rechargeStopwatch = 0f;
                        }
                    }
                    else
                    {
                        if (Slots.primary)
                        {
                            Slots.primary.UnsetSkillOverride(gameObject, lockedPrimary, GenericSkill.SkillOverridePriority.Contextual);
                            Slots.primary.stock = Slots.primary.maxStock;
                            Slots.primary.rechargeStopwatch = 0f;
                            LastFire = Time.time - Mathf.Max(.05f, C.Get(fuelDelay));
                        }
                        if (Slots.special)
                        {
                            Slots.special.UnsetSkillOverride(gameObject, lockedSpecial, GenericSkill.SkillOverridePriority.Contextual);
                            // Preserve spare Lysate Cell stocks. Start the spent charge's
                            // cooldown only AFTER overheating; do not grant a free stock.
                            Slots.special.stock = Mathf.Clamp(specialStockAfterUse, 0, Slots.special.maxStock);
                            Slots.special.rechargeStopwatch = Slots.special.stock >= Slots.special.maxStock
                                ? 0f : Mathf.Max(0f, specialRechargeAfterUse);
                        }
                    }
                    lockoutApplied = overheated;
                }
                if (overheated)
                {
                    // The locked definitions need full visible stock (but requiredStock=2)
                    // to avoid the vanilla skill HUD attempting a nonsensical recharge.
                    if (Slots.primary) { Slots.primary.stock = 1; Slots.primary.rechargeStopwatch = 0f; }
                    if (Slots.special)
                    {
                        Slots.special.stock = Mathf.Clamp(specialStockAfterUse, 0, Slots.special.maxStock);
                        Slots.special.rechargeStopwatch = 0f;
                    }
                }
                // Fuel replenishes all at once; its HUD should show an eight-second reload timer.
                if (Slots.primary && !Ready && !overheated)
                {
                    int maximum = Mathf.Max(1, C.Get(fuelStocks));
                    if (Slots.primary.stock < maximum)
                    {
                        float reload = Mathf.Max(.05f, C.Get(fuelDelay));
                        float elapsed = Mathf.Max(0f, Time.time - LastFire);
                        // Keep the visible timer aligned with the actual full reload.
                        bool firingNow = EntityStateMachine.FindByCustomName(gameObject, PrimaryMachine)?.state is Flamethrower;
                        Slots.primary.rechargeStopwatch = firingNow ? 0f : Mathf.Min(reload - .001f, elapsed);
                        if (elapsed >= reload)
                        {
                            Slots.primary.stock = Mathf.Min(maximum, Slots.primary.stock + Mathf.Max(1, C.Get(fuelRestock)));
                            LastFire = Time.time;
                            Slots.primary.rechargeStopwatch = 0f;
                        }
                    }
                    else Slots.primary.rechargeStopwatch = 0f;
                }
                // Keep both versions' cooldowns moving independently, including the
                // temporarily hidden Flame Jet's stock recovery.
                if (overheated) AdvanceHiddenUtility(ref storedUtilityStock, ref storedUtilityRecharge,
                    Mathf.Max(1, utility.baseMaxStock), Mathf.Max(.05f, C.Get(jetCooldown)));
                else AdvanceHiddenUtility(ref hotStock, ref hotRecharge, 2, Mathf.Max(.05f, C.Get(jetHotCooldown)));
            }
            private static void AdvanceHiddenUtility(ref int stock, ref float recharge, int max, float period)
            {
                if (stock >= max) return;
                recharge += Time.fixedDeltaTime;
                while (recharge >= period && stock < max) { recharge -= period; stock++; }
            }
            private void SwapUtility(bool hot)
            {
                if (!Slots.utility) return;
                if (hot)
                {
                    storedUtilityStock = Slots.utility.stock;
                    storedUtilityRecharge = Slots.utility.rechargeStopwatch;
                    Slots.utility.SetSkillOverride(gameObject, hotUtility, GenericSkill.SkillOverridePriority.Contextual);
                    Slots.utility.stock = hotStock;
                    Slots.utility.rechargeStopwatch = hotRecharge;
                }
                else
                {
                    hotStock = Slots.utility.stock;
                    hotRecharge = Slots.utility.rechargeStopwatch;
                    Slots.utility.UnsetSkillOverride(gameObject, hotUtility, GenericSkill.SkillOverridePriority.Contextual);
                    Slots.utility.stock = storedUtilityStock;
                    Slots.utility.rechargeStopwatch = storedUtilityRecharge;
                }
            }
        }

        private static IncineratorController Controller(BaseState state) => state.GetComponent<IncineratorController>();

        // Search HurtBoxes rather than Collider layers: many enemy hurtboxes are not on
        // entityPrecise, so Physics.OverlapSphere silently found no viable targets.
        // DamageType.IgniteOnHit scales Burn from the triggering hit, which gives
        // the small flamethrower ticks an extremely short apparent burn.
        // Apply vanilla Burn explicitly with a configurable duration instead.
        private static void ApplyTimedBurn(GameObject attacker, GameObject victim)
        {
            if (!NetworkServer.active || !attacker || !victim) return;
            var dot = new InflictDotInfo
            {
                attackerObject = attacker,
                victimObject = victim,
                dotIndex = DotController.DotIndex.Burn,
                duration = Mathf.Max(.1f, C.Get(fireBurnDuration)),
                damageMultiplier = 1f
            };
            CharacterBody source = attacker.GetComponent<CharacterBody>();
            if (source && source.inventory)
                StrengthenBurnUtils.CheckDotForUpgrade(source.inventory, ref dot);
            DotController.InflictDot(ref dot);
        }

        private static readonly BullseyeSearch flameSearch = new BullseyeSearch();
        private static readonly HashSet<HealthComponent> flameHit = new HashSet<HealthComponent>();

        private static void HitCone(BaseState state, float radius, float angle, float coefficient, float proc, float igniteChance, float push)
        {
            if (!NetworkServer.active || !state.characterBody || !state.characterBody.teamComponent) return;
            Ray aim = state.GetAimRay();
            TeamMask mask = TeamMask.allButNeutral;
            mask.RemoveTeam(state.characterBody.teamComponent.teamIndex);
            flameSearch.searchOrigin = aim.origin;
            flameSearch.searchDirection = aim.direction;
            flameSearch.maxDistanceFilter = Mathf.Max(.1f, radius);
            flameSearch.maxAngleFilter = Mathf.Clamp(angle * .5f, 0f, 180f);
            flameSearch.teamMaskFilter = mask;
            flameSearch.sortMode = BullseyeSearch.SortMode.Distance;
            flameSearch.filterByLoS = false;
            flameSearch.filterByDistinctEntity = true;
            flameSearch.RefreshCandidates();
            flameSearch.FilterOutGameObject(state.gameObject);
            flameHit.Clear();
            if (igniteChance > 0f) IgniteNativeOil(aim.origin, aim.direction, radius, angle * .5f);
            foreach (HurtBox box in flameSearch.GetResults())
            {
                HealthComponent health = box ? box.healthComponent : null;
                if (!health || !health.alive || !flameHit.Add(health)) continue;
                CharacterBody victim = health.body;
                if (!victim) continue;
                Vector3 delta = victim.corePosition - state.characterBody.corePosition;
                bool ignite = UnityEngine.Random.value * 100f < igniteChance;
                var info = new DamageInfo {
                    attacker = state.gameObject,
                    inflictor = state.gameObject,
                    damage = state.damageStat * coefficient,
                    procCoefficient = Mathf.Max(0f, proc),
                    crit = Util.CheckRoll(state.characterBody.crit, state.characterBody.master),
                    position = victim.corePosition,
                    damageType = new DamageTypeCombo(DamageType.Generic,
                        DamageTypeExtended.Generic, DamageSource.Primary)
                };
                health.TakeDamage(info);
                GlobalEventManager.instance.OnHitEnemy(info, health.gameObject);
                if (ignite) ApplyTimedBurn(state.gameObject, health.gameObject);
                if (push > 0f && victim.characterMotor && delta.sqrMagnitude > .001f)
                    victim.characterMotor.ApplyForce(delta.normalized * push + Vector3.up * push * .15f, true, true);
            }
        }

        // Vanilla Artificer stores flamethrowerEffectPrefab as a serialized INSTANCE
        // field in its EntityStateConfiguration, not as a static field.
        private static GameObject artificerFlameEffect;
        private static bool warnedMissingNativeFlame;
        private static object ReadMember(object owner, string name)
        {
            if (owner == null) return null;
            for (Type type = owner.GetType(); type != null; type = type.BaseType)
            {
                var field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (field != null) return field.GetValue(owner);
                var property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (property != null && property.GetIndexParameters().Length == 0) return property.GetValue(owner, null);
            }
            return null;
        }
        private static GameObject FindArtificerFlameEffect()
        {
            if (artificerFlameEffect) return artificerFlameEffect;
            try
            {
                foreach (var config in Resources.FindObjectsOfTypeAll<EntityStateConfiguration>())
                {
                    if (!config || !config.name.Contains("Mage.Weapon.Flamethrower")) continue;
                    var collection = ReadMember(config, "serializedFieldsCollection");
                    var entries = ReadMember(collection, "serializedFields") as System.Collections.IEnumerable;
                    if (entries == null) continue;
                    foreach (object entry in entries)
                    {
                        if (!string.Equals(ReadMember(entry, "fieldName") as string, "flamethrowerEffectPrefab", StringComparison.Ordinal)) continue;
                        var value = ReadMember(entry, "fieldValue");
                        var prefab = ReadMember(value, "objectValue") as GameObject;
                        if (prefab) return artificerFlameEffect = prefab;
                    }
                }
            }
            catch (Exception error)
            {
                if (!warnedMissingNativeFlame)
                    ThreeQsVanillaTweaks.Log.Warning("Incinerator: failed to inspect Artificer Flamethrower config: " + error);
            }
            if (!warnedMissingNativeFlame)
            {
                warnedMissingNativeFlame = true;
                ThreeQsVanillaTweaks.Log.Warning("Incinerator: Artificer flamethrowerEffectPrefab not yet available in loaded EntityStateConfiguration assets; suppressing custom M1 particle spam.");
            }
            return null;
        }
        private static Material flameMaterial;
        private static bool visualShaderChecked;
        private static Material GetFlameMaterial()
        {
            if (flameMaterial) return flameMaterial;
            if (visualShaderChecked) return null;
            visualShaderChecked = true;
            // Prefer the renderer from Artificer's actual flamethrower effect.
            var nativeFlames = FindArtificerFlameEffect();
            if (nativeFlames)
            {
                foreach (var particleRenderer in nativeFlames.GetComponentsInChildren<ParticleSystemRenderer>(true))
                {
                    Material nativeMaterial = particleRenderer.sharedMaterial;
                    if (nativeMaterial && nativeMaterial.shader && nativeMaterial.shader.isSupported)
                        return flameMaterial = nativeMaterial;
                }
            }
            // RoR2 strips many stock Unity shaders: Shader.Find can return an unsupported
            // material that renders bright magenta. Reuse an already-rendering native flame.
            foreach (var bodyName in new[] { "FlameDroneBody", "MageBody", "LemurianBody", "GreaterWispBody" })
            {
                var prefab = BodyCatalog.FindBodyPrefab(bodyName);
                if (!prefab) continue;
                foreach (var renderer in prefab.GetComponentsInChildren<ParticleSystemRenderer>(true))
                {
                    var mat = renderer.sharedMaterial;
                    if (!mat || !mat.shader || !mat.shader.isSupported) continue;
                    string label = (renderer.name + " " + mat.name).ToLowerInvariant();
                    if (label.Contains("fire") || label.Contains("flame") || label.Contains("burn"))
                    {
                        flameMaterial = mat;
                        return flameMaterial;
                    }
                }
            }
            // Last resort: copy a known supported particle material rather than making a
            // new Standard/Legacy material (those rendered magenta in the previous build).
            foreach (var renderer in Resources.FindObjectsOfTypeAll<ParticleSystemRenderer>())
            {
                if (!renderer || !renderer.sharedMaterial || !renderer.sharedMaterial.shader ||
                    !renderer.sharedMaterial.shader.isSupported) continue;
                string label = (renderer.name + " " + renderer.sharedMaterial.name).ToLowerInvariant();
                if (label.Contains("fire") || label.Contains("flame"))
                    return flameMaterial = renderer.sharedMaterial;
            }
            return null;
        }

        private static void FlameVisual(BaseState state, float size = 1f, float angle = 26f, int count = 14,
            float speed = 18f, Vector3? origin = null, Vector3? direction = null, float reach = 0f, bool smoke = false)
        {
            if (!state.gameObject) return;
            Ray aim = state.GetAimRay();
            // Prefer Artificer's authored flamethrower effect over generic particles.
            GameObject nativeEffect = FindArtificerFlameEffect();
            if (nativeEffect && reach <= 0f && !smoke)
            {
                Vector3 d = direction ?? aim.direction;
                if (d.sqrMagnitude > .001f)
                {
                    var fx = UnityEngine.Object.Instantiate(nativeEffect, origin ?? (aim.origin + d.normalized * .8f),
                        Quaternion.LookRotation(d.normalized));
                    // Native effect may be authored as a loop; terminate instances explicitly.
                    UnityEngine.Object.Destroy(fx, 0.35f);
                    return;
                }
            }
            Vector3 forward = direction ?? aim.direction;
            if (forward.sqrMagnitude < .001f) forward = state.transform.forward;
            Vector3 position = origin ?? (aim.origin + forward.normalized * 1.1f);
            var obj = new GameObject("3QS Incinerator Flame VFX");
            obj.transform.position = position;
            obj.transform.rotation = Quaternion.LookRotation(forward.normalized);
            var emitter = obj.AddComponent<ParticleSystem>();
            emitter.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = emitter.main;
            main.loop = false;
            main.duration = .45f;
            // A primary flame stream must extend to its actual damage range. Particle lifetime
            // is derived from travel distance, not a fixed short muzzle flash.
            float travel = reach > 0f ? Mathf.Max(2f, reach) : 0f;
            float particleSpeed = reach > 0f ? Mathf.Max(28f, travel * 1.2f) : speed;
            float lifetime = reach > 0f ? travel / particleSpeed : .38f;
            main.startLifetime = reach > 0f
                ? new ParticleSystem.MinMaxCurve(lifetime * .88f, lifetime * 1.08f)
                : new ParticleSystem.MinMaxCurve(.16f, .38f);
            main.startSpeed = reach > 0f ? new ParticleSystem.MinMaxCurve(particleSpeed * .95f, particleSpeed * 1.05f) : new ParticleSystem.MinMaxCurve(speed * .6f, speed);
            main.startSize = reach > 0f ? new ParticleSystem.MinMaxCurve(.5f * size, 1.4f * size) : new ParticleSystem.MinMaxCurve(.25f * size, .9f * size);
            main.startColor = smoke
                ? new ParticleSystem.MinMaxGradient(new Color(.28f, .28f, .28f, .75f), new Color(.55f, .49f, .43f, .45f))
                : new ParticleSystem.MinMaxGradient(new Color(1f, .35f, .035f, 1f), new Color(1f, .82f, .12f, 1f));
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = Mathf.Max(32, count * 2);
            main.playOnAwake = false;
            var emission = emitter.emission;
            emission.enabled = false;
            var shape = emitter.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = Mathf.Clamp(reach > 0f ? 3f : angle, 0f, 85f);
            shape.radius = .15f * size;
            var renderer = obj.GetComponent<ParticleSystemRenderer>();
            Material material = GetFlameMaterial();
            if (material) renderer.sharedMaterial = material;
            else renderer.enabled = false; // Never show the magenta missing-shader placeholder.
            emitter.Play();
            emitter.Emit(Mathf.Max(1, count));
            UnityEngine.Object.Destroy(obj, 1.1f);
        }

        // Vanilla CommandFlamethrower attaches its effect to the model's MuzzleCenter.
        // Keep the flame emitter in world space, but derive its position from that nozzle.
        private static Vector3 FlameNozzlePosition(BaseState state, Ray aim)
        {
            ModelLocator modelLocator = state.gameObject.GetComponent<ModelLocator>();
            Transform model = modelLocator ? modelLocator.modelTransform : null;
            ChildLocator children = model ? model.GetComponent<ChildLocator>() : null;
            Transform muzzle = children ? children.FindChild("MuzzleCenter") : null;
            return muzzle ? muzzle.position : aim.origin + aim.direction.normalized * .8f;
        }

        private static GameObject CreatePrimaryNativeFlames(BaseState state)
        {
            GameObject prefab = FindArtificerFlameEffect();
            if (!prefab) return null;
            Ray aim = state.GetAimRay();
            GameObject instance = UnityEngine.Object.Instantiate(prefab,
                FlameNozzlePosition(state, aim), Quaternion.LookRotation(aim.direction));
            // Artificer's authored prefab is a *timed* particle effect: vanilla sets
            // ScaleParticleSystemDuration.newDuration on every activation. Without it,
            // the drone's effect expires even while Primary continues firing.
            // Sustain one instance for longer than a complete default fuel magazine.
            var durationScale = instance.GetComponent<ScaleParticleSystemDuration>();
            if (durationScale) durationScale.newDuration = 30f;
            foreach (var particle in instance.GetComponentsInChildren<ParticleSystem>(true))
            {
                // The unmodified Artificer effect performs well. Do not multiply all
                // particle velocities or spawn extra emitters to chase the 30m hit range.
                // Bound particles per child for long-held input without scaling size.
                var main = particle.main;
                main.maxParticles = Mathf.Min(main.maxParticles, 90);
            }
            return instance;
        }

        // Primary-only persistent local emitter. One ParticleSystem for the whole firing state;
        // no periodic Instantiate/Destroy, no per-tick VFX allocations.
        private static GameObject CreateContinuousFlames(BaseState state)
        {
            Ray aim = state.GetAimRay();
            var obj = new GameObject("3QS Continuous Incinerator Flame");
            obj.transform.SetPositionAndRotation(FlameNozzlePosition(state, aim),
                Quaternion.LookRotation(aim.direction));
            var particles = obj.AddComponent<ParticleSystem>();
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = particles.main;
            main.loop = true;
            main.duration = 1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(.30f, .39f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(83f, 100f);
            main.startSize = new ParticleSystem.MinMaxCurve(.55f, 1.05f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, .26f, .025f, .9f),
                new Color(1f, .8f, .11f, .9f));
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 144;
            main.playOnAwake = false;
            var emission = particles.emission;
            emission.enabled = true;
            emission.rateOverTime = 75f;
            var shape = particles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 2.5f;
            shape.radius = .10f;
            var renderer = obj.GetComponent<ParticleSystemRenderer>();
            Material material = GetFlameMaterial();
            if (material) renderer.sharedMaterial = material;
            else renderer.enabled = false;
            particles.Play();
            return obj;
        }

        public sealed class Flamethrower : BaseState
        {
            private IncineratorController controller;
            private float nextTick, cycleElapsed, flameVisualAge, nextSoundRefresh;
            private bool firing;
            private GameObject flameVisual;
            private uint flameSoundId;
            public override void OnEnter()
            {
                base.OnEnter();
                controller = Controller(this);
                if (!controller || !controller.CanFire) { if (isAuthority) outer.SetNextStateToMain(); return; }
                controller.LastFire = Time.time;
                if (skillLocator && skillLocator.primary) skillLocator.primary.rechargeStopwatch = 0f;
            }
            public override void OnExit()
            {
                if (firing)
                {
                    if (flameSoundId != 0) AkSoundEngine.StopPlayingID(flameSoundId);
                    Util.PlaySound("Play_mage_R_end", gameObject);
                }
                if (flameVisual) UnityEngine.Object.Destroy(flameVisual);
                if (controller != null) controller.LastFire = Time.time; // Reload countdown starts after release.
                base.OnExit();
            }
            public override void FixedUpdate()
            {
                base.FixedUpdate();
                if (!controller || !controller.CanFire) { if (isAuthority) outer.SetNextStateToMain(); return; }
                if (!firing)
                {
                    if (fixedAge < Mathf.Max(.01f, C.Get(windup))) return;
                    if (!skillLocator || !skillLocator.primary || skillLocator.primary.stock <= 0)
                    {
                        if (isAuthority) outer.SetNextStateToMain();
                        return;
                    }
                    if (isAuthority) skillLocator.primary.stock--; // Spend the FIRST charge before dealing any damage.
                    firing = true; cycleElapsed = 0f; nextTick = 0f;
                    flameSoundId = Util.PlaySound("Play_mage_R_start", gameObject);
                    nextSoundRefresh = Time.time + 4.5f;
                    flameVisual = C.Get(useArtificerFlames) ? CreatePrimaryNativeFlames(this) : CreateContinuousFlames(this);
                    flameVisualAge = 0f;
                }
                // Artificer's original prefab is a timed effect, refreshed only in compatibility mode.
                // The custom stream instead loops continuously with no object churn.
                if (C.Get(useArtificerFlames))
                {
                    flameVisualAge += Time.fixedDeltaTime;
                    if (!flameVisual || flameVisualAge >= 4f)
                    {
                        GameObject replacement = CreatePrimaryNativeFlames(this);
                        if (replacement)
                        {
                            if (flameVisual) UnityEngine.Object.Destroy(flameVisual);
                            flameVisual = replacement;
                            flameVisualAge = 0f;
                        }
                    }
                }
                if (flameVisual)
                {
                    Ray aim = GetAimRay();
                    if (aim.direction.sqrMagnitude > .0001f)
                        flameVisual.transform.SetPositionAndRotation(FlameNozzlePosition(this, aim),
                            Quaternion.LookRotation(aim.direction));
                }
                if (skillLocator && skillLocator.primary) skillLocator.primary.rechargeStopwatch = 0f;
                // The native start event is finite, not a reliable endless Wwise loop.
                // Re-post it well before its observed cutoff while M1 is continuously held.
                if (Time.time >= nextSoundRefresh)
                {
                    uint previousSound = flameSoundId;
                    flameSoundId = Util.PlaySound("Play_mage_R_start", gameObject);
                    if (previousSound != 0) AkSoundEngine.StopPlayingID(previousSound);
                    nextSoundRefresh = Time.time + 4.0f;
                }
                float duration = Mathf.Max(.05f, C.Get(fuelCycle));
                cycleElapsed += Time.fixedDeltaTime;
                float ticks = Mathf.Max(1f, C.Get(fireTicks) * Mathf.Max(.01f, attackSpeedStat));
                float interval = duration / ticks;
                while (nextTick < duration && cycleElapsed >= nextTick)
                {
                    HitCone(this, C.Get(fireRange), 55f, C.Get(fireDamage) / 100f, C.Get(fireProc), C.Get(fireBurn), 0f);
                    nextTick += interval;
                }
                if (cycleElapsed < duration) return;
                controller.LastFire = Time.time;
                if (!inputBank || !inputBank.skill1.down || !skillLocator || !skillLocator.primary || skillLocator.primary.stock <= 0)
                {
                    if (isAuthority) outer.SetNextStateToMain();
                    return;
                }
                if (isAuthority) skillLocator.primary.stock--; // Spend the NEXT charge before the next cycle.
                cycleElapsed = 0f; nextTick = 0f;
            }
            public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.PrioritySkill;
        }

        // CHEF Sear.ProcessOilSpills (vanilla): the relevant components are
        // OilController and OilStateController, both exposing IgniteOil(bool).
        // Their states and visuals are owned by CHEF; do not add a separate decal.
        private static float nextOilIgniteScan;
        private static void IgniteNativeOil(Vector3 origin, Vector3 direction, float range, float halfAngle)
        {
            if (!NetworkServer.active || Time.time < nextOilIgniteScan) return;
            nextOilIgniteScan = Time.time + .5f;
            float sqrRange = range * range;
            Vector3 forward = direction.sqrMagnitude > .0001f ? direction.normalized : Vector3.forward;

            foreach (OilController pool in UnityEngine.Object.FindObjectsOfType<OilController>())
            {
                if (!pool || pool.isFrozen || !OilWithinAttack(pool.transform.position)) continue;
                pool.IgniteOil(false);
            }
            foreach (OilStateController pool in UnityEngine.Object.FindObjectsOfType<OilStateController>())
            {
                if (!pool || pool.IsFrozen || !OilWithinAttack(pool.transform.position)) continue;
                pool.IgniteOil(false);
            }

            bool OilWithinAttack(Vector3 position)
            {
                Vector3 offset = position - origin;
                return offset.sqrMagnitude <= sqrRange &&
                    (halfAngle >= 179.9f || offset.sqrMagnitude < .0001f ||
                     Vector3.Angle(forward, offset) <= halfAngle);
            }
        }

        // Verified against the player's Railgunner Supercharge sound log.
        // ChargeUp is a loop: stop its playing ID when charge completes.
        // The ready and timer-loop sounds belong to the charged/holding phase.
        private const string InfernoChargeSound = "Play_railgunner_R_gun_chargeUp";
        private const string InfernoReadySound = "Play_railgunner_R_gun_ready";
        private const string InfernoHoldSound = "Play_railgunner_R_gun_timer_loop";
        private const string InfernoStopHoldSound = "Stop_railgunner_R_gun_timer_loop";
        private const string InfernoPreFireSound = "Play_railgunner_R_preFire";
        // Captured while using Drifter's Junk Cube. This is its creation cue,
        // not a confirmed cube impact/explosion event.
        private const string InfernoExplosionSound = "Play_GG_CHAR_Drifter_AltSec_JumpPad_Create";

        private static uint PlayInfernoSound(string eventName, GameObject emitter)
        {
            uint playingId = Util.PlaySound(eventName, emitter);
            if (playingId == 0)
                ThreeQsVanillaTweaks.Log.Warning("Incinerator Inferno: sound not accepted by Wwise: " + eventName +
                    " (emitter=" + (emitter ? emitter.name : "null") + "). Check that Railgunner's sound bank is loaded.");
            return playingId;
        }

        private static GameObject droneGlazeProjectile;
        private static bool warnedOilPrefab;
        private static GameObject FindChefGlazeProjectile()
        {
            if (droneGlazeProjectile) return droneGlazeProjectile;
            GameObject original = EntityStates.Chef.Glaze.projectilePrefab;
            if (!original) return null;
            // Glaze's grenade does NOT itself spawn an OilSpillBase pool. CHEF's oil
            // pools are created by OilSpillBase.DropOilPoolAuthority on landing.
            // Clone the grenade so this behaviour does not alter CHEF's own skill.
            droneGlazeProjectile = R2API.PrefabAPI.InstantiateClone(original,
                "3QSIncineratorFuelGlazeWithOil", true);
            droneGlazeProjectile.AddComponent<DroneGlazeOilImpact>();
            return droneGlazeProjectile;
        }

        // Keep oil damage and visual logic on CHEF's native networked oil projectile.
        private sealed class DroneGlazeOilImpact : MonoBehaviour, IProjectileImpactBehavior
        {
            private bool spawned;
            public void OnProjectileImpact(ProjectileImpactInfo impact)
            {
                if (spawned || !NetworkServer.active) return;
                spawned = true;
                var nativePool = EntityStates.Chef.OilSpillBase.projectilePrefab;
                if (!nativePool)
                {
                    if (!warnedOilPrefab)
                    {
                        warnedOilPrefab = true;
                        ThreeQsVanillaTweaks.Log.Warning("Incinerator: CHEF oil-pool projectile prefab is unavailable; Glaze impact cannot create a damaging pool.");
                    }
                    return;
                }
                var grenade = GetComponent<ProjectileController>();
                if (!grenade || !grenade.owner) return;
                Vector3 point = transform.position;
                RaycastHit ground;
                if (Physics.Raycast(point + Vector3.up * 2f, Vector3.down,
                    out ground, 14f, LayerIndex.world.mask)) point = ground.point;
                var projectileDamage = GetComponent<ProjectileDamage>();
                ProjectileManager.instance.FireProjectile(new FireProjectileInfo
                {
                    projectilePrefab = nativePool,
                    position = point,
                    rotation = Quaternion.identity,
                    owner = grenade.owner,
                    damage = projectileDamage ? projectileDamage.damage : 0f,
                    crit = projectileDamage && projectileDamage.crit,
                    force = 0f,
                    damageTypeOverride = DamageTypeCombo.GenericUtility
                });
            }
        }

        public sealed class FuelGlaze : BaseState
        {
            private int fired;
            private float fireTimer;
            private bool warnedMissing;
            public override void OnEnter()
            {
                base.OnEnter();
                StartAimMode();
                if (EntityStates.Chef.Glaze.effectPrefab)
                    EffectManager.SpawnEffect(EntityStates.Chef.Glaze.effectPrefab,
                        new EffectData { origin = GetAimRay().origin, rotation = Quaternion.LookRotation(GetAimRay().direction) }, false);
            }
            public override void FixedUpdate()
            {
                base.FixedUpdate();
                if (!isAuthority) return;
                // Spread seven shots across the activation, as CHEF does, instead of
                // spawning inert local GameObjects on the firing frame.
                int count = Mathf.Max(1, C.Get(glazeCount));
                fireTimer += Time.fixedDeltaTime;
                while (fired < count && fireTimer >= .075f)
                {
                    fireTimer -= .075f;
                    FireGlob(count);
                    fired++;
                }
                if (fired >= count) outer.SetNextStateToMain();
            }
            private void FireGlob(int count)
            {
                var prefab = FindChefGlazeProjectile();
                if (!prefab)
                {
                    if (!warnedMissing)
                    {
                        warnedMissing = true;
                        Debug.LogWarning("[3QsVanillaTweaks] Incinerator Glaze: vanilla CHEF projectilePrefab is null; check content registration.");
                    }
                    return;
                }
                Ray aim = GetAimRay();
                float spread = count <= 1 ? 0f : ((float)fired / (count - 1) - .5f) * C.Get(glazeSpread);
                Vector3 side = Vector3.Cross(Vector3.up, aim.direction);
                if (side.sqrMagnitude < .01f) side = Vector3.right;
                Vector3 dir = Quaternion.AngleAxis(spread, Vector3.up) *
                              Quaternion.AngleAxis(-8f, side.normalized) * aim.direction;
                var info = new FireProjectileInfo {
                    projectilePrefab = prefab,
                    position = aim.origin + aim.direction * 1.5f,
                    rotation = Util.QuaternionSafeLookRotation(dir),
                    owner = gameObject,
                    damage = damageStat * C.Get(glazeDamage) / 100f,
                    force = 0f,
                    crit = RollCrit(),
                    damageTypeOverride = new DamageTypeCombo(DamageType.WeakOnHit,
                        DamageTypeExtended.Generic, DamageSource.Secondary)
                };
                ProjectileManager.instance.FireProjectile(info);
                // CHEF's launch event is ordinarily supplied through the Glaze state.
                // Use its own value rather than silently omitting M2 audio.
                string sound = EntityStates.Chef.Glaze.attackSoundString;
                if (!string.IsNullOrEmpty(sound)) Util.PlaySound(sound, gameObject);
                else Util.PlaySound("Play_Chef_Utility_OilSpill_Launch", gameObject);
            }
        }

        public sealed class FlameJet : BaseState
        {
            private Vector3 recoilDirection;
            private float remainingKinematicTravel;
            public override void OnEnter()
            {
                base.OnEnter();
                var control = Controller(this);
                bool hot = control != null && control.Overheated;
                Ray aim = GetAimRay();
                if (!hot) HitCone(this, C.Get(jetRange), C.Get(jetAngle), C.Get(jetDamage) / 100f, C.Get(jetProc), 100f, C.Get(jetForce));
                if (isAuthority)
                {
                    int remaining = skillLocator && skillLocator.utility ? skillLocator.utility.stock : 0;
                    // The final charge gets a stronger kick, in both normal and overheated modes.
                    float finalChargeBonus = remaining <= 0 ? 1.25f : 1f;
                    float force = C.Get(jetSelfForce) * (hot ? C.Get(jetHotMultiplier) / 100f : 1f) * finalChargeBonus;
                    Vector3 recoil = -aim.direction.normalized;
                    recoilDirection = recoil;
                    // Flying drones commonly have a Rigidbody but no CharacterMotor.
                    // Use actual velocity for drone flight, and motor impulse otherwise.
                    Rigidbody rb = gameObject.GetComponent<Rigidbody>() ?? gameObject.GetComponentInChildren<Rigidbody>();
                    // PlayerFlyState can overwrite a one-frame velocity impulse.
                    // Apply an initial kick AND sustain a displacement during the jet.
                    remainingKinematicTravel = Mathf.Clamp(force / 160f, 8f, 24f);
                    if (rb && !rb.isKinematic)
                        rb.velocity += recoil * Mathf.Clamp(force / Mathf.Max(1f, rb.mass), 28f, 72f);
                    if (characterMotor)
                        characterMotor.ApplyForce(recoil * force * 1.35f, true, true);
                    // Remote drones commonly fly with PseudoCharacterMotor instead.
                    // Resolve its velocityAuthority property without assuming a particular
                    // RoR2 binary exposes it as a public compile-time API.
                    var pseudo = gameObject.GetComponent("PseudoCharacterMotor");
                    if (pseudo)
                    {
                        var property = pseudo.GetType().GetProperty("velocityAuthority", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (property != null && property.CanRead && property.CanWrite)
                        {
                            try { property.SetValue(pseudo, (Vector3)property.GetValue(pseudo, null) + recoil * 34f, null); }
                            catch { }
                        }
                    }
                }
                // Overheated jet is propulsion-only: smoke and a distinct warning cue.
                FlameVisual(this, hot ? 1.1f : 1.5f, C.Get(jetAngle) * .5f,
                    hot ? 28 : 65, hot ? 12f : 25f, smoke: hot);
                Util.PlaySound(hot ? "Play_railgunner_R_gun_ready" : "Play_mage_m1_shoot", gameObject);
            }
            public override void FixedUpdate()
            {
                base.FixedUpdate();
                if (isAuthority && remainingKinematicTravel > 0f)
                {
                    float step = Mathf.Min(remainingKinematicTravel, 75f * Time.fixedDeltaTime);
                    Vector3 position = transform.position;
                    if (Physics.Raycast(position, recoilDirection, out RaycastHit wall, step + .25f, LayerIndex.world.mask))
                        step = Mathf.Max(0f, wall.distance - .25f);
                    Rigidbody rb = gameObject.GetComponent<Rigidbody>() ?? gameObject.GetComponentInChildren<Rigidbody>();
                    // Move the actual body root; moving a child Rigidbody can leave
                    // the remotely controlled PlayerFlyState anchored in place.
                    transform.position = position + recoilDirection * step;
                    remainingKinematicTravel -= Mathf.Max(step, 75f * Time.fixedDeltaTime);
                }
                if (isAuthority && fixedAge >= .55f) outer.SetNextStateToMain();
            }
        }

        public sealed class Inferno : BaseState
        {
            private bool charged;
            private float fullChargeTime;
            private bool autoTriggered;
            private uint chargingSoundId;
            private uint holdingSoundId;
            private uint fireIdleSoundId;
            public override void OnEnter()
            {
                base.OnEnter();
                fullChargeTime = Mathf.Max(.05f, C.Get(infernoCharge) / Mathf.Max(.01f, attackSpeedStat));
                RemoteDroneSkills.ChargeDisplay(this, 0f);
                var controller = Controller(this);
                if (controller != null) controller.MarkSpecialUsed();
                chargingSoundId = PlayInfernoSound(InfernoChargeSound, gameObject);
            }
            public override void FixedUpdate()
            {
                base.FixedUpdate();
                var controller = Controller(this);
                if (!controller || controller.Overheated) { if (isAuthority) outer.SetNextStateToMain(); return; }
                if (!charged)
                {
                    RemoteDroneSkills.ChargeDisplay(this, Mathf.Clamp01(fixedAge / fullChargeTime));
                    if (fixedAge < fullChargeTime) return;
                    charged = true;
                    if (chargingSoundId != 0) { AkSoundEngine.StopPlayingID(chargingSoundId); chargingSoundId = 0; }
                    controller.Ready = true;
                    // The ready cue marks full charge; the timer loop runs until release.
                    // The hold is as long as charging, with the meter reversing.
                    PlayInfernoSound(InfernoReadySound, gameObject);
                    holdingSoundId = PlayInfernoSound(InfernoHoldSound, gameObject);
                    // Vanilla Artificer flame audio; pair its start and end events.
                    fireIdleSoundId = PlayInfernoSound("Play_mage_R_start", gameObject);
                }
                float held = fixedAge - fullChargeTime;
                RemoteDroneSkills.ChargeDisplay(this, 1f - Mathf.Clamp01(held / fullChargeTime), true);
                if (held < fullChargeTime || autoTriggered) return;
                autoTriggered = true;
                if (controller.TryDetonate()) Explode(this);
                if (isAuthority) outer.SetNextStateToMain();
            }
            public override void OnExit()
            {
                if (chargingSoundId != 0) AkSoundEngine.StopPlayingID(chargingSoundId);
                if (holdingSoundId != 0)
                {
                    PlayInfernoSound(InfernoStopHoldSound, gameObject);
                    AkSoundEngine.StopPlayingID(holdingSoundId);
                    holdingSoundId = 0;
                }
                if (fireIdleSoundId != 0)
                {
                    PlayInfernoSound("Play_mage_R_end", gameObject);
                    AkSoundEngine.StopPlayingID(fireIdleSoundId);
                    fireIdleSoundId = 0;
                }
                RemoteDroneSkills.HideCharge(this);
                base.OnExit();
            }
            public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.PrioritySkill;
        }

        private static void Explode(BaseState state)
        {
            var body = state.characterBody;
            if (!body || !body.teamComponent) return;
            Vector3 center = body.corePosition;
            float radius = Mathf.Max(.1f, C.Get(infernoRadius));
            if (NetworkServer.active)
                new BlastAttack {
                    attacker = state.gameObject, inflictor = state.gameObject,
                    teamIndex = body.teamComponent.teamIndex,
                    position = center, radius = radius,
                    baseDamage = state.damageStat * C.Get(infernoDamage) / 100f,
                    crit = Util.CheckRoll(body.crit, body.master),
                    procCoefficient = C.Get(infernoProc),
                    falloffModel = BlastAttack.FalloffModel.None,
                    damageType = new DamageTypeCombo(DamageType.Generic, DamageTypeExtended.Generic, DamageSource.Special),
                    attackerFiltering = AttackerFiltering.NeverHitSelf
                }.Fire();
            if (NetworkServer.active)
            {
                var affected = new HashSet<HealthComponent>();
                foreach (Collider collider in Physics.OverlapSphere(center, radius))
                {
                    HurtBox hurtBox = collider.GetComponent<HurtBox>() ?? collider.GetComponentInParent<HurtBox>();
                    HealthComponent target = hurtBox ? hurtBox.healthComponent : null;
                    if (!target || !target.alive || !affected.Add(target) ||
                        !target.body || !target.body.teamComponent ||
                        target.body.teamComponent.teamIndex == body.teamComponent.teamIndex)
                        continue;
                    ApplyTimedBurn(state.gameObject, target.body.gameObject);
                }
            }
            IgniteNativeOil(center, Vector3.up, radius, 180f);
            // Ring of outward-moving fire travels to the actual damage radius.
            const int spokes = 48;
            for (int i = 0; i < spokes; i++)
            {
                float a = Mathf.PI * 2f * i / spokes;
                Vector3 direction = new Vector3(Mathf.Cos(a), .08f, Mathf.Sin(a)).normalized;
                FlameVisual(state, 1.7f, 10f, 45, radius * 2.3f,
                    center, direction, radius);
            }
            FlameVisual(state, 4.5f, 70f, 130, 30f, center, Vector3.up);
            // The same verified pre-fire and firing cues used by Railgunner Supercharge.
            // Stop the held-charge loop immediately on detonation, including manual detonation
            // from the separate Primary state (whose state exit can happen afterwards).
            PlayInfernoSound(InfernoStopHoldSound, state.gameObject);
            PlayInfernoSound(InfernoPreFireSound, state.gameObject);
            PlayInfernoSound(InfernoExplosionSound, state.gameObject);
        }

        public sealed class OverheatedLockout : BaseState
        {
            public override void OnEnter()
            {
                base.OnEnter();
                if (isAuthority) outer.SetNextStateToMain();
            }
        }

        public sealed class Detonate : BaseState
        {
            public override void OnEnter()
            {
                base.OnEnter();
                var controller = Controller(this);
                if (controller != null && controller.TryDetonate()) Explode(this);
            }
            public override void FixedUpdate()
            {
                base.FixedUpdate();
                if (isAuthority) outer.SetNextStateToMain();
            }
        }
    }
}
