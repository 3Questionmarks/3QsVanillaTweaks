# Changelog

## [0.1.0] - Bazaar, Item Rebalances and Multiplayer Improvements

### Added

- **Lost Seer's Lenses:** Replaced the instant-kill mechanic with a configurable **Void Crit** (1% chance per stack and 1000% damage by default). Added optional damage scaling per additional stack, a boss-targeting setting (enabled by default), **critical damage multiplier, on-crit effect and luck toggles (all enabled by default)**, an on-hit effect, and updated descriptions and LookingGlass statistics.
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

- Organised the expanding configuration into feature-specific sections, with host-synchronised gameplay settings for multiplayer.
- Updated item descriptions to reflect configurable effects.

### Notes

- Multiplayer and compatibility with other gameplay mods are still being tested.
