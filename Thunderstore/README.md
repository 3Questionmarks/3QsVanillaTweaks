# 3QsVanillaTweaks

A configurable collection of tweaks and quality-of-life improvements for Risk of Rain 2. Rebalance vanilla items, expand the Bazaar Between Time, and reduce some of the friction of multiplayer without overhauling the core game.

Most features have their own switches and settings. Use the defaults as a balance preset, adjust individual values, or disable entire modules. Gameplay settings follow the host's configuration in multiplayer where supported.

**Current version: 0.3.0**

## What's new in 0.3.0?

- **Railgunner fixes:** Concussion Device preserves mine jumping without self-damage by default, uses Sweet Spot falloff and has a 0.1 proc coefficient. Backup Magazines grant Marksman 25% extra shot damage per stack instead of extra shots per second.
- **Defensive mechanics:** Configurable one-shot protection, shield and barrier gating, shield recharge, percentage-based healing and barrier decay options.
- **New routes:** A post-Hallowed Concepts secret boss Golden Geode puzzle opens a route through Gilded Coast. Green portals can appear from completed Halcyon Shrines on later stages, with an optional full Path of the Colossus requirement. Void Locus and Planetarium routes are available in Eclipse.
- **Multiplayer Drones:** Player-controlled drones are able to interact with interactables (such as buying chests). Now limited to 1 drone respawn per stage per player.
- **Items and equipment:** Added aspect crafting at the Wandering Chef, reduced Radar Scanner's cooldown and changed Stone Flux Pauldron downside to make it more pickable.

## Main features

- **Consistent stage features:** supported maps gain useful interactables or rewards you can plan around, such as Distant Roost's Boss Printer, Titanic Plains' Uncommon Printer, Siphoned Forest's Legendary Printer and the Iron stages' Command Chests. Spawn chances and placement are configurable.
- Configure survivor stats and applicable skill values, including damage, cooldowns, stocks, proc coefficients, durations and projectile behaviour. Individual modules also include behaviour fixes and clearer descriptions.
- Item changes include regenerating consumed revival items, broader uses for Regenerating Scrap, adjustments to Egocentrism and Lost Seer's Lenses, and elite aspect crafting. Many item changes aim to improve multiplayer usefulness or reduce lag and late-game map-clearing proc chains, inspired by Risky Mod.
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
- **ZetAspects:** Aspect crafting follows its item/equipment mode and preserves its Wake of Vultures combat balance.

These mods are optional. Multiplayer and compatibility testing are ongoing; use compatible mod lists and matching versions when playing together.

## Updating

Existing configuration values are preserved. New defaults do not overwrite saved values, including Railgunner's proc coefficient, falloff and Backup Magazine mode. To use the new Marksman behaviour in an existing config, set **Backup Mag Stacking** to `Damage Multiplier:25`.

## Feedback

Bug reports and balance suggestions are welcome. Please include your BepInEx log, mod version, relevant settings and whether you were playing alone, hosting or joining another player.
