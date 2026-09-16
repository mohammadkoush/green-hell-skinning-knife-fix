# SkinningKnifeFix

**The skinning animation shows the knife you are actually holding.**

Green Hell plays the large-animal harvesting animation with a stone blade in hand, whatever you were
holding. Metal knife, obsidian blade, bone knife — the animation always shows stone. This is a known
quirk: it is reported on the Steam forums as a bug and documented on the wiki as expected behaviour.

This mod swaps the prop. When the skinning animation starts, the stone blade is hidden and a visual
copy of the blade in your hand is placed in the same holder, in the same pose. When the animation
ends, the copy is removed and the stone blade is put back. Only meshes and materials are copied —
never the item itself — so nothing in the game can mistake it for a second knife.

If you are holding something that is not a knife or a machete, nothing changes.

## Install

Needs [BepInEx 5](https://github.com/BepInEx/BepInEx) (x64). Drop `SkinningKnifeFix.dll` into
`Green Hell\BepInEx\plugins\SkinningKnifeFix\`.

## Settings

`BepInEx/config/com.mohammadkoush.skinningknifefix.cfg`, written on first run.

| | |
|---|---|
| `Knife.Enabled` | on/off |
| `Knife.IncludeMachetes` | also swap in a machete (default on) |
| `Pose.PositionOffset` | nudge the blade in the hand, metres, `x y z` — for a knife whose model origin sits off from the stone blade's |
| `Pose.RotationOffset` | turn it, degrees, `x y z` |
| `Diagnostics.LogSwaps` | one log line per swap, naming the item and the mesh count |

## Build

`powershell -ExecutionPolicy Bypass -File build.ps1` — stock .NET Framework `csc.exe`, references
taken from the game install, no Visual Studio needed. Add `-NoDeploy` to build without installing.

MIT licensed.
