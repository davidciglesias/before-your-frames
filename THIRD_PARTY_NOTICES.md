# Third-party notices

BYE Frame Blink is MIT licensed (see [LICENSE](LICENSE)). The release zip contains **only** `BYEFrameBlink.dll`, the README and the LICENSE. It includes no code or binaries from the projects below; they are installed separately by the user and used at runtime, or referenced at build time from the user's own installation.

| Project | Role | License |
|---|---|---|
| [UUVR](https://github.com/Raicuparta/uuvr) by Raicuparta | The VR renderer this plugin runs next to. The plugin finds its types by name at runtime (it has no compile-time reference and includes none of its code) and patches them with Harmony. | GPL-3.0 |
| [Rai Pal](https://github.com/Raicuparta/rai-pal) by Raicuparta | Installs UUVR and BepInEx for the game. | GPL-3.0 |
| [BepInEx](https://github.com/BepInEx/BepInEx) (BepInEx Team) | Plugin loader. Referenced when building; not redistributed. | LGPL-2.1 |
| [HarmonyX](https://github.com/BepInEx/HarmonyX) (BepInEx) / [Harmony](https://github.com/pardeike/Harmony) (Andreas Pardeike) | Runtime patching (`0Harmony.dll`, shipped with BepInEx). Referenced when building; not redistributed. | MIT |
| [frameeyeosc](https://github.com/konsti219/frameeyeosc) by konsti219 | Original Steam Frame eye-data sender. This plugin only receives the OSC messages it sends. | MIT |
| [Steam Frame installer and frameeyeosc builds](https://frame.sasaken1102s.net) by sasaken | Installer used to put frameeyeosc on the headset. Not included. | MIT (per the installer script header) |
| Unity, `Assembly-CSharp.dll` | Referenced from the user's own game install when building; not redistributed. | Proprietary (Unity Technologies; GoodbyeWorld Games) |

*Before Your Eyes* is a game by GoodbyeWorld Games. This project is an unofficial fan mod and is not affiliated with or endorsed by them, Valve, Unity, Raicuparta, or the authors above. No game assets are included; you need your own copy of the game.

Names and trademarks belong to their owners.
