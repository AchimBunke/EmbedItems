using EmbedItems.Core;

namespace EmbedItems.App;

/// <summary>
/// The whole UI. Built in code (no designer file) so it stays readable and easy to change.
/// All work runs on a background task; Log() marshals messages back to the UI thread.
/// </summary>
sealed class MainForm : Form
{
    // shared
    readonly TextBox tmFolder = new() { Dock = DockStyle.Fill };
    readonly CheckBox overwriteFromDisk = new() { Text = "Update embedded items from my Items folder", Checked = true, AutoSize = true };
    readonly RadioButton saveAsNew = new() { Text = "Save as new file  (<name> (embedded).Map.Gbx)", Checked = true, AutoSize = true };
    readonly RadioButton replaceOriginal = new() { Text = "Replace the original map", AutoSize = true };
    readonly RichTextBox log = new()
    {
        ReadOnly = true, WordWrap = false, Dock = DockStyle.Fill, DetectUrls = false,
        BackColor = SystemColors.Window, BorderStyle = BorderStyle.None, Font = new Font("Segoe UI", 9.5f),
    };

    // Map tab
    readonly TextBox mapPath = new() { Dock = DockStyle.Fill };
    readonly TextBox mapName = new() { Dock = DockStyle.Fill, PlaceholderText = "unchanged" };
    readonly CheckBox embeddedSuffix = new() { Text = "Add \"(embedded)\"", Checked = true, AutoSize = true, Margin = new Padding(3, 6, 12, 3) };
    readonly ListView items = NewList(("Item (original path)", 360), ("State", 110), ("Placed", 55), ("What happens", 600));
    readonly CheckBox overwriteOnExtract = new() { Text = "Overwrite existing files when extracting", AutoSize = true };

    // Folder tab
    readonly TextBox scanFolder = new() { Dock = DockStyle.Fill };
    readonly CheckBox filterNotEmbedded = new() { Text = "Items not embedded yet", Checked = true, AutoSize = true };
    readonly CheckBox filterMissing = new() { Text = "Embedded items with missing files (saved in editor)", Checked = true, AutoSize = true };
    readonly CheckBox filterOutdated = new() { Text = "Embedded items older than my Items folder", Checked = true, AutoSize = true };
    readonly ListView maps = NewList(("Map", 260), ("Status", 300), ("File", 600));

    // Watch tab
    readonly TextBox watchFolder = new() { Dock = DockStyle.Fill };
    readonly Button watchButton = new() { Text = "Start watching", AutoSize = true };
    MapWatcher? watcher;

    readonly List<Button> actionButtons = [];

    // updates (Help menu; the "available" entry sits at the right end of the menu bar and only shows when there is one)
    readonly AppSettings settings = AppSettings.Load();
    readonly ToolStripMenuItem checkForUpdates = new("Check for updates at start") { CheckOnClick = true };
    readonly ToolStripMenuItem updateAvailable = new() { Alignment = ToolStripItemAlignment.Right, Visible = false, ForeColor = Color.ForestGreen };

