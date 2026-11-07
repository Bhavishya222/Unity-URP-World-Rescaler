# Unity URP World Rescaler

A Unity Editor extension to scale entire scenes, nested Terrains, and URP lights without breaking physics, splatmaps, or texture tiling. 

Scaling a root GameObject in Unity completely breaks multi-tile terrain physics and ignores light ranges. This tool solves the headache of accidentally building a map 20x too large by safely mathing out the exact proportions and cloning terrain assets so your world shrinks perfectly in one click.

## Features
* **Safe Hierarchy Scaling:** Repositions root objects without skewing nested terrain scale constraints.
* **Terrain Cloning:** Duplicates `TerrainData` and `TerrainLayers` before modifying them, preserving your painted splatmaps and preventing giant, stretched grass/dirt textures.
* **Physics Sync:** Automatically updates the invisible `TerrainCollider` dimensions to match the visual mesh.
* **URP Lighting Scaling:** Scales Point/Spot Light ranges and Camera Clip Planes to match the new world size, with an optional toggle to adjust light intensity (useful for preserving brightness in heavily scaled-down levels).

## Installation
1. Download the `WorldRescalerWindow.cs` script.
2. Drop it anywhere inside an `Assets/Editor` folder in your Unity project.

## Usage
1. Open the tool via the top menu bar: **Tools > World Rescaler**.
2. Set your **Scale Factor**. *(Example: If your world is 5x too big, set the scale factor to `0.2`)*.
3. Check the options relevant to your scene (Terrain scaling, URP light ranges, etc.).
4. Click **Scan Active Scene** to verify there are no structural blockers.
5. Click **Apply Rescale**. 

*Note: Always save a project backup before modifying scene scales. Baked lightmaps, NavMesh data, and fog distances will need to be re-baked manually after scaling.*

---
Built by [Pinwheels Studios](https://github.com/Bhavishya222). Released under the MIT License.
