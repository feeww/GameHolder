# Characters, enemies, and weapons

1. Open `SampleScene` and select `[PureDots_PresentationBootstrap]` in the Hierarchy.
   It references `Assets/GameData/Characters/DefaultCharacter.asset` and the Tank,
   Runner, Skirmisher, and Sniper assets in `Assets/GameData/Enemies`.
   Select those assets in the Project window to edit the existing game settings.
2. In any Project folder under `Assets` (for example `Assets/GameData/Weapons`), use
   **Create > Pure DOTS > Weapon > For Character** or **For Enemies**. Choose **Standard**,
   **Explosive**, or **Laser**, then tune damage, interval, range, size, and lifetime.
   Standard fires a configurable projectile spread. Explosive shots burst on impact or expiry.
   Laser fires a piercing pulse and damages each intersecting enemy once.
   Settings that do not apply to the selected weapon type are hidden.
3. In the Project window, use **Create > Pure DOTS > Character**. Edit its health, speed,
   body radius, pickup radius, invulnerability, respawn grace, texture, and tint in the Inspector. Drag it into the
   bootstrap's **Starting Character** field. Assign a **For Character** weapon to its
   **Weapon** field. The bootstrap's **Starting Weapon Asset** optionally overrides that selection.
   A character and character weapon asset are required.
4. Use **Create > Pure DOTS > Enemy** to create enemy definitions. Configure body stats,
   contact damage, contact attack interval, experience, spawn weight, and appearance.
   **Melee Stopping Distance** optionally stops melee movement before body contact.
   Enable **Ranged**, then assign a **For Enemies**
   weapon. **Attack Range** is the preferred firing distance, limited by the weapon's range.
   Set **Retreat Range** to zero to hold that distance, or positive for kiting. All ranged
   controls are hidden when **Ranged** is unchecked; a ranged enemy requires a weapon asset.
   Player and enemy weapon assets also control projectile/beam tint.
5. Add enemy assets to the bootstrap's **Enemy Types** list. This list is the complete
   spawning roster; **Spawn Weight** controls relative frequency. At least one enemy asset
   must be assigned. Empty slots are ignored. Missing required references appear as Inspector errors.

For another scene, run **Pure DOTS > Setup Active Scene** to add the bootstrap.
Assign its character and enemy roster before entering Play. Duplicate an existing
asset to start with a configured character, enemy, or weapon; the player and enemy
weapon folders include editable Standard, Explosive, and Laser examples.
Settings are copied into native simulation data on Play; restart retains that character
and weapon. Change assets outside Play mode and start again to apply edits.
Combat uses Burst jobs, the existing spatial grid, and preallocated projectile/enemy
pools. New enemy assets share the pool; they do not add per-enemy GameObjects.

**Mass** affects crowd pushing and the player's ability to push through enemies.
Crowd push priority is `Mass * MoveSpeed`: a larger value yields less to nearby enemies.
**Spawn Weight** controls relative spawn frequency, independently of mass.

## Assets and engine limits

Characters, enemies, and weapons are configured entirely through assets in
`Assets/GameData` and copied into native data on Play. The sample scene explicitly
references its configuration; missing references never activate code presets.
`EnemyDefaults`, `WeaponDefaults`, and character/weapon/enemy balance constants have
been removed. The original procedural character and enemy artwork is now stored
as editable texture assets in `Assets/GameData/Textures`.

Engine limits and shared simulation rules remain under `Assets/Scripts/Configuration`:

- `SimulationConstants`: pool, grid, queue, and job capacities; floating origin.
- `CrowdConstants`: spatial hashing, distance tiers, pushing, and steering.
- `CombatConstants`: shared contact tolerance, aiming, cooldown jitter, and cosmetic blast duration.
- `RunDefaults`: waves and directional spawn settings.
- `ProgressionConstants`: experience, level rewards, gem tiers, and recycling.
- `NumericalConstants`: denominator guards and degenerate sweep tolerances.

`Assets/Scripts/Presentation/Configuration` contains camera, render, HUD, audio,
and particle settings in `PresentationConstants`, plus shared inspector and conversion
bounds in `AuthoringLimits`. These remain in the presentation assembly; simulation
jobs depend only on native configuration. These files contain engine rules and
validation bounds, rather than per-character, per-enemy, or per-weapon presets.
