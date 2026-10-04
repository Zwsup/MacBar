using System;
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
    private Timer _hoverTimer;
    private Timer _animTimer;
    private Timer _activeWindowTimer;

    private int _targetY = -28;
    private int _currentY = -28;
    private const int BAR_HEIGHT = 28;

    private const int SC_MINIMIZE = 0xF020;
    private const int SC_MAXIMIZE = 0xF030;
    private const int WM_SYSCOMMAND = 0x0112;

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
    public static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

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
        SetupMotion();
        SetupActiveWindowTracker();
    }

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams cp = base.CreateParams;
            cp.ExStyle |= 0x80;         // WS_EX_TOOLWINDOW
            cp.ExStyle |= 0x08000000;   // WS_EX_NOACTIVATE
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
        // --- SOL PANEL (Logo, Dinamik İsim, Menüler) ---
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

        // Dinamik Genişleyen Aktif Pencere Başlığı (Kırpma yok)
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

        this.Controls.Add(leftPanel);

        // --- SAĞ PANEL (Sistem Tepsisi / Tray, Ses, Pil, Saat) ---
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

        // 2. Pil Durumu
        _batteryLabel = new Label
        {
            ForeColor = Color.FromArgb(200, 200, 200),
            Font = new Font("Segoe UI", 9, FontStyle.Regular),
            AutoSize = true,
            Margin = new Padding(6, 5, 6, 0)
        };
        rightPanel.Controls.Add(_batteryLabel);

        // 3. Hızlı Sistem Ses Kontrolü
        Label volBtn = new Label
        {
            Text = "🔊",
            ForeColor = Color.FromArgb(200, 200, 200),
            Font = new Font("Segoe UI Symbol", 9, FontStyle.Regular),
            AutoSize = true,
            Margin = new Padding(6, 4, 6, 0),
            Cursor = Cursors.Hand
        };
        volBtn.Click += (s, e) => System.Diagnostics.Process.Start("ms-settings:sound");
        rightPanel.Controls.Add(volBtn);

        // 4. Tepsi Taşma Menüsü Butonu (Fotoğraftaki Arka Plan Uygulamalarını Açar)
        Label trayBtn = new Label
        {
            Text = "˄",
            ForeColor = Color.FromArgb(220, 220, 220),
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            AutoSize = true,
            Margin = new Padding(8, 3, 6, 0),
            Cursor = Cursors.Hand
        };
        trayBtn.Click += (s, e) => ToggleSystemTrayOverflow();
        rightPanel.Controls.Add(trayBtn);

        this.Controls.Add(rightPanel);

        // Sağ paneli ekranın sağına yapıştırma
        Action alignRight = () => {
            rightPanel.Location = new Point(this.Width - rightPanel.PreferredWidth, 2);
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

    private void ToggleSystemTrayOverflow()
    {
        // Windows'un arka planda çalışan uygulama ikonları penceresini bulup çağırır
        IntPtr trayWnd = FindWindow("NotifyIconOverflowWindow", null);
        if (trayWnd != IntPtr.Zero)
        {
            POINT p;
            GetCursorPos(out p);
            // Pencereyi barın hemen altına hizala ve aç
            SetWindowPos(trayWnd, IntPtr.Zero, p.x - 70, BAR_HEIGHT + 2, 0, 0, 0x0001 | 0x0040);
            ShowWindow(trayWnd, 5); // SW_SHOW
        }
        else
        {
            // Windows 11'in yeni arayüzünde hızlı eylemler penceresini tetikler
            SendKeys.SendWait("^{ESC}");
        }
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
        winMenu.Items.Add("Bu Bilgisayar Hakkında", null, (s, e) => System.Diagnostics.Process.Start("ms-settings:about"));
        winMenu.Items.Add("Sistem Ayarları...", null, (s, e) => System.Diagnostics.Process.Start("ms-settings:"));
        winMenu.Items.Add(new ToolStripSeparator());
        winMenu.Items.Add("Yeniden Başlat", null, (s, e) => System.Diagnostics.Process.Start("shutdown", "/r /t 0"));
        winMenu.Items.Add("Sistemi Kapat", null, (s, e) => System.Diagnostics.Process.Start("shutdown", "/s /t 0"));
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
        m.Items.Add("Yeni Dosya Gezgini (Win+E)", null, (s, e) => System.Diagnostics.Process.Start("explorer.exe"));
        m.Items.Add("Görev Yöneticisi", null, (s, e) => System.Diagnostics.Process.Start("taskmgr.exe"));
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
        m.Items.Add("Windows İpuçları ve Yardım", null, (s, e) => System.Diagnostics.Process.Start("ms-contact-support:"));
        m.Items.Add("WinTopBar Hakkında", null, (s, e) => MessageBox.Show("WinTopBar v1.1\nmacOS Style Native Top Menu Bar for Windows", "Hakkında"));
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

                // Uygulama son eklerini temizle (örn. "Belge - Visual Studio Code" -> "Visual Studio Code")
                if (title.Contains("-"))
                {
                    string[] parts = title.Split('-');
                    title = parts[parts.Length - 1].Trim();
                }

                // Geniş başlıkları kesmeden göster (Ekranı tamamen taşmayacak 40 karakter sınırı)
                if (title.Length > 40)
                {
                    title = title.Substring(0, 38) + "..";
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
                if (p.y <= 2 || (p.y <= BAR_HEIGHT && this.Bounds.Contains(p.x, p.y)))
                {
                    _targetY = 0;
                }
                else if (p.y > BAR_HEIGHT + 2)
                {
                    _targetY = -BAR_HEIGHT;
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