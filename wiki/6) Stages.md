# General

- Variant Availability: Makes Verdant Falls, Viscous Falls, Shattered Abodes Disturbed Impact, Treeborn Colony, Golden Dieback, Iron Alluvium and Iron Auroras available both before and after looping. Yields to Wolfo's LoopVariantConfig when installed.
- Delays selected enemy and Void Seed spawn cards on Viscous Falls and Disturbed Impact so they cannot spawn on the first stage. On the first stage, Disturbed Impact receives 280 player-scaled interactable credits minus 10 flat, accounting for its guaranteed Large Chest. Yields to LoopVariantConfig when installed.
- Guarantees a Halcyon Shrine on first-stage maps.
- Guarantees a Concept Reliquary on eligible first-stage maps.
- Uses Primordial Teleporters on normal stages from stage 6 onwards, allowing a choice between the Moon and continuing to loop.
- Avoids stage families visited in the current play session. Falls back to the available stages if every eligible destination was visited. 
- Guarantees a selected Newt Altar location on supported stages. If the selected location replaces a normally permanent altar, that extra altar has a 25% chance to remain.
- Guaranteed Legendary Chests award an item for each player.

# Distant Roost

- Adds a guaranteed Boss 3D Printer to both layouts.

# Shattered Abodes/Disturbed Impact

- Adds a guaranteed Aurelionite Fragment.
- Fragment level odds are 60% level 1, 30% level 2 and 10% level 3.

# Siphoned Forest

- Adds a guaranteed Legendary 3D Printer.

# Titanic Plains

- Adds a guaranteed Uncommon 3D Printer to both layouts.

# Verdant Falls/Viscous Falls

- Adds a guaranteed Shrine of Compulsion.

# Abandoned Aqueduct

- Spawns 4 Ancient Gate buttons and requires 3 to open the gate.
- Pressed buttons stay activated.

# Aphelian Sanctuary

- Adds a guaranteed Cleansing Pool and a free Lunar Bud.

# Pretenders Precipice

- Adds a guaranteed Drone Scrapper and Drone Combiner.

# Reformed Altar

- After finishing the chef unlock, the Wok offers a reward with 3 food choices.

# Wetland Aspect

- Keeps N'kuhana's altar skeleton available.
- Destroying it drops 3 random hexes per participating player.

# Damp Caverns

- Adds a guaranteed backpack containing 10 fungi.
- Each item has a 3.33% chance to be Mycelium Cap, 5% Weeping Fungus and 2% Quick Fix; otherwise it is Bustling Fungus.

# Treeborn Colony/Golden Dieback

- Adds up to 3 cloaked chests at fixed locations.
- Their independent spawn chances are 100%, 60% and 30%.

# Iron Alluvium

- Adds a guaranteed Command Chest to Iron Alluvium and Iron Auroras.
- This drops a guaranteed command choice item. The reward rarity is 30% Common, 60% Uncommon or 10% Legendary.
- Costs three times a Large Chest: a base cost of $150 before difficulty scaling.
- Uses RegisterCommandChest's distinct appearance when that optional mod is installed.

# Rallypoint Delta

- After completing [REDACTED], the timed chest uses a 60-second timer starting on stage arrival and awards an Uncommon item instead of the Preon Accumulator.
- Has a 60% chance to add a second timed chest, then a 30% chance to add a third if the second spawned.

# Scorched Acres

- Adds a guaranteed Altar of Gold.
- Purchasing it also awards a level 3 Aurelionite Fragment.

# Sulfur Pools

- Adds 3 guaranteed lockboxes, each with a 15% chance to become a Void Lockbox.
- Adds a guaranteed Rusted Key printer.

# Tenebrous Quagmire

- Adds a guaranteed Hallowed Shrine.

# Helminth Hatchery

- Adds a guaranteed Scrapper.

# Sky Meadow

- Adds a guaranteed Scrapper.

# Commencement 

- Spawns 4–8 pillars, with one of each type required before the jump pads unlock.
- Completed pillars award items, and unused pillars remain available after unlocking the jump pads.
- Reward odds are 50% Common, 20% Uncommon, 5% Legendary, 15% Lunar and 10% Pearl. Pearl rewards have a 20% chance to become Irradiant Pearls.
- Pillars charge at full speed with one player inside. Mass uses a 30-metre radius and 60-second charge; Design and Soul use 20 metres and 30 seconds; Blood uses 20 metres and 10 seconds.
- Stops ambient enemy spawns after unlocking the jump pads. Activating an extra pillar starts its own enemy encounter; Mithrix's fight encounters remain available.
- Spawns 6–12 cauldrons, including at least 3 Uncommon, 2 Legendary and 1 Common.
- The Glass Frog's portal leads to Void Locus.
- Each enemy kill adds **1%** pillar charge.
- Each subsequent pillar gains another **25%** of the first encounter's enemy-spawning credits. Each pillar beyond the required count adds **50%** instead.

# Void Fields

