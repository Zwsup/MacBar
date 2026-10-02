using System;
using System.Drawing;
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

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int x, y;
    }

    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

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

        // Windows Logosu
        Label winLogo = new Label
        {
            Text = "⊞",
            ForeColor = Color.White,
            Font = new Font("Segoe UI Symbol", 12, FontStyle.Regular),
            AutoSize = true,
            Margin = new Padding(0, 0, 10, 0),
            Cursor = Cursors.Hand
        };

        ContextMenuStrip winMenu = new ContextMenuStrip();
        winMenu.Items.Add("Bu Bilgisayar Hakkında", null, (s, e) => System.Diagnostics.Process.Start("ms-settings:about"));
        winMenu.Items.Add("Sistem Ayarları...", null, (s, e) => System.Diagnostics.Process.Start("ms-settings:"));
        winMenu.Items.Add(new ToolStripSeparator());
        winMenu.Items.Add("Yeniden Başlat", null, (s, e) => System.Diagnostics.Process.Start("shutdown", "/r /t 0"));
        winMenu.Items.Add("Sistemi Kapat", null, (s, e) => System.Diagnostics.Process.Start("shutdown", "/s /t 0"));
        winMenu.Items.Add(new ToolStripSeparator());
        winMenu.Items.Add("Bar'ı Kapat", null, (s, e) => Application.Exit());

        winLogo.Click += (s, e) => winMenu.Show(winLogo, new Point(0, winLogo.Height + 4));
        leftPanel.Controls.Add(winLogo);

        // Aktif Uygulama Başlığı (Başlangıçta masaüstü olduğu için boş)
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

        // Menü Öğeleri
        string[] menuItems = { "File", "Edit", "View", "Window", "Help" };
        foreach (string item in menuItems)
        {
            Label lbl = new Label
            {
                Text = item,
                ForeColor = Color.FromArgb(180, 180, 180),
                Font = new Font("Segoe UI", 9, FontStyle.Regular),
                AutoSize = true,
                Margin = new Padding(0, 3, 14, 0),
                Cursor = Cursors.Hand
            };

            lbl.MouseEnter += (s, e) => lbl.ForeColor = Color.White;
            lbl.MouseLeave += (s, e) => lbl.ForeColor = Color.FromArgb(180, 180, 180);

            leftPanel.Controls.Add(lbl);
        }

        this.Controls.Add(leftPanel);

        // Sağ Taraf: Pil / Masaüstü Durumu
        _batteryLabel = new Label
        {
            ForeColor = Color.FromArgb(200, 200, 200),
            Font = new Font("Segoe UI", 9, FontStyle.Regular),
            AutoSize = true,
            Location = new Point(this.Width - 230, 5)
        };
        this.Controls.Add(_batteryLabel);

        // Sağ Taraf: Saat
        _clockLabel = new Label
        {
            ForeColor = Color.FromArgb(240, 240, 240),
            Font = new Font("Segoe UI", 9, FontStyle.Regular),
            AutoSize = true,
            Location = new Point(this.Width - 140, 5)
        };
        this.Controls.Add(_clockLabel);

        _clockTimer = new Timer { Interval = 1000 };
        _clockTimer.Tick += (s, e) => UpdateStatus();
        _clockTimer.Start();
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        _clockLabel.Text = DateTime.Now.ToString("ddd d MMM  HH:mm");
        _clockLabel.Location = new Point(this.Width - _clockLabel.PreferredWidth - 16, 5);

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

        _batteryLabel.Location = new Point(_clockLabel.Location.X - _batteryLabel.PreferredWidth - 16, 5);
    }

    private void SetupActiveWindowTracker()
    {
        _activeWindowTimer = new Timer { Interval = 250 };
        _activeWindowTimer.Tick += (s, e) =>
        {
            IntPtr hWnd = GetForegroundWindow();
            if (hWnd == IntPtr.Zero || hWnd == this.Handle) return;

            // Sınıf adını kontrol et (Masaüstü veya Görev Çubuğu mu?)
            StringBuilder classSb = new StringBuilder(256);
            GetClassName(hWnd, classSb, 256);
            string className = classSb.ToString();

            // Progman / WorkerW (Masaüstü), Shell_TrayWnd (Windows Görev Çubuğu)
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

                if (title.Length > 16)
                {
                    title = title.Substring(0, 14) + "..";
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