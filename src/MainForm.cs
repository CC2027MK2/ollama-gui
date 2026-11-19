using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Threading;
using System.Windows.Forms;

namespace OllamaSwitch
{
    static class Ui
    {
        /* 字号体系（全局统一，单位 pt）：
         * 7.5f 小注 / 8.5f 次要 / 9.5f 正文 / 11f 标题 / 12.5f 强调 / 15f 数据大字
         * 任何窗口的字体都从这里取，不要各写各的 */
        public const float FSmall = 8.2f;
        public const float FSub = 9f;
        public const float FBody = 9.8f;
        public const float FTitle = 11f;
        public const float FStrong = 12.5f;
        public const float FBig = 15f;

        public const int BtnH = 32;   // 统一按钮高度

        public static readonly Color Bg = ColorTranslator.FromHtml("#0d1117");
        public static readonly Color PanelBg = ColorTranslator.FromHtml("#151b23");
        public static readonly Color Panel2 = ColorTranslator.FromHtml("#1b232d");
        public static readonly Color Line = ColorTranslator.FromHtml("#25303c");
        public static readonly Color Ink = ColorTranslator.FromHtml("#e6edf3");
        public static readonly Color Muted = ColorTranslator.FromHtml("#8b9aab");
        public static readonly Color Dim = ColorTranslator.FromHtml("#6b7887");
        public static readonly Color Brand = ColorTranslator.FromHtml("#3b82f6");
        public static readonly Color Ok = ColorTranslator.FromHtml("#22c55e");
        public static readonly Color Warn = ColorTranslator.FromHtml("#f59e0b");
        public static readonly Color Bad = ColorTranslator.FromHtml("#ef4444");
        public static readonly Color[] Series = new Color[] {
            ColorTranslator.FromHtml("#3b82f6"),
            ColorTranslator.FromHtml("#a855f7"),
            ColorTranslator.FromHtml("#14b8a6"),
            ColorTranslator.FromHtml("#f59e0b"),
            ColorTranslator.FromHtml("#ec4899")
        };

        public static Font F(float size, FontStyle style)
        {
            return new Font("Microsoft YaHei UI", size, style, GraphicsUnit.Point);
        }

        public static Label Label(string text, float size, FontStyle style, Color color)
        {
            Label l = new Label();
            l.Text = text;
            l.AutoSize = true;
            l.Font = F(size, style);
            l.ForeColor = color;
            l.BackColor = Color.Transparent;
            return l;
        }

        // 向上找到第一个不透明父容器的背景色，绘制圆角控件前先铺满用它，
        // 否则圆角路径之外的旧像素会留下一圈浅色毛边
        public static Color ParentBack(Control c)
        {
            Control p = c == null ? null : c.Parent;
            while (p != null)
            {
                Color b = p.BackColor;
                if (b.A == 255 && b != Color.Transparent) return b;
                p = p.Parent;
            }
            return Bg;
        }

        public static GraphicsPath RoundRect(Rectangle r, int radius)
        {
            GraphicsPath p = new GraphicsPath();
            int d = radius * 2;
            if (d > r.Width) d = r.Width;
            if (d > r.Height) d = r.Height;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        /* ---------------- 统一按钮：全程序只有这一种按钮 ----------------
         * 三种样式：BtnPrimary 主操作（蓝底）/ BtnSecondary 次操作（深灰底）/ BtnDanger 危险（红字深红底）
         * 统一圆角、统一高度 BtnH、统一字号，悬停有高亮 */
        public const int BtnPrimary = 0;
        public const int BtnSecondary = 1;
        public const int BtnDanger = 2;

        public static Button Button(string text, int style)
        {
            RButton b = new RButton();
            b.Text = text;
            b.Font = F(FBody, FontStyle.Bold);
            b.Size = new Size(112, BtnH);
            b.Cursor = Cursors.Hand;
            ApplyButtonStyle(b, style);
            b.MouseEnter += delegate { b.BackColor = ControlPaint.Light(b.BackColor, 0.10f); };
            b.MouseLeave += delegate { ApplyButtonStyle(b, style); };
            return b;
        }

        public static void ApplyButtonStyle(Button b, int style)
        {
            if (style == BtnPrimary)
            {
                b.BackColor = Brand;
                b.ForeColor = Color.White;
            }
            else if (style == BtnDanger)
            {
                b.BackColor = ColorTranslator.FromHtml("#2a1c1c");
                b.ForeColor = ColorTranslator.FromHtml("#ffb4b4");
            }
            else
            {
                b.BackColor = Panel2;
                b.ForeColor = Ink;
            }
        }

        // 简短说明统一用悬停提示，不再占界面空间
        public static ToolTip Tip(Control c, string text)
        {
            ToolTip t = new ToolTip();
            t.InitialDelay = 350;
            t.SetToolTip(c, text);
            return t;
        }

        // 把 ListView 表头改成深色，避免默认白色在深色主题下刺眼
        public static void DarkHeader(ListView lv)
        {
            DarkHeader h = new DarkHeader();
            h.Attach(lv);
        }
    }

    // 深色表头：关闭 ListView 表头的亮色主题，用 OwnerDraw 按深色配色重画
    public class DarkHeader : NativeWindow
    {
        public void Attach(ListView lv)
        {
            try
            {
                lv.OwnerDraw = true;
                lv.DrawColumnHeader += DrawHeader;
                lv.DrawItem += DrawItemKeepDefault;
                lv.DrawSubItem += DrawSubItemKeepDefault;
                lv.HandleCreated += delegate { DisableHeaderTheme(lv); };
                DisableHeaderTheme(lv);
            }
            catch (Exception) { }
        }

        static void DisableHeaderTheme(ListView lv)
        {
            try
            {
                IntPtr hdr = SendMessage(lv.Handle, 0x101F, IntPtr.Zero, IntPtr.Zero); // LVM_GETHEADER
                if (hdr != IntPtr.Zero) SetWindowTheme(hdr, "", "");
            }
            catch (Exception) { }
        }