- Reduces the encounter to 5 cells, rewarding 2 Common, 2 Uncommon and 1 Legendary item per player.
- Enemies gain 5 Common item stacks in Common rounds, 3 Uncommon stacks in Uncommon rounds and 1 Legendary stack in the final round. New enemies are added every round.
- Activating a cell restores 25% maximum health. Each team kill adds 1% charge.
- Pauses the run timer and allows loot to spawn with half the normal loot credits.
- Cells charge at full speed with one player inside.

# Void Locus

- Spawns 4–8 signals and requires 4 to open the portal.
- Signals charge at full speed with one player inside, using a 30-metre radius and 60-second charge.
- Each completed signal awards a Void Potential per player.
- Unused signals remain available after the portal opens. Enemy spawning stops between encounters, but restarts when an extra signal is activated.
- Each choice rolls 40% Common, 30% Uncommon, 5% Legendary, 10% Void Common, 10% Void Uncommon or 5% Void Legendary.
- Adds a Void shop with pools trading 2 Uncommon items for a Void Common, 3 Common items for a Void Uncommon, or 5 Uncommon items for a Void Legendary.
- The Void Legendary pool also accepts one Legendary item as an alternative.
- Each enemy kill adds **1%** signal charge.
- Each subsequent signal gains another **25%** of the first encounter's enemy-spawning credits. Each signal beyond the required count adds **50%** instead.

# (Secret Hallowed Concepts Realm)

- Spawns a Golden Geode puzzle 30 seconds after the secret boss of Hallowed Concepts is defeated.
- Destroying the final geode opens a gold portal to Gilded Coast. Defeating Aurelionite there offers a green portal to Prime Meridian, or the full route if enabled in the green portal config.

# Eclipse

- Allows Void Locus and Planetarium portal routes in Eclipse.

# Directors and Stage Budgets

- Regular stages use 1.2 times the starting enemy credits by default. Recurring combat director income also defaults to 1.2 times vanilla. The recurring multiplier does not increase scripted or shrine encounter budgets.
- The regular stage director allows up to 60 living monsters (vanilla 40) and attempts up to 8 spawns per wave (vanilla 6). Scripted spawns can exceed the living monster limit.
- Each distinct Teleporter boss spawned on a stage increases the selection weight of other eligible bosses by 1 on future stages. Map restrictions still apply.
- Interactable budgets are configurable per supported stage. Base values are for one player, with 50% added per additional player. Distant Roost and Titanic Plains use 250 credits (vanilla 220), Abandoned Aqueduct uses 280 (vanilla 220), and Sky Meadow uses 550 (vanilla 520).

| Regular-stage interactable | Default director cost | Vanilla cost |
| --- | ---: | ---: |
| Lunar Pod | 20 | 25 |
| Void Seed | 30 | 50 |
| Void Potential | 30 | 40 |
| Cloaked Chest | 5 | 10 |
| Shrine of the Woods | 20 | 15 |
| Stalk | 5 | 15 |
| Common Printer | 10 | 5 |
| Legendary Printer | 10 | 15 |
| Gunner Turret | 5 | 10 |
| Large Drone | 25 | 30 |
| TC-280 Prototype | 35 | 40 |

- Natural Shrine of the Woods and TC-280 Prototype spawns are each capped at one per regular stage by default. Their limits and costs are configurable separately.

# Modded Stages

- These settings apply when the relevant map is installed. Confirmed Newt Altar placements follow the configured altar spawn mode and can coexist with the map's native random altars.
- Broadcast Perch's Command Chest uses the same reward and price settings as the Iron stages. RegisterCommandChest supplies the optional distinct model.
- Fogbound Lagoon and Forlorn Wreckage follow Rallypoint Delta's chest timer and reward settings after completing [REDACTED].
- Stagnant Pipeworks and Sleeping Hollows each receive half the family's stage-selection weight and can appear before or after looping by default.

| Stage | Map | Added or supported feature | Base interactable credits |
| --- | --- | --- | ---: |
| 1 | Desolate Coast | Confirmed Newt Altar. Native eggs remain available. | 300 |
| 2 | Broadcast Perch | Guaranteed Command Chest | 280 |
| 2 | Wetland Downpour | Wetland Aspect's skeleton encounter and Hex reward | 280 |
| 3 | Fogbound Lagoon | Rallypoint Delta rules for the existing timed chest | 280 |
| 3 | Forlorn Wreckage | Up to 3 timed chests following Rallypoint Delta rules | 280 |
| 4 | Sunset Tropics | Fixed Legendary Chest rewards | 400 |
| 4 | Midnight Tropics | Fixed Legendary Chest rewards | 400 |
| 4 | Stagnant Pipeworks | Fixed Legendary Chest rewards | 400 |
| 4 | Sleeping Hollows | Fixed Legendary Chest rewards | 400 |
| 4 | Sunken Tombs | Fixed Legendary Chest rewards | 400 |
| 5 | Ancient Observatory | Guaranteed Scrapper | 570 |
| 5 | Hollow Summit | Guaranteed Scrapper | 550 |
| 5 | Frozen Summit | Guaranteed Scrapper | 550 |