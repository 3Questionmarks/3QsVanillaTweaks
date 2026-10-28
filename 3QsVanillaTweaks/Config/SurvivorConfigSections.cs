using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Configuration;

namespace Config
{
    // Keep the mapping independent of enabled skills, installed DLC and load order.
    internal static class SurvivorConfigSections
    {
        private static readonly Dictionary<string, string> Sections = CreateSections();
        private static Dictionary<string, string> CreateSections()
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            Add(map, "Survivors - 01 Commando", "Double Tap", "01 Primary - Double Tap");
            Add(map, "Survivors - 01 Commando", "Phase Round", "02 Secondary - Phase Round");
            Add(map, "Survivors - 01 Commando", "Phase Blast", "02 Secondary - Phase Blast");
            Add(map, "Survivors - 01 Commando", "Tactical Dive", "03 Utility - Tactical Dive");
            Add(map, "Survivors - 01 Commando", "Tactical Slide", "03 Utility - Tactical Slide");
            Add(map, "Survivors - 01 Commando", "Suppressive Fire", "04 Special - Suppressive Fire");
            Add(map, "Survivors - 01 Commando", "Frag Grenade", "04 Special - Frag Grenade");
            Add(map, "Survivors - 01 Commando", "Sabotage", "04 Special - Sabotage");
            Add(map, "Survivors - 02 Huntress", "Strafe", "01 Primary - Strafe");
            Add(map, "Survivors - 02 Huntress", "Flurry", "01 Primary - Flurry");
            Add(map, "Survivors - 02 Huntress", "Laser Glaive", "02 Secondary - Laser Glaive");
            Add(map, "Survivors - 02 Huntress", "Laser Cyclone", "02 Secondary - Laser Cyclone");
            Add(map, "Survivors - 02 Huntress", "Blink", "03 Utility - Blink");
            Add(map, "Survivors - 02 Huntress", "Phase Blink", "03 Utility - Phase Blink");
            Add(map, "Survivors - 02 Huntress", "Arrow Rain", "04 Special - Arrow Rain");
            Add(map, "Survivors - 02 Huntress", "Ballista", "04 Special - Ballista");
            Add(map, "Survivors - 03 Bandit", "Burst", "01 Primary - Burst");
            Add(map, "Survivors - 03 Bandit", "Blast", "01 Primary - Blast");
            Add(map, "Survivors - 03 Bandit", "Serrated Dagger", "02 Secondary - Serrated Dagger");
            Add(map, "Survivors - 03 Bandit", "Serrated Shiv", "02 Secondary - Serrated Shiv");
            Add(map, "Survivors - 03 Bandit", "Smoke Bomb", "03 Utility - Smoke Bomb");
            Add(map, "Survivors - 03 Bandit", "Lights Out", "04 Special - Lights Out");
            Add(map, "Survivors - 03 Bandit", "Desperado", "04 Special - Desperado");
            Add(map, "Survivors - 04 MUL-T", "Auto-Nailgun", "01 Primary - Auto-Nailgun");
            Add(map, "Survivors - 04 MUL-T", "Rebar Puncher", "01 Primary - Rebar Puncher");
            Add(map, "Survivors - 04 MUL-T", "Scrap Launcher", "01 Primary - Scrap Launcher");
            Add(map, "Survivors - 04 MUL-T", "Power-Saw", "01 Primary - Power-Saw");
            Add(map, "Survivors - 04 MUL-T", "Blast Canister", "02 Secondary - Blast Canister");
            Add(map, "Survivors - 04 MUL-T", "Transport Mode", "03 Utility - Transport Mode");
            Add(map, "Survivors - 04 MUL-T", "Retool", "04 Special - Retool");
            Add(map, "Survivors - 04 MUL-T", "Power Mode", "04 Special - Power Mode");
            Add(map, "Survivors - 05 Engineer", "Bouncing Grenades", "01 Primary - Bouncing Grenades");
            Add(map, "Survivors - 05 Engineer", "Laser Cutter", "01 Primary - Laser Cutter");
            Add(map, "Survivors - 05 Engineer", "Pressure Mines", "02 Secondary - Pressure Mines");
            Add(map, "Survivors - 05 Engineer", "Spider Mines", "02 Secondary - Spider Mines");
            Add(map, "Survivors - 05 Engineer", "Bubble Shield", "03 Utility - Bubble Shield");
            Add(map, "Survivors - 05 Engineer", "Thermal Harpoons", "03 Utility - Thermal Harpoons");
            Add(map, "Survivors - 05 Engineer", "TR12 Gauss Auto-Turret", "04 Special - TR12 Gauss Auto-Turret");
            Add(map, "Survivors - 05 Engineer", "TR58 Carbonizer Turret", "04 Special - TR58 Carbonizer Turret");
            Add(map, "Survivors - 06 Artificer", "Flame Bolt", "01 Primary - Flame Bolt");
            Add(map, "Survivors - 06 Artificer", "Plasma Bolt", "01 Primary - Plasma Bolt");
            Add(map, "Survivors - 06 Artificer", "Charged Nano-Bomb", "02 Secondary - Charged Nano-Bomb");
            Add(map, "Survivors - 06 Artificer", "Cast Nano-Spear", "02 Secondary - Cast Nano-Spear");
            Add(map, "Survivors - 06 Artificer", "Snapfreeze", "03 Utility - Snapfreeze");
            Add(map, "Survivors - 06 Artificer", "Tectonic Blast", "03 Utility - Tectonic Blast");
            Add(map, "Survivors - 06 Artificer", "Flamethrower", "04 Special - Flamethrower");
            Add(map, "Survivors - 06 Artificer", "Ion Surge", "04 Special - Ion Surge");
            Add(map, "Survivors - 06 Artificer", "Voltaic Surge", "04 Special - Voltaic Surge");
            Add(map, "Survivors - 07 Mercenary", "Laser Sword", "01 Primary - Laser Sword");
            Add(map, "Survivors - 07 Mercenary", "Whirlwind", "02 Secondary - Whirlwind");
            Add(map, "Survivors - 07 Mercenary", "Rising Thunder", "02 Secondary - Rising Thunder");
            Add(map, "Survivors - 07 Mercenary", "Blinding Assault", "03 Utility - Blinding Assault");
            Add(map, "Survivors - 07 Mercenary", "Focused Assault", "03 Utility - Focused Assault");
            Add(map, "Survivors - 07 Mercenary", "Eviscerate", "04 Special - Eviscerate");
            Add(map, "Survivors - 07 Mercenary", "Slicing Winds", "04 Special - Slicing Winds");
            Add(map, "Survivors - 08 REX", "Directive Inject", "01 Primary - Directive Inject");
            Add(map, "Survivors - 08 REX", "Directive Drill", "02 Secondary - Directive Drill");
            Add(map, "Survivors - 08 REX", "Seed Barrage", "02 Secondary - Seed Barrage");
            Add(map, "Survivors - 08 REX", "Directive Disperse", "03 Utility - Directive Disperse");
            Add(map, "Survivors - 08 REX", "Bramble Volley", "03 Utility - Bramble Volley");
            Add(map, "Survivors - 08 REX", "Proliferation", "04 Special - Proliferation");
            Add(map, "Survivors - 08 REX", "Directive Harvest", "04 Special - Directive Harvest");
            Add(map, "Survivors - 08 REX", "Tangling Growth", "04 Special - Tangling Growth");
            Add(map, "Survivors - 09 Loader", "Knuckleboom", "01 Primary - Knuckleboom");
            Add(map, "Survivors - 09 Loader", "Grapple Fist", "02 Secondary - Grapple Fist");
            Add(map, "Survivors - 09 Loader", "Spiked Fist", "02 Secondary - Spiked Fist");
            Add(map, "Survivors - 09 Loader", "Charged Gauntlet", "03 Utility - Charged Gauntlet");
            Add(map, "Survivors - 09 Loader", "Thunder Gauntlet", "03 Utility - Thunder Gauntlet");
            Add(map, "Survivors - 09 Loader", "M551 Pylon", "04 Special - M551 Pylon");
            Add(map, "Survivors - 09 Loader", "Thunderslam", "04 Special - Thunderslam");
            Add(map, "Survivors - 10 Acrid", "Vicious Wounds", "01 Primary - Vicious Wounds");
            Add(map, "Survivors - 10 Acrid", "Neurotoxin", "02 Secondary - Neurotoxin");
            Add(map, "Survivors - 10 Acrid", "Ravenous Bite", "02 Secondary - Ravenous Bite");
            Add(map, "Survivors - 10 Acrid", "Caustic Leap", "03 Utility - Caustic Leap");
            Add(map, "Survivors - 10 Acrid", "Frenzied Leap", "03 Utility - Frenzied Leap");
            Add(map, "Survivors - 10 Acrid", "Epidemic", "04 Special - Epidemic");
            Add(map, "Survivors - 10 Acrid", "Cloying Breath", "04 Special - Cloying Breath");
            Add(map, "Survivors - 11 Captain", "Vulcan Shotgun", "01 Primary - Vulcan Shotgun");
            Add(map, "Survivors - 11 Captain", "Charged Shotgun", "02 Secondary - Charged Shotgun");
            Add(map, "Survivors - 11 Captain", "Power Tazer", "02 Secondary - Power Tazer");
            Add(map, "Survivors - 11 Captain", "Orbital Probe", "03 Utility - Orbital Probe");
            Add(map, "Survivors - 11 Captain", "OGM-72 Diablo Strike", "03 Utility - OGM-72 Diablo Strike");
            Add(map, "Survivors - 11 Captain", "Orbital Supply Beacon", "04 Special - Orbital Supply Beacon");
            Add(map, "Survivors - 11 Captain", "Healing Beacon", "04 Special - Orbital Supply Beacon - Healing Beacon");
            Add(map, "Survivors - 11 Captain", "Shocking Beacon", "04 Special - Orbital Supply Beacon - Shocking Beacon");
            Add(map, "Survivors - 11 Captain", "Resupply Beacon", "04 Special - Orbital Supply Beacon - Resupply Beacon");
            Add(map, "Survivors - 11 Captain", "Hacking Beacon", "04 Special - Orbital Supply Beacon - Hacking Beacon");
            Add(map, "Survivors - 12 Railgunner", "XQR Smart Round System", "01 Primary - XQR Smart Round System");
            Add(map, "Survivors - 12 Railgunner", "M99 Sniper", "02 Secondary - M99 Sniper");
            Add(map, "Survivors - 12 Railgunner", "HH44 Marksman", "02 Secondary - HH44 Marksman");
            Add(map, "Survivors - 12 Railgunner", "Concussion Device", "03 Utility - Concussion Device");
            Add(map, "Survivors - 12 Railgunner", "Polar Field Device", "03 Utility - Polar Field Device");
            Add(map, "Survivors - 12 Railgunner", "Supercharge", "04 Special - Supercharge");
            Add(map, "Survivors - 12 Railgunner", "Cryocharge", "04 Special - Cryocharge");
            Add(map, "Survivors - 13 Void Fiend", "Drown", "01 Primary - Drown");
            Add(map, "Survivors - 13 Void Fiend", "Corrupted Drown", "01 Primary - Drown - Corrupted Drown");
            Add(map, "Survivors - 13 Void Fiend", "Flood", "02 Secondary - Flood");
            Add(map, "Survivors - 13 Void Fiend", "Corrupted Flood", "02 Secondary - Flood - Corrupted Flood");
            Add(map, "Survivors - 13 Void Fiend", "Trespass", "03 Utility - Trespass");
            Add(map, "Survivors - 13 Void Fiend", "Corrupted Trespass", "03 Utility - Trespass - Corrupted Trespass");
            Add(map, "Survivors - 13 Void Fiend", "Suppress", "04 Special - Suppress");
            Add(map, "Survivors - 13 Void Fiend", "Corrupted Suppress", "04 Special - Suppress - Corrupted Suppress");
            Add(map, "Survivors - 14 Seeker", "Spirit Punch", "01 Primary - Spirit Punch");
            Add(map, "Survivors - 14 Seeker", "Unseen Hand", "02 Secondary - Unseen Hand");
            Add(map, "Survivors - 14 Seeker", "Soul Spiral", "02 Secondary - Soul Spiral");
            Add(map, "Survivors - 14 Seeker", "Sojourn", "03 Utility - Sojourn");
            Add(map, "Survivors - 14 Seeker", "Reprieve", "03 Utility - Reprieve");
            Add(map, "Survivors - 14 Seeker", "Meditate", "04 Special - Meditate");
            Add(map, "Survivors - 14 Seeker", "Palm Blast", "04 Special - Palm Blast");
            Add(map, "Survivors - 14 Seeker", "Soul Search", "04 Special - Meditate - Soul Search");
            Add(map, "Survivors - 15 False Son", "Club of the Forsaken", "01 Primary - Club of the Forsaken");
            Add(map, "Survivors - 15 False Son", "Lunar Spikes", "02 Secondary - Lunar Spikes");
            Add(map, "Survivors - 15 False Son", "Lunar Stakes", "02 Secondary - Lunar Stakes");
            Add(map, "Survivors - 15 False Son", "Step of the Brothers", "03 Utility - Step of the Brothers");
            Add(map, "Survivors - 15 False Son", "Meridians Will", "03 Utility - Meridians Will");
            Add(map, "Survivors - 15 False Son", "Laser of the Father", "04 Special - Laser of the Father");
            Add(map, "Survivors - 15 False Son", "Laser Burst", "04 Special - Laser Burst");
            Add(map, "Survivors - 16 CHEF", "Dice", "01 Primary - Dice");
            Add(map, "Survivors - 16 CHEF", "Sear", "02 Secondary - Sear");
            Add(map, "Survivors - 16 CHEF", "Ice Box", "02 Secondary - Ice Box");
            Add(map, "Survivors - 16 CHEF", "Roll", "03 Utility - Roll");
            Add(map, "Survivors - 16 CHEF", "Oil Spill", "03 Utility - Oil Spill");
            Add(map, "Survivors - 16 CHEF", "Glaze", "04 Special - Glaze");
            Add(map, "Survivors - 16 CHEF", "Yes CHEF", "04 Special - Yes CHEF");
            Add(map, "Survivors - 16 CHEF", "Boosted Dice", "04 Special - Yes CHEF - Boosted Dice");
            Add(map, "Survivors - 16 CHEF", "Boosted Sear", "04 Special - Yes CHEF - Boosted Sear");
            Add(map, "Survivors - 16 CHEF", "Boosted Ice Box", "04 Special - Yes CHEF - Boosted Ice Box");
            Add(map, "Survivors - 16 CHEF", "Boosted Roll", "04 Special - Yes CHEF - Boosted Roll");
            Add(map, "Survivors - 16 CHEF", "Boosted Oil Spill", "04 Special - Yes CHEF - Boosted Oil Spill");
            Add(map, "Survivors - 17 Operator", "H3-11 OCR Custom", "01 Primary - H3-11 OCR Custom");
            Add(map, "Survivors - 17 Operator", "Admin-Override", "02 Secondary - Admin-Override");
            Add(map, "Survivors - 17 Operator", "CMD-SWARM", "02 Secondary - CMD-SWARM");
            Add(map, "Survivors - 17 Operator", "Ascent Protocol", "03 Utility - Ascent Protocol");
            Add(map, "Survivors - 17 Operator", "Firewall", "03 Utility - Firewall");
            Add(map, "Survivors - 17 Operator", "Ejection Core", "04 Special - Ejection Core");
            Add(map, "Survivors - 17 Operator", "Amp Core", "04 Special - Amp Core");
            Add(map, "Survivors - 18 Drifter", "Blunt Force", "01 Primary - Blunt Force");
            Add(map, "Survivors - 18 Drifter", "Cleanup", "02 Secondary - Cleanup");
            Add(map, "Survivors - 18 Drifter", "Junk Cube", "02 Secondary - Junk Cube");
            Add(map, "Survivors - 18 Drifter", "Repossess", "03 Utility - Repossess");
            Add(map, "Survivors - 18 Drifter", "Bludgeon", "03 Utility - Repossess - Bludgeon");
            Add(map, "Survivors - 18 Drifter", "Discard", "03 Utility - Repossess - Discard");
            Add(map, "Survivors - 18 Drifter", "Tornado Slam", "03 Utility - Tornado Slam");
            Add(map, "Survivors - 18 Drifter", "Salvage", "04 Special - Salvage");
            Add(map, "Survivors - 18 Drifter", "Tinker", "04 Special - Tinker");
            Add(map, "Survivors - 19 Enforcer", "Trenchbroom", "01 Primary - Trenchbroom");
            Add(map, "Survivors - 19 Enforcer", "Shrapnel Grenade", "01 Primary - Shrapnel Grenade");
            Add(map, "Survivors - 19 Enforcer", "Steel Protection", "02 Secondary - Steel Protection");
            Add(map, "Survivors - 19 Enforcer", "Surge Protection", "02 Secondary - Surge Protection");
            Add(map, "Survivors - 19 Enforcer", "Shield Tackle", "03 Utility - Shield Tackle");
            Add(map, "Survivors - 19 Enforcer", "Ripcord", "03 Utility - Ripcord");
            Add(map, "Survivors - 19 Enforcer", "Rally Point", "04 Special - Rally Point");
            Add(map, "Survivors - 19 Enforcer", "Crash Point", "04 Special - Crash Point");
            Add(map, "Survivors - 20 Acolyte", "Blade Slice", "01 Primary - Blade Slice");
            Add(map, "Survivors - 20 Acolyte", "Bola Throw", "02 Secondary - Bola Throw");
            Add(map, "Survivors - 20 Acolyte", "Soul Jar", "02 Secondary - Soul Jar");
            Add(map, "Survivors - 20 Acolyte", "Blade Sling", "03 Utility - Blade Sling");
            Add(map, "Survivors - 20 Acolyte", "Javelin", "03 Utility - Javelin");
            Add(map, "Survivors - 20 Acolyte", "Blade Dash", "03 Utility - Blade Sling and Javelin - Blade Dash");
            Add(map, "Survivors - 20 Acolyte", "Sacrifice", "04 Special - Sacrifice");
            Add(map, "Survivors - 20 Acolyte", "Dark Summons", "04 Special - Sacrifice - Dark Summons");
            Add(map, "Survivors - 20 Acolyte", "Torrential Slash", "04 Special - Sacrifice - Dark Summons - Torrential Slash");
            Add(map, "Survivors - 20 Acolyte", "Harrowing Chains", "04 Special - Sacrifice - Dark Summons - Harrowing Chains");
            Add(map, "Survivors - 20 Acolyte", "Soul Storm", "04 Special - Sacrifice - Dark Summons - Soul Storm");
            Add(map, "Survivors - 20 Acolyte", "Banish", "04 Special - Sacrifice - Dark Summons - Banish");
            Add(map, "Survivors - 20 Acolyte", "Anima Echo", "04 Special - Sacrifice - Anima Echo");
            return map;
        }

