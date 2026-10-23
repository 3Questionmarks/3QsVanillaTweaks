using System.Collections.Generic;
using BepInEx.Configuration;
using RoR2;
using UnityEngine.Networking;
using ModConfig = Config.ConfigManager;

namespace Tweaks.DebugStuff
{
    /// <summary>Optional testing items for exploring stages. Independent of Newt Altar tweaks.</summary>
    internal static class TestLoadout
    {
        private const string Section = "Debug";
        private static ConfigEntry<bool> grantTestItems;
        private static Run lastLoadoutRun;
        private static readonly HashSet<CharacterMaster> grantedLoadouts = new HashSet<CharacterMaster>();

        internal static void Init()
        {
            grantTestItems = ModConfig.BindBool(Section, "Give Test Item Loadout", false,
                "Give each player a test item loadout once per run: 40 Mocha, 100 Hopoo Feathers, " +
                "50 Alien Heads, 10 Dio's Best Friends, 10 Rejuvenation Racks, " +
                "10 Weeping Fungus, 10 Ceremonial Daggers, 10 Lens-Maker's Glasses " +
                "and 10 Laser Scopes.");
            CharacterBody.onBodyStartGlobal += OnPlayerBodyStart;
            ThreeQsVanillaTweaks.Log.Info("Debug test loadout initialized.");
        }

        private static void OnPlayerBodyStart(CharacterBody body)
        {
            if (!NetworkServer.active || !ModConfig.Get(grantTestItems) || Run.instance == null ||
                body == null || body.master == null || body.inventory == null ||
                body.master.playerCharacterMasterController == null) return;

            if (lastLoadoutRun != Run.instance)
            {
                grantedLoadouts.Clear();
                lastLoadoutRun = Run.instance;
            }
            CharacterMaster master = body.master;
            if (!grantedLoadouts.Add(master)) return;

            GiveTestItem(body.inventory, "AttackSpeedAndMoveSpeed", 40, "Mocha");
            GiveTestItem(body.inventory, "Feather", 100, "Hopoo Feather");
            GiveTestItem(body.inventory, "AlienHead", 50, "Alien Head");
            GiveTestItem(body.inventory, "ExtraLife", 10, "Dio's Best Friend");
            GiveTestItem(body.inventory, "IncreaseHealing", 10, "Rejuvenation Rack");
            GiveTestItem(body.inventory, "MushroomVoid", 10, "Weeping Fungus");
            GiveTestItem(body.inventory, "Dagger", 10, "Ceremonial Dagger");
            GiveTestItem(body.inventory, "CritGlasses", 10, "Lens-Maker's Glasses");
            GiveTestItem(body.inventory, "CritDamage", 10, "Laser Scope");
            ThreeQsVanillaTweaks.Log.Info("Debug: granted test loadout to " + master.name + ".");
        }

        private static void GiveTestItem(Inventory inventory, string internalName, int quantity, string displayName)
        {
            ItemIndex item = ItemCatalog.FindItemIndex(internalName);
            if (item == ItemIndex.None)
            {
                ThreeQsVanillaTweaks.Log.Warning("Debug test loadout: could not find " + displayName +
                    " (internal item '" + internalName + "').");
                return;
            }
            inventory.GiveItemPermanent(item, quantity);
        }
    }
}
