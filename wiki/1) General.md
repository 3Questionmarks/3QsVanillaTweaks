# Lunar Coins

- Removed the drop-rate reduction: in vanilla, every Lunar Coin drop halves the chance of another dropping that run.
- Picked-up Lunar Coins are awarded to all players, including dead players, rather than only the player who collects them.

# Pickup Recovery

- Items, equipment, Lunar Coins and choice pickups that fall out of bounds are returned to a safe recovery location.

# One-Shot Protection

- Grants 0.5 seconds of immunity when one-shot protection triggers (up from 0.1 seconds). This now blocks all follow-up damage during that time, including delayed hits that bypass the vanilla protection window.
- Has a 5-second cooldown after triggering. Healing back above the threshold does not reset it instantly.

# Shields

- Damage that breaks a shield no longer spills into health. For example, a 300-damage hit against 100 HP and 100 shield removes the shield but leaves 100 HP.
- Shields take 3 seconds to fully recharge after the regeneration delay (up from 2 seconds). The regeneration delay remains at 7 seconds.
- Fall damage is calculated using maximum health plus shield.
- Percentage-based healing uses maximum health plus shield, improving interactions such as Transcendence with N’kuhana’s Opinion. Healing still restores health rather than shield; flat healing is unchanged.

# Barrier

- Damage that breaks barrier no longer spills into shield or health.
- Maximum barrier uses combined health and shield before HP curse reductions, defaulting to 100% of that total.
- Adds optional scaled, pre-Collective and custom decay formulas. The default remains vanilla decay.

# Freeze

- Freeze hits can execute bosses below 10% combined health and shield, including bosses that resist the frozen state. This does not let freeze stun immune bosses.
- The normal freeze execute threshold remains 30%; both thresholds and boss execution are configurable.

# Temporary Items

- Temporary item stacks last 90 seconds (up from 80 seconds).

# Multiplayer Drones

- Player-controlled drones can use interactables.
- Drone respawns are limited to 1 per player per stage by default, with a configurable limit.
