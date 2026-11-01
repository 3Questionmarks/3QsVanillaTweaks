using System;
using BepInEx.Configuration;
using RoR2;
using A = ThreeQsVanillaTweaks.ReflectionTools;
using C = Config.ConfigManager;
using S = Tweaks.Items.Common.CommonItemSupport;

namespace Tweaks.Items.Common
{
    // Pickup text is deliberately a short summary; detailed numbers stay in the
    // full description. Bind after all modules have initialized their settings.
    internal static class CommonItemPickupDescriptions
    {
        internal static void Init() => RoR2Application.onLoad += Register;

        static Type Module(string name) => typeof(CommonItemPickupDescriptions).Assembly.GetType("Tweaks.Items.Common." + name, true);
        static bool Active(string name)
        {
            if (name == "DelicateWatch") return DelicateWatch.Active;
            var type = Module(name);
            var settings = A.Field(type, "settings")?.GetValue(null) as VanillaCommonConfig;
            return settings != null ? settings.Active : A.Field(type, "enabled")?.GetValue(null) is ConfigEntry<bool> enabled && C.Get(enabled);
        }
        static bool Flag(string name, string field) => A.Field(Module(name), field)?.GetValue(null) is ConfigEntry<bool> value && C.Get(value);
        static float Number(string name, string field)
        {
            var value = A.Field(Module(name), field)?.GetValue(null);
            return value is ConfigEntry<float> f ? C.Get(f) : value is ConfigEntry<int> i ? C.Get(i) : 0;
        }
        static void Bind(string module, string itemName, Func<string> summary)
        {
            // Disabled modules leave the game's (or another mod's) token alone.
            if (!Active(module)) return;
            var item = ItemCatalog.GetItemDef(ItemCatalog.FindItemIndex(itemName));
            if (!item) return;
            string originalToken = item.pickupToken;
            string original = Language.GetString(originalToken);
            string token = "3QS_ITEM_" + itemName.ToUpperInvariant() + "_PICKUP";
            Func<string> text = () => Active(module) ? summary() : original;
            S.Text(token, text);
            if (!string.IsNullOrEmpty(originalToken)) S.Text(originalToken, text);
            item.pickupToken = token;
        }
        static string ElixirEffects(string module, string heal, string barrier)
        {
            bool heals = Number(module, heal) > 0, shields = Number(module, barrier) > 0;
            return heals && shields ? " restores <style=cIsHealing>health</style> and grants <style=cIsHealing>barrier</style>"
                : heals ? " restores <style=cIsHealing>health</style>"
                : shields ? " grants <style=cIsHealing>barrier</style>" : "";
        }
        static string RepairWatch => (DelicateWatchRework.Active ? DelicateWatchRework.RepairOnStage : Flag("DelicateWatchBroken", "regen")) ? " Repairs next stage." : "";
        static void Register()
        {
            Bind("DelicateWatch", "FragileDamageBonus", () => DelicateWatchRework.Active
                ? "Increase <style=cIsDamage>damage</style>. Taking damage wears out and eventually breaks the watches." + RepairWatch
                : "Increase <style=cIsDamage>damage</style>. Breaks at low health.");
            Bind("DelicateWatchBroken", "FragileDamageBonusConsumed", () => (Number("DelicateWatchBroken", "damage") > 0 || Number("DelicateWatchBroken", "extra") > 0 ? "Slightly increase <style=cIsDamage>damage</style>." : "A broken watch.") + RepairWatch);
            Bind("PowerElixir", "HealingPotion", () => "At low health, consuming this elixir" + ElixirEffects("PowerElixir", "heal", "barrier") + ".");
            Bind("PowerElixirConsumed", "HealingPotionConsumed", () => (Flag("PowerElixirConsumed", "onHit") ? "Taking damage" + ElixirEffects("PowerElixirConsumed", "heal", "barrier") + "." : "An empty elixir.") + (Flag("PowerElixirConsumed", "regen") ? " Refills next stage." : ""));
            Bind("Crowbar", "Crowbar", () => "Deal bonus <style=cIsDamage>damage</style> " + (A.Field(Module("Crowbar"), "mode")?.GetValue(null) is ConfigEntry<Crowbar.DamageMode> mode && C.Get(mode) == Crowbar.DamageMode.FirstHit ? "on your first hit against each enemy." : "to enemies at high health."));
            Bind("BundleOfFireworks", "Firework", () => "Opening an interactable launches <style=cIsDamage>fireworks</style> at nearby enemies.");
            Bind("BustlingFungus", "Mushroom", () => "Standing still creates a zone that <style=cIsHealing>heals</style> you and nearby allies.");
            Bind("EclipseLite", "BarrierOnCooldown", () => "Gain <style=cIsHealing>barrier</style> when a skill recharges.");
            Bind("OddlyShapedOpal", "OutOfCombatArmor", () => "Gain <style=cIsHealing>armor</style> after avoiding damage, until you are hit again.");
            Bind("StickyBomb", "StickyBomb", () => "Chance on hit to attach a <style=cIsDamage>bomb</style> to an enemy.");
            Bind("StunGrenade", "StunChanceOnHit", () => "Chance on hit to <style=cIsDamage>stun</style> enemies" + (Number("StunGrenade", "damage") > 0 || Number("StunGrenade", "damageStack") > 0 ? " and deal bonus damage." : "."));
            Bind("TopazBrooch", "BarrierOnKill", () => "Gain temporary <style=cIsHealing>barrier</style> on kill.");
            Bind("TougherTimes", "Bear", () => "Chance to <style=cIsHealing>block</style> incoming damage.");
            Bind("Warbanner", "WardOnLevel", () => "Drop a <style=cIsUtility>banner</style> that strengthens nearby allies" + (Flag("Warbanner", "onLevel") ? " when you level up" : "") + (Flag("Warbanner", "holdouts") ? " or begin charging a holdout zone" : "") + (Flag("Warbanner", "bosses") ? " or face a final boss" : "") + ".");
            Bind("BolsteringLantern", "AttackSpeedPerNearbyAllyOrEnemy", () => "Gain <style=cIsDamage>attack speed</style> for nearby enemies and allies.");
            Bind("ChronicExpansion", "IncreaseDamageOnMultiKill", () => "Kills increase <style=cIsDamage>damage</style>. Keep killing to maintain the bonus.");
        }
    }
}
