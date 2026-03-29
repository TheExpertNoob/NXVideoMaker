using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace NXVideoMaker;

public sealed class MainForm : Form
{
    // ── Config ────────────────────────────────────────────────────────────────
    private AppConfig _config = AppConfig.Load();

    // ── Title Information fields ──────────────────────────────────────────────
    private readonly TextBox _txtTitle   = new();
    private readonly TextBox _txtAuthor  = new();
    private readonly TextBox _txtVersion = new();
    private readonly TextBox _txtTitleId = new();

    // ── File fields ───────────────────────────────────────────────────────────
    private readonly TextBox _txtIcon  = new();
    private readonly TextBox _txtVideo = new();
    private readonly TextBox _txtKeys  = new();

    // ── Icon preview ──────────────────────────────────────────────────────────
    private readonly PictureBox _iconPreview = new();

    // ── Build controls ────────────────────────────────────────────────────────
    private readonly Button      _btnBuild  = new();
    private readonly Button      _btnCancel = new();
    private readonly RichTextBox _log       = new();

    private CancellationTokenSource? _cts;

    // ── DPI-aware sizing — all dimensions derived from font, set in BuildUi ──
    private int _rowH;       // row height
    private int _labelW;     // label column width
    private int _btnW;       // browse button width

    // ─────────────────────────────────────────────────────────────────────────
    public MainForm()
    {
        // Font must be set before BuildUi so measurements are correct
        Font = new Font("Segoe UI", 10f);
        BuildUi();
        LoadConfig();
        CheckFirstRun();
    }

