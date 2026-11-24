using System.Security.Cryptography;
using System.Text;
using GBX.NET;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Plug;

namespace EmbedItems.Core;

/// <summary>Reading and rewriting the parts of a Gbx file that embedding touches.</summary>
public static class GbxFileUtils
{
    public record Ref(int AncestorLevel, string FilePath);

    /// <summary>
    /// Texture files named by custom materials. Required: UserTextures entries. Optional: the files a BaseTexture
    /// prefix can stand for (the game only loads the suffixes its shading model needs, so missing ones are fine).
    /// IsPrefab: the item is a prefab (moving items are), which never reads such textures from a map.
    /// </summary>
    public record MaterialTextures(List<string> Required, List<string> Optional, bool IsPrefab);

    // Suffixes the game appends to a BaseTexture prefix (found in-game: _B base color, _S roughness, _N normal,
    // _I self-illumination, _E energy).
    static readonly string[] BaseTextureSuffixes = ["_B", "_S", "_N", "_I", "_E"];

    /// <summary>
    /// Texture paths in the custom materials (CPlugMaterialUserInst) of an item, mesh or material file. These are
    /// plain strings, not references, so ReadRefs doesn't see them. Walks the known item layouts explicitly
    /// (no reflection, so it stays trim-safe). Meshes in their own files are found through the references instead.
    /// </summary>
    public static MaterialTextures ReadMaterialTextures(byte[] gbx)
    {
        var materials = new List<CPlugMaterialUserInst>();
        void AddMesh(CPlugSolid2Model? mesh)
        {
            foreach (var m in mesh?.CustomMaterials ?? [])
                if (m.MaterialUserInst is { } u) materials.Add(u);
        }
        void AddEntity(object? entity)
        {
            switch (entity)
            {
                case CGameCommonItemEntityModel common: AddMesh(common.StaticObject?.Mesh); break;
                case CPlugPrefab prefab: foreach (var ent in prefab.Ents ?? []) AddEntity(ent.Model); break;
                case CPlugStaticObjectModel stat: AddMesh(stat.Mesh); break;
                case CPlugDynaObjectModel dyna: AddMesh(dyna.Mesh); break;
            }
        }

        var node = Gbx.ParseNode(new MemoryStream(gbx));
        bool isPrefab = false;
        switch (node)
        {
            case CGameItemModel item:
                isPrefab = item.EntityModel is CPlugPrefab;
                AddEntity(item.EntityModel);
                break;
            case CPlugSolid2Model mesh: AddMesh(mesh); break;
            case CPlugMaterialUserInst material: materials.Add(material); break;
            default: AddEntity(node); break;
        }

        var required = new List<string>();
        var optional = new List<string>();
        foreach (var u in materials)
        {
            foreach (var t in u.UserTextures ?? [])
                if (!string.IsNullOrEmpty(t.Texture)) required.Add(t.Texture);
            if (!string.IsNullOrEmpty(u.BaseTexture))
                optional.AddRange(BaseTextureSuffixes.Select(s => u.BaseTexture + s + ".dds"));
        }
        return new MaterialTextures(required, optional, isPrefab);
    }

    /// <summary>
    /// External references of a Gbx file (empty if it has none). Uses a raw-body parse: fast, because the
    /// body isn't decoded, and works for every Gbx class.
    /// </summary>
    public static List<Ref> ReadRefs(byte[] gbx)
    {
        var raw = Gbx.Parse(new MemoryStream(gbx), new GbxReadSettings { ReadRawBody = true });
        return raw.RefTable is { } rt ? rt.Files.Select(f => new Ref(rt.AncestorLevel, f.FilePath)).ToList() : [];
    }

    /// <summary>
    /// Replaces every reference path (in order) and sets the ancestor level to 0, so all paths are relative
    /// to the file's own folder. Needs a raw-body parse: after a full parse GBX.NET writes the references
    /// from the node graph and ignores edits to RefTable.Files.
    /// </summary>
    public static byte[] RewriteRefs(byte[] gbx, IReadOnlyList<string> newPaths)
    {
        var raw = Gbx.Parse(new MemoryStream(gbx), new GbxReadSettings { ReadRawBody = true });
        var rt = raw.RefTable ?? throw new InvalidOperationException("file has no references");
        var files = rt.Files.ToList();
        if (files.Count != newPaths.Count) throw new InvalidOperationException("reference count changed");
        for (int i = 0; i < files.Count; i++) files[i].FilePath = newPaths[i];
        rt.AncestorLevel = 0;
        var ms = new MemoryStream();
        raw.Save(ms);
        return ms.ToArray();
    }

    /// <summary>Description of an item. Needs a full parse: the header doesn't contain it.</summary>
    public static string? ReadDescription(byte[] item) =>
        Gbx.ParseNode<CGameItemModel>(new MemoryStream(item)).Description;

    /// <summary>
    /// Item with a new Description, plus its ident. The ident (and so the author) is left untouched on purpose:
    /// an item whose author was changed after it was built is not shown in play mode.
    /// </summary>
    public static (byte[] Bytes, Ident Ident) SetDescription(byte[] item, Func<string?, string> change)
    {
        var gbx = Gbx.Parse<CGameItemModel>(new MemoryStream(item));
        gbx.Node.Description = change(gbx.Node.Description);
        var ms = new MemoryStream();
        gbx.Save(ms);
        return (ms.ToArray(), gbx.Node.Ident);
    }

    public static Ident ReadIdent(byte[] item) => Gbx.ParseHeaderNode<CGameItemModel>(new MemoryStream(item)).Ident;

    /// <summary>Short content hash, stored in the marker to detect a changed disk file.</summary>
    public static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA1.HashData(bytes))[..16];

    /// <summary>Stable 8-character postfix for an item's original path (same path -> same embedded name).</summary>
    public static string PathHash(string originalPath) =>
        Convert.ToHexStringLower(SHA1.HashData(Encoding.UTF8.GetBytes(originalPath.ToLowerInvariant())))[..8];
}
