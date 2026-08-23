# BVR - Balance and Variety Rework

## What this is:
This rebalance project aims to push Nuclear Option's gameplay towards a more realistic simlite experience while still honouring ShockFront's vision.
Its main design focus is customisation.
Any change the mod introduces can be toggled and all values can be tweaked. (allows sandbox-style play)
When playing multiplayer, it has a seed system which allows easy sharing of config settings, and a hash system so you know you all have the same settings.

## What this does currently:

### General Missile & Countermeasure Changes:
- Slightly buffs IR missile flare rejection while giving aircraft more flares (both use customizable multipliers, defaulting to 2.0x).
- Adds customizable SARH lock persistence for R9 Stratolance and RAM45 missiles (defaults to 3.0s, set to 600 for effective infinite persistence).
- Adds SARH relocking to original target after a delay for R9 and RAM45, with configurable delay and attempt limits (defaults to 3.0s delay, infinite attempts).

### SAH-46 Chicane Changes:
- Gives the Chicane Flak shells on the 30mm (they have a very small splash radius, but they can intercept munitions, giving the Chicane a new role as a flying AFV6AA).
- Gives the Chicane internal bays AGR-18 Lynchpin (x14) and AGR-24 Kingpin (x8) double rocket pod options.
- Includes a symmetry fix to properly center the right internal weapon bay pylon.
- Optionally gives the Chicane single (x1) or double (x2) AAM-24 Scythe mounts on its inner wing stub pylons.

### EW-25 Medusa Changes:
- Buffs the Medusa's laser by lowering its energy consumption from 120 to 60 power draw (giving it a new role as a decent-ish area defense unit).
- Gives the Medusa AGR-18 Lynchpin (x14) and AGR-24 Kingpin (x8) double rocket pod options on hardpoint set 3.
- Gives the Medusa R9 Stratolance single (x1) mounts on hardpoint sets 3 and 4. (R9 Stratolance does NOT work with Radome)
- Gives the Medusa R9 Stratolance double (x2) mount on hardpoint set 4. (Hardpoint sets start at 0 and increase from left to right in the loadout selection screen)

### Expanded Heavy Rocket Pod Options Across Airframes:
Adds double AGR-18 Lynchpin (x14) and double AGR-24 Kingpin (x8) rocket pods as customizable loadout choices across multiple airframes:
- **CI-22 Cricket:** Available on hardpoint sets 2 and 3.
- **T/A-30 Compass:** Available on hardpoint set 1.
- **VT-7 Vagrant:** Available on hardpoint set 3.
- **UH-90 Ibis:** Available on hardpoint sets 0 and 1. (Asymmetric pylons are always considered separately, so 0 and 1 in this case are the stubs)
- **FS-12 Revoker:** Available on hardpoint set 2.
- **FS-20 Vortex:** Available on hardpoint set 3.
- **VL-49 Tarantula:** Available on hardpoint sets 4 and 5.
- **KR-67 Ifrit:** Available on hardpoint sets 4 and 5.

## More things are planned and in active development! :D
If you have any other changes you'd like to see, sound them out in the issues section or on the Discord mod forums. I'll keep track and consider all of them. <3
Things that just tweak values and take loadouts from other airframes are relatively easy to code.
Changing the scripts / logic in the game (i.e. SARH relocking infinitely, which I did implement eventually) is quite a bit harder to do. But I'll still try! :D

I love when you guys offer feedback, so please let me know what you think so I get the motivation to keep working on this project!
I hope you have a wonderful day and have fun! <3