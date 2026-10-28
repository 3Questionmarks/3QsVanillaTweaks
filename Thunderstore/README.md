# 3QsVanillaTweaks

A configurable collection of tweaks and quality-of-life improvements for Risk of Rain 2. Rebalance vanilla items, expand the Bazaar Between Time, and reduce some of the friction of multiplayer without overhauling the core game.

Most features have their own switches and settings. Use the defaults as a balance preset, adjust individual values, or disable entire modules. Gameplay settings follow the host's configuration in multiplayer where supported.

**Current version: 0.2.0**

## What's new in 0.2.0?

- **Survivor configuration:** Editable stats and skill settings for all 21 supported vanilla survivors, including the DLC survivors. Applicable skills have options for damage, proc coefficients, cooldowns, stocks, sprint behaviour, durations, projectile speeds and areas of effect.
- **More stage variety:** Optional stage variant mixing, previous-run stage avoidance, configurable fixed interactables and expanded special-stage options.
- **Command Chests:** Optional RegisterCommandChest integration, improved ground placement and configurable rarity weights on Iron Alluvium and Iron Auroras.
- **Clearer descriptions:** Updated skill descriptions, keyword tooltips and LookingGlass statistics for supported changes.
- **Organised configuration:** Separate files for the main feature groups, survivor stats and optional mod compatibility.

## Main features

- More maps have a useful feature you can plan around. By default, supported stages gain fixed interactables, guaranteed opportunities or additional rewards, with configurable spawn chances and placement. For example: **Distant Roost:** A Boss Printer on either layout. **Titanic Plains:** An Uncommon Printer on either layout. **Siphoned Forest:** A Legendary Printer. **Shattered Abodes and Disturbed Impact:** An Aurelionite Fragment. **Iron Alluvium and Iron Auroras:** A Command Chest with configurable rarity odds.
- Configure survivor stats and applicable skill values, including damage, cooldowns, stocks, proc coefficients, durations and projectile behaviour. Individual modules also include behaviour fixes and clearer descriptions.
- Item changes including regenerating consumed revival items, broader uses for Regenerating Scrap, adjustments to Egocentrism and Lost Seer's Lenses.
- The Bazaar has options for extra interactables, cauldrons, Lunar shop purchases and rerolls, equipment recharge and Newt's behaviour. Each feature can be adjusted independently.
- Options for customising shrines, Teleporters, the Wandering Chef, Lunar Coin drops and sharing, and out-of-bounds pickup recovery.
- Added the **Artifact of the Fleeting Moon**, an artifact that makes Lunar Coins a per-run currency. The artifact doesn't affect your save file's normal, permanent Lunar Coins.
- Settings are generated under `BepInEx/config/`, split into feature-specific files. Survivor stats have their own file. Gameplay settings follow the host.

**For individual changes, configuration options and default values, see the [mod wiki](https://thunderstore.io/c/riskofrain2/p/3Questionmarks/3QsVanillaTweaks/wiki/).**

## Optional integrations

- **LookingGlass:** Expanded item and skill statistics.
- **Moffein's Heretic:** Shared skill support and optional stat overrides.
- **RegisterCommandChest:** The distinct registered Command Chest prefab on the Iron stages.
- **LoopVariantConfig:** Our overlapping stage-variant changes automatically yield to Wolfo's mod while it is loaded.

These mods are optional. Multiplayer and compatibility testing are ongoing; use compatible mod lists and matching versions when playing together.

## Feedback

Bug reports and balance suggestions are welcome. Please include your BepInEx log, mod version, relevant settings and whether you were playing alone, hosting or joining another player.
