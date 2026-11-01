# Changelog

## [0.4.0] - Item Rebalance, Equipment Configuration and Multiplayer Hexes

### Added

- **Item and equipment configuration:** Added configuration for Boss, Common, Uncommon, Rare, Lunar, Void, Hallowed, Hex and Meal items, plus equipment and elite aspects.
- **Item reworks:** added default reworks for Delicate Watch, Chance Doll, Hunter's Harpoon, Leeching Seed, Lepton Daisy, Old Guillotine, Old War Stealthkit, Happiest Mask, Resonance Disc, Symbiotic Scorpion, Neutronium Weight and Vengeful Spirit. Configurable original modes remain available where supported.
- **Multiplayer Hexes:** Rain, Strife and Greed use holder-only penalties; Patience uses the holder's stacks and only harm them, and Haunting/Mimicry enemies target their holder. Loss removes permanent items at stage entry, prioritising scrap and protecting Lunar and important items by default.
- **Equipment passives:** Functional Coupler supports passive equipment effects in extra slots. Aspect settings yield to ZetAspects.
- **Holdout events:** pillar and Deep Void Signal kills grant 1% charge. Each subsequent encounter gains 25% of the initial enemy credit budget, with doubled increases beyond the required count.
- **Allies:** allied drones are immune to void fog. Goobo Jr. and Happiest Mask clones can seek enemies across the map so they hopefully wont just stand around doing nothing.
- **Recycler:** a 1-second cooldown while the run timer is paused, without triggering equipment-use effects in that mode.

### Changed

- **Item balance:** adjusted damage, stacking, healing, barrier and proc coefficients across all main rarities. Many direct-damage effects gain stronger values while producing fewer proc chains. Full defaults are listed in the wiki.
- **Alien Head:** moves to Uncommon and grants 15% cooldown reduction per stack.
- **Orphaned Core:** automatically cleanses every 30 seconds, reduced by 10% per extra stack. Item inheritance can be set to None, Movement or All; Movement remains the default.
- **Defiant Gouge:** defeated shrine enemies award items, with no reward after a failed Chance Shrine purchase. Summoned enemies drop no gold by default. 
- **Huntress — Laser Cyclone:** 4.5-second cooldown, half-speed return and no return-time limit. Each returned cyclone allows one stock to recharge, including with extra stocks.
- **Captain:** Healing and Hacking Beacons last 30 seconds; Resupply lasts 45 seconds. Hacking takes three times vanilla's duration. The lifetime timer continues during a hack, but the beacon finishes its current hack before expiring.
- **Teleporter:** post-boss charge speed defaults to 1.5 times normal rather than 2 times. Soul-binding ritual (overcharging) retain normal speed.
- **Shrine of Combat:** Combat Shrine enemies drop 50% of their normal gold to acount for the free item now given.
- **Shield and barrier gating:** hits exceeding three times the remaining shield or barrier bypass that layer's gate by default. Transcendence disables normal one-shot protection while shield gating is enabled.
- **Bazaar:** blocks gold generation to prevent gold and converted-experience farming.

### Fixed

- **Railgunner:** corrected the default scoped-secondary stock handling that could produce an excessively large stock count.
- **Common cauldrons:** corrected repeated purchases for the added 2-green-to-3-white cauldrons in the Bazaar and Void Locus.
- **Shields:** corrected maximum-health recalculation behaviour associated with shield breaking and regeneration.
- **Extra holdout events:** restored enemy spawning for pillars and Deep Void Signals activated after the required count, without disabling Mithrix's fight encounters.
- **Tenebrous Quagmire:** the added Hallowing Shrine now spawns its complete base and linked hands, preventing repeated errors when used.
- **Optimised Startup:** Fixed really inefficient implementations for item and newt changes, the mod should now initilise much faster (50+ seconds down to ~10 after change in my tests)

### Notes

- Existing saved settings are preserved. Reset the relevant values to adopt changed defaults.
- Multiplayer and compatibility testing remain ongoing.

## [0.3.0] - Railgunner Fixes, Defensive Mechanics and New Routes


### Fixed

- **Railgunner — Concussion Device:** Prevents self-damage by default while preserving launch force for mine jumping. Self-damage has its own toggle.
- **Engineer:** Improved Bubble Shield end-effect loading and guarded Carbonizer targeting and beam updates against missing references.

### Changed