        void DrawHeader(object sender, DrawListViewColumnHeaderEventArgs e)
        {
            Rectangle r = e.Bounds;
            using (SolidBrush b = new SolidBrush(ColorTranslator.FromHtml("#1b232d")))
                e.Graphics.FillRectangle(b, r);
            using (Pen p = new Pen(Ui.Line))
                e.Graphics.DrawRectangle(p, r.X, r.Y, r.Width - 1, r.Height - 1);
            TextRenderer.DrawText(e.Graphics, e.Header.Text, Ui.F(Ui.FSub, FontStyle.Bold), r, Ui.Ink,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }

        void DrawItemKeepDefault(object sender, DrawListViewItemEventArgs e)
        {
            e.DrawDefault = true;
        }

        void DrawSubItemKeepDefault(object sender, DrawListViewSubItemEventArgs e)
        {
            e.DrawDefault = true;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [System.Runtime.InteropServices.DllImport("uxtheme.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        static extern int SetWindowTheme(IntPtr hWnd, string sub, string name);
    }

    // 圆角按钮：用 OwnerDraw 画出圆角矩形，代替系统方块按钮
    public class RButton : Button
    {
        public int Radius = 8;

        public RButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            UseVisualStyleBackColor = false;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            // 先用父容器背景铺满整块，再画圆角，避免圆角外的残留像素形成毛边
            using (SolidBrush bg = new SolidBrush(Ui.ParentBack(this)))
                e.Graphics.FillRectangle(bg, ClientRectangle);

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            Color back = Enabled ? BackColor : ColorTranslator.FromHtml("#222a33");
            Color fore = Enabled ? ForeColor : Ui.Dim;
            using (GraphicsPath gp = Ui.RoundRect(r, Radius))
            using (SolidBrush b = new SolidBrush(back))
                e.Graphics.FillPath(b, gp);
            TextRenderer.DrawText(e.Graphics, Text, Font, r, fore,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    // 单选按钮：系统 Flat 样式在深色底上只剩一个小白点，这里自绘圆环 + 实心点
    public class RRadio : RadioButton
    {
        public RRadio()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            AutoSize = false;
            Height = 24;
            Cursor = Cursors.Hand;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            using (SolidBrush bg = new SolidBrush(Ui.ParentBack(this)))
                e.Graphics.FillRectangle(bg, ClientRectangle);

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            int d = 14;
            Rectangle ring = new Rectangle(1, (Height - d) / 2, d, d);
            Color mark = !Enabled ? ColorTranslator.FromHtml("#3a4550")
                        : (Checked ? Ui.Brand : ColorTranslator.FromHtml("#5a6b7d"));
            Color text = !Enabled ? Ui.Dim : (Checked ? Ui.Ink : Ui.Muted);

            using (Pen p = new Pen(mark, Checked ? 2.2f : 1.4f))
                e.Graphics.DrawEllipse(p, ring);
            if (Checked)
                using (SolidBrush b = new SolidBrush(mark))
                    e.Graphics.FillEllipse(b, ring.X + 4, ring.Y + 4, d - 8, d - 8);

            Rectangle tr = new Rectangle(d + 8, 0, Width - d - 9, Height);
            TextRenderer.DrawText(e.Graphics, Text, Font, tr, text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }

        protected override void OnCheckedChanged(EventArgs e)
        {
            base.OnCheckedChanged(e);
            Invalidate();
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            Invalidate();
        }
    }

    // 原生 ComboBox 的下拉箭头始终按系统亮色绘制，这里在绘制完成后补成暗色，
    // 否则深色卡片上会露出一块白方块
    public class CBox : ComboBox
    {
        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg != 0x000F || !IsHandleCreated || Width < 80 || Height < 16) return;
            int aw = 22;
            using (Graphics g = Graphics.FromHwnd(Handle))
            {
                using (SolidBrush b = new SolidBrush(BackColor))
                    g.FillRectangle(b, new Rectangle(Width - aw, 1, aw, Height - 2));
                using (Pen p = new Pen(Ui.Line))
                    g.DrawLine(p, Width - aw, 2, Width - aw, Height - 4);
                int cx = Width - aw / 2 - 1;
                int cy = Height / 2 - 1;
                Point[] pts = { new Point(cx - 4, cy - 2), new Point(cx + 4, cy - 2), new Point(cx, cy + 3) };
                using (SolidBrush b = new SolidBrush(Ui.Dim))
                    g.FillPolygon(b, pts);
                // 系统默认的亮色描边也一并覆盖掉
                using (Pen p = new Pen(Ui.Line))
                    g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
            }
        }
    }

    // 原生滚动条（多行文本框 / 列表）在深色界面上是一条白色亮块，
    // 这里在系统绘制完成之后用窗口 DC 覆盖成暗色并自绘滑块
    public static class DarkScroll
    {
        public const int WM_SIZE = 0x0005;
        public const int WM_PAINT = 0x000F;
        public const int WM_NCPAINT = 0x0085;
        public const int WM_NCMOUSEMOVE = 0x00A0;
        public const int WM_NCLBUTTONDOWN = 0x00A1;
        public const int WM_NCLBUTTONUP = 0x00A2;
        public const int WM_VSCROLL = 0x0115;
        public const int WM_MOUSEWHEEL = 0x020A;
        public const int WM_NCMOUSELEAVE = 0x02A2;

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        struct ScrollInfo
        {
            public uint cbSize;
            public uint fMask;
            public int nMin;
            public int nMax;
            public uint nPage;
            public int nPos;
            public int nTrackPos;
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        struct Rect
        {
            public int Left, Top, Right, Bottom;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool GetScrollInfo(IntPtr hwnd, int bar, ref ScrollInfo si);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern IntPtr GetWindowDC(IntPtr hwnd);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern int GetWindowLong(IntPtr hwnd, int index);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr w, IntPtr l);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);

        public static bool NeedsPaint(int msg)
        {
            return msg == WM_NCPAINT || msg == WM_VSCROLL || msg == WM_MOUSEWHEEL ||
                   msg == WM_NCMOUSEMOVE || msg == WM_NCLBUTTONDOWN || msg == WM_NCLBUTTONUP ||
                   msg == WM_NCMOUSELEAVE || msg == WM_SIZE || msg == WM_PAINT;
        }

        public static void Paint(Control c)
        {
            if (!c.IsHandleCreated) return;
            int style = GetWindowLong(c.Handle, -16);
            if ((style & 0x00200000) == 0) return;   // 没有竖向滚动条就不用画

            int bar = SystemInformation.VerticalScrollBarWidth;
            int w = c.Width;
            int h = c.Height;
            int top = 1;

            ListView lv = c as ListView;
            if (lv != null)
            {
                top = 0;
                if (lv.View == View.Details && lv.HeaderStyle != ColumnHeaderStyle.None)
                {
                    IntPtr hdr = SendMessage(lv.Handle, 0x101F, IntPtr.Zero, IntPtr.Zero);
                    Rect hr;
                    if (hdr != IntPtr.Zero && GetWindowRect(hdr, out hr))
                    {
                        Point p = lv.PointToClient(new Point(hr.Left, hr.Top));
                        if (p.Y > 0 && p.Y < h - 10) top = p.Y;
                    }
                }
            }

            IntPtr dc = GetWindowDC(c.Handle);
            if (dc == IntPtr.Zero) return;
            try
            {
                using (Graphics g = Graphics.FromHdc(dc))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    Rectangle rc = new Rectangle(w - bar, top, bar, h - top - 1);
                    using (SolidBrush b = new SolidBrush(c.BackColor))
                        g.FillRectangle(b, rc);
                    using (Pen p = new Pen(Ui.Line))
                        g.DrawLine(p, rc.X, rc.Y, rc.X, rc.Bottom - 1);

                    ScrollInfo si = new ScrollInfo();
                    si.cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(ScrollInfo));
                    si.fMask = 0x17;
                    if (GetScrollInfo(c.Handle, 1, ref si) && si.nPage > 0 && si.nMax > si.nPage - 1)
                    {
                        int trackY = rc.Y + 4;
                        int trackH = rc.Height - 8;
                        long range = (long)si.nMax - si.nPage + 1;
                        if (range < 1) range = 1;
                        int thumbH = (int)((long)trackH * si.nPage / ((long)si.nMax + 1));
                        if (thumbH < 24) thumbH = 24;
                        if (thumbH > trackH) thumbH = trackH;
                        int thumbY = trackY + (int)((long)(trackH - thumbH) * si.nPos / range);
                        Rectangle tr = new Rectangle(w - bar + 3, thumbY, bar - 6, thumbH);
                        using (GraphicsPath gp = Ui.RoundRect(tr, (bar - 6) / 2))
                        using (SolidBrush b = new SolidBrush(ColorTranslator.FromHtml("#3b4b5e")))
                            g.FillPath(b, gp);
                    }

                    // 顺带把系统亮色边框也改成暗色
                    if (c is TextBoxBase)
                    {
                        using (Pen p = new Pen(Ui.Line))
                            g.DrawRectangle(p, 0, 0, w - 1, h - 1);
                    }
                }
            }
            finally { ReleaseDC(c.Handle, dc); }
        }
    }

    public class DarkBox : TextBox
    {
        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (Multiline && DarkScroll.NeedsPaint(m.Msg)) DarkScroll.Paint(this);
        }
    }

    public class DarkList : ListView
    {
        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (DarkScroll.NeedsPaint(m.Msg)) DarkScroll.Paint(this);
        }
    }

    public class DarkFlow : FlowLayoutPanel
    {
        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (DarkScroll.NeedsPaint(m.Msg)) DarkScroll.Paint(this);
        }
    }

    public class BPanel : Panel
    {
        public Color BorderColor = Ui.Line;
        public int Radius = 10;