        private static void Add(Dictionary<string, string> map, string root, string oldName, string newName)
        { map.Add(root + " - " + oldName, root + " - " + newName); }

        internal static string Sorted(string section)
        {
            if (Sections.TryGetValue(section, out string sorted)) return sorted;
            // Passives and general settings precede the four active skill slots.
            if (!section.StartsWith("Survivors - ", StringComparison.Ordinal)) return section;
            int separator = section.IndexOf(" - ", "Survivors - ".Length, StringComparison.Ordinal);
            if (separator < 0 || section.StartsWith("Survivors - 00 General", StringComparison.Ordinal)) return section;
            string tail = section.Substring(separator + 3);
            if (tail.StartsWith("00 ", StringComparison.Ordinal) || tail.StartsWith("01 ", StringComparison.Ordinal) ||
                tail.StartsWith("02 ", StringComparison.Ordinal) || tail.StartsWith("03 ", StringComparison.Ordinal) ||
                tail.StartsWith("04 ", StringComparison.Ordinal)) return section;
            return section.Substring(0, separator) + " - 00 General and Passive - " + tail;
        }

        // Runs before any survivor bindings, including disabled/unavailable skills.
        // A saved canonical value takes precedence over a legacy duplicate.
        internal static bool Migrate(ConfigFile file)
        {
            var property = typeof(ConfigFile).GetProperty("OrphanedEntries", BindingFlags.Instance | BindingFlags.NonPublic);
            var orphaned = property?.GetValue(file, null) as Dictionary<ConfigDefinition, string>;
            if (orphaned == null) throw new InvalidOperationException("Cannot access unbound survivor configuration entries.");
            bool changed = false;
            foreach (var pair in orphaned.ToArray())
            {
                string section = Sorted(pair.Key.Section);
                if (section == pair.Key.Section) continue;
                var target = new ConfigDefinition(section, pair.Key.Key);
                if (!orphaned.ContainsKey(target) && !file.ContainsKey(target)) orphaned.Add(target, pair.Value);
                orphaned.Remove(pair.Key);
                changed = true;
            }
            return changed;
        }
    }
}