    public MainForm(string? initialMap)
    {
        Text = "EmbedItems";
        Size = new Size(1150, 780);
        StartPosition = FormStartPosition.CenterScreen;
        maps.CheckBoxes = true;
        AddPathMenu(items, includeMap: true);
        AddPathMenu(maps, includeMap: false);

        var tm = TrackmaniaFolder.Find(initialMap);
        tmFolder.Text = tm?.Root ?? "";
        watchFolder.Text = tm?.MapsPath ?? "";
        scanFolder.Text = tm?.MapsPath ?? "";
        mapPath.Text = initialMap ?? "";

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(MapTab());
        tabs.TabPages.Add(FolderTab());
        tabs.TabPages.Add(WatchTab());

        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 440 };
        split.Panel1.Controls.Add(tabs);
        var logPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        logPanel.Controls.Add(new Label { Text = "Messages", AutoSize = true, Margin = new Padding(3, 8, 3, 3), Font = new Font("Segoe UI", 9f, FontStyle.Bold) });
        logPanel.Controls.Add(Button("Clear", () => log.Clear()));
        logPanel.Controls.Add(log);
        logPanel.SetColumnSpan(log, 2);
        logPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        logPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        logPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        logPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        split.Panel2.Controls.Add(logPanel);
        // A new file is called "<name> (embedded)" in game by default; replacing the original keeps the name.
        // The checkbox can override that either way.
        saveAsNew.CheckedChanged += (_, _) => embeddedSuffix.Checked = saveAsNew.Checked;
        embeddedSuffix.CheckedChanged += (_, _) => mapName.Text = WithSuffix(mapName.Text, embeddedSuffix.Checked);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(8) };
        root.Controls.Add(PathRow("Trackmania folder (contains Items\\):", tmFolder, BrowseFolder(tmFolder)));
        root.Controls.Add(OptionsGroup());
        root.Controls.Add(split);
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(root);
        var menu = MenuBar();
        Controls.Add(menu);   // added after the Fill panel so it docks above it
        MainMenuStrip = menu;
        Shown += async (_, _) => await UpdateCheckAtStart();

        Log("EmbedItems copies custom items and the files they use (textures, meshes, ...) into a map, so other players see them.");
        Log("Validate your map in the editor first. Don't open and save the embedded map in the editor afterwards: the game removes");
        Log("the extra files again (if it happens, just embed that map once more).");
        if (tm is null) Log("! Could not find your Trackmania folder. Please choose it at the top.");
        if (initialMap is not null) _ = Scan();
    }

    // --- layout ----------------------------------------------------------------------------------------------

    Control OptionsGroup()
    {
        var box = new GroupBox { Text = "Embed options", Dock = DockStyle.Top, AutoSize = true };
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
        flow.Controls.AddRange([overwriteFromDisk, new Label { Width = 30 }, saveAsNew, replaceOriginal]);
        box.Controls.Add(flow);
        new ToolTip().SetToolTip(overwriteFromDisk, "Items embedded by EmbedItems before are replaced by the current version from your Items folder.\n" +
                                                    "Off: they are kept as they are; only missing files are put back.");
        return box;
    }

    MenuStrip MenuBar()
    {
        var help = new ToolStripMenuItem("Help");
        help.DropDownItems.Add("Check for updates", null, (_, _) => _ = CheckForUpdates(manual: true));
        help.DropDownItems.Add(checkForUpdates);
        help.DropDownItems.Add(new ToolStripSeparator());
        help.DropDownItems.Add("About EmbedItems", null, (_, _) => ShowAbout());

        checkForUpdates.Checked = settings.CheckForUpdates == true;
        checkForUpdates.ToolTipText = "Asks GitHub once a day whether a newer version exists (a normal web request, no personal data).\n" +
                                      "Nothing is downloaded: an entry at the right of the menu bar appears when there is a new version.";
        checkForUpdates.CheckedChanged += (_, _) =>
        {
            settings.CheckForUpdates = checkForUpdates.Checked;
            settings.Save();
        };
        updateAvailable.Click += (_, _) => { if (updateAvailable.Tag is string url) OpenUrl(url); };

        var menu = new MenuStrip { ShowItemToolTips = true };
        menu.Items.Add(help);
        menu.Items.Add(updateAvailable);
        return menu;
    }

    const string Author = "Achioto";
    const string AuthorGitHub = "https://github.com/AchimBunke";
    const string RepoUrl = "https://github.com/AchimBunke/EmbedItems";
    const string DiscordUrl = "";   // invite or profile link; the line is hidden while empty

    /// <summary>Small About window with clickable links (a MessageBox can't show links).</summary>
    void ShowAbout()
    {
        var version = UpdateChecker.CurrentVersion();
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(16), AutoSize = true };
        layout.Controls.Add(new Label { Text = $"EmbedItems {(version is null ? "(local build)" : version.ToString(3))}", AutoSize = true, Font = headerFont });
        layout.Controls.Add(new Label { Text = "Embeds custom items and the files they use into Trackmania maps.", AutoSize = true, Margin = new Padding(3, 6, 3, 12) });
        layout.Controls.Add(new Label { Text = $"Made by {Author}", AutoSize = true });
        layout.Controls.Add(Link("GitHub: " + AuthorGitHub, AuthorGitHub));
        if (DiscordUrl.Length > 0) layout.Controls.Add(Link("Discord: " + DiscordUrl, DiscordUrl));
        layout.Controls.Add(Link("Source code, releases and issues: " + RepoUrl, RepoUrl));

        var ok = new Button { Text = "OK", AutoSize = true, DialogResult = DialogResult.OK, Anchor = AnchorStyles.Right, Margin = new Padding(3, 16, 3, 3) };
        layout.Controls.Add(ok);

        using var dialog = new Form
        {
            Text = "About EmbedItems", FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false,
            ShowInTaskbar = false, StartPosition = FormStartPosition.CenterParent, AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink, AcceptButton = ok, CancelButton = ok,
        };
        dialog.Controls.Add(layout);
        dialog.ShowDialog(this);
    }

    static LinkLabel Link(string text, string url)
    {
        var link = new LinkLabel { Text = text, AutoSize = true };
        link.LinkClicked += (_, _) => OpenUrl(url);
        return link;
    }

    static void OpenUrl(string url) =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });

    /// <summary>
    /// First start: asks whether to check for updates. Afterwards checks at most once a day if enabled, and otherwise
    /// shows what the last check found.
    /// </summary>
    async Task UpdateCheckAtStart()
    {
        if (settings.CheckForUpdates is null)
        {
            settings.CheckForUpdates = MessageBox.Show(this,
                "Check for new versions of EmbedItems when it starts?\n\n" +
                "It asks GitHub at most once a day and sends no personal data. Nothing is downloaded automatically:\n" +
                "an entry at the right of the menu bar appears when there is a new release.\n" +
                "You can change this at any time under Help.",
                "EmbedItems", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
            settings.Save();
            checkForUpdates.Checked = settings.CheckForUpdates.Value;
        }
        if (settings.CheckForUpdates != true) return;

        if (settings.LastUpdateCheck is { } last && DateTime.UtcNow - last < TimeSpan.FromDays(1))
            ShowUpdate(UpdateChecker.ParseTag(settings.LatestVersion));
        else
            await CheckForUpdates(manual: false);
    }

    /// <summary>Asks GitHub for the latest version. manual: report every outcome in the messages, not only a new version.</summary>
    async Task CheckForUpdates(bool manual)
    {
        var latest = await UpdateChecker.GetLatestAsync();
        if (latest is not null)
        {
            settings.LastUpdateCheck = DateTime.UtcNow;
            settings.LatestVersion = latest.ToString(3);
            settings.Save();
        }
        bool isNew = ShowUpdate(latest);
        if (!manual) return;

        var current = UpdateChecker.CurrentVersion();
        if (latest is null) Log("! Could not reach GitHub to check for updates.");
        else if (isNew) Log($"EmbedItems {latest.ToString(3)} is available: click it at the right of the menu bar.");
        else if (current is null) Log($"Local build: no version to compare. The latest release is {latest.ToString(3)}.");
        else Log($"You have the latest version ({current.ToString(3)}).");
    }

    /// <summary>Shows the menu-bar entry if latest is newer than this program. Returns whether it is.</summary>
    bool ShowUpdate(Version? latest)
    {
        var current = UpdateChecker.CurrentVersion();
        bool isNew = latest is not null && current is not null && UpdateChecker.IsNewer(latest, current);
        updateAvailable.Visible = isNew;
        if (isNew)
        {
            updateAvailable.Text = $"⬆ EmbedItems {latest!.ToString(3)} available";
            updateAvailable.ToolTipText = "Opens the release page on GitHub";
            updateAvailable.Tag = UpdateChecker.ReleaseUrl(latest);
        }
        return isNew;
    }

    TabPage MapTab()
    {
        var page = new TabPage("Map");
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1 };
        layout.Controls.Add(PathRow("Map:", mapPath, BrowseMap(), Action("Scan", () => Scan())));
        layout.Controls.Add(items);
        var bottom = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        bottom.Controls.AddRange([new Label { Text = "New map name:", AutoSize = true, Margin = new Padding(3, 8, 3, 3) }]);
        mapName.Width = 250;
        mapName.Dock = DockStyle.None;
        bottom.Controls.Add(mapName);
        bottom.Controls.Add(embeddedSuffix);
        bottom.Controls.Add(Action("Embed", EmbedMap));
        bottom.Controls.Add(new Label { Width = 40 });
        bottom.Controls.Add(Action("Extract items...", Extract));
        bottom.Controls.Add(overwriteOnExtract);
        layout.Controls.Add(bottom);
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        page.Controls.Add(layout);
        return page;
    }

    TabPage FolderTab()
    {
        var page = new TabPage("Folder");
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1 };
        layout.Controls.Add(PathRow("Folder:", scanFolder, BrowseFolder(scanFolder), Action("Scan folder", ScanFolder)));
        var filters = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        filters.Controls.AddRange([new Label { Text = "List maps with:", AutoSize = true, Margin = new Padding(3, 6, 3, 3) }, filterNotEmbedded, filterMissing, filterOutdated]);
        layout.Controls.Add(filters);
        layout.Controls.Add(maps);
        var bottom = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        bottom.Controls.Add(Button("Select all", () => SetAllChecked(true)));
        bottom.Controls.Add(Button("Select none", () => SetAllChecked(false)));
        bottom.Controls.Add(new Label { Width = 40 });
        bottom.Controls.Add(Action("Embed selected", EmbedSelected));
        layout.Controls.Add(bottom);
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        page.Controls.Add(layout);
        return page;
    }

    TabPage WatchTab()
    {
        var page = new TabPage("Watch");
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1 };
        layout.Controls.Add(PathRow("Maps folder:", watchFolder, BrowseFolder(watchFolder), watchButton));
        layout.Controls.Add(new Label
        {
            AutoSize = true,
            Text = "While watching, every map you save in the editor (below this folder) is embedded automatically,\n" +
                   "using the embed options above. Leave this window open while you map.",
        });
        watchButton.Click += (_, _) => ToggleWatch();
        page.Controls.Add(layout);
        return page;
    }

    /// <summary>
    /// Right-click menu (Show in Explorer, Copy path) and double-click to open in Explorer, for a list whose
    /// rows carry their file path in Tag.
    /// </summary>
    void AddPathMenu(ListView list, bool includeMap)
    {
        string? Selected() => list.SelectedItems.Count > 0 ? list.SelectedItems[0].Tag as string : null;

        var menu = new ContextMenuStrip();
        menu.Items.Add("Show in Explorer", null, (_, _) => ShowInExplorer(Selected()));
        menu.Items.Add("Copy path", null, (_, _) => { if (Selected() is { } p) Clipboard.SetText(p); });
        if (includeMap)
        {
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Show map in Explorer", null, (_, _) => ShowInExplorer(mapPath.Text));
        }
        menu.Opening += (_, e) => e.Cancel = Selected() is null && !includeMap;
        list.ContextMenuStrip = menu;
        list.DoubleClick += (_, _) => ShowInExplorer(Selected());
    }

    /// <summary>Opens Explorer with the file selected (or its folder, if the file doesn't exist).</summary>
    void ShowInExplorer(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        if (File.Exists(path))
            System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\"");
        else if (Path.GetDirectoryName(path) is { } dir && Directory.Exists(dir))
        {
            Log($"! {Path.GetFileName(path)} is not in that folder (it may only exist embedded in the map). Opening the folder.");
            System.Diagnostics.Process.Start("explorer.exe", $"\"{dir}\"");
        }
        else Log($"! Not found on disk: {path} (it may only exist embedded in the map).");
    }

    static ListView NewList(params (string Title, int Width)[] columns)
    {
        var list = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true };
        foreach (var (title, width) in columns) list.Columns.Add(title, width);
        return list;
    }

    static Control PathRow(string label, TextBox box, params Control[] buttons)
    {
        var row = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2 + buttons.Length };
        row.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(3, 7, 3, 3) });
        row.Controls.Add(box);
        foreach (var b in buttons) row.Controls.Add(b);
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < buttons.Length; i++) row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        return row;
    }

    static Button Button(string text, Action onClick)
    {
        var b = new Button { Text = text, AutoSize = true };
        b.Click += (_, _) => onClick();
        return b;
    }

    /// <summary>A button that runs work in the background; all such buttons are disabled while one runs.</summary>
    Button Action(string text, Func<Task> work)
    {
        var b = new Button { Text = text, AutoSize = true };
        b.Click += async (_, _) => await work();
        actionButtons.Add(b);
        return b;
    }

    Button BrowseFolder(TextBox target) => Button("...", () =>
    {
        using var dialog = new FolderBrowserDialog { InitialDirectory = target.Text };
        if (dialog.ShowDialog(this) == DialogResult.OK) target.Text = dialog.SelectedPath;
    });

    Button BrowseMap() => Button("...", () =>
    {
        using var dialog = new OpenFileDialog { Filter = "Trackmania maps (*.Map.Gbx)|*.Map.Gbx", InitialDirectory = SafeDir(mapPath.Text) ?? SafeDir(scanFolder.Text) };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            mapPath.Text = dialog.FileName;
            _ = Scan();
        }
    });

    static string? SafeDir(string path) =>
        Directory.Exists(path) ? path : File.Exists(path) ? Path.GetDirectoryName(path) : null;

    // --- actions ---------------------------------------------------------------------------------------------

    /// <summary>Scans the map and fills the item list. quiet: refresh the list only (after embedding).</summary>
    async Task Scan(bool quiet = false)
    {
        if (!TryGetInputs(out var tm, needMap: true)) return;
        var path = mapPath.Text;
        var options = Options();
        var plan = await Run(quiet ? null : $"Scan {Path.GetFileName(path)}", () =>
        {
            var p = MapPlanner.Plan(path, tm, options);
            if (!quiet) foreach (var line in PlanText.Describe(p)) Log(line);
            return p;
        }, reportDone: !quiet);
        if (plan is null) return;

        items.Items.Clear();
        foreach (var item in plan.Items)
        {
            var row = new ListViewItem(["Items\\" + PathUtils.ToRef(item.OriginalPath), PlanText.StateLabel(item.State), item.PlacedCount.ToString(), item.Summary])
            {
                Tag = tm.DiskPath(item.OriginalPath),   // for "Show in Explorer" / "Copy path"
            };
            if (item.State == ItemState.CannotEmbed) row.ForeColor = Color.Firebrick;
            else if (item.Changes) row.ForeColor = Color.DarkGreen;
            row.ToolTipText = string.Join("\n", item.Notes);
            items.Items.Add(row);
        }
        items.ShowItemToolTips = true;
        if (scannedMap != path)   // a different map: start from its own name again
        {
            scannedMap = path;
            mapName.Text = WithSuffix(plan.MapName, embeddedSuffix.Checked);
        }
    }

    string? scannedMap;
    const string Suffix = " (embedded)";

    /// <summary>The name with or without " (embedded)" at the end (never twice).</summary>
    static string WithSuffix(string name, bool add)
    {
        var plain = name.EndsWith(Suffix, StringComparison.OrdinalIgnoreCase) ? name[..^Suffix.Length] : name;
        return add ? plain + Suffix : plain;
    }

    async Task EmbedMap()
    {
        if (!TryGetInputs(out var tm, needMap: true)) return;
        var path = mapPath.Text;
        var options = Options() with { MapName = mapName.Text.Trim() };
        if (options.ReplaceOriginal && !Confirm($"Replace the original map file?\n{path}")) return;
        await Run($"Embed {Path.GetFileName(path)}", () => Embedder.EmbedFile(path, tm, options, Log));
        await Scan(quiet: true);
    }

    async Task Extract()
    {
        if (!TryGetInputs(out var tm, needMap: true)) return;
        using var dialog = new FolderBrowserDialog
        {
            Description = "Folder to extract into (normally your Items folder). Items go to their original place below it.",
            InitialDirectory = tm.ItemsPath,
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var path = mapPath.Text;
        var target = dialog.SelectedPath;
        var options = new ExtractOptions { Overwrite = overwriteOnExtract.Checked };
        await Run($"Extract {Path.GetFileName(path)} into {target}", () => Extractor.Extract(path, target, tm, options, Log));
    }

    async Task ScanFolder()
    {
        if (!TryGetInputs(out var tm, needMap: false)) return;
        var folder = scanFolder.Text;
        if (!Directory.Exists(folder)) { Log("! Folder not found: " + folder); return; }
        var filter = (filterNotEmbedded.Checked ? ScanFilter.NotEmbedded : 0)
                   | (filterMissing.Checked ? ScanFilter.MissingFiles : 0)
                   | (filterOutdated.Checked ? ScanFilter.Outdated : 0);
        var options = Options();
        var found = await Run($"Scan folder {folder}", () => FolderScanner.Scan(folder, tm, options, filter, Log));
        if (found is null) return;

        maps.Items.Clear();
        foreach (var m in found)
        {
            var row = new ListViewItem([m.MapName, m.Error is null ? m.Summary : "can't read: " + m.Error, m.MapPath])
            {
                Tag = m.MapPath,
                Checked = m.NeedsWork,
            };
            if (m.Error is not null) row.ForeColor = Color.Firebrick;
            maps.Items.Add(row);
        }
    }

    async Task EmbedSelected()
    {
        if (!TryGetInputs(out var tm, needMap: false)) return;
        var selected = maps.CheckedItems.Cast<ListViewItem>().Select(i => (string)i.Tag!).ToList();
        if (selected.Count == 0) { Log("No maps selected."); return; }
        var options = Options();
        if (options.ReplaceOriginal && !Confirm($"Replace the original files of {selected.Count} map(s)?")) return;
        await Run($"Embed {selected.Count} selected map(s)", () =>
        {
            foreach (var path in selected)
            {
                try { Embedder.EmbedFile(path, tm, options, Log); }
                catch (Exception ex) { Log($"Error: {Path.GetFileName(path)}: {ex.Message}"); }
            }
            return true;
        });
    }

    void ToggleWatch()
    {
        if (watcher is not null)
        {
            watcher.Dispose();
            watcher = null;
            watchButton.Text = "Start watching";
            Log("Stopped watching.");
            return;
        }
        if (!TryGetInputs(out var tm, needMap: false)) return;
        var folder = watchFolder.Text;
        if (!Directory.Exists(folder)) { Log("! Folder not found: " + folder); return; }

        var gate = new object();
        watcher = new MapWatcher(folder, path =>
        {
            lock (gate)   // one map at a time, with the options as they are right now
            {
                var options = (EmbedOptions)Invoke(Options);
                try
                {
                    Log("");
                    Log($"▶ You saved {Path.GetFileName(path)}, embedding it");
                    if (Embedder.EmbedFile(path, tm, options, Log) is { } written) watcher?.Ignore(written);
                    Log("Done.");
                }
                catch (Exception ex) { Log($"Error: {Path.GetFileName(path)}: {ex.Message}"); }
            }
        });
        watchButton.Text = "Stop watching";
        Log($"Watching {folder}. Every map you save there is embedded.");
    }

    // --- helpers ---------------------------------------------------------------------------------------------

    EmbedOptions Options() => new()
    {
        OverwriteFromDisk = overwriteFromDisk.Checked,
        ReplaceOriginal = replaceOriginal.Checked,
    };

    bool TryGetInputs(out TrackmaniaFolder tm, bool needMap)
    {
        tm = null!;
        if (!Directory.Exists(Path.Combine(tmFolder.Text, "Items")))
        {
            Log("! Choose your Trackmania folder at the top (the folder that contains Items\\).");
            return false;
        }
        if (needMap && !File.Exists(mapPath.Text))
        {
            Log("! Choose a map first.");
            return false;
        }
        tm = new TrackmaniaFolder(tmFolder.Text);
        return true;
    }

    bool Confirm(string question) =>
        MessageBox.Show(this, question, "EmbedItems", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes;

    void SetAllChecked(bool value)
    {
        foreach (ListViewItem i in maps.Items) i.Checked = value;
    }

    /// <summary>
    /// Runs work in the background with the action buttons disabled. Returns default on error (logged).
    /// A title starts the output with an empty line and a "▶ title" header, so operations are easy to tell apart.
    /// </summary>
    async Task<T?> Run<T>(string? title, Func<T> work, bool reportDone = true)
    {
        if (title is not null)
        {
            if (log.TextLength > 0) Log("");
            Log("▶ " + title);
        }
        foreach (var b in actionButtons) b.Enabled = false;
        UseWaitCursor = true;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var result = await Task.Run(work);
            if (reportDone) Log($"Done ({watch.Elapsed.TotalSeconds:0.0} s).");
            return result;
        }
        catch (Exception ex)
        {
            Log("Error: " + ex.Message);
            return default;
        }
        finally
        {
            foreach (var b in actionButtons) b.Enabled = true;
            UseWaitCursor = false;
        }
    }

    void Log(string line)
    {
        if (InvokeRequired) { BeginInvoke(() => Log(line)); return; }
        log.SelectionStart = log.TextLength;
        log.SelectionLength = 0;
        log.SelectionColor = LineColor(line);
        log.SelectionFont = line.StartsWith('▶') ? headerFont : log.Font;
        log.AppendText(line + Environment.NewLine);
        log.ScrollToCaret();
    }

    readonly Font headerFont = new("Segoe UI", 10f, FontStyle.Bold);

    // Colors follow the text conventions of PlanText / Embedder (warnings start with "!").
    static Color LineColor(string line)
    {
        var t = line.TrimStart();
        if (t.StartsWith('▶')) return Color.SteelBlue;
        if (t.StartsWith("Error") || t.Contains("can't be embedded")) return Color.Firebrick;
        if (t.StartsWith('!')) return Color.DarkOrange;
        if (t.StartsWith("Done") || t.StartsWith("Saved ")) return Color.ForestGreen;
        if (t.StartsWith("- ")) return Color.DimGray;
        return SystemColors.WindowText;
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        watcher?.Dispose();
        base.OnFormClosed(e);
    }
}
