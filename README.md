# Multiplayer Toolset
## Description
A set of **unconditionally free reusable tools** for online multiplayer Unity game development:

* Two-click **quick building and playtesting menu** for multiplayer testing
* **Command-line support** for hosting and connecting to sessions upon boot
* **Generic, despecialised** `Ticker` class for the **client prediction and reconciliation** technique, using simple timeline terminology such as "Seek" and "Rewind".
* **Timeline visualiser** for Ticker debugging
* Examples that can be easily built upon

It also includes [Mirror](https://github.com/vis2k/Mirror)-specific features using a [custom Mirror fork](https://github.com/LXShades/Mirror):
* (WIP - I can't remember if this works lol) **Predictive object spawning** enabling clients to produce networked objects before the server knows they exist.
* More WIP.

## Goals
These tools are designed to accelerate iterative multiplayer development in Unity. It recognises the need for:

* Simplicity
* Fast iteration and testing
* An **easy, minimally confusing** solution to **hack-proof character movement**
* Prediction abilities
* Debugging tools (especially to make prediction techniques more understandable and approachable)

## Todo
Among current todo's are:

### General
* Speedhack prevention
* Faster play mode boot, ideally skipping boot scene and using a boot prefab instead.
* More examples

### Fixes
* Example Player Character still needs to be changed for each example scene
* DeltaTime substepping in Seek between confirmed states

## Warrantn't!!
Although public-facing, this toolset currently comes with no warranty or instruction manual. Code should be considered beta, with inline documentation and usage examples under occasional ongoing development.

# Mirror Features and License
Features in the Mirror subfolder rely on a custom Mirror fork, which may or may not suit your needs (although I personally recommend it, having tested the other free options as well). You can grab this fork [here](https://github.com/LXShades/Mirror). All Mirror code is held [under an MIT License](https://github.com/LXShades/Mirror/blob/master/LICENSE), which is not identical to this repo's license but still highly permissive (read it!).

To remove this dependency and related features, **delete the Mirror folder**. The remaining tools will still be available for you to use. 
(I personally recommend Mirror, having tried several of the current free options in realistic game contexts.)

# Remaining License
Free, free, free! Creative Commons Zero v1.0 means you can use this code for any diddly-darn old thing you want, no credit required, no strings attached. You just aren't allowed to sue me when it breaks. ;)

If these tools did help you - a little nod of credit or a link to this toolset is always welcomed and appreciated. After all, making multiplayer games in Unity is hard, so let's make it easier. Spread the word!

Contributions are also welcome, just make a PR and we can work out how/whether to fit it in with the rest of the code. :)