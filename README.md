# Unity Multiplayer Essentials
## Description
A set of **unconditionally free reusable tools** for online multiplayer Unity game development:

* Two-click **quick building and playtesting menu** for local multiplayer testing with up to 4 clients
* Includes **automatic build compilation, sending your latest code changes directly to your playtest build folder** so you can **quickly test them in multiplayer** without needing to rebuild. (This is code-only: new builds are still required for scene and asset changes!)
* **Command-line support** for hosting and connecting to sessions upon boot
* **Generic, despecialised** `Ticker` class for **client prediction and reconciliation**, using simple timeline terminology such as "Seek" and "Rewind".
* * **Movement** component for immediate movement, with collision detection, suitable for prediction/reconciliation (buggy, WIP)
* **Timeline visualiser** for `Ticker` debugging
* Pre-made examples that can be easily built upon

This package also includes [Mirror](https://github.com/vis2k/Mirror)-specific features using a [custom Mirror fork](https://github.com/LXShades/Mirror):
* (WIP - I can't remember if this works lol) **Predictive object spawning** enabling clients to produce networked objects before the server knows they exist.
* More WIP.

## Goals
These tools are designed to accelerate iterative multiplayer development in Unity. It recognises the need for:

* **Simplicity**
* **Fast iteration** and testing
* Powerful, yet optional and decoupled **client prediction** featuresets
* An **easy, minimally confusing** solution to **hack-proof character movement**
* **Debugging** tools


## Todo
Among current todo's are:

### General
* Speedhack prevention
* Faster play mode boot, ideally skipping boot scene and using a boot prefab instead.
* More examples

### Fixes
* Example Player Character still needs to be changed for each example scene...this is very suboptimal

## Warrantn't!!
Although public-facing, this toolset currently comes with no warranty or instruction manual. Code should be considered beta, with inline documentation and usage examples under occasional ongoing development. However, it is majorly functional, and ready to be tweaked to suit a multiplayer project's needs and accelerate development.

# Mirror Features and License
Features in the Mirror subfolders rely on a custom Mirror fork, which may or may not suit your needs. You can grab this fork [here](https://github.com/LXShades/Mirror). All Mirror code is held [under an MIT License](https://github.com/LXShades/Mirror/blob/master/LICENSE), which is not identical to this repo's license but still highly permissive (read it!).

To activate this piece of the project, define MIRROR_LXSHADES_BRANCH in the player settings.

# Remaining License
Free, free, free! Creative Commons Zero v1.0 means you can use this code for any diddly-darn old thing you want, no credit required, no strings attached. You just aren't allowed to sue me when it breaks. ;)

If these tools did help you - a little nod of credit or a link to this toolset is always welcomed and appreciated. After all, making multiplayer games in Unity is hard, so let's make it easier. Spread the word!

Contributions are also welcome, just make a PR and we can work out how/whether to fit it in with the rest of the code. :)