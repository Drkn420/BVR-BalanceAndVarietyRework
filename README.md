# BVR - Balance and Variety Rework

## What this is:
This rebalance project aims to push Nuclear Option's gameplay towards a more realistic simlite experience while still honouring ShockFront's vision.
Its main design focus is customisation.
Any change the mod introduces can be toggled and all values can be tweaked. (allows sandbox-style play)

BVR captures its settings at startup, so config changes require a full game restart to take effect.

When playing multiplayer, it has a seed system which allows easy sharing of config settings, and a hash system so you know you all have the same settings.
The current config hash and seed can be viewed in the `Important Notices` section, and seeds can be imported through the `Import Config Seed` field.

## What this does currently:

### General Missile & Countermeasure Changes:
- Slightly buffs IR missile flare rejection while giving aircraft more flares.
  - Both use customizable multipliers, defaulting to `2.0x`.
- Adds customizable SARH lock persistence for R9 Stratolance and RAM-45 missiles.
  - Defaults to `3.0s`.
  - Set to `600` for effectively infinite persistence.
- Adds SARH relocking to the original target after a delay for R9 Stratolance and RAM-45.
  - Configurable delay and attempt limits.
  - Defaults to `3.0s` delay and infinite attempts.

### Cruise Missile RCS Changes:
Adds configurable radar cross-section values for cruise missiles:
- **ALM-C450:** default `0.0005` (vanilla is `0.005`)
- **AGM-99:** default `0.008` (vanilla is `0.008`)
- **AShM-300:** default `0.005` (vanilla is `0.005`)
- **ALND-4 (20kt):** default `0.001` (vanilla is `0.005`)

### ARH Seeker Changes:
Adds configurable loft-factor support for active radar missiles:
- **Scythe (AAM2):** configurable `loftAmount`, default `0.7` (vanilla is `0.7`)
- **Scimitar (AAM4):** configurable `loftAmount`, default `0.1` (vanilla is `0.1`)

These options are included so the behaviour can be adjusted even though the defaults currently match vanilla.

### SAH-46 Chicane Changes:
- Enables the proximity fuse on the Chicane's 30mm gun.
  - This gives it flak-like behaviour with a small splash radius.
  - It can intercept munitions, giving the Chicane a new role as a flying AFV6AA-style point defence platform.
- Gives the Chicane internal bays AGR-18 Lynchpin (x14) and AGR-24 Kingpin (x8) double rocket pod options.
- Optionally gives the Chicane single (x1) or double (x2) AAM-24 Scythe mounts on hardpoint set 2.
- Includes a symmetry fix to properly center the right internal weapon bay pylon.

### EW-25 Medusa Changes:
- Buffs the Medusa's laser by lowering its energy consumption from `120` to `60` power draw.
  - This gives it a new role as a decent-ish area defence unit.
- Gives the Medusa AGR-18 Lynchpin (x14) and AGR-24 Kingpin (x8) double rocket pod options on hardpoint sets 3 and 4.
- Gives the Medusa AGR-18 Lynchpin (x21) and AGR-24 Kingpin (x12) triple rocket pod options on hardpoint sets 3 and 4.
- Gives the Medusa RAM-45 x3 launchers on hardpoint sets 3 and 4.
- Gives the Medusa internal RAM-45 x3 launchers on hardpoint set 1.
- Gives the Medusa R9 Stratolance x2 launchers on hardpoint sets 3 and 4.
- Gives the Medusa internal R9 Stratolance x2 launchers on hardpoint set 1.

### Expanded Blueprint Weapon Options Across Airframes:
Adds the following customizable blueprint loadout options across multiple airframes.  
Hardpoint sets start at `0` and increase from left to right in the loadout selection screen.

- **CI-22 Cricket:**
  - AGR-18 Lynchpin (x14) double and AGR-24 Kingpin (x8) double on hardpoint sets 2 and 3.

- **T/A-30 Compass:**
  - AGR-18 Lynchpin (x14) double and AGR-24 Kingpin (x8) double on hardpoint set 1.

- **VT-7 Vagrant:**
  - AGR-18 Lynchpin (x14) double and AGR-24 Kingpin (x8) double on hardpoint set 3.

- **UH-90 Ibis:**
  - AGR-18 Lynchpin (x14) double and AGR-24 Kingpin (x8) double on hardpoint sets 0 and 1.
  - Asymmetric pylons are always considered separately, so 0 and 1 in this case are the stubs.

- **FS-12 Revoker:**
  - AGR-18 Lynchpin (x14) double and AGR-24 Kingpin (x8) double on hardpoint set 2.
  - AGR-18 Lynchpin (x21) triple and AGR-24 Kingpin (x12) triple on hardpoint set 2.

- **FS-20 Vortex:**
  - AGR-18 Lynchpin (x14) double and AGR-24 Kingpin (x8) double on hardpoint set 3.

- **VL-49 Tarantula:**
  - AGR-18 Lynchpin (x14) double and AGR-24 Kingpin (x8) double on hardpoint sets 4 and 5.
  - 20mm CIWS rotary cannon on hardpoint set 3.
  - 57mm Flak side mount on hardpoint set 2.
  - 57mm belly mount on hardpoint set 2.

- **KR-67 Ifrit:**
  - AGR-18 Lynchpin (x14) double and AGR-24 Kingpin (x8) double on hardpoint set 4.

## More things are planned and in active development! :D
If you have any other changes you'd like to see, sound them out in the issues section or on the Discord mod forums. I'll keep track and consider all of them. <3
Things that just tweak values and take loadouts from other airframes are relatively easy to code.
Changing the scripts / logic in the game (i.e. SARH relocking infinitely, which I did implement eventually) is quite a bit harder to do. But I'll still try! :D

I love when you guys offer feedback, so please let me know what you think so I get the motivation to keep working on this project!
I hope you have a wonderful day and have fun! <3
