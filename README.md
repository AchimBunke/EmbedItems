# EmbedItems

Embeds the custom items and blocks used in a Trackmania map into the map file, **together with every
file they reference** (meshes, textures, materials, ...). The result is a map that works for other
players without them having your Items folder.

## Download

Grab `EmbedItems.exe` from the [Releases](../../releases) page. It is a single self-contained file
(no .NET install needed). Put it anywhere you like.

## Usage

**Double-click `EmbedItems.exe`**
It finds your Trackmania folder (`Documents\Trackmania2020` or `Documents\Trackmania`) and watches
your `Maps` folder. Every time you save a map in the editor, an embedded copy is written next to it.
Close the window to stop.

**Drag a map onto `EmbedItems.exe`**
Embeds that one map and keeps the window open so you can read the result.

Either way the original map is not changed. The output is saved next to it as
`<name> (embedded).Map.Gbx`, with " (embedded)" added to the map name.
Embedded items are renamed to `<item> (embedded).Item.Gbx` so they don't clash with the
copies in your Items folder.

## Command line

```
EmbedItems                                            watch your Maps folder
EmbedItems <map.Map.Gbx>                              embed once
EmbedItems <TrackmaniaFolder> <map.Map.Gbx>           embed once
EmbedItems <TrackmaniaFolder> [--watch] [mapsFolder]  embed after every save

--dry-run              print every resolved reference, change nothing
--verbose              also list complete / not-found items
--standard-ancestors   AncestorLevel N = N folders above the owner's folder
                       (default: level 1 = the owner's own folder, level 2 = one up, ...)
```

`<TrackmaniaFolder>` is the folder that contains `Items\`. Pass it if your Documents folder is
somewhere unusual and auto-detection fails.

## Notes

- Only custom items/blocks from your `Items\` / `Blocks\` folders are embedded. Stock game files are
  reported as "not found under Items" and skipped, which is expected.
- Files that are already embedded in the map are never overwritten.

## Building

```
dotnet publish EmbedItems.csproj -c Release -o publish
```

Produces a single self-contained `publish\EmbedItems.exe` (win-x64).

## Credits

- [GBX.NET](https://github.com/BigBang1112/gbx-net) by BigBang1112, for reading and writing Gbx files (MIT)
- GBX.NET.LZO and [NativeSharpLzo](https://github.com/BigBang1112/NativeSharpLzo), for Gbx LZO compression (GPL-3.0-or-later)
- [LZO](https://www.oberhumer.com/opensource/lzo/) by Markus F.X.J. Oberhumer (GPL)
- Developed with help from [Claude](https://claude.ai) by Anthropic

## License

EmbedItems is licensed under the [GNU General Public License v3.0 or later](LICENSE).
It has to be, because the exe bundles GBX.NET.LZO / NativeSharpLzo, which are GPL-3.0-or-later.
