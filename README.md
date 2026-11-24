# EmbedItems

Embeds the custom items used in a Trackmania map into the map file, **together with every file they use**
(meshes, textures, materials, ...), so other players see them without having your Items folder.
It can also update embedded items from your Items folder, repair maps after an editor save, and extract
embedded items back to their original place.

## Download

From the [Releases](../../releases) page:

- **`EmbedItems.exe`**: the app. Double-click it.
- **`EmbedItemsCli.exe`**: the command line version, with the same features (for scripts or automation).

Both are single self-contained files (no .NET install needed). Put them anywhere.

## Before you start

1. **Validate your map** in the editor first. Only validated maps can be played directly.
2. Embed it with EmbedItems. The result is saved as `<name> (embedded).Map.Gbx` next to it (or over the original,
   if you choose). The map name in game stays the same unless you change it.
3. **Share the embedded map as it is.** If you open and save it in the editor, the game removes the embedded
   files again. That's not a problem: just embed that map once more and they're back.

## Using the app

- **Map tab**: choose a map. EmbedItems scans it and lists every custom item and what will happen to it.
  - **Embed** writes the embedded map.
  - **Extract items...** writes the items embedded in a map back to their original place in your Items
    folder (or any folder you choose), together with their files. Existing files are skipped unless you tick
    "Overwrite existing files".
- **Folder tab**: scans all maps below a folder and lists those that need embedding (choose what to look for:
  items not embedded yet, embedded items with missing files, embedded items older than your Items folder).
  Tick the ones you want and press **Embed selected**.
- **Watch tab**: embeds every map you save in the editor, automatically.
- **Embed options** (top):
  - *Update embedded items from my Items folder* (on by default): items embedded before are replaced by the
    current version from your Items folder. Off: they're kept and only missing files are put back.
  - *Save as new file* or *Replace the original map*.

You can also drop a map onto `EmbedItems.exe` to open it.

## Command line

```
EmbedItemsCli scan <map> [<map> ...]                    show what embedding would do, change nothing
EmbedItemsCli embed <map> [<map> ...] [embed options]   embed
EmbedItemsCli scan-folder <folder> [--filter <list>] [--embed] [embed options]
EmbedItemsCli extract <map> [--target <ItemsFolder>] [--overwrite]
EmbedItemsCli watch [<mapsFolder>] [embed options]

Embed options:
  --no-overwrite        keep items embedded earlier as they are (only put back missing files)
  --replace             save over the original map
  --map-name <name>     new map name (default: unchanged)

--filter <list>         not-embedded, missing, outdated, all (default: all)
--tm <folder>           your Trackmania folder (the one containing Items\), if auto-detection fails
```

## What happens to an item

Trackmania only loads an embedded file's dependencies if they are **below that file's own folder** in the map.
An item in `Items\Trees\` that uses `Items\_Assets\Tex.dds` therefore doesn't work when simply copied into the
map. EmbedItems solves this like this:

- Items that use extra files are copied into the map's **Items root** as `<name>_<id>.Item.Gbx`, where every
  other file is below them. Their references are rewritten accordingly.
- The files they use keep their usual paths, so items that share a texture share one copy.
- The item's original path is stored in its description (`[EmbedItems] Items\Trees\Tree.Item.Gbx`). That's how
  EmbedItems finds the item again for updates and how Extract puts it back where it was.
- The item's author is never changed (items whose author was changed afterwards don't show in play mode).
- Items without extra files that aren't in the map yet are added unchanged at their own path, the way the editor
  does when saving. So a map that never went through the editor (for example a generated one) is complete too.
  Once in the map, they are treated like items the game embedded and left alone.
- Textures named by custom materials (UserTextures / BaseTexture, `Items\...` paths) are embedded at that path.
  Moving items never read those from the map, only from the Items folder.

An item can't be embedded (and is reported and left unchanged) when:

- it uses a file outside your Items folder, or
- one of its dependency files (for example a `.Texture.Gbx`) points to a file **above** its own folder
  (`..\Image\x.dds`). Keep such files next to, or below, the file that uses them.

Custom blocks aren't supported yet.

## Building

```
dotnet test Tests/EmbedItems.Tests.csproj
dotnet publish App/EmbedItems.App.csproj -c Release -o publish
dotnet publish Cli/EmbedItems.Cli.csproj -c Release -o publish
```

Projects: `Core` (all embedding logic), `App` (WinForms UI), `Cli` (command line), `Tests`.

## Credits

- [GBX.NET](https://github.com/BigBang1112/gbx-net) by BigBang1112, for reading and writing Gbx files (MIT)
- GBX.NET.LZO and [NativeSharpLzo](https://github.com/BigBang1112/NativeSharpLzo), for Gbx LZO compression (GPL-3.0-or-later)
- [LZO](https://www.oberhumer.com/opensource/lzo/) by Markus F.X.J. Oberhumer (GPL)
- Developed with help from [Claude](https://claude.ai) by Anthropic

## License

EmbedItems is licensed under the [GNU General Public License v3.0 or later](LICENSE).
It has to be, because the exes bundle GBX.NET.LZO / NativeSharpLzo, which are GPL-3.0-or-later.
