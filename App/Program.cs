namespace EmbedItems.App;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        // a map dropped onto the exe is opened in the Map tab
        Application.Run(new MainForm(args.FirstOrDefault(a => a.EndsWith(".Map.Gbx", StringComparison.OrdinalIgnoreCase))));
    }
}
