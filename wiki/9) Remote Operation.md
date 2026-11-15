# Remote Operation

Remote Operation drones now are alot more mechanically interesting an all have a full 4 skills. Base stats have been reduced to compensate, and drone control prices have been increased.

## Shared rules

- You can use interactables, collect items and benefit from held items while controlling a drone.
- **Disconnect** (new equipment ability for remote operation) leaves the current drone and lets you select another. Its **180-second cooldown** is shared across drones and applies after drone death by default. If you have a revival item, Disconnect waits up to **3 seconds** to let it return you to your survivor before opening the selection menu.
- The Teleporter finishing its charge, including overcharge, returns drone players to their original survivors. The same applies to supported standalone boss encounters in hidden realms such as Gilded Coast and Soulbound Threshold. Revival places you on safe ground below your drone when possible and grants **3 seconds** of invulnerability.
- Drones can contribute to Teleporter charge if you enable the option in the Remote Operation config. It is off by default.
- The config exposes per-drone stats and skill values. The stat module removes the native **70% regeneration penalty** from player-controlled drones.

## Default drone stats

The stats columns show the base value and the gain per level. The price is the selection cost before difficulty scaling.

| Drone | Health | Health regeneration per second | Damage | Base price |
| --- | ---: | ---: | ---: | ---: |
| Gunner Drone | 85 (+25) | 1 (+0.2) | 7 (+1.6) | 0 |
| Healing Drone | 95 (+30) | 1.5 (+0.3) | 6 (+1.5) | 0 |
| Junk Drone | 200 (+60) | 1 (+0.2) | 9 (+1.8) | 50 |
| Emergency Drone | 200 (+60) | 1.5 (+0.3) | 10 (+2) | 100 |
| Incinerator Drone | 180 (+50) | 1 (+0.2) | 10 (+2) | 100 |
| Cleanup Drone | 180 (+50) | 1 (+0.2) | 10 (+2) | 100 |
| Missile Drone | 150 (+35) | 1.5 (+0.5) | 12 (+2) | 200 |
| Jailer Drone | 250 (+75) | 1 (+0.2) | 11 (+1.8) | 200 |
| Barrier Drone | 230 (+60) | 1 (+0.2) | 10 (+2) | 200 |
| Bombardment Drone | 220 (+70) | 1 (+0.2) | 10 (+2) | 300 |
| Freeze Drone | 220 (+70) | 1 (+0.2) | 10 (+2) | 300 |
| TC-280 Prototype | 600 (+180) | 2 (+0.4) | 14 (+2.8) | 500 |

## Gunner Drone

| Slot | Skill | Effect |
| --- | --- | --- |
| Primary | **Fusillade** | Fires **4 shots for 50% damage each**. The base interval is **0.15 seconds** between shots. Shot range defaults to **200 m**. |
| Secondary | **Charged Shot** | Hold to charge a piercing projectile from **200% to 600% damage** over **2 seconds**. Each direct hit stuns for **1 second** and causes a **2.5 m** blast for **50% of the direct hit's damage**. A fully charged shot can be held for **2 more seconds** before it fires automatically. **5-second cooldown**. |
| Utility | **Dash** | Dashes **8 m** in your movement direction over **0.2 seconds**. Has **2 stocks** with a **3-second recharge**. |
| Special | **Rapid Burst** | Fires **6 shots for 100% damage each** over **0.6 seconds** at base attack speed. Attack speed adds shots within the same burst duration. **10-second cooldown**. |

## Healing Drone

| Slot | Skill | Effect |
| --- | --- | --- |
| Primary | **Heal Beam** | Heals an ally within **60 m** for **50% damage per second** for **5 seconds**. **10-second cooldown**. |
| Secondary | **Charged Shot** | Charges over **2 seconds** and can be held fully charged for **2 more seconds**. The shot can bounce between up to **5 targets**, with **12 m** between bounces. It deals **100–400% damage** to enemies or heals allies for the same amount. **6-second cooldown**. A fully charged shot gives allies **+30% attack speed**, **+30% movement speed** and **30% damage reduction** for **8 seconds**. Enemies gain **Leech** for **8 seconds**, taking **10% damage every 0.2 seconds** and healing attackers for **10% of the health and shield damage** they deal to the target. |
| Utility | **Dash** | Dashes **8 m** in your movement direction over **0.2 seconds**. Has **2 stocks** with a **3-second recharge**. |
| Special | **Rescue** | Aim at an ally within **100 m**. After a **0.4-second** cast, teleport beside them and give both of you barrier equal to **50% of your respective combined maximum health**. Terrain does not block targeting. **25-second cooldown**. |

