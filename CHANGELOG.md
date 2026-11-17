# Changelog

## [0.5.2]

### New
- **Enemy difficulty**
  - Time scaling multiplier: difficulty rises 1.2× faster over time (configurable).
  - Enemy health and damage bonuses, applied as vanilla hidden items (10% steps):
    - Regular enemies: +30% health, +10% damage
    - Bosses (champions): +40% health, +10% damage
    - Major bosses: +60% health, +10% damage. Covers Mithrix, Voidling, False Son, Solus Wing, Solus Heart, Twisted Scavengers, DLC 4 Secret Boss and the Anima Harvester. The list is configurable by body name.
- **Enemy gold scaling**
  - Enemy kill gold is locked to the stage's entry difficulty, rather than scaling with time.
  - Kill gold falls behind chest prices as difficulty rises.
  - Kill gold is offset against the Enemy Credits multiplier
  - Overall Gold Multiplier, 0.85 by default.
  - Item, barrel and shrine gold are unaffected, so gold items, and these interactables are relatively stronger.
- **Holdout turrets** Gunner Turrets teleport around the centre of a holdout zone when it starts.
- **Bazaar locked contents** (Unlock Mode)
  - Locked Lunar Shop slots are empty and roll an item when unlocked, rather than displaying an item always.
  - Locked printers, cauldrons and Hallowed Pools show a hidden "?" item until unlocked.
  - Locked Lunar Seers don't show their stage until unlocked.

### Changes
- Artifact of the Fleeting Moon: the boss coin reward is given once per boss encounter, not once per boss killed.
- Warbanner: the final-boss banner trigger now also covers Solus Heart and the Anima Harvester.

### Fixes
- Captain: Charged Shotgun has a blue icon again, to tell it apart from the primary.
- Bazaar Seers: using any Lunar Seer now closes the others, including the extra third seer.
- Bazaar Wandering Chef: ingredients sometimes couldn't be added. This was a conflict with LookingGlass's menu sorting.
- Green portals in the Bazaar (and other hidden realms) went to the wrong stage tier, such as Prime Meridian at stage 3, which put later stage numbers out of step.
- Enemies with "No Healing On Level Up" could spawn with a fraction of their health if there stats where calculated after they had spawned (for example solus wing).
- Guaranteed Legendary Chests: Sale Star reopens with RetroactiveMacro gave one item per player instead of one.
- Warbanner: the final-boss trigger never fired for DLC4 Sectet Boss or Voidling.

### Performance and multiplayer
- Config sync rewritten:
  - The host sends only non-default settings, once per request.
  - Clients no longer re-request while a transfer is in progress.
  - Setting changes are batched.
  - Clients no longer re-parse settings on every read.
- Less per-hit and per-tick overhead:
  - Cached reflection and compiled stat accessors (stat helpers, Barrier Mechanics).
  - Crowbar no longer allocates on every hit.
  - Squid Polyp no longer touches turret inventories every tick.
  - Loader no longer searches the whole scene every tick.
  - Collector's Compulsion no longer builds a stack trace on every item drop.

## [0.5.1]

### Fixes
- Fixed missing Dependancies

## [0.5.0] - Remote Operation, Bazaar Unlocks and Stage Expansion

### Added