        public BPanel()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
            BackColor = Ui.PanelBg;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath gp = Ui.RoundRect(r, Radius))
            using (Pen pen = new Pen(BorderColor, 1f))
            {
                e.Graphics.DrawPath(pen, gp);
            }
        }
    }

    public class VramBar : Panel
    {
        public List<KeyValuePair<Color, double>> Segments = new List<KeyValuePair<Color, double>>();

        public VramBar()
        {
            DoubleBuffered = true;
            Height = 12;
            BackColor = ColorTranslator.FromHtml("#0b1016");
        }

        public void SetSegments(List<KeyValuePair<Color, double>> segs)
        {
            Segments = segs;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle full = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath gp = Ui.RoundRect(full, Height / 2))
            {
                e.Graphics.SetClip(gp);
                e.Graphics.Clear(ColorTranslator.FromHtml("#0b1016"));
                double x = 0;
                foreach (KeyValuePair<Color, double> kv in Segments)
                {
                    double w = kv.Value * Width;
                    if (w <= 0) continue;
                    using (SolidBrush b = new SolidBrush(kv.Key))
                        e.Graphics.FillRectangle(b, (float)x, 0, (float)w, Height);
                    x += w;
                }
                e.Graphics.ResetClip();
                using (Pen pen = new Pen(Ui.Line))
                    e.Graphics.DrawPath(pen, gp);
            }
        }
    }

    public class MainForm : Form
    {
        Snapshot _snap = new Snapshot();
        bool _busy;
        bool _polling;
        string _listSig = "";
        readonly Dictionary<string, Label> _timers = new Dictionary<string, Label>();
        readonly Dictionary<string, RunningInfo> _runningMap = new Dictionary<string, RunningInfo>();

        System.Windows.Forms.Timer _pollTimer;
        System.Windows.Forms.Timer _tickTimer;

        Label _dot, _connText, _verLabel, _baseLabel, _refreshLabel;
        Label _gpuName;
        Label _vramUsed, _vramSub, _utilLabel, _tempLabel, _tempSub, _powerLabel, _powerSub;
        VramBar _bar;
        FlowLayoutPanel _legend;
        ComboBox _cbMain, _cbAlt, _cbKeep;
        Button _btnToMain, _btnToAlt, _btnSettings;
        Label _switchStatus;
        ComboBox _cbLight;
        Button _btnLightLoad, _btnLightUnload;
        Label _lightStatus;
        CheckBox _chkGame;
        Label _gameStatus;
        bool _gameReleased;
        bool _gameBusy;
        FlowLayoutPanel _runningList, _standbyList;
        Label _runningCount, _standbyCount;
        Panel _overlay;
        Label _overlayMsg, _overlayHint;

        readonly List<double> _utilHistory = new List<double>();

        public MainForm()
        {
            Text = "Ollama 模型切换器";
            ClientSize = new Size(1180, 956);
            MinimumSize = new Size(1000, 760);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Ui.Bg;
            ForeColor = Ui.Ink;
            Font = Ui.F(Ui.FBody, FontStyle.Regular);
            DoubleBuffered = true;

            BuildTopBar();
            BuildBody();
            BuildOverlay();

            _pollTimer = new System.Windows.Forms.Timer();
            _pollTimer.Interval = 2000;
            _pollTimer.Tick += (s, e) => Poll();
            _pollTimer.Start();

            _tickTimer = new System.Windows.Forms.Timer();
            _tickTimer.Interval = 500;
            _tickTimer.Tick += (s, e) => { UpdateTimers(); GameWatchTick(); };
            _tickTimer.Start();

            _chkGame.Checked = Ollama.LoadConfig().GameMode;
            UpdateGameStatus();

            Poll();
        }

        /* ============================ 构建界面 ============================ */
        void BuildTopBar()
        {
            Panel top = new Panel();
            top.Dock = DockStyle.Top;
            top.Height = 64;
            top.BackColor = Ui.PanelBg;
            top.Paint += (s, e) =>
            {
                using (Pen p = new Pen(Ui.Line))
                    e.Graphics.DrawLine(p, 0, top.Height - 1, top.Width, top.Height - 1);
            };
            Controls.Add(top);

            // 右侧按钮区：固定贴右，永不被左侧标题挤压
            FlowLayoutPanel right = new FlowLayoutPanel();
            right.Dock = DockStyle.Right;
            right.FlowDirection = FlowDirection.LeftToRight;
            right.WrapContents = false;
            right.AutoSize = true;
            right.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            right.Padding = new Padding(0, 16, 20, 0);
            right.BackColor = Color.Transparent;
            top.Controls.Add(right);

            _connText = Ui.Label("连接中…", Ui.FSub, FontStyle.Regular, Ui.Muted);
            _connText.Margin = new Padding(0, 6, 10, 0);
            right.Controls.Add(_connText);

            _baseLabel = Ui.Label("127.0.0.1:11434", Ui.FSub, FontStyle.Regular, Ui.Dim);
            _baseLabel.Margin = new Padding(0, 6, 14, 0);
            right.Controls.Add(_baseLabel);

            _refreshLabel = Ui.Label("", Ui.FSmall, FontStyle.Regular, Ui.Dim);
            _refreshLabel.Margin = new Padding(0, 7, 14, 0);
            right.Controls.Add(_refreshLabel);

            _btnSettings = MakeButton("设置", Ui.BtnSecondary);
            _btnSettings.Size = new Size(78, Ui.BtnH);
            _btnSettings.Click += (s, e) => OpenSettings();
            right.Controls.Add(_btnSettings);

            Button btnAsr = Ui.Button("语音转文字", Ui.BtnSecondary);
            btnAsr.Size = new Size(116, Ui.BtnH);
            btnAsr.Margin = new Padding(10, 0, 0, 0);
            btnAsr.Click += (s, e) => OpenSpeech();
            right.Controls.Add(btnAsr);

            Button btnTr = Ui.Button("一键翻译", Ui.BtnPrimary);
            btnTr.Size = new Size(116, Ui.BtnH);
            btnTr.Margin = new Padding(10, 0, 0, 0);
            btnTr.Click += (s, e) => OpenTranslate();
            right.Controls.Add(btnTr);

            // 左侧标题区：填充剩余宽度，超宽时省略号收尾，不会顶到右侧按钮
            Panel left = new Panel();
            left.Dock = DockStyle.Fill;
            left.BackColor = Color.Transparent;
            top.Controls.Add(left);
            left.BringToFront();

            _dot = Ui.Label("●", Ui.FStrong, FontStyle.Bold, Ui.Dim);
            _dot.Location = new Point(20, 22);
            left.Controls.Add(_dot);

            Label title = Ui.Label("Ollama 模型切换器", Ui.FStrong, FontStyle.Bold, Ui.Ink);
            title.Location = new Point(42, 17);
            left.Controls.Add(title);

            _verLabel = Ui.Label("", Ui.FSmall, FontStyle.Regular, Ui.Dim);
            _verLabel.Location = new Point(232, 23);
            left.Controls.Add(_verLabel);
        }

        void OpenTranslate()
        {
            Config c = Ollama.LoadConfig();
            string model = string.IsNullOrEmpty(c.MainModel) ? "qwen3.5-9b-local:latest" : c.MainModel;
            string light = string.IsNullOrEmpty(c.LightModel) ? "qwen3.5-0.8b-local:latest" : c.LightModel;
            using (TranslateForm f = new TranslateForm(model, light))
                f.ShowDialog(this);
        }

        void OpenSpeech()
        {
            Config c = Ollama.LoadConfig();
            string light = string.IsNullOrEmpty(c.LightModel) ? "qwen3.5-0.8b-local:latest" : c.LightModel;
            using (SpeechForm f = new SpeechForm(light))
                f.ShowDialog(this);
        }

        void BuildBody()
        {
            // 顶部流式布局：卡片按顺序垂直排列，宽度跟随窗口，卡片之间永不重叠；
            // 纵向放不下时出现滚动条，而不是把内容挤到一起
            FlowLayoutPanel main = new DarkFlow();
            main.Dock = DockStyle.Fill;
            main.FlowDirection = FlowDirection.TopDown;
            main.WrapContents = false;
            main.AutoScroll = true;
            main.Padding = new Padding(14, 12, 14, 14);
            main.BackColor = Ui.Bg;
            Controls.Add(main);

            // 每张卡片给足内容所需高度，底部两列吃剩余空间：默认尺寸下不出现滚动条
            Control gpu = BuildGpuPanel();
            gpu.Size = new Size(1104, 190);
            main.Controls.Add(gpu);

            Control sw = BuildSwitchPanel();
            sw.Size = new Size(1104, 158);
            main.Controls.Add(sw);

            Control game = BuildGamePanel();
            game.Size = new Size(1104, 80);
            main.Controls.Add(game);

            Control light = BuildLightPanel();
            light.Size = new Size(1104, 56);
            main.Controls.Add(light);

            Control api = BuildApiPanel();
            api.Size = new Size(1104, 160);
            main.Controls.Add(api);

            Control cols = BuildColumns();
            cols.Size = new Size(1104, 160);
            main.Controls.Add(cols);

            main.Resize += (s, e) =>
            {
                int w = main.ClientSize.Width - main.Padding.Horizontal - 2;
                if (w >= 200)
                    foreach (Control c in main.Controls) c.Width = w;
                // 六张卡片各留 10px 间距，其余高度全部给下面两列
                int fixedH = gpu.Height + sw.Height + game.Height + light.Height + api.Height;
                int rest = main.ClientSize.Height - fixedH - 60 - main.Padding.Vertical - 2;
                cols.Height = Math.Max(150, rest);
            };

            main.BringToFront();
        }

        Control BuildGpuPanel()
        {
            BPanel card = new BPanel();
            card.Margin = new Padding(0, 0, 0, 10);
            card.Padding = new Padding(16, 12, 16, 10);

            Label h = Ui.Label("GPU 实时状态", Ui.FTitle, FontStyle.Bold, Ui.Ink);
            h.Location = new Point(16, 12);
            card.Controls.Add(h);

            _gpuName = Ui.Label("", Ui.FSub, FontStyle.Regular, Ui.Muted);
            _gpuName.Location = new Point(122, 16);
            card.Controls.Add(_gpuName);

            TableLayoutPanel metrics = new TableLayoutPanel();
            metrics.Location = new Point(16, 38);
            metrics.Size = new Size(1104, 82);
            metrics.ColumnCount = 4;
            metrics.RowCount = 1;
            metrics.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            for (int i = 0; i < 4; i++) metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
            card.Controls.Add(metrics);
            FitWidth(card, metrics, 16, 32);

            metrics.Controls.Add(MakeMetric("显存", out _vramUsed, out _vramSub), 0, 0);
            metrics.Controls.Add(MakeMetric("利用率", out _utilLabel, out _utilSub), 1, 0);
            metrics.Controls.Add(MakeMetric("温度", out _tempLabel, out _tempSub), 2, 0);
            metrics.Controls.Add(MakeMetric("功耗", out _powerLabel, out _powerSub), 3, 0);

            _bar = new VramBar();
            _bar.Location = new Point(16, 130);
            _bar.Size = new Size(1104, 12);
            _bar.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            card.Controls.Add(_bar);
            FitWidth(card, _bar, 16, 32);

            _legend = new FlowLayoutPanel();
            _legend.Location = new Point(12, 148);
            _legend.Size = new Size(1112, 32);
            _legend.BackColor = Color.Transparent;
            _legend.WrapContents = false;
            _legend.AutoScroll = true;
            card.Controls.Add(_legend);
            FitWidth(card, _legend, 12, 28);

            return card;
        }

        Label _utilSub;

        Control MakeMetric(string caption, out Label value, out Label sub)
        {
            BPanel p = new BPanel();
            p.Dock = DockStyle.Fill;
            p.Margin = new Padding(0, 0, 10, 0);
            p.BackColor = Ui.Panel2;
            p.Radius = 8;

            Label cap = Ui.Label(caption, Ui.FSub, FontStyle.Regular, Ui.Muted);
            cap.Location = new Point(12, 7);
            p.Controls.Add(cap);

            value = Ui.Label("—", Ui.FBig, FontStyle.Bold, Ui.Ink);
            value.Location = new Point(12, 24);
            p.Controls.Add(value);

            sub = Ui.Label("—", Ui.FSmall, FontStyle.Regular, Ui.Dim);
            sub.Location = new Point(12, 52);
            p.Controls.Add(sub);

            return p;
        }

        Control BuildSwitchPanel()
        {
            BPanel card = new BPanel();
            card.Margin = new Padding(0, 0, 0, 10);
            card.Padding = new Padding(16, 12, 16, 10);

            Label h = Ui.Label("快速切换", Ui.FTitle, FontStyle.Bold, Ui.Ink);
            h.Location = new Point(16, 12);
            card.Controls.Add(h);

            // 说明不再挤在界面上，鼠标放「快速切换」标题上看
            Ui.Tip(h, "切换时会先卸载其他驻留模型，避免 8GB 显存被挤爆");

            TableLayoutPanel row = new TableLayoutPanel();
            row.Location = new Point(16, 38);
            row.Size = new Size(1104, 70);
            row.ColumnCount = 3;
            row.RowCount = 1;
            row.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 36f));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 36f));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28f));
            card.Controls.Add(row);
            FitWidth(card, row, 16, 32);

            _cbMain = MakeCombo();
            row.Controls.Add(Field("主力模型（日常翻译）", _cbMain), 0, 0);
            _cbAlt = MakeCombo();
            row.Controls.Add(Field("批量 / 备用模型", _cbAlt), 1, 0);
            _cbKeep = MakeCombo();
            _cbKeep.Items.AddRange(new object[] { "常驻（不卸载）", "5 分钟", "10 分钟", "30 分钟", "1 小时" });
            _cbKeep.SelectedIndex = 0;
            row.Controls.Add(Field("保持时长", _cbKeep), 2, 0);

            _btnToMain = MakeButton("一键切到主力", Ui.BtnPrimary);
            _btnToMain.Location = new Point(16, 116);
            _btnToMain.Size = new Size(140, Ui.BtnH);
            _btnToMain.Click += (s, e) => DoSwitch(SelectedModel(_cbMain), _btnToMain);
            card.Controls.Add(_btnToMain);

            _btnToAlt = MakeButton("一键切到批量", Ui.BtnPrimary);
            _btnToAlt.Location = new Point(166, 116);
            _btnToAlt.Size = new Size(140, Ui.BtnH);
            _btnToAlt.Click += (s, e) => DoSwitch(SelectedModel(_cbAlt), _btnToAlt);
            card.Controls.Add(_btnToAlt);

            _switchStatus = Ui.Label("", Ui.FSub, FontStyle.Regular, Ui.Muted);
            _switchStatus.Location = new Point(316, 124);
            card.Controls.Add(_switchStatus);

            _cbMain.SelectedIndexChanged += (s, e) => SavePair("main");
            _cbAlt.SelectedIndexChanged += (s, e) => SavePair("alt");
            _cbKeep.SelectedIndexChanged += (s, e) =>
            {
                Config c = Ollama.LoadConfig();
                c.KeepAlive = KeepValue();
                Ollama.SaveConfig(c);
            };

            return card;
        }

        Control BuildGamePanel()
        {
            BPanel card = new BPanel();
            card.Margin = new Padding(0, 0, 0, 10);
            card.Padding = new Padding(16, 10, 16, 10);

            Label h = Ui.Label("游戏模式", Ui.FTitle, FontStyle.Bold, Ui.Ink);
            h.Location = new Point(16, 12);
            card.Controls.Add(h);

            Ui.Tip(h, "检测到指定游戏进程启动时，自动卸载全部驻留模型，把显存让给游戏");

            _chkGame = new CheckBox();
            _chkGame.Text = "启用游戏模式";
            _chkGame.AutoSize = true;
            _chkGame.ForeColor = Ui.Ink;
            _chkGame.Font = Ui.F(Ui.FBody, FontStyle.Bold);
            _chkGame.Location = new Point(16, 44);
            _chkGame.CheckedChanged += (s, e) =>
            {
                Config c = Ollama.LoadConfig();
                c.GameMode = _chkGame.Checked;
                Ollama.SaveConfig(c);
                _gameReleased = false;
                UpdateGameStatus();
            };
            card.Controls.Add(_chkGame);

            Button pick = MakeButton("选择游戏进程…", Ui.BtnSecondary);
            pick.Location = new Point(150, 38);
            pick.Size = new Size(132, Ui.BtnH);
            pick.Click += (s, e) => PickGameProcess();
            card.Controls.Add(pick);

            _gameStatus = Ui.Label("", Ui.FSub, FontStyle.Regular, Ui.Muted);
            _gameStatus.Location = new Point(300, 46);
            _gameStatus.AutoSize = false;
            _gameStatus.Size = new Size(820, 20);
            card.Controls.Add(_gameStatus);
            FitWidth(card, _gameStatus, 300, 16);

            return card;
        }

        void UpdateGameStatus()
        {
            if (_gameStatus == null) return;
            Config c = Ollama.LoadConfig();
            if (!c.GameMode)
            {
                _gameStatus.Text = "游戏模式已关闭";
                _gameStatus.ForeColor = Ui.Dim;
                return;
            }
            if (string.IsNullOrEmpty(c.GameProcess))
            {
                _gameStatus.Text = "未设置游戏进程，点「选择游戏进程…」挑选，填写的进程名会记入配置";
                _gameStatus.ForeColor = Ui.Dim;
                return;
            }
            bool running = Ollama.GameProcessRunning(c.GameProcess);
            if (running && _gameReleased)
            {
                _gameStatus.Text = "游戏运行中 · 已释放显存（" + c.GameProcess + ".exe）";
                _gameStatus.ForeColor = ColorTranslator.FromHtml("#7ee2a8");
            }
            else if (running)
            {
                _gameStatus.Text = "检测到游戏进程 " + c.GameProcess + ".exe，正在释放显存…";
                _gameStatus.ForeColor = Ui.Warn;
            }
            else
            {
                _gameStatus.Text = "监控中：" + c.GameProcess + ".exe（未运行）";
                _gameStatus.ForeColor = Ui.Muted;
            }
        }

        /* ============================ 轻量通道 ============================ */
        Control BuildLightPanel()
        {
            BPanel card = new BPanel();
            card.Margin = new Padding(0, 0, 0, 10);
            card.Padding = new Padding(16, 8, 16, 8);

            Label h = Ui.Label("轻量通道", Ui.FTitle, FontStyle.Bold, Ui.Ink);
            h.Location = new Point(16, 17);
            card.Controls.Add(h);
            Ui.Tip(h, "快翻专用的小模型通道，约 0.7 GB 显存，也能给语音转写做润色");

            _cbLight = MakeCombo();
            _cbLight.Location = new Point(110, 13);
            _cbLight.Size = new Size(280, 30);
            _cbLight.SelectedIndexChanged += (s, e) => SavePair("light");
            card.Controls.Add(_cbLight);

            _btnLightLoad = MakeButton("加载到显存", Ui.BtnPrimary);
            _btnLightLoad.Location = new Point(400, 12);
            _btnLightLoad.Size = new Size(104, Ui.BtnH);
            _btnLightLoad.Click += (s, e) => DoSwitch(SelectedModel(_cbLight), _btnLightLoad);
            card.Controls.Add(_btnLightLoad);

            _btnLightUnload = MakeButton("卸载", Ui.BtnSecondary);
            _btnLightUnload.Location = new Point(512, 12);
            _btnLightUnload.Size = new Size(72, Ui.BtnH);
            _btnLightUnload.Click += (s, e) => DoUnload(SelectedModel(_cbLight), _btnLightUnload);
            card.Controls.Add(_btnLightUnload);

            _lightStatus = Ui.Label("", Ui.FSub, FontStyle.Regular, Ui.Dim);
            _lightStatus.AutoSize = false;
            _lightStatus.TextAlign = ContentAlignment.MiddleLeft;
            _lightStatus.Location = new Point(596, 18);
            _lightStatus.Size = new Size(380, 20);
            card.Controls.Add(_lightStatus);
            FitWidth(card, _lightStatus, 596, 16);

            return card;
        }

        void UpdateLightStatus(Snapshot st)
        {
            if (_lightStatus == null) return;
            RunningInfo r = null;
            foreach (RunningInfo x in st.Running)
                if (x.Name == st.Cfg.LightModel) r = x;
            if (r != null)
            {
                _lightStatus.Text = r.VramBytes > 0
                    ? "已驻留 " + FormatBytes(r.VramBytes) + " · 快翻可用"
                    : "已驻留内存 · 快翻可用";
                _lightStatus.ForeColor = ColorTranslator.FromHtml("#7ee2a8");
            }
            else
            {
                _lightStatus.Text = "未加载 · 快翻时自动加载";
                _lightStatus.ForeColor = Ui.Dim;
            }
        }

        void PickGameProcess()
        {
            Form dlg = new Form();
            dlg.Text = "选择游戏进程";
            dlg.ClientSize = new Size(440, 168);
            dlg.StartPosition = FormStartPosition.CenterParent;
            dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
            dlg.MaximizeBox = false;
            dlg.MinimizeBox = false;
            dlg.BackColor = Ui.PanelBg;
            dlg.ForeColor = Ui.Ink;
            dlg.Font = Ui.F(Ui.FBody, FontStyle.Regular);

            Config c = Ollama.LoadConfig();

            Label l1 = Ui.Label("游戏进程名（不含 .exe）", Ui.FSub, FontStyle.Regular, Ui.Muted);
            l1.Location = new Point(20, 16);
            dlg.Controls.Add(l1);

            TextBox tb = new TextBox();
            tb.Text = c.GameProcess;
            tb.Location = new Point(20, 38);
            tb.Size = new Size(400, 28);
            tb.BackColor = ColorTranslator.FromHtml("#0e141b");
            tb.ForeColor = Ui.Ink;
            tb.BorderStyle = BorderStyle.FixedSingle;
            tb.Font = Ui.F(Ui.FBody, FontStyle.Regular);
            dlg.Controls.Add(tb);

            Label l2 = Ui.Label("在任务管理器「详细信息」里能看到，如 eldenring、witcher3、cs2", Ui.FSmall, FontStyle.Regular, Ui.Dim);
            l2.Location = new Point(20, 72);
            dlg.Controls.Add(l2);

            Button ok = MakeButton("确定", Ui.BtnPrimary);
            ok.Location = new Point(20, 112);
            ok.Size = new Size(100, Ui.BtnH);
            ok.Click += (s, e) =>
            {
                string v = tb.Text.Trim();
                if (v.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) v = v.Substring(0, v.Length - 4);
                Config nc = Ollama.LoadConfig();
                nc.GameProcess = v;
                Ollama.SaveConfig(nc);
                dlg.Close();
                _gameReleased = false;
                UpdateGameStatus();
            };
            dlg.Controls.Add(ok);

            Button cancel = MakeButton("取消", Ui.BtnSecondary);
            cancel.Location = new Point(130, 112);
            cancel.Size = new Size(100, Ui.BtnH);
            cancel.Click += (s, e) => dlg.Close();
            dlg.Controls.Add(cancel);

            dlg.ShowDialog(this);
        }

        void GameWatchTick()
        {
            if (_gameBusy) return;
            Config c = Ollama.LoadConfig();
            if (!c.GameMode || string.IsNullOrEmpty(c.GameProcess)) { UpdateGameStatus(); return; }

            bool running = Ollama.GameProcessRunning(c.GameProcess);
            if (!running)
            {
                _gameReleased = false;
                UpdateGameStatus();
                return;
            }
            if (_gameReleased || !_snap.Online) { UpdateGameStatus(); return; }

            // 游戏进程已出现且还没释放过显存 → 自动卸载全部模型
            _gameBusy = true;
            ThreadPool.QueueUserWorkItem(delegate
            {
                string msg;
                bool ok = true;
                try
                {
                    int n = Ollama.UnloadAll();
                    msg = "游戏模式：已释放显存，卸载 " + n + " 个驻留模型";
                }
                catch (Exception ex) { ok = false; msg = "游戏模式释放显存失败：" + ex.Message; }
                try
                {
                    BeginInvoke(new Action(delegate
                    {
                        _gameBusy = false;
                        if (ok) _gameReleased = true;
                        SetBusy(false, msg, ok ? ColorTranslator.FromHtml("#7ee2a8") : ColorTranslator.FromHtml("#ff9b9b"));
                        _listSig = "";
                        Poll();
                        UpdateGameStatus();
                    }));
                }
                catch (Exception) { _gameBusy = false; }
            });
        }

        Control Field(string caption, Control input)
        {
            Panel p = new Panel();
            p.Dock = DockStyle.Fill;
            p.Margin = new Padding(0, 0, 12, 0);
            p.BackColor = Color.Transparent;

            Label l = Ui.Label(caption, Ui.FSub, FontStyle.Regular, Ui.Muted);
            l.Location = new Point(1, 0);
            p.Controls.Add(l);

            input.Location = new Point(0, 22);
            input.Size = new Size(p.Width, 30);
            input.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            p.Controls.Add(input);

            return p;
        }

        /* ============================ 调用接口 ============================ */
        ComboBox _cbApi;
        Label _apiBadge;
        Label _apiMsg;

        Control BuildApiPanel()
        {
            BPanel card = new BPanel();
            card.Margin = new Padding(0, 0, 0, 10);
            card.Padding = new Padding(16, 10, 16, 10);

            Label h = Ui.Label("模型调用接口", Ui.FTitle, FontStyle.Bold, Ui.Ink);
            h.Location = new Point(16, 10);
            card.Controls.Add(h);

            Ui.Tip(h, "给浏览器翻译插件等外部工具用：base URL 填下面任一地址（OpenAI 兼容最通用），API Key 随便填或留空，model 填选中的模型名");

            AddApiRow(card, 38, "OpenAI 兼容", "http://127.0.0.1:11434/v1", 352);
            AddApiRow(card, 78, "Ollama 原生", "http://127.0.0.1:11434/api", 352);

            Label lm = Ui.Label("模型名", Ui.FSub, FontStyle.Regular, Ui.Muted);
            lm.Location = new Point(16, 122);
            card.Controls.Add(lm);

            _cbApi = new CBox();
            _cbApi.DropDownStyle = ComboBoxStyle.DropDownList;
            _cbApi.FlatStyle = FlatStyle.Flat;
            _cbApi.BackColor = ColorTranslator.FromHtml("#0e141b");
            _cbApi.ForeColor = Ui.Ink;
            _cbApi.Font = Ui.F(Ui.FBody, FontStyle.Regular);
            _cbApi.Location = new Point(120, 118);
            _cbApi.Size = new Size(300, 30);
            _cbApi.SelectedIndexChanged += (s, e) => UpdateApiBadge();
            card.Controls.Add(_cbApi);

            Button cp = MakeButton("复制", Ui.BtnSecondary);
            cp.Location = new Point(428, 117);
            cp.Size = new Size(64, Ui.BtnH);
            cp.Click += (s, e) => CopyText(Convert.ToString(_cbApi.SelectedItem));
            card.Controls.Add(cp);

            _apiBadge = Ui.Label("", Ui.FSub, FontStyle.Regular, Ui.Muted);
            _apiBadge.Location = new Point(502, 124);
            card.Controls.Add(_apiBadge);

            // 详细填法收进悬停提示（鼠标放标题「模型调用接口」上）
            _apiMsg = Ui.Label("", Ui.FSub, FontStyle.Regular, Ui.Ok);
            _apiMsg.Location = new Point(620, 124);
            card.Controls.Add(_apiMsg);

            return card;
        }

        void AddApiRow(BPanel card, int y, string caption, string url, int btnX)
        {
            Label c = Ui.Label(caption, Ui.FSub, FontStyle.Regular, Ui.Muted);
            c.Location = new Point(16, y + 3);
            card.Controls.Add(c);

            Label u = Ui.Label(url, Ui.FBody, FontStyle.Regular, Ui.Ink);
            u.Location = new Point(120, y);
            card.Controls.Add(u);

            Button b = MakeButton("复制", Ui.BtnSecondary);
            b.Location = new Point(btnX, y - 3);
            b.Size = new Size(64, Ui.BtnH);
            b.Click += (s, e) => CopyText(url);
            card.Controls.Add(b);
        }

        void CopyText(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            try
            {
                Clipboard.SetText(text);
                _apiMsg.Text = "已复制：" + text;
                _apiMsg.ForeColor = Ui.Ok;
            }
            catch (Exception ex)
            {
                _apiMsg.Text = "复制失败：" + ex.Message;
                _apiMsg.ForeColor = Ui.Bad;
            }
        }

        void UpdateApiBadge()
        {
            if (_apiBadge == null) return;
            string sel = Convert.ToString(_cbApi.SelectedItem);
            Config c = Ollama.LoadConfig();
            if (!string.IsNullOrEmpty(sel) && sel == c.MainModel) { _apiBadge.Text = "主力模型"; _apiBadge.ForeColor = Ui.Brand; }
            else if (!string.IsNullOrEmpty(sel) && sel == c.AltModel) { _apiBadge.Text = "批量模型"; _apiBadge.ForeColor = Ui.Ok; }
            else if (!string.IsNullOrEmpty(sel) && sel == c.LightModel) { _apiBadge.Text = "轻量模型"; _apiBadge.ForeColor = Ui.Warn; }
            else { _apiBadge.Text = ""; }
        }

        void ApplyApi(Snapshot st)
        {
            string[] names = new string[st.Models.Count];
            for (int i = 0; i < st.Models.Count; i++) names[i] = st.Models[i].Name;
            string sig = string.Join("|", names);
            string want = Convert.ToString(_cbApi.SelectedItem);
            if (Convert.ToString(_cbApi.Tag) != sig)
            {
                _cbApi.Items.Clear();
                if (names.Length == 0) _cbApi.Items.Add("（无可用模型）");
                else _cbApi.Items.AddRange(names);
                _cbApi.Tag = sig;
                int i = Array.IndexOf(names, want);
                if (i >= 0) _cbApi.SelectedIndex = i;
                else if (names.Length > 0)
                {
                    int m = Array.IndexOf(names, st.Cfg.MainModel);
                    _cbApi.SelectedIndex = m >= 0 ? m : 0;
                }
            }
            UpdateApiBadge();
        }

        Control BuildColumns()
        {
            TableLayoutPanel cols = new TableLayoutPanel();
            cols.Dock = DockStyle.Fill;
            cols.ColumnCount = 2;
            cols.RowCount = 1;
            cols.BackColor = Ui.Bg;
            cols.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            cols.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));

            cols.Controls.Add(BuildColumn("驻留显存", out _runningCount, out _runningList, 0), 0, 0);
            cols.Controls.Add(BuildColumn("待机（已下载未加载）", out _standbyCount, out _standbyList, 1), 1, 0);
            return cols;
        }

        Control BuildColumn(string title, out Label count, out FlowLayoutPanel list, int index)
        {
            Panel wrap = new Panel();
            wrap.Dock = DockStyle.Fill;
            wrap.BackColor = Ui.Bg;
            wrap.Margin = new Padding(index == 0 ? 0 : 5, 0, index == 0 ? 5 : 0, 0);

            Panel head = new Panel();
            head.Dock = DockStyle.Top;
            head.Height = 34;
            head.BackColor = Color.Transparent;
            wrap.Controls.Add(head);

            Label h = Ui.Label(title, Ui.FTitle, FontStyle.Bold, Ui.Ink);
            h.Location = new Point(2, 4);
            head.Controls.Add(h);

            Label cnt = Ui.Label("0", Ui.FSub, FontStyle.Bold, Ui.Muted);
            cnt.AutoSize = false;
            cnt.TextAlign = ContentAlignment.MiddleCenter;
            cnt.Location = new Point(h.Right + 6, 5);
            cnt.Size = new Size(32, 20);
            cnt.BackColor = Ui.Panel2;
            head.Controls.Add(cnt);

            FlowLayoutPanel lp = new DarkFlow();
            lp.Dock = DockStyle.Fill;
            lp.FlowDirection = FlowDirection.TopDown;
            lp.WrapContents = false;
            lp.AutoScroll = true;
            lp.BackColor = Ui.Bg;
            lp.Padding = new Padding(0, 0, 6, 0);
            lp.Resize += (s, e) => ResizeCards(lp);
            wrap.Controls.Add(lp);
            lp.BringToFront();

            count = cnt;
            list = lp;
            return wrap;
        }

        void ResizeCards(FlowLayoutPanel list)
        {
            int w = list.ClientSize.Width - 8;
            if (w < 200) return;
            foreach (Control c in list.Controls)
                if (c is BPanel) c.Width = w;
        }

        void BuildOverlay()
        {
            _overlay = new Panel();
            _overlay.Dock = DockStyle.Fill;
            _overlay.BackColor = Ui.Bg;
            _overlay.Visible = false;
            Controls.Add(_overlay);
            _overlay.BringToFront();

            TableLayoutPanel center = new TableLayoutPanel();
            center.ColumnCount = 1;
            center.RowCount = 4;
            center.AutoSize = true;
            center.Anchor = AnchorStyles.None;
            center.BackColor = Ui.Bg;
            _overlay.Controls.Add(center);
            _overlay.Resize += (s, e) =>
            {
                center.Left = (_overlay.ClientSize.Width - center.Width) / 2;
                center.Top = (_overlay.ClientSize.Height - center.Height) / 2;
            };

            Label t = Ui.Label("Ollama 服务未连接", Ui.FBig, FontStyle.Bold, Ui.Ink);
            t.Margin = new Padding(0, 0, 0, 8);
            center.Controls.Add(t);

            _overlayMsg = Ui.Label("", Ui.FBody, FontStyle.Regular, Ui.Muted);
            _overlayMsg.AutoSize = false;
            _overlayMsg.TextAlign = ContentAlignment.MiddleCenter;
            _overlayMsg.Size = new Size(560, 36);
            center.Controls.Add(_overlayMsg);

            Button start = MakeButton("启动本地 Ollama 服务", Ui.BtnPrimary);
            start.Size = new Size(210, Ui.BtnH);
            start.Margin = new Padding(0, 10, 0, 8);
            start.Click += (s, e) =>
            {
                try
                {
                    Ollama.StartService();
                    _overlayMsg.Text = "已请求启动服务，正在等待端口就绪…";
                    ThreadPool.QueueUserWorkItem(delegate
                    {
                        Thread.Sleep(3500);
                        try { BeginInvoke(new Action(Poll)); }
                        catch (Exception) { }
                    });
                }
                catch (Exception ex)
                {
                    _overlayMsg.Text = "启动失败：" + ex.Message;
                }
            };
            center.Controls.Add(start);

            _overlayHint = Ui.Label("", Ui.FSmall, FontStyle.Regular, Ui.Dim);
            _overlayHint.AutoSize = false;
            _overlayHint.TextAlign = ContentAlignment.MiddleCenter;
            _overlayHint.Size = new Size(620, 34);
            center.Controls.Add(_overlayHint);
        }

        /* ============================ 控件工厂 ============================ */
        // 全程序统一按钮工厂：圆角、统一字号/高度，三种样式
        Button MakeButton(string text, int style)
        {
            return Ui.Button(text, style);
        }

        ComboBox MakeCombo()
        {
            ComboBox c = new CBox();
            c.DropDownStyle = ComboBoxStyle.DropDownList;
            c.FlatStyle = FlatStyle.Flat;
            c.BackColor = ColorTranslator.FromHtml("#0e141b");
            c.ForeColor = Ui.Ink;
            c.Font = Ui.F(Ui.FBody, FontStyle.Regular);
            c.Height = 30;
            return c;
        }

        // 卡片内的控件在父容器还没尺寸时设左右锚定，宽度会被算成负数边距而溢出，
        // 这里统一按父容器实际宽度重算，避免右侧内容被裁掉
        static void FitWidth(Control parent, Control child, int left, int right)
        {
            EventHandler apply = delegate
            {
                int w = parent.ClientSize.Width - left - right;
                if (w > 50) { child.Left = left; child.Width = w; }
            };
            parent.Resize += apply;
            apply(null, EventArgs.Empty);
        }

        /* ============================ 数据刷新 ============================ */
        void Poll()
        {
            if (_polling) return;
            _polling = true;
            ThreadPool.QueueUserWorkItem(delegate
            {
                Snapshot s;
                try { s = Ollama.GetSnapshot(); }
                catch (Exception ex)
                {
                    s = new Snapshot();
                    s.Online = false;
                    s.Error = ex.Message;
                }
                try
                {
                    BeginInvoke(new Action(delegate
                    {
                        _snap = s;
                        Apply(s);
                        _polling = false;
                    }));
                }
                catch (Exception) { _polling = false; }
            });
        }

        void Apply(Snapshot st)
        {
            if (st.Online)
            {
                _dot.ForeColor = Ui.Ok;
                _connText.Text = "已连接";
                _connText.ForeColor = Ui.Muted;
                _overlay.Visible = false;
            }
            else
            {
                _dot.ForeColor = Ui.Bad;
                _connText.Text = "未连接";
                _connText.ForeColor = ColorTranslator.FromHtml("#ff9b9b");
                _overlay.Visible = true;
                _overlay.BringToFront();
                _overlayMsg.Text = string.IsNullOrEmpty(st.Error) ? "无法访问 Ollama HTTP 接口" : st.Error;
                _overlayHint.Text = "将尝试启动：" + st.ExePath;
            }

            _verLabel.Text = string.IsNullOrEmpty(st.Version) ? "" : "v" + st.Version;
            _baseLabel.Text = "127.0.0.1:11434";
            _refreshLabel.Text = DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);

            ApplyGpu(st.Gpu, st.Running);
            ApplyLists(st);
            ApplyCombos(st);
            ApplyApi(st);
            UpdateLightStatus(st);
        }

        void ApplyGpu(GpuInfo g, List<RunningInfo> running)
        {
            if (!g.Valid)
            {
                _gpuName.Text = "nvidia-smi 不可用";
                _vramUsed.Text = "—";
                _vramSub.Text = "—";
                _utilLabel.Text = "—";
                _utilSub.Text = "—";
                _tempLabel.Text = "—";
                _tempSub.Text = "—";
                _powerLabel.Text = "—";
                _powerSub.Text = "—";
                _legend.Controls.Clear();
                _bar.SetSegments(new List<KeyValuePair<Color, double>>());
                return;
            }
            _gpuName.Text = g.Name;
            _vramUsed.Text = g.UsedMiB.ToString(CultureInfo.InvariantCulture) + " / " + g.TotalMiB + " MiB";
            double pct = g.TotalMiB > 0 ? g.UsedMiB * 100.0 / g.TotalMiB : 0;
            _vramSub.Text = pct.ToString("0.0", CultureInfo.InvariantCulture) + "% 已使用";
            _vramSub.ForeColor = pct > 92 ? ColorTranslator.FromHtml("#ff9b9b")
                : pct > 80 ? ColorTranslator.FromHtml("#ffd28a") : Ui.Dim;

            _utilLabel.Text = g.Util.ToString(CultureInfo.InvariantCulture) + " %";
            _utilSub.Text = g.Util >= 80 ? "高负载" : g.Util >= 10 ? "推理中" : "空闲";
            _tempLabel.Text = g.Temp.ToString(CultureInfo.InvariantCulture) + " °C";
            _tempSub.Text = g.Temp >= 80 ? "偏高" : g.Temp >= 65 ? "正常偏高" : "正常";
            _tempSub.ForeColor = g.Temp >= 80 ? ColorTranslator.FromHtml("#ff9b9b") : Ui.Dim;
            _powerLabel.Text = g.Power > 0 ? g.Power.ToString("0", CultureInfo.InvariantCulture) + " W" : "—";
            _powerSub.Text = g.PowerLimit > 0 ? "上限 " + g.PowerLimit.ToString("0", CultureInfo.InvariantCulture) + " W" : "—";

            List<KeyValuePair<Color, double>> segs = new List<KeyValuePair<Color, double>>();
            _legend.Controls.Clear();
            double accounted = 0;
            int i = 0;
            foreach (RunningInfo r in running)
            {
                double mib = r.VramBytes / 1048576.0;
                accounted += mib;
                Color col = Ui.Series[i % Ui.Series.Length];
                segs.Add(new KeyValuePair<Color, double>(col, g.TotalMiB > 0 ? mib / g.TotalMiB : 0));
                _legend.Controls.Add(LegendItem(col, r.Name + "  " + ((int)mib) + " MiB"));
                i++;
            }
            double other = Math.Max(0, g.UsedMiB - accounted);
            segs.Add(new KeyValuePair<Color, double>(ColorTranslator.FromHtml("#39424e"), g.TotalMiB > 0 ? other / g.TotalMiB : 0));
            _legend.Controls.Add(LegendItem(ColorTranslator.FromHtml("#39424e"), "桌面 / 其他  " + ((int)other) + " MiB"));
            _bar.SetSegments(segs);
        }

        Control LegendItem(Color c, string text)
        {
            Panel p = new Panel();
            p.AutoSize = true;
            p.BackColor = Color.Transparent;
            p.Margin = new Padding(2, 2, 10, 0);

            Panel sw = new Panel();
            sw.BackColor = c;
            sw.Size = new Size(9, 9);
            sw.Location = new Point(0, 4);
            p.Controls.Add(sw);

            Label l = Ui.Label(text, Ui.FSub, FontStyle.Regular, Ui.Muted);
            l.Location = new Point(16, 0);
            p.Controls.Add(l);
            p.Height = 20;
            p.Width = l.Right + 4;
            return p;
        }

        void ApplyLists(Snapshot st)
        {
            _runningCount.Text = st.Running.Count.ToString(CultureInfo.InvariantCulture);
            _standbyCount.Text = st.Standby.Count.ToString(CultureInfo.InvariantCulture);

            _runningMap.Clear();
            foreach (RunningInfo r in st.Running) _runningMap[r.Name] = r;

            string sig = "R:";
            foreach (RunningInfo r in st.Running) sig += r.Name + "," + r.ExpiresAt + ";";
            sig += "S:";
            foreach (ModelInfo m in st.Standby) sig += m.Name + ";";
            sig += "C:" + st.Cfg.MainModel + "|" + st.Cfg.AltModel + "|" + st.Cfg.LightModel;

            if (sig == _listSig) return;
            _listSig = sig;

            _runningList.SuspendLayout();
            _standbyList.SuspendLayout();
            _runningList.Controls.Clear();
            _standbyList.Controls.Clear();
            _timers.Clear();

            if (st.Running.Count == 0)
                _runningList.Controls.Add(EmptyHint("当前没有模型驻留显存"));
            foreach (RunningInfo r in st.Running)
                _runningList.Controls.Add(RunningCard(r, st.Cfg));

            if (st.Standby.Count == 0)
                _standbyList.Controls.Add(EmptyHint("没有待机模型，或尚未下载任何模型"));
            foreach (ModelInfo m in st.Standby)
                _standbyList.Controls.Add(StandbyCard(m, st.Cfg));

            _runningList.ResumeLayout();
            _standbyList.ResumeLayout();
            ResizeCards(_runningList);
            ResizeCards(_standbyList);
        }

        Control EmptyHint(string text)
        {
            BPanel p = new BPanel();
            p.Radius = 10;
            p.Height = 46;
            p.Width = 420;
            p.Margin = new Padding(0, 0, 0, 8);

            Label l = Ui.Label(text, Ui.FSub, FontStyle.Regular, Ui.Dim);
            l.AutoSize = false;
            l.TextAlign = ContentAlignment.MiddleCenter;
            l.Dock = DockStyle.Fill;
            p.Controls.Add(l);
            return p;
        }

        Control RunningCard(RunningInfo r, Config cfg)
        {
            BPanel card = new BPanel();
            card.Height = 158;
            card.Width = 420;
            card.Margin = new Padding(0, 0, 0, 9);
            card.Padding = new Padding(14, 10, 14, 10);
            card.BorderColor = ColorTranslator.FromHtml("#28527f");
            card.BackColor = ColorTranslator.FromHtml("#16202c");

            Label name = Ui.Label(r.Name, Ui.FTitle, FontStyle.Bold, Ui.Ink);
            name.Location = new Point(14, 8);
            name.MaximumSize = new Size(card.Width - 200, 24);
            card.Controls.Add(name);

            FlowLayoutPanel badges = new FlowLayoutPanel();
            badges.AutoSize = true;
            badges.WrapContents = false;
            badges.BackColor = Color.Transparent;
            badges.Location = new Point(card.Width - 150, 9);
            badges.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            badges.Controls.Add(Pill("驻留显存", ColorTranslator.FromHtml("#1d3b28"), ColorTranslator.FromHtml("#6ee7a0")));
            if (cfg.MainModel == r.Name) badges.Controls.Add(Pill("主力", ColorTranslator.FromHtml("#1e3450"), ColorTranslator.FromHtml("#93c5fd")));
            if (cfg.AltModel == r.Name) badges.Controls.Add(Pill("批量", ColorTranslator.FromHtml("#1e3450"), ColorTranslator.FromHtml("#93c5fd")));
            if (cfg.LightModel == r.Name) badges.Controls.Add(Pill("轻量", ColorTranslator.FromHtml("#3a2d14"), ColorTranslator.FromHtml("#fbd38d")));
            card.Controls.Add(badges);

            string meta = "";
            if (!string.IsNullOrEmpty(r.ParamSize)) meta += r.ParamSize + "  ·  ";
            if (!string.IsNullOrEmpty(r.Quant)) meta += r.Quant + "  ·  ";
            meta += FormatBytes(r.SizeBytes);
            Label metaL = Ui.Label(meta, Ui.FSub, FontStyle.Regular, Ui.Muted);
            metaL.Location = new Point(14, 32);
            card.Controls.Add(metaL);

            Label stats = Ui.Label(string.Format("{0:0.00} GB 显存      {1} 上下文",
                r.VramBytes / 1e9, r.ContextLength > 0 ? r.ContextLength.ToString(CultureInfo.InvariantCulture) : "—"),
                Ui.FBody, FontStyle.Bold, Ui.Ink);
            stats.Location = new Point(14, 54);
            card.Controls.Add(stats);

            Label tl = Ui.Label("剩余", Ui.FSub, FontStyle.Regular, Ui.Dim);
            tl.Location = new Point(14, 78);
            card.Controls.Add(tl);

            Label tv = Ui.Label(RemainText(r), Ui.FBody, FontStyle.Bold, ColorTranslator.FromHtml("#ffd28a"));
            tv.Location = new Point(52, 77);
            tv.Tag = r.ExpiresLocal;
            if (IsForever(r)) tv.ForeColor = ColorTranslator.FromHtml("#7ee2a8");
            card.Controls.Add(tv);
            _timers[r.Name] = tv;

            Button unload = MakeButton("卸载", Ui.BtnDanger);
            unload.Location = new Point(14, 116);
            unload.Size = new Size(80, Ui.BtnH);
            unload.Click += (s, e) => DoUnload(r.Name, unload);
            card.Controls.Add(unload);

            Button forever = MakeButton("设为常驻", Ui.BtnSecondary);
            forever.Location = new Point(102, 116);
            forever.Size = new Size(96, Ui.BtnH);
            forever.Enabled = !IsForever(r);
            forever.Click += (s, e) => DoKeep(r.Name, "-1", forever);
            card.Controls.Add(forever);

            if (!IsForever(r))
            {
                Button plus = MakeButton("+10 分钟", Ui.BtnSecondary);
                plus.Location = new Point(206, 116);
                plus.Size = new Size(96, Ui.BtnH);
                plus.Click += (s, e) => DoKeep(r.Name, "+10m", plus);
                card.Controls.Add(plus);
            }

            return card;
        }

        Control StandbyCard(ModelInfo m, Config cfg)
        {
            // 单行卡片：名称 + 元信息 + 角色徽章 + 右侧一个加载/切换按钮
            BPanel card = new BPanel();
            card.Height = 54;
            card.Width = 420;
            card.Margin = new Padding(0, 0, 0, 8);

            Label name = Ui.Label(m.Name, Ui.FBody, FontStyle.Bold, Ui.Ink);
            name.AutoSize = false;
            name.AutoEllipsis = true;
            name.Height = 22;
            name.TextAlign = ContentAlignment.MiddleLeft;
            card.Controls.Add(name);

            string metaText = "";
            if (!string.IsNullOrEmpty(m.ParamSize)) metaText += m.ParamSize + " · ";
            if (!string.IsNullOrEmpty(m.Quant)) metaText += m.Quant + " · ";
            metaText += FormatBytes(m.SizeBytes);
            Label meta = Ui.Label(metaText, Ui.FSmall, FontStyle.Regular, Ui.Muted);
            meta.AutoSize = false;
            meta.AutoEllipsis = true;
            meta.Height = 20;
            meta.TextAlign = ContentAlignment.MiddleLeft;
            card.Controls.Add(meta);

            // 角色徽章直接放在卡片上，从右往左排，空间不够就隐藏
            List<Control> rolePills = new List<Control>();
            if (cfg.MainModel == m.Name) rolePills.Add(Pill("主力", ColorTranslator.FromHtml("#1e3450"), ColorTranslator.FromHtml("#93c5fd")));
            if (cfg.AltModel == m.Name) rolePills.Add(Pill("批量", ColorTranslator.FromHtml("#1e3450"), ColorTranslator.FromHtml("#93c5fd")));
            if (cfg.LightModel == m.Name) rolePills.Add(Pill("轻量", ColorTranslator.FromHtml("#3a2d14"), ColorTranslator.FromHtml("#fbd38d")));
            foreach (Control p in rolePills) card.Controls.Add(p);

            bool anyRunning = _snap != null && _snap.Running.Count > 0;
            Button act = MakeButton(anyRunning ? "切换" : "加载", Ui.BtnPrimary);
            act.Size = new Size(84, Ui.BtnH);
            if (anyRunning)
            {
                act.Click += (s, e) => DoSwitch(m.Name, act);
                Ui.Tip(act, "加载到显存并切换；显存不够时会先卸载其它模型");
            }
            else
            {
                act.Click += (s, e) => DoLoad(m.Name, act);
                Ui.Tip(act, "加载到显存，冷启动首次约需 50 秒");
            }
            card.Controls.Add(act);

            // 卡片宽度随窗口变化，单行内容按剩余宽度重新排布
            EventHandler reflow = delegate
            {
                int w = card.ClientSize.Width;
                int btnX = w - 14 - act.Width;
                if (btnX < 160) btnX = 160;
                act.Location = new Point(btnX, (card.Height - act.Height) / 2);

                // 徽章从右往左紧贴按钮排，放不下就隐藏
                int px = btnX - 10;
                for (int i = rolePills.Count - 1; i >= 0; i--)
                {
                    Control p = rolePills[i];
                    if (px - p.Width < 130) { p.Visible = false; continue; }
                    px -= p.Width;
                    p.Visible = true;
                    p.Location = new Point(px, (card.Height - p.Height) / 2);
                    px -= 6;
                }
                int right = (rolePills.Count > 0 && px < btnX - 10) ? px - 2 : btnX - 10;

                int avail = Math.Max(60, right - 14);
                int nameW = avail * 55 / 100;
                if (nameW < 80) nameW = avail;
                name.Width = nameW;
                name.Location = new Point(14, (card.Height - name.Height) / 2);

                meta.Left = 14 + nameW + 12;
                int metaW = right - meta.Left;
                meta.Visible = metaW > 70;
                if (meta.Visible)
                {
                    meta.Width = metaW;
                    meta.Top = (card.Height - meta.Height) / 2;
                }
            };
            card.Resize += reflow;
            reflow(null, EventArgs.Empty);
            act.BringToFront();

            return card;
        }

        Control Pill(string text, Color back, Color fore)
        {
            Label l = new Label();
            l.Text = "  " + text + "  ";
            l.AutoSize = true;
            l.Font = Ui.F(Ui.FSmall, FontStyle.Bold);
            l.BackColor = back;
            l.ForeColor = fore;
            l.Margin = new Padding(4, 0, 0, 0);
            l.Padding = new Padding(2, 1, 2, 1);
            return l;
        }

        /* ============================ 交互动作 ============================ */
        string SelectedModel(ComboBox cb)
        {
            if (cb.SelectedItem == null) return "";
            return cb.SelectedItem.ToString();
        }

        string KeepValue()
        {
            switch (_cbKeep.SelectedIndex)
            {
                case 1: return "5m";
                case 2: return "10m";
                case 3: return "30m";
                case 4: return "1h";
                default: return "-1";
            }
        }

        void SetBusy(bool busy, string text, Color color)
        {
            _busy = busy;
            _btnToMain.Enabled = !busy;
            _btnToAlt.Enabled = !busy;
            if (_btnLightLoad != null) _btnLightLoad.Enabled = !busy;
            if (_btnLightUnload != null) _btnLightUnload.Enabled = !busy;
            _switchStatus.Text = text;
            _switchStatus.ForeColor = color;
        }

        void DoLoad(string model, Button btn)
        {
            RunAsync(btn, "加载中…", "正在加载 " + model + "（冷启动约 50 秒）…", delegate
            {
                Ollama.LoadModel(model, KeepValue());
                return "已加载 " + model;
            });
        }

        void DoUnload(string model, Button btn)
        {
            RunAsync(btn, "卸载中…", "正在卸载 " + model + "…", delegate
            {
                Ollama.UnloadModel(model);
                return "已卸载 " + model;
            });
        }

        void DoKeep(string model, string ka, Button btn)
        {
            string text = ka == "-1" ? "常驻" : (ka == "+10m" ? "延长 10 分钟" : ka);
            RunAsync(btn, "设置中…", "正在设置 " + model + " → " + text + "…", delegate
            {
                if (ka == "+10m")
                {
                    // 在当前剩余时间上加 10 分钟：直接设为 10m 会缩短，故读当前 expiry
                    DateTime exp = DateTime.MinValue;
                    foreach (RunningInfo r in _snap.Running)
                        if (r.Name == model) exp = r.ExpiresLocal;
                    if (exp == DateTime.MinValue || exp > DateTime.Now.AddYears(5))
                    {
                        Ollama.SetKeepAlive(model, "10m");
                    }
                    else
                    {
                        double remain = (exp - DateTime.Now).TotalSeconds + 600;
                        if (remain < 60) remain = 600;
                        Ollama.SetKeepAlive(model, ((int)remain) + "s");
                    }
                }
                else
                {
                    Ollama.SetKeepAlive(model, ka);
                }
                return "已设置 " + model + " → " + text;
            });
        }

        void DoSwitch(string target, Button btn)
        {
            if (string.IsNullOrEmpty(target))
            {
                _switchStatus.Text = "请先选择模型";
                _switchStatus.ForeColor = ColorTranslator.FromHtml("#ff9b9b");
                return;
            }
            RunAsync(btn, "切换中…", "正在切换到 " + target + "…", delegate
            {
                int n = Ollama.SwitchModel(target, KeepValue());
                return n > 0 ? "已切换到 " + target + "（卸载了 " + n + " 个模型）" : "已切换到 " + target;
            });
        }

        delegate string Work();

        void RunAsync(Button btn, string busyText, string statusText, Work work)
        {
            if (_busy) return;
            SetBusy(true, statusText, Ui.Muted);
            string old = btn != null ? btn.Text : "";
            if (btn != null) { btn.Enabled = false; btn.Text = busyText; }

            ThreadPool.QueueUserWorkItem(delegate
            {
                string msg;
                bool ok = true;
                try { msg = work(); }
                catch (Exception ex) { ok = false; msg = "操作失败：" + ex.Message; }
                try
                {
                    BeginInvoke(new Action(delegate
                    {
                        if (btn != null) { btn.Enabled = true; btn.Text = old; }
                        SetBusy(false, msg, ok ? ColorTranslator.FromHtml("#7ee2a8") : ColorTranslator.FromHtml("#ff9b9b"));
                        _listSig = "";
                        Poll();
                    }));
                }
                catch (Exception) { }
            });
        }

        void SavePair(string which)
        {
            if (_busy) return;
            Config c = Ollama.LoadConfig();
            if (which == "main") c.MainModel = SelectedModel(_cbMain);
            else if (which == "light") c.LightModel = SelectedModel(_cbLight);
            else c.AltModel = SelectedModel(_cbAlt);
            Ollama.SaveConfig(c);
            _listSig = "";
        }

        void ApplyCombos(Snapshot st)
        {
            if (_busy) return;
            string[] names = new string[st.Models.Count];
            for (int i = 0; i < st.Models.Count; i++) names[i] = st.Models[i].Name;

            FillCombo(_cbMain, names, st.Cfg.MainModel);
            FillCombo(_cbAlt, names, st.Cfg.AltModel);
            FillCombo(_cbLight, names, st.Cfg.LightModel);

            string keep = st.Cfg.KeepAlive;
            int idx = 0;
            if (keep == "5m") idx = 1;
            else if (keep == "10m") idx = 2;
            else if (keep == "30m") idx = 3;
            else if (keep == "1h") idx = 4;
            if (_cbKeep.SelectedIndex != idx) _cbKeep.SelectedIndex = idx;
        }

        void FillCombo(ComboBox cb, string[] names, string want)
        {
            string sig = string.Join("|", names);
            if (Convert.ToString(cb.Tag) != sig)
            {
                string prev = cb.SelectedItem != null ? cb.SelectedItem.ToString() : "";
                cb.Items.Clear();
                if (names.Length == 0) cb.Items.Add("（无可用模型）");
                else cb.Items.AddRange(names);
                cb.Tag = sig;
                int i = Array.IndexOf(names, want);
                if (i >= 0) cb.SelectedIndex = i;
                else if (names.Length > 0)
                {
                    int p = Array.IndexOf(names, prev);
                    cb.SelectedIndex = p >= 0 ? p : 0;
                }
            }
        }

        /* ============================ 计时 ============================ */
        void UpdateTimers()
        {
            foreach (KeyValuePair<string, Label> kv in _timers)
            {
                RunningInfo r;
                if (_runningMap.TryGetValue(kv.Key, out r))
                {
                    kv.Value.Text = RemainText(r);
                    kv.Value.ForeColor = IsForever(r) ? ColorTranslator.FromHtml("#7ee2a8") : ColorTranslator.FromHtml("#ffd28a");
                }
            }
        }

        static bool IsForever(RunningInfo r)
        {
            if (r.ExpiresLocal == DateTime.MinValue) return false;
            return r.ExpiresLocal > DateTime.Now.AddYears(5);
        }

        static string RemainText(RunningInfo r)
        {
            if (r.ExpiresLocal == DateTime.MinValue) return "未知";
            if (IsForever(r)) return "常驻";
            TimeSpan d = r.ExpiresLocal - DateTime.Now;
            if (d.TotalSeconds <= 0) return "即将卸载";
            if (d.TotalHours >= 1)
                return string.Format("{0}:{1:00}:{2:00}", (int)d.TotalHours, d.Minutes, d.Seconds);
            return string.Format("{0:00}:{1:00}", (int)d.TotalMinutes, d.Seconds);
        }

        static string FormatBytes(long b)
        {
            if (b <= 0) return "0 GB";
            double gb = b / 1e9;
            if (gb < 1) return Math.Round(b / 1e6).ToString("0", CultureInfo.InvariantCulture) + " MB";
            return gb.ToString("0.00", CultureInfo.InvariantCulture) + " GB";
        }

        /* ============================ 设置窗口 ============================ */
        void OpenSettings()
        {
            Form dlg = new Form();
            dlg.Text = "设置";
            dlg.ClientSize = new Size(620, 448);
            dlg.StartPosition = FormStartPosition.CenterParent;
            dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
            dlg.MaximizeBox = false;
            dlg.MinimizeBox = false;
            dlg.BackColor = Ui.PanelBg;
            dlg.ForeColor = Ui.Ink;
            dlg.Font = Ui.F(Ui.FBody, FontStyle.Regular);

            Config c = Ollama.LoadConfig();

            Label l1 = Ui.Label("Ollama 可执行文件路径（用于“启动服务”）", Ui.FSub, FontStyle.Regular, Ui.Muted);
            l1.Location = new Point(20, 16);
            dlg.Controls.Add(l1);

            TextBox tb = new TextBox();
            tb.Text = c.OllamaExe;
            tb.Location = new Point(20, 38);
            tb.Size = new Size(580, 28);
            tb.BackColor = ColorTranslator.FromHtml("#0e141b");
            tb.ForeColor = Ui.Ink;
            tb.BorderStyle = BorderStyle.FixedSingle;
            tb.Font = Ui.F(Ui.FBody, FontStyle.Regular);
            dlg.Controls.Add(tb);

            Label l2 = Ui.Label("默认保持时长", Ui.FSub, FontStyle.Regular, Ui.Muted);
            l2.Location = new Point(20, 84);
            dlg.Controls.Add(l2);

            ComboBox cb = MakeCombo();
            cb.Location = new Point(20, 106);
            cb.Width = 220;
            cb.Items.AddRange(new object[] { "常驻（不卸载）", "5 分钟", "10 分钟", "30 分钟", "1 小时" });
            int idx = 0;
            if (c.KeepAlive == "5m") idx = 1;
            else if (c.KeepAlive == "10m") idx = 2;
            else if (c.KeepAlive == "30m") idx = 3;
            else if (c.KeepAlive == "1h") idx = 4;
            cb.SelectedIndex = idx;
            dlg.Controls.Add(cb);

            Label l3 = Ui.Label("游戏模式（检测到指定游戏进程时自动释放显存）", Ui.FSub, FontStyle.Regular, Ui.Muted);
            l3.Location = new Point(20, 152);
            dlg.Controls.Add(l3);

            CheckBox gchk = new CheckBox();
            gchk.Text = "启用游戏模式";
            gchk.AutoSize = true;
            gchk.ForeColor = Ui.Ink;
            gchk.Font = Ui.F(Ui.FSub, FontStyle.Regular);
            gchk.Location = new Point(20, 176);
            gchk.Checked = c.GameMode;
            dlg.Controls.Add(gchk);

            TextBox gtb = new TextBox();
            gtb.Text = c.GameProcess;
            gtb.Location = new Point(150, 173);
            gtb.Size = new Size(320, 28);
            gtb.BackColor = ColorTranslator.FromHtml("#0e141b");
            gtb.ForeColor = Ui.Ink;
            gtb.BorderStyle = BorderStyle.FixedSingle;
            gtb.Font = Ui.F(Ui.FBody, FontStyle.Regular);
            dlg.Controls.Add(gtb);

            Label glab = Ui.Label("游戏进程名（不含 .exe）", Ui.FSmall, FontStyle.Regular, Ui.Dim);
            glab.Location = new Point(480, 178);
            dlg.Controls.Add(glab);

            Label l4 = Ui.Label("语音转文字：Python 路径（留空自动查找，需已安装 faster-whisper）", Ui.FSub, FontStyle.Regular, Ui.Muted);
            l4.Location = new Point(20, 218);
            dlg.Controls.Add(l4);

            TextBox tbPy = new TextBox();
            tbPy.Text = c.AsrPython;
            tbPy.Location = new Point(20, 240);
            tbPy.Size = new Size(580, 28);
            tbPy.BackColor = ColorTranslator.FromHtml("#0e141b");
            tbPy.ForeColor = Ui.Ink;
            tbPy.BorderStyle = BorderStyle.FixedSingle;
            tbPy.Font = Ui.F(Ui.FBody, FontStyle.Regular);
            dlg.Controls.Add(tbPy);

            Label l5 = Ui.Label("Whisper 模型目录（faster-whisper 格式，包含 model.bin）", Ui.FSub, FontStyle.Regular, Ui.Muted);
            l5.Location = new Point(20, 284);
            dlg.Controls.Add(l5);

            TextBox tbModel = new TextBox();
            tbModel.Text = c.AsrModel;
            tbModel.Location = new Point(20, 306);
            tbModel.Size = new Size(580, 28);
            tbModel.BackColor = ColorTranslator.FromHtml("#0e141b");
            tbModel.ForeColor = Ui.Ink;
            tbModel.BorderStyle = BorderStyle.FixedSingle;
            tbModel.Font = Ui.F(Ui.FBody, FontStyle.Regular);
            dlg.Controls.Add(tbModel);

            Label path = Ui.Label("配置文件：" + Ollama.ConfigPath(), Ui.FSmall, FontStyle.Regular, Ui.Dim);
            path.Location = new Point(20, 352);
            path.AutoSize = false;
            path.Size = new Size(580, 20);
            dlg.Controls.Add(path);

            Button ok = MakeButton("保存", Ui.BtnPrimary);
            ok.Location = new Point(20, 396);
            ok.Size = new Size(100, Ui.BtnH);
            ok.Click += (s, e) =>
            {
                Config nc = Ollama.LoadConfig();
                nc.OllamaExe = tb.Text.Trim();
                switch (cb.SelectedIndex)
                {
                    case 1: nc.KeepAlive = "5m"; break;
                    case 2: nc.KeepAlive = "10m"; break;
                    case 3: nc.KeepAlive = "30m"; break;
                    case 4: nc.KeepAlive = "1h"; break;
                    default: nc.KeepAlive = "-1"; break;
                }
                nc.GameMode = gchk.Checked;
                string gp = gtb.Text.Trim();
                if (gp.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) gp = gp.Substring(0, gp.Length - 4);
                nc.GameProcess = gp;
                nc.AsrPython = tbPy.Text.Trim();
                nc.AsrModel = tbModel.Text.Trim();
                Ollama.SaveConfig(nc);
                dlg.Close();
                _listSig = "";
                _gameReleased = false;
                _chkGame.Checked = nc.GameMode;
                Poll();
                UpdateGameStatus();
            };
            dlg.Controls.Add(ok);

            Button cancel = MakeButton("取消", Ui.BtnSecondary);
            cancel.Location = new Point(130, 396);
            cancel.Size = new Size(100, Ui.BtnH);
            cancel.Click += (s, e) => dlg.Close();
            dlg.Controls.Add(cancel);

            dlg.ShowDialog(this);
        }

        /* ============================ 截图（用于自检） ============================ */
        public void CaptureTo(string path)
        {
            Application.DoEvents();
            using (Bitmap bmp = new Bitmap(Width, Height))
            {
                try
                {
                    DrawToBitmap(bmp, new Rectangle(0, 0, Width, Height));
                }
                catch (Exception)
                {
                    using (Graphics g = Graphics.FromImage(bmp))
                        g.CopyFromScreen(Bounds.Left, Bounds.Top, 0, 0, Size);
                }
                bmp.Save(path, ImageFormat.Png);
            }
        }
    }

    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            string shot = null;
            for (int i = 0; i < args.Length; i++)
                if (args[i] == "--shot" && i + 1 < args.Length) shot = args[i + 1];

            MainForm f = new MainForm();
            if (shot != null)
            {
                f.TopMost = true;
                f.Shown += delegate
                {
                    System.Windows.Forms.Timer t = new System.Windows.Forms.Timer();
                    t.Interval = 4000;
                    t.Tick += delegate
                    {
                        t.Stop();
                        try { f.CaptureTo(shot); }
                        catch (Exception) { }
                        Application.Exit();
                    };
                    t.Start();
                };
            }
            Application.Run(f);
        }
    }
}