## Junk Drone

| Slot | Skill | Effect |
| --- | --- | --- |
| Primary | **Junk Cube** | Lobs an explosive cube for **600% damage** in an **8 m** radius. It explodes on impact or when its **3-second fuse** expires. Has **4 stocks** and a **5-second recharge per stock** before Rapid's attack speed scaling. |
| Secondary | **Salvage** | Converts an eligible item pickup within **60 m** into scrap of its rarity. **20-second cooldown**. |
| Utility | **Slip Away** | Cloaks you and grants **+50% movement speed** for **4 seconds**. Attacking leaves the cloak active by default. **10-second cooldown**. |
| Special | **Surprise!** | Drops up to **5 temporary items**. The number of drops scales with ambient difficulty. **120-second cooldown**. |

## Emergency Drone

| Slot | Skill | Effect |
| --- | --- | --- |
| Primary | **Healing Beams** | Attach healing beams to up to **8 allies** within **60 m**. Each beam heals for **50% damage per second** and stays attached until detached. Aim at an ally to attach a beam. |
| Secondary | **Siphoning Salvo** | Fires every available orb at a valid target. Each orb deals **250% damage** to enemies or heals allies for **250% damage**, with excess healing becoming barrier. On enemies, the orb applies **3 seconds of Slow** and inflicts **Leech** if the target was already slowed. Has **4 stocks** with **2-second recharge per orb**, shortened by attack speed. |
| Utility | **Healing Nova** | Charges over **2 seconds** at base attack speed. Releases a **25 m** pulse that heals allies for **200–800% damage** and pushes enemies away. Two pulses at **+2 and +4 seconds** heal for **50%** of the initial amount. **12-second cooldown**. |
| Special | **Overdrive** | Enhances all three other skills for **15 seconds**. The **15-second cooldown** starts when Overdrive ends. |

- **Siphoning Salvo's Leech** stacks for **3 seconds** per application. Each stack deals **10% damage every 0.2 seconds** and heals attackers for **10% of the health and shield damage** they deal to the target. Later orbs in the same volley can apply Leech after the first orb slows the target.
- **Healing Nova** grants recipients **+10–50% movement speed** and **+10–50 armour** for **6 seconds**, depending on charge. Follow-up pulses refresh the buff. Fully charged pulses also grant regeneration of **5% maximum health per second** for **3 seconds**.
- **Overdrive** boosts Healing Beams to **150% of normal healing** and turns overhealing into barrier. Siphoning Salvo recharges each orb in **1 second** before attack speed and can bounce to **2 more targets** per orb. It gives allies **+30% attack speed**, **+30% movement speed** and **30% damage reduction** for **3 seconds**. Enhanced Healing Nova damages enemies as well as healing allies and produces **4 follow-up pulses**, one each second.

## Incinerator Drone

| Slot | Skill | Effect |
| --- | --- | --- |
| Primary | **Flamethrower** | After a **0.6-second wind-up**, fires **5 hits for 25% damage each** per **0.5-second** fuel cycle at base attack speed. Each hit has a **50% chance to Ignite** and a **0.2 proc coefficient**. Range is **30 m**. Attack speed adds hits per cycle. All **40 fuel charges** refill after **8 seconds** without firing. |
| Secondary | **Fuel Glaze** | Fires **7 globs for 150% damage each**, applying **Weak** and **Oiled** for **5 seconds**. Fuel left on the ground can be ignited to damage enemies standing in it. **10-second cooldown**. |
| Utility | **Flame Jet** | Blasts a **75° cone** up to **12 m** for **400% damage**, igniting and knocking back enemies while propelling the drone backwards. Has **2 stocks** and a **7-second recharge**. The final charge provides extra propulsion. |
| Special | **Inferno** | Charge for **2 seconds** at base attack speed, then press Primary to detonate for **3,000% damage** in a **40 m** radius and ignite enemies. The drone overheats for **5 seconds**. The **30-second cooldown** begins after overheating. |

