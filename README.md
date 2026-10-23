# 3QsVanillaTweaks

A configurable collection of tweaks and quality-of-life improvements for **Risk of Rain 2**. Rebalance vanilla items, expand the Bazaar Between Time, and reduce some of the friction of multiplayer without overhauling the core game.

**Current version: 0.1.0**

Most features have their own settings, so you can use the changes you like and disable the rest, and even configure many to your preferances. Gameplay settings are follow the host's configuration in multiplayer.

## Features

### Items

The tables below compare **vanilla behaviour** with the mod's **default settings**.

#### Uncommon (Green)

| Item | Vanilla | Modded (default) |
| --- | --- | --- |
| **Regenerating Scrap** | Can be used as a substitute for an Uncommon item in Uncommon 3D Printers. | Can be used in **Common and Uncommon 3D Printers**. |

#### Legendary (Red)

| Item | Vanilla | Modded (default) |
| --- | --- | --- |
| **Dio's Best Friend** | Revives you once, then remains consumed. | **Regenerates at the start of the next stage** after being consumed, allowing another revive. |
| **Laser Scope** | Increases critical strike damage by **100% per stack**. | Additionally grants **+10% critical strike chance** from the first stack. Critical damage remains unchanged. |

#### Lunar (Blue)

| Item | Vanilla | Modded (default) |
| --- | --- | --- |
| **Egocentrism** | Converts a random eligible item into another Egocentrism every **60 seconds**. | Converts an item every **120 seconds** (**1 second faster per additional stack**, minimum **10 seconds**). Prioritises **White Scrap → Green Scrap → Yellow Scrap → Red Scrap → Lunar items → other eligible items**. Void items are excluded by default, and assimilation pauses while the run stopwatch is paused. |
| **Gesture of the Drowned** | Automatically activates ready equipment, including in the Bazaar; reduces equipment cooldowns. | **Does not automatically activate equipment in the Bazaar Between Time or Computational Exchange**. Equipment still recharges normally and can be used manually. Its normal cooldown reduction is unchanged. |

#### Void (All Void Tiers)

| Item | Vanilla | Modded (default) |
| --- | --- | --- |
| **Lost Seer's Lenses** | **0.5% chance per stack** to instantly kill eligible non-boss enemies on hit. | **1% chance per stack** to trigger a **Void Crit dealing 1000% of the hit's damage**. **Bosses are affected**. Void Crits **benefit from critical damage multipliers, trigger on-crit effects, and are affected by luck** (all configurable, enabled by default). |
| **Pluripotent Larva** | Revives you once, corrupts eligible items, then remains consumed. | **Regenerates at the start of the next stage** after being consumed, allowing another revive. |

Item descriptions reflect configured values where supported. Optional **LookingGlass** compatibility is included.

### Bazaar Between Time

Make the Bazaar a more varied (but a bit stronger) stop between stages with independently configurable options:

- **Equipment cooldowns:** Cap equipment recharge at **1 second by default** in the Bazaar and, optionally, **Computational Exchange**. Particularly useful for Recycler so other players dont have to wait several minutes for someone to finish re-rolling a bunch of items.
- **Lunar shop:** Refresh purchased lunar items, configure reroll pricing and limit rerolls after a configurable number of purchases.
- **Newt:** Configure his health, regeneration and armour, prevent him from kicking players out, and adjust Bazaar karma behaviour accordinly (Karma gain is on kill if he is set to never kick you out). This change is aimed at making Newt a dps dummy so you can check your build against a target mid run.
- **Cauldrons:** Add an optional Uncommon-to-Common cauldron (50% spawn chance by default); allow the Legendary cauldron to accept one Legendary item as an alternative to five Uncommon items.
- **Extra interactables:** Configure additional 3D Printers, Scrappers, Shrines of Order, Cleansing Pools, Void Cradles, Lunar Buds and lockboxes, Each has their own spawn chances that are rolled each time you visit the bazaar.

### Stages and artefacts

- **Newt Altars:** Configurable altar spawning and stage-specific location support. By default every stage has 1 location that always spawns a newt altar
- **Artifact of Command:** If a Command drop has just one available choice, receive that pickup directly instead of opening a cube.
- **Artifact of the Fleeting Moon:** An optional custom artefact built around turning Lunar Coins into a per run currency. Has configurable starting coins, drop-rate adjustments and conversion to permanent coins. Permanent lunar coins are not overriden by them, so you can switch between playing with the artifact and not. 
- **Lunar Coins:** Options for coin drop rate and sharing collected Lunar Coins between players.

## Configuration and multiplayer

Settings are available through the mod's **BepInEx configuration**.

Gameplay settings are host-synchronised so clients use the host's configured values where supported. This release has been developed with multiplayer in mind, but not every combination of settings and other mods has been tested.

**Optional compatibility:** LookingGlass provides expanded item statistics. The mod does not require LookingGlass for its core item changes.

## Feedback

Bug reports, compatibility issues and balance suggestions are welcome. When reporting a problem, please include your BepInEx log, mod version and the relevant configuration settings.
