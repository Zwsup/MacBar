using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

public class MacTopBar : Form
{
    private Timer _clockTimer;
    private Label _clockLabel;
    private Label _batteryLabel;
    private Label _activeAppLabel;
    private Label _hardwareLabel;
    private Label _mediaLabel;
    
    // Spotlight Arama Bileşenleri
    private Panel _searchContainer;
    private TextBox _searchBox;
    private Panel _searchTriggerPill;
    private bool _isSearchOpen = false;

    private Timer _hoverTimer;
    private Timer _animTimer;
    private Timer _activeWindowTimer;
    private Timer _appIconsTimer;
    private Timer _hwTimer;

    private FlowLayoutPanel _runningAppsPanel;
    private FlowLayoutPanel _rightPanel;
    private Panel _volumePopup;
    private TrackBar _volumeTrackBar;

    private PerformanceCounter _cpuCounter;
    private PerformanceCounter _ramCounter;

    private int _targetY = -30;
    private int _currentY = -30;
    private const int BAR_HEIGHT = 30;

    // Modern Tasarım Renkleri
    private readonly Color BG_DARK = Color.FromArgb(28, 28, 30);
    private readonly Color BORDER_COLOR = Color.FromArgb(44, 44, 46);
    private readonly Color HOVER_BG = Color.FromArgb(50, 50, 54);
    private readonly Color CARD_BG = Color.FromArgb(38, 38, 42);
    private readonly Color TEXT_MUTED = Color.FromArgb(170, 170, 175);

    private const int SC_MINIMIZE = 0xF020;
    private const int SC_MAXIMIZE = 0xF030;
    private const int WM_SYSCOMMAND = 0x0112;