- **Remote Operation** adds configurable multiplayer loadouts for the Gunner, Healing, Junk, Emergency, Incinerator, Cleanup, Missile, Jailer, Barrier, Bombardment and Freeze Drones, plus the TC-280 Prototype. Each has its own stats and skill settings.
- **Player-controlled drones** can interact with everything on the stage that a normal player would by default. Drone spawns are unlimited per stage, with a 180-second Disconnect cooldown that also applies after drone death. You can manually disconnect from the drone to change drones, or respawn if you have a revive item. Teleporter completion and supported hidden-realm boss defeats return drone players to their original survivors at the drone's location. Allowing drones to charge the Teleporter is optional and off by default. Drone purchase costs are configurable, but generally increased to acount for them being stronger.
- **Bazaar unlock mode** This mode adds some progression to the shop, this default to be a meta progression system that resets when you win a run, but can be configured to reset on every run, last unitil you reset the game, or be a perminant meta progression system. The bazaar now spawns a bunch of interactables that are locked with crystal locks. You can spend Lunar Coins to break these crystal locks on the interactables, enabling thier use. By default this mode is only advailable if you have the artifact of fleeting moon enabled for the run.
- **Bazaar progression** has settings for which interactables spawn, whether they start locked and how many coins they cost to unlock. Unlocks go to everyone currently in the game by default, with host-only and buyer-only options. Progress that carries across runs follows the host. Unlocks reset on a victory or Fate Unknown by default, with alternatives for every visit, every run, every launch or permanent unlocks. Launch and permanent progression can be scoped by survivor or game mode. Game mode separation is on by default (although not really usable since eclipse doesn't allow artifacts to access it).
- **The Bazaar** gains Hallowed Pools, Shrines of the Woods, the Wandering Chef and more copies of existing extras, including printers, cauldrons, lockboxes, pods and seers. Both modes use the same placement settings, although unlock mode can override. In normal mode the second Hallowed Pool and third Lunar Seer each default to a 50% spawn chance, with most of the added duplicate slots off by default. In unlock mode the third lockbox is always a Void Lockbox and the first two are regular lockboxes.
- **Hallowed Pools** cost 1 Hex by default and banish the displayed item from the run's loot pools. A banished item will not appear for the rest of the run.
- **Bazaar Lunar Coin use fees** can be configured independently of unlock prices. The Cleansing Pool defaults to 1 Lunar Coin alongside its usual item or equipment payment. Lunar Seers default to 1 coin per use and Shrines of the Woods also use 1 coin in the Bazaar. Other new surcharges default to zero.
- **Newt** speaks in chat when players arrive, buy a Lunar shop item or Lunar Pod, unlock an interactable, use an exit portal or kill him. His dialogue is on by default and has a single toggle.
- **Fleeting Moon objectives** award 3 temporary Lunar Coins for defeating a boss, 2 on stage completion if all items were collected and 5 per Karma gained. These rewards do not contribute to permanent coin earnings. The optional team wallet is on by default and deducts purchases from every player while the artifact is active.
- **The Shrine of the Woods** retains its healing zone and changes a skill in the slot indicated by the shrine's colour. Special cases such as Captain's two beacons and MUL-T's second primary have their own settings.
- **Teleporter rewards** in multiplayer come in individual pickups with identical contents for each player. Players can claim every item in their pickup. Boss-item replacement defaults to 15% per reward. Horde of Many rewards have a 25% chance per reward to become a bundle of 3 Common items. Drifter's Tinker cannot reroll these pickups by default.
- **Barrels and Stalks** gain configurable gold, experience and scaling. Cloaked chests briefly glimmer every 4 seconds by default, with configurable drop weights that default to 80% Uncommon and 20% Legendary.
- **Director settings** cover interactable costs and limits, per-stage loot budgets, starting and recurring enemy credits, and monster spawn limits. Regular stage directors default to 60 living monsters and 8 spawns per wave. Starting and recurring enemy credits default to 1.2×. Teleporter boss selection gives other eligible bosses more weight as the run progresses. Ambient enemy levels can reach 9,999, and enemies no longer heal on level-up by default. Player levels default to a cap of 100 using the original total experience budget.
- **The enemy item blacklist** is configurable and applies to enemy item rolls such as Evolution, Void Fields and Simulacrum.
- **Modded stage support** adds configurable features and interactable credits for 13 maps and variants. Supported maps keep their native features such as Desolate Coast's eggs and fixed Stage 4 legendary chests.
  - **Stage 1** includes Desolate Coast, which has a confirmed Newt Altar and 300 base interactable credits. First-stage Halcyon Shrines and Concept Reliquaries can spawn on modded stages with the relevant expansion enabled.
  - **Stage 2** gains a guaranteed Command Chest on Broadcast Perch and Wetland Aspect's skeleton encounter and Hex drops on Wetland Downpour.
  - **Stage 3** uses the Rallypoint Delta rules for Fogbound Lagoon's existing timed chest. Forlorn Wreckage gains three possible timed chest locations using those same rules.
  - **Stage 4** supports fixed legendary chests on Sunset Tropics, Midnight Tropics, Stagnant Pipeworks, Sleeping Hollows and Sunken Tombs. Stagnant Pipeworks and Sleeping Hollows share selection weight and can appear before or after looping by default. The day and night variants use matching interactable credits (as it is intended for you to configure them to be able to appear before an after looping)
  - **Stage 5** gets fixed Scrappers on Ancient Observatory, Hollow Summit and Frozen Summit. Hollow Summit and Frozen Summit use matching interactable credits.
- **Bulwark's Ambry** guarantees an Artifact Key after 25 eligible enemy kills without one by default. A random key drop resets the counter.
- **The debug placement editor** opens on F6 and saves positions and rotations for registered interactables, stage features and supported pickups. Also added a Remote Operation test menu on F7, a Fleeting Moon coin grant on F9, extra-player simulation, test loadout presets, skill projectile and sound logging, and expanded stage selection and altar diagnostics. These require the debug master switch, which is off by default.

### Changed

- **Stage selection** now avoids repeating a stage family for the host's entire play session by default, including earlier stages in the current run, while another eligible destination exists. Alternate layouts and day/night variants count as one family. The previous-run setting is still available.
- **Confirmed Newt Altars** on supported modded stages can spawn alongside random native locations. Viscous Falls now defaults to location A.
- **Guaranteed Stage 4 legendary chests** are recognised more reliably, including on supported modded maps. The guaranteed chest drops an item per player by default.
- **Halcyon Shrines and the special Gilded Coast route** only guarantee a green portal while False Son is undefeated in the current run.
- **The Bazaar layout** uses updated default placements for the added interactables.
- **Commando's Combat Roll** restocks secondary and special skills by default, with separate settings for each. Other abilities can be used while cooking Frag Grenades.
- **MUL-T's Nailgun** spins up over 5 seconds of sustained fire by default, reaching 150% fire rate and 50% spread. Spin decays after releasing the trigger. The crosshair no longer expands while using the Nailgun with spin-up enabled.
- **Mercenary's Focused Assault and Eviscerate** +2 seconds of base cooldown. Eviscerate can reset its own cooldown once per cast when it kills an enemy. Improved kill tracking for Focused Assault and Mercenary's jump refresh.
- **Default Survivor Stats** Acrid and Huntress default to 8 m/s movement speed, with higher acceleration for Huntress. Bandit gains 5% base crit. Artificer starts with 55 health and 55 shield. Seeker defaults to 110 health and 0 base armour.
- **Skill Icons** Icons added by this mod have been updated. 
- **Purity and Benthic Bloom** swap their positive effects by default. Purity upgrades items and keeps its bad luck. Benthic Bloom gains flat cooldown reduction and still corrupts 57 Leaf Clover. The upgrade effect defaults to 5 selections plus 3 per extra stack, prioritises scrap, prefers the same item category and upgrades one copy per selection, instead of the entire stack. The effect swap and upgrade rules are configurable.
- **Hex of Loss** removes permanent items when the Teleporter starts charging. It prioritises lower-tier scrap and shows which item was lost. Rain, Strife and Greed use diminishing stacking penalties.
- **Delicate Watch rework** ignores passive damage-over-time ticks such as bleed and burn and also ignores void fog by default. Fall damage is ignored by a separate, default-on setting.
- **Squid Polyp** periodically summons a Squid Turret near the centre of an actively charging holdout zone. The default interval is 15 seconds per holder.
- **Executive Card** keeps unbought multishop terminals open for everyone while a player holds it. Gaining a card can reopen previously closed terminals. Cashback is configurable and defaults to 10%. RetroactiveMacro takes over retroactive reopening when installed.
- **Sale Star** supports multiple chest uses per stage, including reopening already opened chests with RetroactiveMacro's Sale Star option. Each successful reopen spends one use, and the item tooltip reflects the active behaviour.
- **Optional integrations** add QoL Chests glow range and blindness settings, expand LookingGlass support and coordinate the paused-timer Recycler cooldown with ReduceRecycler.

### Fixed

- **Resonance Disc** Now correctly uncharges after shooting instead of remaining primied
- **Newt** can receive on-hit effects such as bleed. Forgive Me Please can trigger on-kill effects such as Gasoline and Chronic Expansion while scenery is still filtered out.
- **Kinetic Dampener** preserves its native shield while applying configured bonuses.
- **Chance Doll** bonus drops launch clear of the shrine.
- **The Logbook** handles entries with missing lore and hides the released form of Neutronium Weight. Restored Substandard Duplicator's temporary-copy text and Executive Card's full coloured description. LookingGlass shows Safer Spaces' total cooldown and updated Corpsebloom, Purity and Benthic Bloom details.
- **Newt Altar discovery, and fire damage ** received fixes for startup and stage-loading errors. 

### Notes

- Existing settings generally keep their saved values. Reset the entries you want to adopt from this release, including the preferred altar for Viscous Falls and the boss-item replacement chance if you already changed it.
- The Bazaar placement editor saves overrides separately from the regular Bazaar configuration. A saved override takes precedence over the new built-in position in either mode.
- Multiplayer and optional mod compatibility testing remain ongoing. Please include a BepInEx log and relevant settings when reporting an issue.

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