    // ── UI construction ───────────────────────────────────────────────────────
    private void BuildUi()
    {
        // Derive all sizing from the current font — DPI-safe
        using var g = CreateGraphics();
        float dpi   = g.DpiX;
        float scale = dpi / 96f;

        _labelW = (int)g.MeasureString("Display Version:", Font).Width + 16;
        _rowH   = (int)(Font.GetHeight(g) * 1.8f);
        _btnW   = (int)g.MeasureString("…", Font).Width + (int)(24 * scale);

        Text            = "NX Video Maker";
        MinimumSize     = new Size((int)(700 * scale), (int)(540 * scale));
        Size            = new Size((int)(800 * scale), (int)(600 * scale));
        FormBorderStyle = FormBorderStyle.Sizable;
        StartPosition   = FormStartPosition.CenterScreen;
        BackColor       = Color.FromArgb(30, 30, 30);
        ForeColor       = Color.White;

        try
        {
            using var stream = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream("NXVideoMaker.Resources.icon.ico");
            if (stream != null) Icon = new Icon(stream);
        }
        catch { }

        int previewSize = (int)(256 * scale);
        int logHeight   = (int)(180 * scale);

        // ── Root: [top content] / [button row] / [log] ───────────────────────
        var root = new TableLayoutPanel
        {
            Dock      = DockStyle.Fill, ColumnCount = 1, RowCount = 3,
            Padding   = new Padding((int)(10 * scale)),
            BackColor = Color.Transparent,
        };
        root.RowStyles.Clear();
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // content grows
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // buttons
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, logHeight));

        // ── Top: [left groups] | [preview] ───────────────────────────────────
        var topRow = new TableLayoutPanel
        {
            Dock      = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
            BackColor = Color.Transparent,
        };
        topRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        topRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, previewSize + (int)(16 * scale)));
        topRow.AutoSize = true;
        topRow.AutoSizeMode = AutoSizeMode.GrowAndShrink;

        var leftStack = new TableLayoutPanel
        {
            Dock      = DockStyle.Fill, ColumnCount = 1, RowCount = 2,
            BackColor = Color.Transparent,
        };
        leftStack.AutoSize = true;
        leftStack.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        leftStack.Dock = DockStyle.Top;
        leftStack.Controls.Add(BuildTitleGroup(), 0, 0);
        leftStack.Controls.Add(BuildFilesGroup(), 0, 1);

        topRow.Controls.Add(leftStack,                     0, 0);
        topRow.Controls.Add(BuildPreviewPanel(previewSize), 1, 0);

        root.Controls.Add(topRow,           0, 0);
        root.Controls.Add(BuildButtonRow(), 0, 1);
        root.Controls.Add(BuildLogPanel(),  0, 2);

        Controls.Add(root);
    }

    // ── Title Information group ───────────────────────────────────────────────
    private GroupBox BuildTitleGroup()
    {
        var grp = MakeGroup("Title Information");
        var tbl = MakeFieldTable(4);

        AddField(tbl, 0, "Title:",           _txtTitle);
        AddField(tbl, 1, "Author:",          _txtAuthor);
        AddField(tbl, 2, "Display Version:", _txtVersion);

        _txtTitleId.CharacterCasing = CharacterCasing.Upper;
        _txtTitleId.MaxLength       = 16;
        _txtTitleId.Font            = new Font("Consolas", Font.Size);
        AddField(tbl, 3, "Title ID:",        _txtTitleId);

        grp.Controls.Add(tbl);
        return grp;
    }

    // ── Files group ───────────────────────────────────────────────────────────
    private GroupBox BuildFilesGroup()
    {
        var grp = MakeGroup("Files");
        var tbl = MakeFieldTable(3);

        AddBrowseField(tbl, 0, "Icon:",         _txtIcon,  BrowseIcon);
        AddBrowseField(tbl, 1, "Video Folder:",       _txtVideo, BrowseVideoFolder);
        AddBrowseField(tbl, 2, "Keys File:",          _txtKeys,  BrowseKeys);

        grp.Controls.Add(tbl);
        return grp;
    }

    // ── Icon preview panel ────────────────────────────────────────────────────
    private Panel BuildPreviewPanel(int previewSize)
    {
        _iconPreview.Size        = new Size(previewSize, previewSize);
        _iconPreview.SizeMode    = PictureBoxSizeMode.Zoom;
        _iconPreview.BackColor   = Color.FromArgb(20, 20, 20);
        _iconPreview.BorderStyle = BorderStyle.FixedSingle;
        _iconPreview.Anchor      = AnchorStyles.Top | AnchorStyles.Left;

        var placeholder = new Label
        {
            Text      = "No icon\nselected",
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Color.FromArgb(100, 100, 100),
            Dock      = DockStyle.Fill,
        };
        _iconPreview.Controls.Add(placeholder);

        var panel = new Panel
        {
            Dock      = DockStyle.Fill,
            BackColor = Color.Transparent,
            Padding   = new Padding(8, 4, 0, 0),
        };
        _iconPreview.Location = new Point(8, 4);
        _iconPreview.Anchor   = AnchorStyles.Top | AnchorStyles.Left;
        panel.Controls.Add(_iconPreview);
        return panel;
    }

    // ── Button row ────────────────────────────────────────────────────────────
    private Panel BuildButtonRow()
    {
        using var g    = CreateGraphics();
        int btnHeight  = _rowH + 4;
        int buildWidth = (int)g.MeasureString("Build NSP", Font).Width + (int)(32 * (g.DpiX / 96f));
        int cancelWidth= (int)g.MeasureString("Cancel",    Font).Width + (int)(32 * (g.DpiX / 96f));

        _btnBuild.Text      = "Build NSP";
        _btnBuild.Height    = btnHeight;
        _btnBuild.Width     = buildWidth;
        _btnBuild.BackColor = Color.FromArgb(0, 122, 204);
        _btnBuild.ForeColor = Color.White;
        _btnBuild.Font      = new Font(Font, FontStyle.Bold);
        _btnBuild.FlatStyle = FlatStyle.Flat;
        _btnBuild.FlatAppearance.BorderSize = 0;
        _btnBuild.Click    += OnBuild;

        _btnCancel.Text      = "Cancel";
        _btnCancel.Height    = btnHeight;
        _btnCancel.Width     = cancelWidth;
        _btnCancel.BackColor = Color.FromArgb(100, 30, 30);
        _btnCancel.ForeColor = Color.White;
        _btnCancel.Font      = new Font(Font, FontStyle.Bold);
        _btnCancel.FlatStyle = FlatStyle.Flat;
        _btnCancel.FlatAppearance.BorderSize = 0;
        _btnCancel.Enabled   = false;
        _btnCancel.Click    += OnCancel;

        var panel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock          = DockStyle.Fill,
            Height        = btnHeight + 12,
            BackColor     = Color.Transparent,
            Padding       = new Padding(0, 4, 0, 4),
        };
        panel.Controls.Add(_btnBuild);
        panel.Controls.Add(_btnCancel);
        return panel;
    }

    // ── Log panel ─────────────────────────────────────────────────────────────
    private Panel BuildLogPanel()
    {
        var label = new Label
        {
            Text      = "Build Output",
            Dock      = DockStyle.Top,
            ForeColor = Color.FromArgb(180, 180, 180),
            AutoSize  = false,
            Height    = _rowH,
        };

        _log.Dock        = DockStyle.Fill;
        _log.ReadOnly    = true;
        _log.BackColor   = Color.FromArgb(12, 12, 12);
        _log.ForeColor   = Color.FromArgb(200, 200, 200);
        _log.Font        = new Font("Consolas", Font.Size - 0.5f);
        _log.ScrollBars  = RichTextBoxScrollBars.Vertical;
        _log.BorderStyle = BorderStyle.None;
        _log.WordWrap    = false;

        var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 4, 0, 0) };
        panel.Controls.Add(_log);
        panel.Controls.Add(label);
        return panel;
    }

    // ── Config I/O ────────────────────────────────────────────────────────────
    private void LoadConfig()
    {
        _txtTitle.Text   = _config.Title;
        _txtAuthor.Text  = _config.Author;
        _txtVersion.Text = _config.DisplayVersion;
        _txtTitleId.Text = _config.TitleId;
        _txtIcon.Text    = _config.IconPath;
        _txtVideo.Text   = _config.VideoFolder;
        _txtKeys.Text    = _config.KeysPath;
        UpdateIconPreview(_config.IconPath);
    }

    private void SaveConfig()
    {
        _config.Title          = _txtTitle.Text.Trim();
        _config.Author         = _txtAuthor.Text.Trim();
        _config.DisplayVersion = _txtVersion.Text.Trim();
        _config.TitleId        = _txtTitleId.Text.Trim();
        _config.IconPath       = _txtIcon.Text.Trim();
        _config.VideoFolder    = _txtVideo.Text.Trim();
        _config.KeysPath       = _txtKeys.Text.Trim();
        _config.Save();
    }

    // ── First-run keys.dat check ──────────────────────────────────────────────
    private void CheckFirstRun()
    {
        string exeDir      = AppContext.BaseDirectory;
        string keysDefault = Path.Combine(exeDir, "keys.dat");

        if (File.Exists(keysDefault))
        {
            if (string.IsNullOrWhiteSpace(_txtKeys.Text))
                _txtKeys.Text = keysDefault;
            return;
        }

        string template = Path.Combine(exeDir, "keys.dat.template");
        if (!File.Exists(template))
        {
            File.WriteAllText(template,
                "# NX Video Maker — Keys Template\n" +
                "# Rename this file to keys.dat and fill in your actual key values.\n" +
                "# Lines starting with # are comments.\n\n" +
                "header_key = xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx\n" +
                "key_area_key_application_00 = xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx\n" +
                "key_area_key_application_01 = xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx\n" +
                "# .\n" +
                "# .\n" +
                "# .\n" +
                "titlekek_00 = xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx\n" +
                "titlekek_01 = xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx\n" +
                "# .\n" +
                "# .\n" +
                "# .\n" +
                "# ... add all required keys for your target firmware\n");

            AppendLog("─────────────────────────────────────────");
            AppendLog(" FIRST RUN: keys.dat not found.");
            AppendLog(" A template has been created at:");
            AppendLog($"   {template}");
            AppendLog(" Rename it to keys.dat and fill in your keys,");
            AppendLog(" or use the Keys File browser to select your file.");
            AppendLog("─────────────────────────────────────────");
        }
    }

    // ── Browse handlers ───────────────────────────────────────────────────────
    private void BrowseIcon()
    {
        using var dlg = new OpenFileDialog
        {
            Title  = "Select Icon (256×256 JPG)",
            Filter = "JPEG Image|*.jpg;*.jpeg",
        };
        if (dlg.ShowDialog() != DialogResult.OK) return;

        // Validate dimensions
        try
        {
            using var ms  = new MemoryStream(File.ReadAllBytes(dlg.FileName));
            using var img = Image.FromStream(ms);
            if (img.Width != 256 || img.Height != 256)
            {
                MessageBox.Show(
                    $"Selected image is {img.Width}×{img.Height}.\n" +
                    "The icon must be exactly 256×256 pixels.",
                    "Invalid Icon Size",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }
        }
        catch
        {
            MessageBox.Show(
                "Could not read the selected file as an image.",
                "Invalid File",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        _txtIcon.Text = dlg.FileName;
        UpdateIconPreview(dlg.FileName);
    }

    private void BrowseVideoFolder()
    {
        using var dlg = new FolderBrowserDialog
        {
            Description            = "Select video folder (must contain index.html or video.mp4)",
            UseDescriptionForTitle = true,
        };
        if (dlg.ShowDialog() == DialogResult.OK)
            _txtVideo.Text = dlg.SelectedPath;
    }

    private void BrowseKeys()
    {
        using var dlg = new OpenFileDialog
        {
            Title  = "Select Keys File",
            Filter = "Keys File|*.dat;*.keys|All Files|*.*",
        };
        if (dlg.ShowDialog() == DialogResult.OK)
            _txtKeys.Text = dlg.FileName;
    }

    // ── Icon preview ──────────────────────────────────────────────────────────
    private void UpdateIconPreview(string? path)
    {
        bool hasLabel = _iconPreview.Controls.Count > 0 && _iconPreview.Controls[0] is Label;

        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            try
            {
                using var ms = new MemoryStream(File.ReadAllBytes(path));
                var img = Image.FromStream(ms);
                _iconPreview.Image?.Dispose();
                _iconPreview.Image = img;
                if (hasLabel) _iconPreview.Controls[0].Visible = false;
                return;
            }
            catch { }
        }

        _iconPreview.Image?.Dispose();
        _iconPreview.Image = null;
        if (hasLabel) _iconPreview.Controls[0].Visible = true;
    }

    // ── Build ─────────────────────────────────────────────────────────────────
    private async void OnBuild(object? sender, EventArgs e)
    {
        SaveConfig();
        _log.Clear();
        SetBusy(true);

        _cts = new CancellationTokenSource();
        var progress = new Progress<string>(AppendLog);

        var cfg = new BuildConfig(
            Title:          _txtTitle.Text.Trim(),
            Author:         _txtAuthor.Text.Trim(),
            DisplayVersion: _txtVersion.Text.Trim(),
            TitleId:        _txtTitleId.Text.Trim(),
            IconPath:       _txtIcon.Text.Trim(),
            VideoFolder:    _txtVideo.Text.Trim(),
            KeysPath:       _txtKeys.Text.Trim(),
            KeyGen:         _config.KeyGen,
            SdkVersion:     _config.SdkVersion,
            SysVersion:     _config.SysVersion,
            ExeDir:         AppContext.BaseDirectory
        );

        try
        {
            await Task.Run(() => BuildRunner.RunAsync(cfg, progress, _cts.Token), _cts.Token);
        }
        catch (OperationCanceledException)
        {
            AppendLog("[cancelled] Build cancelled by user.");
        }
        catch (Exception ex)
        {
            AppendLog($"[error] {ex.Message}");
        }
        finally
        {
            SetBusy(false);
            _cts?.Dispose();
            _cts = null;
        }
    }

    private void OnCancel(object? sender, EventArgs e) => _cts?.Cancel();

    private void SetBusy(bool busy)
    {
        _btnBuild.Enabled  = !busy;
        _btnCancel.Enabled = busy;
        Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
    }

    private void AppendLog(string text)
    {
        if (_log.InvokeRequired) { _log.BeginInvoke(AppendLog, text); return; }
        _log.AppendText(text + "\n");
        _log.ScrollToCaret();
    }

    // ── UI helpers ────────────────────────────────────────────────────────────
    private static GroupBox MakeGroup(string title) => new()
    {
        Text      = title,
        Dock      = DockStyle.Top,
        AutoSize  = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        ForeColor = Color.FromArgb(180, 180, 180),
        BackColor = Color.Transparent,
        Padding   = new Padding(10),
        Margin    = new Padding(0, 0, 0, 10),
    };

    private static Label MakeLabel(string text) => new()
    {
        Text      = text,
        TextAlign = ContentAlignment.MiddleLeft,
        ForeColor = Color.FromArgb(220, 220, 220),
        AutoSize  = false,
        Dock      = DockStyle.Fill,
    };

    private TableLayoutPanel MakeFieldTable(int rows)
    {
        var tbl = new TableLayoutPanel
        {
            Dock        = DockStyle.Top,
            ColumnCount = 2,
            AutoSize    = true,
            AutoSizeMode= AutoSizeMode.GrowAndShrink,
            Padding     = new Padding(0, 6, 0, 6),
        };
    
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, _labelW));   // label
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); // input
    
        return tbl;
    }

    private void AddField(TableLayoutPanel tbl, int row, string labelText, TextBox box)
    {
        box.Dock        = DockStyle.Fill;
        box.BackColor   = Color.FromArgb(45, 45, 45);
        box.ForeColor   = Color.White;
        box.BorderStyle = BorderStyle.FixedSingle;
        box.Margin      = new Padding(0, 2, 4, 2);
    
        var label = MakeLabel(labelText);
        label.Margin = new Padding(0, 6, 8, 0);
    
        tbl.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        tbl.Controls.Add(label, 0, row);
        tbl.Controls.Add(box,   1, row);
    }

    private void AddBrowseField(
        TableLayoutPanel tbl, int row, string labelText,
        TextBox box, Action browse)
    {
        box.Dock        = DockStyle.Fill;
        box.BackColor   = Color.FromArgb(45, 45, 45);
        box.ForeColor   = Color.White;
        box.BorderStyle = BorderStyle.FixedSingle;
        box.Margin      = new Padding(0, 2, 4, 2);
    
        var btn = new Button
        {
            Text      = "…",
            Dock      = DockStyle.Fill,
            Width     = 30,
            BackColor = Color.FromArgb(55, 55, 55),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Margin    = new Padding(2, 2, 0, 2),
        };
        btn.Click += (_, _) => browse();
    
        var inner = new TableLayoutPanel
        {
            Dock        = DockStyle.Fill,
            ColumnCount = 2,
            AutoSize    = true,
        };
        inner.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        inner.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
    
        inner.Controls.Add(box, 0, 0);
        inner.Controls.Add(btn, 1, 0);
    
        var label = MakeLabel(labelText);
        label.Margin = new Padding(0, 6, 8, 0);
    
        tbl.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        tbl.Controls.Add(label, 0, row);
        tbl.Controls.Add(inner, 1, row);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _iconPreview.Image?.Dispose();
        base.Dispose(disposing);
    }
}