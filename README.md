# 3QsVanillaTweaks

A configurable collection of balance changes and quality-of-life improvements for Risk of Rain 2. Give stages consistent features, make more survivor skills and items useful (and some small nerfs to others), and reduce some of the annoyances of multiplayer.

Basically everything has its own config toggles and settings. The defaults are intended to be balanced-ish but I'm not the best player so my opinions on what is good or bad/need changing is going to differ to other peoples. You can edit the setting to suit your idea of balance, or revert changes to vanilla. Gameplay settings follow the host's configuration (Hopefully).

**Current version: 0.5.0**

## What's new in 0.5.0?

- **Remote Operation** Gives Remote Operation drones configurable stats and skill sets. In general every drone now has 4 abilities that make them feel more interesting to play. They have had their HP and damage tuned down a bit but the extra skill more than make up for it. Additionally, you can now disconnect from the drone with a press of the Equipment button to return to spectating or another drone, this will also trigger revival items to allow you to play your survivor gain if you obtain one while a drone. Drone players can now interact with the world like any normal player would, although they still cannot charge teleporter zones. Drone players return to their survivors automatically when the Teleporter finishes charging or after a supported hidden-realm boss is defeated.
- **Bazaar Unlock Mode** This mode adds some progression to the shop, this default to be a meta progression system that resets when you win a run, but can be configured to reset on every run, last unitil you reset the game, or be a perminant meta progression system. The bazaar now spawns a bunch of interactables that are locked with crystal locks. You can spend Lunar Coins to break these crystal locks on the interactables, enabling thier use. By default this mode is only advailable if you have the artifact of fleeting moon enabled for the run.
- **Hallowed Pools, Wandering Chef and other new bazzar things** Adds new Hallowed Pools that let you prevent items from appearing for the run by spending Hexes. Also adds a Wandering chef to the bazaar that can cook you one specific meal with the items you are carring, and adds a Shrines of the Woods to change your skill loadout mid run. Newt now uses some prevoiusly unused dialogue (as well as some bonus ones) and reacts to actions you take in the bazaar. Also the Cleansing Pool in the bazaar now requires lunar coins to use.
- **Fleeting Moon** In multiplayer, each player now shares lunar coins with this artifact on, rather than having their own total. Defeating bosses, collecting every item on a stage and gaining Karma award extra coins.
- **Stage selection** Avoids repeats across the host's play session when possible, not just from the previous run. Also added support for 13 modded maps/variants - adding consistant features in the same manor to the vanilla maps, and tuning some of their interactable budgets to be more balanced with the changes made to the vanilla stages.
- **Teleport Rewards** In multiplayer, rather than the teleporter directly droping items for players, instead they will drop a single potential for each player that cointains all the items dropped for 1 player as to prevent large item drops causing lag.
- **Characters** Commando can use other skills while cooking grenades, and Combat Roll restocks secondary and special skills. MUL-T's Nailgun gains fire rate and accuracy as it spins up. 
- **Items** Purity and Benthic Bloom swap their positive effects by default. The Delicate Watch rework ignores passive damage and fall damage by default.
- **Gameplay fixes** restore Resonance Disc targeting and visuals and Forgive Me Please on-kill effects, as well as serval other bugs with items that I can't remember exactly but I know there was more. The update also fixes Logbook and tooltip issues and adds support for RetroactiveMacro, ReduceRecycler and QoL Chests.

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
- **Bazaar and routes:** extra interactables, cauldrons, Lunar shop options and Newt settings including Newt dialogue and optional unlock meta progression. It is also now possible to go from the hallowed concepts secret boss into false son (then into alloyed collective).
- **Multiplayer** gets player-controlled Remote Operation drones, individual Teleporter reward pickups, Lunar Coin sharing and holder-specific Hex penalties.
- **Quality of life:** Lunar Coin sharing, out-of-bounds pickup recovery, shrine options, multiplayer drone interaction and faster Recycler use while the run timer is paused.
- **Artifact of the Fleeting Moon:** makes Lunar Coins a per-run currency without affecting the permanent coins in your save.

**See the [mod wiki](https://thunderstore.io/c/riskofrain2/p/3Questionmarks/3QsVanillaTweaks/wiki/) for individual changes and default values.**

## Optional integrations

- **LookingGlass:** modified item and skill statistics, including supported live values.
- **Moffein's Heretic:** shared skill support and optional stat overrides.
- **RegisterCommandChest:** distinct Command Chests on the Iron stages.
- **LoopVariantConfig:** takes precedence over overlapping stage-variant changes.
- **ZetAspects:** supported aspect crafting; aspect equipment settings yield to its configuration.
- **RetroactiveMacro** handles reopening closed multishops and supports this mod's Sale Star use count when its chest-reopen option is active.
- **ReduceRecycler** works alongside the paused-timer Recycler cooldown.
- **QoL Chests** gains configurable highlight range and an option to hide glow while blinded.
- **RTAutoSprintEx** coordinates sprint behaviour for supported skills.
- **Modded stage support** adds optional features for maps by wormsworms, Winslow and JaceDaDorito.

These mods are completly optional, this is intended to use with these mod as it is what I am using, but it should also work fine without them. mandatory.

## Updating

Settings are generated under `BepInEx/config/` in feature-specific files. Survivor stats have their own file, and items are grouped by rarity.

Existing configuration values will be preserved though updates, so if I have changed something, you might want to delete the files to regenerate them with default values (or change them youselves). The debug option to regenerate configuration removes obsolete entries that I have removed between updates, while preserving your configured values.

## Feedback

Multiplayer and compatibility testing remain ongoing; use compatible mod lists and matching versions when playing together.
Bug reports and balance suggestions are welcome. Include your BepInEx log, mod version, relevant settings and whether you were playing alone, hosting or joining another player.
