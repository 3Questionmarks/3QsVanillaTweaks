# 3QsVanillaTweaks

A configurable collection of balance changes and quality-of-life improvements for Risk of Rain 2. Give stages consistent features, make more survivor skills and items useful (and some small nerfs to others), and reduce some of the annoyances of multiplayer.

Basically everything has its own config toggles and settings. The defaults are intended to be balanced-ish but I'm not the best player so my opinions on what is good or bad/need changing is going to differ to other peoples. You can edit the setting to suit your idea of balance, or revert changes to vanilla. Gameplay settings follow the host's configuration (Hopefully).

**Current version: 0.4.0**

## What's new in 0.4.0?

- **Item rebalance:** changes across every main item rarity, including reworks for Delicate Watch, Hunter's Harpoon, Happiest Mask, Symbiotic Scorpion and Neutronium Weight. Many damaging items trade proc chains for stronger direct effects. This is in hopes of moving the game away from late game proc chains that just whipe the map or deal absurds amount of damage in a single hit.
- **More configuration:** item and equipment settings are organised by rarity, with vanilla options for otherwise unchanged items, Meal items and elite aspects. Unchanged items have their changes disabled by default, but if you want to edit them, you can by just enabling it in the config, and changing the values.
- **Multiplayer-focused Hexes:** several Hexes now penalise their holder instead of the whole team.
- **Survivors and allies:** revised Laser Cyclone return and recharge behaviour, shorter Captain beacon lifetimes, more active survivor clones and void-fog immunity for allied drones.
- **Holdout events:** pillars and Deep Void Signals gain charge from kills and now spawn stronger enemies the more you activate. Teleporter charging after the boss is defeated now defaults to 1.5 times normal speed and no longer effects the soul-binding trial (overcharging teleporter event with hallowed concepts).
- **Fixed several bugs: 
	-Railgunner’s scoped secondary: corrected stock handling that could give it around 100,000–1,000,000 stocks instead of the intended behaviour.
	-Added Common cauldrons: fixed subsequent purchases getting stuck after the first use, accepting items without producing a reward. Applies to the Bazaar and Void Locus versions.
	-Shield/health recalculation: corrected maximum health fluctuating after shields broke and began regenerating when holding % based HP increase items.
	-Extra pillars and Deep Void Signals: corrected enemies no longer spawning when activating encounters beyond the required count.
	-Tenebrous Quagmire’s Hallowing Shrine: corrected spawning an individual hand without its shrine base, which caused repeated exceptions when used.
	-Teleporter charging: prevented post-boss charging multiplier from incorrectly affecting the Soul-binding ritual and special overcharge phase.
	-Optimised Startup: Fixed really inefficient implementations for item and newt changes, the mod should now initilise much faster (50+ seconds down to ~10 after change in my tests)

## Disclaimers

I am only one person and this mod changes quite alot of features, items and other things - most of the changes I have confirmed working and I have done several runs with the mod so everything is atleast mostly functional. I cannot guarentee that nothing is broken however since theire is obviously 1000s of edge cases with the way items stack or how the game spawns cirtain things run to run. If you find an issue, please let me know on the Github page by reporting and issue - the log and relevant config would also be ideal to have, as well as any items or other things that might be effecting the run for added context in my recreation and debugging atempts.

### AI Usage Note
I just want to be forthcoming that I did use some AI in the creation of this mod. I know most people have a strong opinion about the use of Generative AI and I respect people right to either support or not a creator who has used AI.

AI was NOT used for ideas, planning, writing text descriptions for anything, or generating art and assets.

AI was ONLY used for the writing of code and debugging by reading error logs and identifying problems with the code I wrote. All code that that the AI produced was read fully and understood by me before actual implementation - nothing was blindly copy and pasted.

I'm not the most knowledgeable about coding, ontop of the reasorces online that I could find for this game where fairly limited - I've tried my best to code myself and only rely on AI to help with potentual methods for implementing features, but AI has written some of the code for this mod that I have explisitally tested, and tried to understand.

If you do not want to use this mod because of this fact, then I fully respect your decision and have no issues with it.


## Main features

- **Consistent stage features:** supported maps gain useful interactables or rewards you can plan around, including Distant Roost's Boss Printer, Titanic Plains' Uncommon Printer, Siphoned Forest's Legendary Printer and the Iron stages' Command Chests. Spawn chances and placement are configurable.
- **Survivor options:** configurable stats and skill values such as damage, cooldowns, stocks, durations and projectile behaviour. Several underperforming skills receive buffs or alternative behaviour.
- **Item choices:** rebalanced stacking, optional reworks, regenerating consumed items, broader uses for Regenerating Scrap and elite aspect crafting at the Wandering Chef. Many changes aim to improve multiplayer usefulness or reduce lag and late-game map-clearing proc chains.
- **Defensive mechanics:** configurable one-shot protection, shield and barrier gating, shield recharge, healing calculations and barrier decay.
- **Bazaar and routes:** extra interactables, cauldrons, Lunar shop options and Newt settings. It is also now possible to go from the hallowed concepts secret boss into false son (then into alloyed collective).
- **Quality of life:** Lunar Coin sharing, out-of-bounds pickup recovery, shrine options, multiplayer drone interaction and faster Recycler use while the run timer is paused.
- **Artifact of the Fleeting Moon:** makes Lunar Coins a per-run currency without affecting the permanent coins in your save.

**See the [mod wiki](https://thunderstore.io/c/riskofrain2/p/3Questionmarks/3QsVanillaTweaks/wiki/) for individual changes and default values.**

## Optional integrations

- **LookingGlass:** modified item and skill statistics, including supported live values.
- **Moffein's Heretic:** shared skill support and optional stat overrides.
- **RegisterCommandChest:** distinct Command Chests on the Iron stages.
- **LoopVariantConfig:** takes precedence over overlapping stage-variant changes.
- **ZetAspects:** supported aspect crafting; aspect equipment settings yield to its configuration.

These integrations do not make their respective mods mandatory. Multiplayer and compatibility testing remain ongoing; use compatible mod lists and matching versions when playing together.

## Updating

Settings are generated under `BepInEx/config/` in feature-specific files. Survivor stats have their own file, and items are grouped by rarity.

Existing configuration values will be preserved though updates, so if I have changed something, you might want to delete the files to regenerate them with default values (or change them youselves). The debug option to regenerate configuration removes obsolete entries that I have removed between updates, while preserving your configured values.

## Feedback

Bug reports and balance suggestions are welcome. Include your BepInEx log, mod version, relevant settings and whether you were playing alone, hosting or joining another player.