During Inferno's overheat, **Overheated Flame Jet** remains available for movement. It has a **5-second recharge** and **60%** of the normal propulsion before the last-stock bonus. The other abilities cannot be used during overheated, although Flamethrower refuels fully when overheating ends.

## Cleanup Drone

| Slot | Skill | Effect |
| --- | --- | --- |
| Primary | **Vacuum** | Pulls enemies into a **24 m** area beneath the drone, slowing them by **50%** and dealing **80% damage per second** in **5 ticks per second**. Your movement speed falls by **30%** while active. Use **Cancel Vacuum** to stop early. Maximum duration is **20 seconds** and cooldown scales from **2 to 15 seconds** with use time. |
| Secondary | **Trash Cannon** | Fires **5 scrap projectiles** per stock. Each deals **100% direct damage**, stuns for **1 second** on a direct hit and explodes in a **6 m** radius for up to **50% damage**. Starts with **10 of 20 stocks** and fires at most once every **0.5 seconds**. Recycle enemies to reload. |
| Utility | **Suction Boost** | Fires an **18 m**, **90°** cone blast for **800% damage**, knocks enemies away and launches the drone backwards. Starts with **3 of 5 stocks**. Recycle enemies to restore charges. |
| Special | **Mass Recycling** | Target a non-boss enemy below **50% health** within **60 m** and drag it in for execution. While processing, move **40% slower** and lose access to other skills. Processing takes **1.5 seconds plus adjusted base health ÷ 200**, capped at **10 seconds**. The **15-second cooldown** starts after processing. If the target cannot be captured in **3 seconds**, the failed attempt incurs a **6.5-second cooldown**. |

- **Vacuum** has the Slayer effect. Enemies defeated in it queue for recycling. Already queued enemies keep processing when Vacuum ends. A successful recycle refunds **1 Trash Cannon stock** and **0.5 Suction Boost stock**, in addition to its pickups.
- **Mass Recycling** grants gold nuggets, healing pickups and stock refunds based on the enemy's level-one base health. Elite health modifiers affect processing time.

| Target size | Base health | Gold nuggets | Healing pickups | Trash Cannon refund | Suction Boost refund |
| --- | ---: | ---: | ---: | ---: | ---: |
| Small | Below 200 | 1 | 2 | 3 | 1 |
| Medium | 200 to below 450 | 2 | 3 | 6 | 2 |
| Large | 450 to below 900 | 3 | 5 | 9 | 3 |
| Very large | 900 or higher | 5 | 8 | 12 | 5 |

## Missile Drone

| Slot | Skill | Effect |
| --- | --- | --- |
| Primary | **Guided Missile** | Fires once per **1 second** at base attack speed. Each missile explodes for **250% damage** in a **1 m** radius. Hold the trigger to steer missiles towards your crosshair. On release, missiles continue in their current direction. |
| Secondary | **Missile Painting** | Hold to mark enemies within **80 m**, then release to fire **4 homing missiles for 200% damage each** at each marked enemy. Has **4 stocks** and can mark up to one distinct enemy per available stock. Stocks recharge every **3 seconds**, with **1 second** between volleys. |
| Utility | **Sticky Bomb** | Launches a sticky bomb. Press the button again to detonate it. The blast deals **600% damage** in a **12 m** radius using SweetSpot falloff and knocks enemies and the drone away. It auto-detonates after **30 seconds**. The **6-second cooldown** starts at detonation. |
| Special | **Missile Barrage** | Designate an area within **120 m** to rain down **20 missiles** within a **6 m** targeting radius. Each missile deals **400% damage** in a **5.5 m** explosion and stuns for **1 second**. |

## Jailer Drone

