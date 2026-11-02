# Third-party notices

Before Your Frames is MIT licensed (see [LICENSE](LICENSE)). The release zip contains `BYEFrameBlink.dll`, this file, the README and the LICENSE. With one exception (OpenVR, below), it includes no code or binaries from the projects listed here; they are installed separately by the user and used at runtime, or referenced at build time from the user's own installation.

| Project | Role | License |
|---|---|---|
| [UUVR](https://github.com/Raicuparta/uuvr) by Raicuparta | The VR renderer this plugin runs next to. The plugin finds its types by name at runtime (it has no compile-time reference and includes none of its code) and patches them with Harmony. | GPL-3.0 |
| [Rai Pal](https://github.com/Raicuparta/rai-pal) by Raicuparta | Installs UUVR and BepInEx for the game. | GPL-3.0 |
| [BepInEx](https://github.com/BepInEx/BepInEx) (BepInEx Team) | Plugin loader. Referenced when building; not redistributed. | LGPL-2.1 |
| [HarmonyX](https://github.com/BepInEx/HarmonyX) (BepInEx) / [Harmony](https://github.com/pardeike/Harmony) (Andreas Pardeike) | Runtime patching (`0Harmony.dll`, shipped with BepInEx). Referenced when building; not redistributed. | MIT |
| [OpenVR SDK](https://github.com/ValveSoftware/openvr) by Valve Corporation (`openvr_api.cs`, v2.0.10) | Reads Steam Frame / SteamVR controller buttons for the in-game shortcuts (pause menu, recenter, eye separation, reticle toggle), through the already-running `openvr_api.dll` that UUVR's XR backend loads. This one **is** compiled into `BYEFrameBlink.dll`. | BSD-3-Clause (Copyright (c) 2015, Valve Corporation) |
| [frameeyeosc](https://github.com/sasaken1102r/frameeyeosc) by sasaken1102r, a fork of [konsti219/frameeyeosc](https://github.com/konsti219/frameeyeosc) | Runs on the headset and sends the eye data. This plugin only receives the OSC messages it sends. Not included. | MIT (Copyright (c) 2026 konsti219; Copyright (c) 2026 sasaken1102r) |
| [Steam Frame installer](https://github.com/sasaken1102r/frame) by sasaken1102r | Installer used to put frameeyeosc on the headset. Not included. | MIT |
| Unity, `Assembly-CSharp.dll` | Referenced from the user's own game install when building; not redistributed. | Proprietary (Unity Technologies; GoodbyeWorld Games) |

*Before Your Eyes* is a game by GoodbyeWorld Games. This project is an unofficial fan mod and is not affiliated with or endorsed by them, Valve, Unity, Raicuparta, or the authors above. No game assets are included; you need your own copy of the game.

Names and trademarks belong to their owners.

## OpenVR SDK license (BSD-3-Clause)

```
Copyright (c) 2015, Valve Corporation
All rights reserved.

Redistribution and use in source and binary forms, with or without modification,
are permitted provided that the following conditions are met:

1. Redistributions of source code must retain the above copyright notice, this
list of conditions and the following disclaimer.

2. Redistributions in binary form must reproduce the above copyright notice,
this list of conditions and the following disclaimer in the documentation and/or
other materials provided with the distribution.

3. Neither the name of the copyright holder nor the names of its contributors
may be used to endorse or promote products derived from this software without
specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE FOR
ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
(INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON
ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
```
