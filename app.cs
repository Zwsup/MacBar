using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

public class MacTopBar : Form
{
    private Timer _clockTimer;
    private Label _clockLabel;
    private Label _batteryLabel;
    private Label _activeAppLabel;
    private Label _hardwareLabel;
    private Label _mediaLabel;
    private TextBox _searchBox;

    private Timer _hoverTimer;
    private Timer _animTimer;
    private Timer _activeWindowTimer;
    private Timer _appIconsTimer;
    private Timer _hwTimer;

    private FlowLayoutPanel _runningAppsPanel;
    private Panel _volumePopup;
    private TrackBar _volumeTrackBar;

    private PerformanceCounter _cpuCounter;
    private PerformanceCounter _ramCounter;

    private int _targetY = -28;
    private int _currentY = -28;
    private const int BAR_HEIGHT = 28;

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
        this.BackColor = Color.FromArgb(24, 24, 26);
        this.Height = BAR_HEIGHT;
        this.Width = Screen.PrimaryScreen.Bounds.Width;
        this.Location = new Point(0, -BAR_HEIGHT);

        SetupContextMenu();
        InitUI();
        SetupVolumePopup();
        SetupMotion();
        SetupActiveWindowTracker();
        SetupRunningAppIconsTracker();
        SetupHardwareMonitor();
    }

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams cp = base.CreateParams;
            cp.ExStyle |= 0x80;         // WS_EX_TOOLWINDOW
            // Arama kutusuna klavye odağı verebilmek için WS_EX_NOACTIVATE kaldırıldı
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
            Location = new Point(10, 3),
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
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            AutoSize = true,
            Margin = new Padding(0, 3, 0, 0),
            Visible = false,
            Cursor = Cursors.Default
        };
        leftPanel.Controls.Add(_activeAppLabel);

        leftPanel.Controls.Add(CreateMenuLabel("File", CreateFileMenu()));
        leftPanel.Controls.Add(CreateMenuLabel("Edit", CreateEditMenu()));
        leftPanel.Controls.Add(CreateMenuLabel("View", CreateViewMenu()));
        leftPanel.Controls.Add(CreateMenuLabel("Window", CreateWindowMenu()));
        leftPanel.Controls.Add(CreateMenuLabel("Help", CreateHelpMenu()));

        // --- GOOGLE SPOTLIGHT ARAMA KUTUSU ---
        Panel searchContainer = new Panel
        {
            Width = 150,
            Height = 20,
            BackColor = Color.FromArgb(38, 38, 42),
            Margin = new Padding(8, 2, 8, 0)
        };

        Label searchIcon = new Label
        {
            Text = "🔍",
            Font = new Font("Segoe UI", 7),
            ForeColor = Color.FromArgb(160, 160, 160),
            Size = new Size(16, 16),
            Location = new Point(3, 2),
            BackColor = Color.Transparent
        };
        searchContainer.Controls.Add(searchIcon);

        _searchBox = new TextBox
        {
            BorderStyle = BorderStyle.None,
            BackColor = Color.FromArgb(38, 38, 42),
            ForeColor = Color.FromArgb(150, 150, 150),
            Font = new Font("Segoe UI", 8),
            Text = "Google'da ara...",
            Location = new Point(20, 3),
            Width = 125
        };

        _searchBox.Enter += (s, e) =>
        {
            if (_searchBox.Text == "Google'da ara...")
            {
                _searchBox.Text = "";
                _searchBox.ForeColor = Color.White;
            }
        };

        _searchBox.Leave += (s, e) =>
        {
            if (string.IsNullOrEmpty(_searchBox.Text.Trim()))
            {
                _searchBox.Text = "Google'da ara...";
                _searchBox.ForeColor = Color.FromArgb(150, 150, 150);
            }
        };

        _searchBox.KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                string query = _searchBox.Text.Trim();
                if (!string.IsNullOrEmpty(query) && query != "Google'da ara...")
                {
                    string url = "https://www.google.com/search?q=" + Uri.EscapeDataString(query);
                    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                    
                    _searchBox.Text = "Google'da ara...";
                    _searchBox.ForeColor = Color.FromArgb(150, 150, 150);
                    this.ActiveControl = null; // Odağı kaldır
                }
                e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Escape)
            {
                _searchBox.Text = "Google'da ara...";
                _searchBox.ForeColor = Color.FromArgb(150, 150, 150);
                this.ActiveControl = null;
                e.SuppressKeyPress = true;
            }
        };

        searchContainer.Controls.Add(_searchBox);
        leftPanel.Controls.Add(searchContainer);

        this.Controls.Add(leftPanel);

        // --- SAĞ PANEL ---
        FlowLayoutPanel rightPanel = new FlowLayoutPanel
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
            ForeColor = Color.FromArgb(240, 240, 240),
            Font = new Font("Segoe UI", 9, FontStyle.Regular),
            AutoSize = true,
            Margin = new Padding(12, 5, 12, 0)
        };
        rightPanel.Controls.Add(_clockLabel);

        // 2. Pil
        _batteryLabel = new Label
        {
            ForeColor = Color.FromArgb(200, 200, 200),
            Font = new Font("Segoe UI", 9, FontStyle.Regular),
            AutoSize = true,
            Margin = new Padding(6, 5, 6, 0)
        };
        rightPanel.Controls.Add(_batteryLabel);

        // 3. Donanım Monitörü
        _hardwareLabel = new Label
        {
            Text = "CPU: -% | RAM: -%",
            ForeColor = Color.FromArgb(175, 175, 180),
            Font = new Font("Segoe UI", 8, FontStyle.Regular),
            AutoSize = true,
            Margin = new Padding(8, 6, 6, 0),
            Cursor = Cursors.Hand
        };
        _hardwareLabel.Click += (s, e) => Process.Start("taskmgr.exe");
        rightPanel.Controls.Add(_hardwareLabel);

        // 4. Medya (Now Playing)
        _mediaLabel = new Label
        {
            Text = "",
            ForeColor = Color.FromArgb(140, 215, 140),
            Font = new Font("Segoe UI", 9, FontStyle.Regular),
            AutoSize = true,
            Margin = new Padding(8, 5, 6, 0),
            Visible = false,
            Cursor = Cursors.Hand
        };

        ContextMenuStrip mediaMenu = new ContextMenuStrip();
        mediaMenu.Items.Add("▶ / ❚❚  Oynat / Duraklat", null, (s, e) => TriggerMediaKey(VK_MEDIA_PLAY_PAUSE));
        mediaMenu.Items.Add("⏭  Sonraki Şarkı", null, (s, e) => TriggerMediaKey(VK_MEDIA_NEXT_TRACK));
        mediaMenu.Items.Add("⏮  Önceki Şarkı", null, (s, e) => TriggerMediaKey(VK_MEDIA_PREV_TRACK));
        _mediaLabel.Click += (s, e) => mediaMenu.Show(_mediaLabel, new Point(0, _mediaLabel.Height + 4));

        rightPanel.Controls.Add(_mediaLabel);

        // 5. Ses
        Label volBtn = new Label
        {
            Text = "🔊",
            ForeColor = Color.FromArgb(220, 220, 220),
            Font = new Font("Segoe UI Symbol", 10, FontStyle.Regular),
            AutoSize = true,
            Margin = new Padding(6, 4, 6, 0),
            Cursor = Cursors.Hand
        };
        volBtn.MouseEnter += (s, e) => ShowVolumePopup(volBtn);
        volBtn.MouseWheel += (s, e) => {
            if (e.Delta > 0) ChangeVolume(VK_VOLUME_UP);
            else ChangeVolume(VK_VOLUME_DOWN);
        };
        rightPanel.Controls.Add(volBtn);

        // 6. Çalışan Uygulama İkonları
        _runningAppsPanel = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.RightToLeft,
            Height = BAR_HEIGHT - 3,
            WrapContents = false,
            BackColor = Color.Transparent,
            Margin = new Padding(6, 0, 8, 0)
        };
        rightPanel.Controls.Add(_runningAppsPanel);

        this.Controls.Add(rightPanel);

        Action alignRight = () => {
            int rightWidth = rightPanel.GetPreferredSize(Size.Empty).Width;
            rightPanel.Location = new Point(this.Width - rightWidth, 2);
        };

        rightPanel.SizeChanged += (s, e) => alignRight();

        _clockTimer = new Timer { Interval = 1000 };
        _clockTimer.Tick += (s, e) => {
            UpdateStatus();
            alignRight();
        };
        _clockTimer.Start();

        UpdateStatus();
        alignRight();
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
        PictureBox pb = new PictureBox
        {
            Size = new Size(16, 16),
            Margin = new Padding(2, 3, 10, 0),
            Cursor = Cursors.Hand,
            BackColor = Color.Transparent
        };

        string logoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logo.png");
        if (File.Exists(logoPath))
        {
            pb.Image = Image.FromFile(logoPath);
            pb.SizeMode = PictureBoxSizeMode.Zoom;
        }
        else
        {
            pb.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (Brush b = new SolidBrush(Color.White))
                {
                    e.Graphics.FillRectangle(b, 0, 0, 7, 7);
                    e.Graphics.FillRectangle(b, 9, 0, 7, 7);
                    e.Graphics.FillRectangle(b, 0, 9, 7, 7);
                    e.Graphics.FillRectangle(b, 9, 9, 7, 7);
                }
            };
        }

        ContextMenuStrip winMenu = new ContextMenuStrip();
        winMenu.Items.Add("Bu Bilgisayar Hakkında", null, (s, e) => Process.Start("ms-settings:about"));
        winMenu.Items.Add("Sistem Ayarları...", null, (s, e) => Process.Start("ms-settings:"));
        winMenu.Items.Add(new ToolStripSeparator());
        winMenu.Items.Add("Yeniden Başlat", null, (s, e) => Process.Start("shutdown", "/r /t 0"));
        winMenu.Items.Add("Sistemi Kapat", null, (s, e) => Process.Start("shutdown", "/s /t 0"));
        winMenu.Items.Add(new ToolStripSeparator());
        winMenu.Items.Add("Bar'ı Kapat", null, (s, e) => Application.Exit());

        pb.Click += (s, e) => winMenu.Show(pb, new Point(0, pb.Height + 5));
        return pb;
    }

    private Label CreateMenuLabel(string text, ContextMenuStrip menu)
    {
        Label lbl = new Label
        {
            Text = text,
            ForeColor = Color.FromArgb(180, 180, 180),
            Font = new Font("Segoe UI", 9, FontStyle.Regular),
            AutoSize = true,
            Margin = new Padding(0, 3, 14, 0),
            Cursor = Cursors.Hand
        };

        lbl.MouseEnter += (s, e) => lbl.ForeColor = Color.White;
        lbl.MouseLeave += (s, e) => lbl.ForeColor = Color.FromArgb(180, 180, 180);
        lbl.Click += (s, e) => menu.Show(lbl, new Point(0, lbl.Height + 5));

        return lbl;
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
        m.Items.Add("WinTopBar Hakkında", null, (s, e) => MessageBox.Show("WinTopBar v1.4\nmacOS Style Native Top Menu Bar for Windows", "Hakkında"));
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
                    _activeAppLabel.Margin = new Padding(0, 3, 0, 0);
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
                        _activeAppLabel.Margin = new Padding(0, 3, 0, 0);
                    }
                    return;
                }

                if (title.Contains("-"))
                {
                    string[] parts = title.Split('-');
                    title = parts[parts.Length - 1].Trim();
                }

                if (title.Length > 30)
                {
                    title = title.Substring(0, 28) + "..";
                }

                if (_activeAppLabel.Text != title)
                {
                    _activeAppLabel.Text = title;
                    _activeAppLabel.Visible = true;
                    _activeAppLabel.Margin = new Padding(0, 3, 14, 0);
                }
            }
        };
        _activeWindowTimer.Start();
    }

    private void SetupMotion()
    {
        _hoverTimer = new Timer { Interval = 30 };
        _hoverTimer.Tick += (s, e) =>
        {
            POINT p;
            if (GetCursorPos(out p))
            {
                bool inBar = (p.y <= BAR_HEIGHT && this.Bounds.Contains(p.x, p.y));
                bool inVolume = _volumePopup.Visible && _volumePopup.Bounds.Contains(this.PointToClient(new Point(p.x, p.y)));
                bool isSearching = (_searchBox != null && _searchBox.Focused);

                // Arama kutusuna yazı yazılırken veya fare bardayken AÇIK TUT
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
                int step = 4;
                if (_currentY < _targetY)
                    _currentY = Math.Min(_targetY, _currentY + step);
                else
                    _currentY = Math.Max(_targetY, _currentY - step);

                this.Location = new Point(0, _currentY);
            }
        };
        _animTimer.Start();
    }

    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MacTopBar());
    }
}