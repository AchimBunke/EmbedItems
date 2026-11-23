using GBX.NET;
using GBX.NET.LZO;

namespace EmbedItems.Core;

public static class GbxSetup
{
    static bool done;

    /// <summary>Maps and items are LZO compressed. Call once before reading any Gbx file.</summary>
    public static void Init()
    {
        if (done) return;
        Gbx.LZO = new Lzo();
        done = true;
    }
}