| Slot | Skill | Effect |
| --- | --- | --- |
| Primary | **Restraint Bolt** | Fires a lightly homing bolt for **200% damage** every **0.8 seconds** at base attack speed. Slows by **50% for 2 seconds**. The bolt can bounce to nearby rooted or stunned enemies within **18 m**, losing **50 percentage points** of base damage per bounce. |
| Secondary | **Lockdown** | Hold to aim, then release to place a **12 m** field within **80 m**. It lasts **5 seconds**, deals **200% damage** once to each enemy it catches and repeatedly refreshes a **1-second root** on enemies inside. Has **2 stocks** and a **15-second recharge**. |
| Utility | **Prison Transfer** | Hold to aim, then release to teleport up to **40 m** forward. Enemies within **10 m** of the arrival take **200% damage** and are rooted for **2 seconds**. Non-boss enemies are also moved to a safe spot near your starting point when possible. **8-second cooldown**. |
| Special | **Containment Cage** | Sends a seeking chain to an enemy within **80 m**, dealing **500% damage** and stunning it for **2 seconds** on contact. The **6-second** cage draws in enemies from up to **20 m** away and deals **50% damage every 0.25 seconds** inside its **10 m** damage radius. **20-second cooldown**. |

## Barrier Drone

| Slot | Skill | Effect |
| --- | --- | --- |
| Primary | **Barrier Orb** | Charge an arcing orb for **0.2–2.5 seconds** at base attack speed. It deals **200–500% damage** to enemies or gives allies **200–400% barrier**. After attaching, it detonates in **1 second**, then can bounce to **1–3 additional targets** within **24 m**. Has **4 stocks** and reloads all missing stocks after **4 seconds** without firing at base attack speed. |
| Secondary | **Barrier Buildup** | Links to an ally within **50 m** and grants **800% barrier** over **5 seconds**. You can use your other skills while linked. **10-second cooldown** from cast. |
| Utility | **Shield Projection** | Projects a **15 m** shield that moves with you for **10 seconds**. Deployment pushes non-boss enemies out, and the shield blocks hostile projectiles. Allies inside gain **+50 armour**, **+30% attack speed** and **100% barrier per second**. **18-second cooldown** from cast. |
| Special | **Recharge Field** | Launches a beacon that pierces ordinary enemies for **200% damage**. If it hits a boss or a large enemy it explodes for **600% damage** in a **6 m** radius instead of planting. Hitting terrain deploys a support field for **20 seconds**. The **20-second cooldown** starts when the field expires. |

- **Barrier Orb** has a **1-second** delay before it can charge again after firing. You have **2 seconds** to release a fully charged orb. Overcharging or releasing without a valid target causes a harmless discharge that shocks nearby enemies for **5 seconds**. Overcharging disables all skills for **4 seconds**. Releasing without a target disables only the orb for that time.
- **Recharge Field** pulses every **5 seconds** and also on deployment and expiry. Each pulse restores allied Primary stocks and advances Secondary, Utility and Special cooldowns by **1 second**. Killing an enemy inside grants each ally inside **200% barrier**.

## Bombardment Drone

| Slot | Skill | Effect |
| --- | --- | --- |
| Primary | **Arc Lightning** | Fires a bolt for **250% damage**, with a base interval of **0.8 seconds** and **50 m** initial range. A direct hit arcs to up to **3** more enemies within **15 m**. If the bolt hits terrain, it can redirect to up to **3** visible enemies within **15 m** of impact. Each hit has a **50% chance** to stun for **0.5 seconds**. |
| Secondary | **Bombardment** | Moves the drone's native bombardment to Secondary. Aim at terrain to deploy a drone that launches repeated strikes over the area, stunning enemies hit and knocking them upward. Damage, radius, strike count, timing and stocks default to the native Remote Operation skill. |
| Utility | **EMP** | Emits a **12 m** pulse that deals **200% damage**, shocks enemies for **1 second** and knocks non-boss enemies back. You gain **+100% movement speed** and **+50% attack speed** for **4 seconds**. **10-second cooldown**. |
| Special | **Orbital Thunderbolt** | Charge for up to **5 seconds** to call down a strike for **1,000–6,000% damage** in an **8–30 m** radius. It launches **3–12 arcing orbs**, each striking the ground for **300–600% damage** in a **4–6 m** radius. The main strike and orbs shock for **2 seconds**. |

