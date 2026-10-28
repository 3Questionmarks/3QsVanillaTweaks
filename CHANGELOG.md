# Changelog

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