- **Railgunner — Concussion Device:** Defaults to Sweet Spot area falloff and a 0.1 proc coefficient. 
- **Railgunner — Marksman:** Backup Magazines now add 25% shot damage per stack by default, replacing this mod's previous extra-shot-per-second behaviour.
- **Stone Flux Pauldron:** Defaults to changing its downside of 50 armour lost per stack instead of movement speed.
- **Radar Scanner:** Base cooldown reduced from 45 to 20 seconds; scan radius, reveal duration and pulse settings are configurable.
- **Aurelionite Fragments:** Removed forced Seekers of the Storm choices from shrine rewards and this mod's fixed fragments. Eligible Storm items remain in the normal pool.
- **Temporary items:** Default duration increased from 80 to 90 seconds.

### Added

- **One-shot protection:** Now should more reliably actually protect you from "one shots", 0.5-second immunity to follow-up hits and a 5-second cooldown after triggering.
- **Shields:** Shield-breaking damage no longer spills into health by default. Shields replenish over 3 seconds after a 7-second delay. Additionally, fall damage and percentage based healing now uses shield in their calculations. (Healing is still only recovers health)
- **Barrier:** Barrier-breaking damage no longer spills into shields or health by default. Maximum barrier uses pre-curse combined HP; optional decay formulas are available, with vanilla decay retained by default.
- **Freeze:** Can now execute bosses - Boss execution defaults to a 10% combined-health.
- **Multiplayer drones:** You can now interact with interactables by default, but you only have 1 drone respawn per player per stage by default.
- **Buff presentation:** Icons and descriptions for Bubble Shield support, Huntress Momentum, Captain support, Carbonizer slow and the one-shot protection cooldown.
- **Hallowed Concepts Secret Boss:** A Golden Geode puzzle appears 30 seconds after the boss' defeat. Completing the puzzle opens a gold portal to Gilded Coast, followed by a green portal after Aurelionite.
- **Green portals:** Completed Halcyon Shrines can always open a green portal, including on stages 4 and 5. Late portals lead to Prime Meridian by default. An optional full-route setting starts at Reformed Altar, continues through Treeborn Colony or its loop variant, then reaches Prime Meridian.
- **Eclipse void route:** Allows Void Locus and Planetarium portal routes during eclipse.
- **Aspect crafting:** Added configurable recipes all using Wake of Vultures at the Wandering Chef. Optional ZetAspects support.

### Notes

- Existing config values are preserved. To adopt the new Railgunner defaults in an existing config, set Concussion Device's **AOE Damage Falloff** to `SweetSpot`, **Proc Coefficient** to `0.1`, and Marksman's **Backup Mag Stacking** to `Damage Multiplier:25`. The will not update automatically. You can also just delete the Survivors.cfg file to regenerate it with default values.

## [0.2.0] - Survivor Configuration, Heresy Skills and Stage Expansion

### Added

- **Survivor configuration:** Added editable stats and applicable skill settings across all 21 supported vanilla survivors, including DLC survivors.
- **Survivor descriptions:** Added or updated descriptions and keyword tooltips to reflect supported configuration changes.
- **General survivor changes:** In general, several underperforming or uninteresting skills have been buffed to make them more useful. Commando, Huntress, and Rex in particular have received fairly large buffs to bring them more in line with the power of other characters, especially considering the survivor power creep with the DLCs.
- **Heresy items:** Added separate item-stacking and skill settings for Visions, Hooks, Strides and Essence of Heresy, all under their respective Lunar item sections. All changes to Heretic and the Heresy items are compatible with Heretic by Moffein.
- **Stage features:** Added configurable fixed interactables, stage rewards and placement options across supported stages, plus expanded options for Void Fields, Void Locus and Commencement. This generally follows vanilla examples such as Abandoned Aqueduct's buttons, which guarantee Kjaro's and Runald's Bands, and gives each stage a unique feature that rewards you with different items.
- **Stage selection:** Added Stage 1 and Stage 3 variant mixing and optional avoidance of stage families visited in the previous hosted run.
- **Stage 1 rebalance:** Added a toggle that changes both first-stage variants to be more in line with other first stages. First-stage Disturbed Impact uses 280 player-scaled interactable credits minus 10 flat to account for its position as a first stage and its guaranteed Large Chest. These changes are turned off in favour of *LoopVariantConfig by Wolfo* if that mod is installed (They make the same changes, while Wolfo’s mod offers more customisation and support for modded stage variants.)
- **Iron Command Chests:** Added configurable rarity weights for Iron Alluvium and Iron Auroras, defaulting to 30% Common, 60% Uncommon and 10% Legendary. Zero-weight rarities are excluded, and rarity odds are normalised independently of item-pool sizes. The chest now uses RegisterCommandChest to give it a distinct appearance.
- **Interactables:** Added settings for Shrines of Blood, Chance and Combat, Teleporters, Void Seed limits and some quality-of-life improvements for the Wandering Chef to save time in multiplayer.
- **Pickup recovery:** Added recovery options for supported pickups and droplets that fall out of bounds.