At full charge, **Orbital Thunderbolt** chains from each directly hit enemy to nearby enemies within **12 m** for **10% of the originating strike's damage**, applying **Weaken for 5 seconds**. You can hold full charge for **5 more seconds**. Firing disables the weapons for **1 second per second spent charging**, up to **5 seconds**. Utility remains available. The **30-second cooldown** starts after the overload.

## Freeze Drone

| Slot | Skill | Effect |
| --- | --- | --- |
| Primary | **Frost Disc** | Throws up to **3 discs**, one every **0.5 seconds** at base attack speed. Each applies **1 Frost** and deals **150% damage** once per enemy on the outward trip and again on return. Hold Primary to keep the discs spinning at their destinations. Each deals **50% damage every 0.25 seconds** and applies **1 Frost every second** within **3 m**. Release to recall the discs and restore their stocks as they return. |
| Secondary | **Freeze Disc** | Fires the drone's native freezing disc for **200% damage**, with up to **6 additional bounces** and a **36 m** bounce range. **15-second cooldown**. |
| Utility | **Frost Field** | Deploys an **18 m** blizzard for **8 seconds**. Enemies inside are slowed by **50%**, take **25% damage every 0.5 seconds** and gain **1 Frost every second**. Enemies already frozen stay frozen inside. You gain **2 armour per Frost stack** and **15 armour per frozen enemy** inside your fields. Each Freeze execution inside adds **1.5 seconds** to its duration. **15-second cooldown**. |
| Special | **Absolute Zero** | Charges an ice spear over **5 seconds**, with up to **5 more seconds** of hold time. It explodes for **1,000–5,000% damage** in an **8–25 m** radius and freezes for **2 seconds**. The hit gains **+10% damage per existing Frost stack**, or **+100% damage** if the target is already frozen. |

- **Absolute Zero** executions shatter for **300% damage** and apply **3 Frost** within **8 m**. Shatters can lead to further executions and explosions.
- At full charge, the impact leaves a **20 m** icy field for **12 seconds**, applying **1 Frost per second**. Frozen non-boss enemies inside can be executed at **50% combined health**. Each Freeze execution inside extends the field by **1.5 seconds**.
- Firing **Absolute Zero** overloads the weapons for up to **5 seconds**, based on time spent charging. Utility stays available. The **25-second cooldown** starts after overload.

## TC-280 Prototype

| Slot | Skill | Effect |
| --- | --- | --- |
| Primary | **Heavy Machine Gun** | Fires for **220% damage per bullet** with a **0.5 proc coefficient**. At base attack speed, the rate rises from about **3.33 to 10 shots per second** over **4 seconds** of sustained fire and spread tightens from **8° to 1°**. Move **40% slower** while firing. |
| Secondary | **Twin Rocket** | Fires **2 rockets**, each dealing **400% damage** in a **6 m** blast. Has **2 stocks** with a **6-second recharge** and at least **0.5 seconds** between salvos. |
| Utility | **Auxiliary Thrusters** | Hold to boost in your movement direction, adding up to **20 m/s**. A full base tank lasts **5 seconds**. Fuel starts to refill **1 second** after boosting stops and takes **12 seconds** to refill a base tank before cooldown reduction. Extra Utility stocks increase capacity. |
| Special | **Full Arsenal** | Enhances the machine gun and rockets for **15 seconds**. The **30-second cooldown** starts when the enhancement ends. |

- Continuous fire brings the machine gun to full heat in **14 seconds**. At full heat, every bullet ignites enemies and every other shot also ignites the drone. Heat begins to dissipate **1 second** after firing stops and takes **5 seconds** to clear from full.
- **Full Arsenal** spins the gun from **5 to 20 shots per second** over **2 seconds** at base attack speed and tightens spread from **5° to 0°**. Full-heat shots still ignite enemies, and the enhancement suppresses self-ignition.
- **Full Arsenal** lets Twin Rocket home onto a visible locked target within **100 m** and recharges its stocks **4 times as fast**.