    private const byte VK_VOLUME_DOWN = 0xAE;
    private const byte VK_VOLUME_UP = 0xAF;
    private const byte VK_MEDIA_NEXT_TRACK = 0xB0;
    private const byte VK_MEDIA_PREV_TRACK = 0xB1;
    private const byte VK_MEDIA_PLAY_PAUSE = 0xB3;

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int x, y; }

    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    public static extern IntPtr PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "GetClassLongPtr")]
    private static extern IntPtr GetClassLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetClassLong")]
    private static extern IntPtr GetClassLongPtr32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll")]
    public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    private static IntPtr GetClassLongPtr(IntPtr hWnd, int nIndex)
    {
        return IntPtr.Size > 4 ? GetClassLongPtr64(hWnd, nIndex) : GetClassLongPtr32(hWnd, nIndex);
    }

    public MacTopBar()
    {
        this.FormBorderStyle = FormBorderStyle.None;
        this.ShowInTaskbar = false;
        this.TopMost = true;
        this.StartPosition = FormStartPosition.Manual;
        this.BackColor = BG_DARK;
        this.Height = BAR_HEIGHT;
        this.Width = Screen.PrimaryScreen.Bounds.Width;
        this.Location = new Point(0, -BAR_HEIGHT);
        this.DoubleBuffered = true;

        SetupContextMenu();
        InitUI();
        SetupVolumePopup();
        SetupMotion();
        SetupActiveWindowTracker();
        SetupRunningAppIconsTracker();
        SetupHardwareMonitor();

        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    private void OnDisplaySettingsChanged(object sender, EventArgs e)
    {
        this.Width = Screen.PrimaryScreen.Bounds.Width;
        AlignRightPanel();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using (Pen pen = new Pen(BORDER_COLOR, 1))
        {
            e.Graphics.DrawLine(pen, 0, this.Height - 1, this.Width, this.Height - 1);
        }
    }

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams cp = base.CreateParams;
            cp.ExStyle |= 0x80; // WS_EX_TOOLWINDOW
            return cp;
        }
    }

    private void SetupContextMenu()
    {
        ContextMenuStrip contextMenu = new ContextMenuStrip();
        ToolStripMenuItem exitItem = new ToolStripMenuItem("Çıkış Yap");
        exitItem.Click += (s, e) => Application.Exit();
        contextMenu.Items.Add(exitItem);
        this.ContextMenuStrip = contextMenu;
    }

    private void InitUI()
    {
        // --- SOL PANEL ---
        FlowLayoutPanel leftPanel = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            Location = new Point(12, 3),
            Height = BAR_HEIGHT - 3,
            WrapContents = false,
            BackColor = Color.Transparent
        };

        Control logoBox = CreateLogoControl();
        leftPanel.Controls.Add(logoBox);

        _activeAppLabel = new Label
        {
            Text = "",
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 9.25f, FontStyle.Bold),
            AutoSize = true,
            Margin = new Padding(2, 4, 0, 0),
            Visible = false,
            Cursor = Cursors.Default
        };
        leftPanel.Controls.Add(_activeAppLabel);

        leftPanel.Controls.Add(CreateMenuPill("File", CreateFileMenu()));
        leftPanel.Controls.Add(CreateMenuPill("Edit", CreateEditMenu()));
        leftPanel.Controls.Add(CreateMenuPill("View", CreateViewMenu()));
        leftPanel.Controls.Add(CreateMenuPill("Window", CreateWindowMenu()));
        leftPanel.Controls.Add(CreateMenuPill("Help", CreateHelpMenu()));

        // --- SPOTLIGHT ARAMA KAPSÜLÜ ---
        _searchTriggerPill = new Panel
        {
            Size = new Size(26, 22),
            BackColor = Color.Transparent,
            Margin = new Padding(6, 1, 4, 0),
            Cursor = Cursors.Hand
        };
        Label searchIcon = new Label
        {
            Text = "🔍",
            Font = new Font("Segoe UI", 8.5f),
            ForeColor = TEXT_MUTED,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter
        };
        _searchTriggerPill.Controls.Add(searchIcon);
        ApplyPillHover(_searchTriggerPill, searchIcon);
        searchIcon.Click += (s, e) => ToggleSearch();
        _searchTriggerPill.Click += (s, e) => ToggleSearch();
        leftPanel.Controls.Add(_searchTriggerPill);

        _searchContainer = new Panel
        {
            Width = 150,
            Height = 22,
            BackColor = CARD_BG,
            Margin = new Padding(2, 1, 8, 0),
            Visible = false
        };

        _searchBox = new TextBox
        {
            BorderStyle = BorderStyle.None,
            BackColor = CARD_BG,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 8.5f),
            Location = new Point(8, 4),
            Width = 134
        };

        _searchBox.KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                string query = _searchBox.Text.Trim();
                if (!string.IsNullOrEmpty(query))
                {
                    string url = "https://www.google.com/search?q=" + Uri.EscapeDataString(query);
                    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                    ToggleSearch(false);
                }
                e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Escape)
            {
                ToggleSearch(false);
                e.SuppressKeyPress = true;
            }
        };

        _searchBox.LostFocus += (s, e) => ToggleSearch(false);
        _searchContainer.Controls.Add(_searchBox);
        leftPanel.Controls.Add(_searchContainer);

        this.Controls.Add(leftPanel);

        // --- SAĞ PANEL ---
        _rightPanel = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.RightToLeft,
            Height = BAR_HEIGHT - 3,
            WrapContents = false,
            BackColor = Color.Transparent
        };

        // 1. Saat
        _clockLabel = new Label
        {
            ForeColor = Color.FromArgb(245, 245, 247),
            Font = new Font("Segoe UI", 9f, FontStyle.Regular),
            AutoSize = true,
            Margin = new Padding(8, 5, 12, 0)
        };
        _rightPanel.Controls.Add(_clockLabel);

        // 2. Pil
        _batteryLabel = new Label
        {
            ForeColor = TEXT_MUTED,
            Font = new Font("Segoe UI", 8.5f, FontStyle.Regular),
            AutoSize = true,
            Margin = new Padding(6, 5, 4, 0)
        };
        _rightPanel.Controls.Add(_batteryLabel);

        // 3. Donanım Monitörü
        _hardwareLabel = new Label
        {
            Text = "CPU: -% | RAM: -%",
            ForeColor = Color.FromArgb(145, 145, 150),
            Font = new Font("Segoe UI", 8f, FontStyle.Regular),
            AutoSize = true,
            Margin = new Padding(6, 6, 6, 0),
            Cursor = Cursors.Hand
        };
        _hardwareLabel.Click += (s, e) => Process.Start("taskmgr.exe");
        _rightPanel.Controls.Add(_hardwareLabel);

        // 4. Medya (Now Playing)
        _mediaLabel = new Label
        {
            Text = "",
            ForeColor = Color.FromArgb(100, 210, 145),
            Font = new Font("Segoe UI", 8.5f, FontStyle.Regular),
            AutoSize = true,
            Margin = new Padding(6, 5, 6, 0),
            Visible = false,
            Cursor = Cursors.Hand
        };

        ContextMenuStrip mediaMenu = new ContextMenuStrip();
        mediaMenu.Items.Add("▶ / ❚❚  Oynat / Duraklat", null, (s, e) => TriggerMediaKey(VK_MEDIA_PLAY_PAUSE));
        mediaMenu.Items.Add("⏭  Sonraki Şarkı", null, (s, e) => TriggerMediaKey(VK_MEDIA_NEXT_TRACK));
        mediaMenu.Items.Add("⏮  Önceki Şarkı", null, (s, e) => TriggerMediaKey(VK_MEDIA_PREV_TRACK));
        _mediaLabel.Click += (s, e) => mediaMenu.Show(_mediaLabel, new Point(0, _mediaLabel.Height + 4));
        _rightPanel.Controls.Add(_mediaLabel);

        // 5. Ses
        Panel volPill = new Panel
        {
            Size = new Size(24, 22),
            BackColor = Color.Transparent,
            Margin = new Padding(4, 1, 4, 0),
            Cursor = Cursors.Hand
        };
        Label volIcon = new Label
        {
            Text = "🔊",
            Font = new Font("Segoe UI Symbol", 9.5f),
            ForeColor = TEXT_MUTED,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter
        };
        volPill.Controls.Add(volIcon);
        ApplyPillHover(volPill, volIcon);
        volIcon.MouseEnter += (s, e) => ShowVolumePopup(volPill);
        volIcon.MouseWheel += (s, e) => {
            if (e.Delta > 0) ChangeVolume(VK_VOLUME_UP);
            else ChangeVolume(VK_VOLUME_DOWN);
        };
        _rightPanel.Controls.Add(volPill);

        // 6. Çalışan Uygulama İkonları
        _runningAppsPanel = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.RightToLeft,
            Height = BAR_HEIGHT - 3,
            WrapContents = false,
            BackColor = Color.Transparent,
            Margin = new Padding(4, 0, 6, 0)
        };
        _rightPanel.Controls.Add(_runningAppsPanel);

        this.Controls.Add(_rightPanel);

        _rightPanel.SizeChanged += (s, e) => AlignRightPanel();

        _clockTimer = new Timer { Interval = 1000 };
        _clockTimer.Tick += (s, e) => {
            UpdateStatus();
            AlignRightPanel();
        };
        _clockTimer.Start();

        UpdateStatus();
        AlignRightPanel();
    }

    private void AlignRightPanel()
    {
        if (_rightPanel != null)
        {
            int rightWidth = _rightPanel.GetPreferredSize(Size.Empty).Width;
            _rightPanel.Location = new Point(this.Width - rightWidth, 2);
        }
    }

    private Control CreateMenuPill(string text, ContextMenuStrip menu)
    {
        Panel pill = new Panel
        {
            AutoSize = true,
            Height = 22,
            BackColor = Color.Transparent,
            Margin = new Padding(1, 1, 2, 0),
            Padding = new Padding(7, 3, 7, 3),
            Cursor = Cursors.Hand
        };

        Label lbl = new Label
        {
            Text = text,
            ForeColor = TEXT_MUTED,
            Font = new Font("Segoe UI", 8.75f, FontStyle.Regular),
            AutoSize = true,
            Dock = DockStyle.Fill,
            Cursor = Cursors.Hand
        };

        pill.Controls.Add(lbl);
        ApplyPillHover(pill, lbl);

        Action showMenu = () => menu.Show(pill, new Point(0, pill.Height + 5));
        lbl.Click += (s, e) => showMenu();
        pill.Click += (s, e) => showMenu();

        return pill;
    }

    private void ApplyPillHover(Panel pill, Label lbl)
    {
        EventHandler onEnter = (s, e) => {
            pill.BackColor = HOVER_BG;
            lbl.ForeColor = Color.White;
        };
        EventHandler onLeave = (s, e) => {
            pill.BackColor = Color.Transparent;
            lbl.ForeColor = TEXT_MUTED;
        };

        pill.MouseEnter += onEnter;
        pill.MouseLeave += onLeave;
        lbl.MouseEnter += onEnter;
        lbl.MouseLeave += onLeave;
    }

    private void ToggleSearch(bool? forceState = null)
    {
        _isSearchOpen = forceState.HasValue ? forceState.Value : !_isSearchOpen;

        if (_isSearchOpen)
        {
            _searchContainer.Visible = true;
            _searchTriggerPill.BackColor = HOVER_BG;
            _searchBox.Text = "";
            _searchBox.Focus();
        }
        else
        {
            _searchContainer.Visible = false;
            _searchTriggerPill.BackColor = Color.Transparent;
            this.ActiveControl = null;
        }
    }

    private void TriggerMediaKey(byte vk)
    {
        keybd_event(vk, 0, 0, UIntPtr.Zero);
        keybd_event(vk, 0, 2, UIntPtr.Zero);
    }

    private void SetupHardwareMonitor()
    {
        try
        {
            _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            _ramCounter = new PerformanceCounter("Memory", "% Committed Bytes In Use");

            _hwTimer = new Timer { Interval = 1500 };
            _hwTimer.Tick += (s, e) =>
            {
                try
                {
                    int cpu = (int)_cpuCounter.NextValue();
                    int ram = (int)_ramCounter.NextValue();
                    _hardwareLabel.Text = string.Format("CPU: {0}%  RAM: {1}%", cpu, ram);
                }
                catch { }
            };
            _hwTimer.Start();
        }
        catch { }
    }

    private void SetupVolumePopup()
    {
        _volumePopup = new Panel
        {
            Size = new Size(130, 36),
            BackColor = Color.FromArgb(32, 32, 36),
            Visible = false
        };

        _volumeTrackBar = new TrackBar
        {
            Minimum = 0,
            Maximum = 10,
            Value = 5,
            TickStyle = TickStyle.None,
            Dock = DockStyle.Fill
        };

        int lastVal = 5;
        _volumeTrackBar.ValueChanged += (s, e) => {
            if (_volumeTrackBar.Value > lastVal)
                ChangeVolume(VK_VOLUME_UP);
            else if (_volumeTrackBar.Value < lastVal)
                ChangeVolume(VK_VOLUME_DOWN);

            lastVal = _volumeTrackBar.Value;
        };

        _volumePopup.Controls.Add(_volumeTrackBar);
        this.Controls.Add(_volumePopup);

        _volumePopup.MouseLeave += (s, e) => {
            POINT p;
            GetCursorPos(out p);
            if (!_volumePopup.Bounds.Contains(this.PointToClient(new Point(p.x, p.y))))
                _volumePopup.Visible = false;
        };
    }

    private void ShowVolumePopup(Control anchor)
    {
        Point pt = anchor.Location;
        _volumePopup.Location = new Point(pt.X - 100, BAR_HEIGHT);
        _volumePopup.Visible = true;
        _volumePopup.BringToFront();
    }

    private void ChangeVolume(byte vk)
    {
        keybd_event(vk, 0, 0, UIntPtr.Zero);
        keybd_event(vk, 0, 2, UIntPtr.Zero);
    }

    private void SetupRunningAppIconsTracker()
    {
        _appIconsTimer = new Timer { Interval = 2000 };
        _appIconsTimer.Tick += (s, e) => RefreshRunningIcons();
        _appIconsTimer.Start();
        RefreshRunningIcons();
    }

    private void RefreshRunningIcons()
    {
        List<IntPtr> windows = new List<IntPtr>();
        string currentPlaying = null;

        EnumWindows((hWnd, lParam) => {
            if (!IsWindowVisible(hWnd)) return true;

            int exStyle = GetWindowLong(hWnd, -20);
            if ((exStyle & 0x80) != 0) return true;

            StringBuilder sb = new StringBuilder(256);
            GetWindowText(hWnd, sb, 256);
            string title = sb.ToString();

            if (!string.IsNullOrEmpty(title) && hWnd != this.Handle)
            {
                StringBuilder classSb = new StringBuilder(256);
                GetClassName(hWnd, classSb, 256);
                string cls = classSb.ToString();

                if (cls.Contains("Chrome_WidgetWin") && title.Contains(" - ") && !title.Contains("Google Chrome") && !title.Contains("Visual Studio"))
                {
                    currentPlaying = title;
                }

                if (cls != "Progman" && cls != "WorkerW" && cls != "Shell_TrayWnd" && cls != "Windows.UI.Core.CoreWindow")
                {
                    windows.Add(hWnd);
                }
            }
            return true;
        }, IntPtr.Zero);

        if (!string.IsNullOrEmpty(currentPlaying))
        {
            if (currentPlaying.Length > 24) currentPlaying = currentPlaying.Substring(0, 22) + "..";
            _mediaLabel.Text = "🎵 " + currentPlaying;
            _mediaLabel.Visible = true;
        }
        else
        {
            _mediaLabel.Visible = false;
        }

        _runningAppsPanel.SuspendLayout();

        // Önceki PictureBox ve Bitmap kaynaklarını serbest bırak (Bellek Sızıntısı Önlemi)
        foreach (Control c in _runningAppsPanel.Controls)
        {
            PictureBox pbOld = c as PictureBox;
            if (pbOld != null && pbOld.Image != null)
            {
                pbOld.Image.Dispose();
            }
            c.Dispose();
        }
        _runningAppsPanel.Controls.Clear();

        int count = 0;
        foreach (IntPtr wnd in windows)
        {
            if (count >= 6) break;

            Icon appIcon = GetAppIcon(wnd);
            if (appIcon != null)
            {
                PictureBox pb = new PictureBox
                {
                    Size = new Size(16, 16),
                    Image = appIcon.ToBitmap(),
                    SizeMode = PictureBoxSizeMode.Zoom,
                    Margin = new Padding(3, 4, 3, 0),
                    Cursor = Cursors.Hand
                };

                // Çıkarılan Icon handle'ını serbest bırak
                DestroyIcon(appIcon.Handle);
                appIcon.Dispose();

                IntPtr targetWnd = wnd;
                pb.Click += (s, e) => SetForegroundWindow(targetWnd);

                _runningAppsPanel.Controls.Add(pb);
                count++;
            }
        }
        _runningAppsPanel.ResumeLayout();
    }

    private Icon GetAppIcon(IntPtr hWnd)
    {
        try
        {
            IntPtr hIcon = SendMessage(hWnd, 0x007F, (IntPtr)2, IntPtr.Zero);
            if (hIcon == IntPtr.Zero)
                hIcon = SendMessage(hWnd, 0x007F, (IntPtr)0, IntPtr.Zero);
            if (hIcon == IntPtr.Zero)
                hIcon = GetClassLongPtr(hWnd, -34);
            if (hIcon == IntPtr.Zero)
                hIcon = GetClassLongPtr(hWnd, -14);

            if (hIcon != IntPtr.Zero)
                return Icon.FromHandle(hIcon);
        }
        catch { }
        return null;
    }

    private Control CreateLogoControl()
    {
        Panel logoPill = new Panel
        {
            Size = new Size(24, 22),
            Margin = new Padding(0, 1, 8, 0),
            Cursor = Cursors.Hand,
            BackColor = Color.Transparent
        };

        string logoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logo.png");
        if (File.Exists(logoPath))
        {
            PictureBox pb = new PictureBox
            {
                Image = Image.FromFile(logoPath),
                SizeMode = PictureBoxSizeMode.Zoom,
                Dock = DockStyle.Fill
            };
            logoPill.Controls.Add(pb);
        }
        else
        {
            logoPill.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (Brush b = new SolidBrush(Color.White))
                {
                    e.Graphics.FillRectangle(b, 4, 4, 6, 6);
                    e.Graphics.FillRectangle(b, 12, 4, 6, 6);
                    e.Graphics.FillRectangle(b, 4, 12, 6, 6);
                    e.Graphics.FillRectangle(b, 12, 12, 6, 6);
                }
            };
        }

        logoPill.MouseEnter += (s, e) => logoPill.BackColor = HOVER_BG;
        logoPill.MouseLeave += (s, e) => logoPill.BackColor = Color.Transparent;

        ContextMenuStrip winMenu = new ContextMenuStrip();
        winMenu.Items.Add("Bu Bilgisayar Hakkında", null, (s, e) => Process.Start("ms-settings:about"));
        winMenu.Items.Add("Sistem Ayarları...", null, (s, e) => Process.Start("ms-settings:"));
        winMenu.Items.Add(new ToolStripSeparator());
        winMenu.Items.Add("Yeniden Başlat", null, (s, e) => Process.Start("shutdown", "/r /t 0"));
        winMenu.Items.Add("Sistemi Kapat", null, (s, e) => Process.Start("shutdown", "/s /t 0"));
        winMenu.Items.Add(new ToolStripSeparator());
        winMenu.Items.Add("Bar'ı Kapat", null, (s, e) => Application.Exit());

        logoPill.Click += (s, e) => winMenu.Show(logoPill, new Point(0, logoPill.Height + 5));
        return logoPill;
    }

    private ContextMenuStrip CreateFileMenu()
    {
        ContextMenuStrip m = new ContextMenuStrip();
        m.Items.Add("Yeni Dosya Gezgini (Win+E)", null, (s, e) => Process.Start("explorer.exe"));
        m.Items.Add("Görev Yöneticisi", null, (s, e) => Process.Start("taskmgr.exe"));
        return m;
    }

    private ContextMenuStrip CreateEditMenu()
    {
        ContextMenuStrip m = new ContextMenuStrip();
        m.Items.Add("Geri Al (Undo)", null, (s, e) => SendKeystroke("^z"));
        m.Items.Add(new ToolStripSeparator());
        m.Items.Add("Kes (Cut)", null, (s, e) => SendKeystroke("^x"));
        m.Items.Add("Kopyala (Copy)", null, (s, e) => SendKeystroke("^c"));
        m.Items.Add("Yapıştır (Paste)", null, (s, e) => SendKeystroke("^v"));
        m.Items.Add(new ToolStripSeparator());
        m.Items.Add("Tümünü Seç (Select All)", null, (s, e) => SendKeystroke("^a"));
        return m;
    }

    private ContextMenuStrip CreateViewMenu()
    {
        ContextMenuStrip m = new ContextMenuStrip();
        m.Items.Add("Masaüstünü Göster", null, (s, e) => {
            Type shellType = Type.GetTypeFromProgID("Shell.Application");
            dynamic shell = Activator.CreateInstance(shellType);
            shell.ToggleDesktop();
        });
        return m;
    }

    private ContextMenuStrip CreateWindowMenu()
    {
        ContextMenuStrip m = new ContextMenuStrip();
        m.Items.Add("Simge Durumuna Küçült (Minimize)", null, (s, e) => {
            IntPtr active = GetForegroundWindow();
            if (active != IntPtr.Zero && active != this.Handle)
                PostMessage(active, WM_SYSCOMMAND, (IntPtr)SC_MINIMIZE, IntPtr.Zero);
        });
        m.Items.Add("Ekranı Kapla / Geri Yükle", null, (s, e) => {
            IntPtr active = GetForegroundWindow();
            if (active != IntPtr.Zero && active != this.Handle)
                PostMessage(active, WM_SYSCOMMAND, (IntPtr)SC_MAXIMIZE, IntPtr.Zero);
        });
        return m;
    }

    private ContextMenuStrip CreateHelpMenu()
    {
        ContextMenuStrip m = new ContextMenuStrip();
        m.Items.Add("Windows İpuçları ve Yardım", null, (s, e) => Process.Start("ms-contact-support:"));
        m.Items.Add("WinTopBar Hakkında", null, (s, e) => MessageBox.Show("WinTopBar v2.0\nModern Native Top Menu Bar", "Hakkında"));
        return m;
    }

    private void SendKeystroke(string keys)
    {
        Timer t = new Timer { Interval = 100 };
        t.Tick += (s, e) => {
            t.Stop();
            t.Dispose();
            SendKeys.SendWait(keys);
        };
        t.Start();
    }

    private void UpdateStatus()
    {
        _clockLabel.Text = DateTime.Now.ToString("ddd d MMM  HH:mm");

        PowerStatus power = SystemInformation.PowerStatus;
        if (power.BatteryChargeStatus == BatteryChargeStatus.NoSystemBattery)
        {
            _batteryLabel.Text = "⚡ Masaüstü";
        }
        else
        {
            int percent = (int)(power.BatteryLifePercent * 100);
            string chargingSymbol = (power.PowerLineStatus == PowerLineStatus.Online) ? "⚡" : "";
            _batteryLabel.Text = string.Format("{0}% {1}", percent, chargingSymbol);
        }
    }

    private void SetupActiveWindowTracker()
    {
        _activeWindowTimer = new Timer { Interval = 250 };
        _activeWindowTimer.Tick += (s, e) =>
        {
            IntPtr hWnd = GetForegroundWindow();
            if (hWnd == IntPtr.Zero || hWnd == this.Handle) return;

            StringBuilder classSb = new StringBuilder(256);
            GetClassName(hWnd, classSb, 256);
            string className = classSb.ToString();

            bool isDesktopOrShell = className == "Progman" || 
                                    className == "WorkerW" || 
                                    className == "Shell_TrayWnd" || 
                                    className == "Shell_SecondaryTrayWnd";

            if (isDesktopOrShell)
            {
                if (_activeAppLabel.Visible)
                {
                    _activeAppLabel.Text = "";
                    _activeAppLabel.Visible = false;
                    _activeAppLabel.Margin = new Padding(2, 4, 0, 0);
                }
                return;
            }

            StringBuilder sb = new StringBuilder(256);
            if (GetWindowText(hWnd, sb, 256) > 0)
            {
                string title = sb.ToString().Trim();

                if (string.IsNullOrEmpty(title))
                {
                    if (_activeAppLabel.Visible)
                    {
                        _activeAppLabel.Text = "";
                        _activeAppLabel.Visible = false;
                        _activeAppLabel.Margin = new Padding(2, 4, 0, 0);
                    }
                    return;
                }

                if (title.Contains("-"))
                {
                    string[] parts = title.Split('-');
                    title = parts[parts.Length - 1].Trim();
                }

                if (title.Length > 28)
                {
                    title = title.Substring(0, 26) + "..";
                }

                if (_activeAppLabel.Text != title)
                {
                    _activeAppLabel.Text = title;
                    _activeAppLabel.Visible = true;
                    _activeAppLabel.Margin = new Padding(2, 4, 10, 0);
                }
            }
        };
        _activeWindowTimer.Start();
    }

    private void SetupMotion()
    {
        _hoverTimer = new Timer { Interval = 25 };
        _hoverTimer.Tick += (s, e) =>
        {
            POINT p;
            if (GetCursorPos(out p))
            {
                bool inBar = (p.y <= BAR_HEIGHT && this.Bounds.Contains(p.x, p.y));
                bool inVolume = _volumePopup.Visible && _volumePopup.Bounds.Contains(this.PointToClient(new Point(p.x, p.y)));
                bool isSearching = _isSearchOpen;

                if (p.y <= 2 || inBar || inVolume || isSearching)
                {
                    _targetY = 0;
                }
                else if (p.y > BAR_HEIGHT + 36)
                {
                    _targetY = -BAR_HEIGHT;
                    _volumePopup.Visible = false;
                }
            }
        };
        _hoverTimer.Start();

        _animTimer = new Timer { Interval = 15 };
        _animTimer.Tick += (s, e) =>
        {
            if (_currentY != _targetY)
            {
                int diff = _targetY - _currentY;
                int step = (int)Math.Ceiling(Math.Abs(diff) * 0.28);
                if (step < 1) step = 1;

                if (_currentY < _targetY)
                    _currentY = Math.Min(_targetY, _currentY + step);
                else
                    _currentY = Math.Max(_targetY, _currentY - step);

                this.Location = new Point(0, _currentY);
            }
        };
        _animTimer.Start();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            if (_cpuCounter != null) _cpuCounter.Dispose();
            if (_ramCounter != null) _ramCounter.Dispose();
            if (_clockTimer != null) _clockTimer.Dispose();
            if (_hoverTimer != null) _hoverTimer.Dispose();
            if (_animTimer != null) _animTimer.Dispose();
            if (_activeWindowTimer != null) _activeWindowTimer.Dispose();
            if (_appIconsTimer != null) _appIconsTimer.Dispose();
            if (_hwTimer != null) _hwTimer.Dispose();
        }
        base.Dispose(disposing);
    }

    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MacTopBar());
    }
}