### Changed

- Split configuration into module-specific files, with a separate Survivor-Stats file and organised Mod Compatibility sections.
- Added a toggle to clarify many vanilla skill descriptions. When enabled, descriptions show damage and damage intervals to reflect the actual effects, rather than ambiguous damage-per-second figures. This also fixes many instances of vanilla descriptions reporting incorrect skill damage.
- Improved Command Chest ground placement by aligning the model's base rather than only its root pivot, while retaining the configured height offset.

### Fixed

- **Lunar Coins:** Updated the successful-drop multiplier patch to find the generated death handler. Corrected duplicated initial-chance scaling and preserved Fleeting Moon's multiplier.

### Notes

- The changes to skills are fully compatible with LookingGlass skill statistics.
- Existing config values are preserved. Changed defaults do not overwrite values already saved in your configuration. Added an option to the debug file to regenerate config - this removes obsolete entries while maintaining your existing settings.
- Optional integrations do not make their respective mods mandatory. When LoopVariantConfig is loaded, its configuration takes precedence over our overlapping features.
- Multiplayer and compatibility testing remain ongoing; please include logs and relevant settings when reporting an issue.

## [0.1.0] - Bazaar, Item Rebalances and Multiplayer Improvements

### Added

- **Lost Seer's Lenses:** Replaced the instant-kill mechanic with a configurable **Void Crit** (1% chance per stack and 1000% damage by default). Added optional damage scaling per additional stack, a boss-targeting setting (enabled by default), **critical damage multiplier, on-crit effect and luck toggles (all enabled by default)**, an on-hit effect, and updated descriptions.
- **Gesture of the Drowned:** Suppressed automatic equipment use in the Bazaar and Computational Exchange by default; manual use and normal cooldown progression remain available. Its cooldown reductions retain vanilla values by default.
- **Laser Scope:** Now grants **+10% critical strike chance** from the first stack by default, in addition to its unchanged critical damage bonus.
- **Egocentrism:** Changed item assimilation from **60 to 120 seconds** by default (**-1 second per additional stack**, minimum **10 seconds**); prioritises scrap and Lunar items, excludes Void items, and pauses assimilation when the run timer is paused. Bomb damage and spawn frequency retain their vanilla values by default.
- **Bazaar equipment cooldown:** Added an optional 1-second equipment cooldown cap, including support for Computational Exchange, to make equipment such as Recycler less time-wasting in multiplayer (real time, the game is paused in these realms).
- **Bazaar expansions:** Added configurable extra interactables, additional 3D Printers, a Common cauldron, an alternative Legendary cauldron payment, and Lunar shop reroll/refresh options.
- **Newt improvements:** Added configurable survivability and kick-out behaviour, plus Bazaar karma options.
- **Newt Altars:** Added configurable altar spawning and support for stage-specific placements.
- **Artifact of Command:** Automatically drops the item when its Command cube would have only one choice.
- **Artifact of the Fleeting Moon:** Added a custom artefact featuring temporary Lunar Coins and configurable coin rewards.
- **Lunar Coin options:** Added drop-rate and multiplayer sharing settings.
- **LookingGlass support:** Added compatibility for displaying modified item statistics.

### Changed

- Changes to items are fully compatible with the LookingGlass mod.
- Organised the expanding configuration into feature-specific sections, with host-synchronised gameplay settings for multiplayer.
- Updated item descriptions to reflect configurable effects.

### Notes

- Multiplayer and compatibility with other gameplay mods are still being tested.


## [0.0.1] - Initial Release

### Added

- **Regenerating Scrap**

  - Can now be used with additional 3D Printer rarities.
  - Added configurable support for Common, Uncommon, Legendary, and Boss Printers.

- **Dio's Best Friend**

  - Consumed copies regenerate at the beginning of each stage.

- **Pluripotent Larva**

  - Consumed copies regenerate at the beginning of each stage.

### Notes

- Multiplayer functionality has not yet been fully verified.
- Players should use matching configuration settings when playing together. (Probably going to make it use the host’s settings in the future, when I figure out how.)
