# Lunar Coins

- Removed the drop-rate reduction: in vanilla, every Lunar Coin drop halves the chance of another dropping that run.
- Picked-up Lunar Coins are awarded to all players, including dead players, rather than only the player who collects them.

# Pickup Recovery

- Items, equipment, Lunar Coins and choice pickups that fall out of bounds are returned to a safe recovery location.

# One-Shot Protection

- Grants 0.5 seconds of immunity when one-shot protection triggers (up from 0.1 seconds). This now blocks all follow-up damage during that time, including delayed hits that bypass the vanilla protection window.
- Has a 5-second cooldown after triggering. Healing back above the threshold does not reset it instantly.

# Shields

- Damage that breaks a shield no longer spills into health. For example, a 200-damage hit against 100 HP and 100 shield removes the shield but leaves 100 HP.
- A hit dealing more than three times the remaining shield bypasses the gate.
- Shields take 3 seconds to fully recharge after the regeneration delay (up from 2 seconds). The regeneration delay remains at 7 seconds.
- Fall damage is calculated using maximum health plus shield.
- Percentage-based healing uses maximum health plus shield, improving interactions such as Transcendence with N’kuhana’s Opinion. Healing still restores health rather than shield; flat healing is unchanged.

# Barrier

- Damage that breaks barrier no longer spills into shield or health.
- A hit dealing more than three times the remaining barrier bypasses the gate.
- Maximum barrier uses combined health and shield before HP curse reductions, defaulting to 100% of that total.
- Adds optional scaled, pre-Collective and custom decay formulas. The default remains vanilla decay.

# Freeze

- Freeze hits can execute bosses below 10% combined health and shield, including bosses that resist the frozen state. This does not let freeze stun immune bosses.
- The normal freeze execute threshold remains 30%; both thresholds and boss execution are configurable.

# Temporary Items

- Temporary item stacks last 90 seconds (up from 80 seconds).

# Remote Operation

- Player-controlled Remote Operation drones can use interactables, collect items and benefit from held items by default.
- There is a cooldown betweeen respawning times. You can use the equipment to activate this cooldown early and change your drone.
- See **Remote Operation** Page for the drone loadouts and the Remote Operation settings.

# Non-enemy objects

- Rocks and other non-enemy objects do not trigger supported on-kill or on-hit item effects such as Brittle Crown.
- Newt can receive on-hit effects such as bleed. Forgive Me Please can still trigger on-kill effects, such as Gasoline and Chronic Expansion.

# Player and Enemy Levels

- The player level cap is 100 (vanilla 94). The new curve reaches the cap at the same total XP as vanilla and still requires 20 XP for the first level-up.
- The ambient enemy level cap is 9,999 (vanilla 99). Enemies keep their current health when they level up by default.
- The configurable enemy item blacklist applies to rolls such as Evolution, Void Fields and Simulacrum. It adds Tougher Times, Safer Spaces, Shuriken, Shipping Request Form, Regenerating Scrap, Kinetic Dampener, Unstable Tesla Coil, N'kuhana's Opinion, Frost Relic, Spare Drone Parts and Empathy Cores to existing restrictions.

