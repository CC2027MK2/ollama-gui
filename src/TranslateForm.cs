using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace OllamaSwitch
{
    public class TranslateForm : Form
    {
        readonly List<string> _files = new List<string>();
        readonly string _precise;
        readonly string _light;
        string _outDir;
        bool _running;
        bool _fast;

        Panel _drop;
        ListView _list;
        SplitContainer _split;
        TextBox _ocrBox, _trBox;
        Label _outLabel, _status, _subLabel;
        RadioButton _rdFast, _rdPrecise;
        Button _btnStart, _btnOpen, _btnPick, _btnClose, _btnStop;

        public TranslateForm(string model, string light)
        {
            _precise = model;
            _light = string.IsNullOrEmpty(light) ? "qwen3.5-0.8b-local:latest" : light;
            _fast = false;
            Text = "一键翻译";
            ClientSize = new Size(1020, 760);
            MinimumSize = new Size(860, 640);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Ui.Bg;
            ForeColor = Ui.Ink;
            Font = Ui.F(Ui.FBody, FontStyle.Regular);

            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            // 优先放 exe 旁边的「翻译结果」（最容易找到）；被安全软件拦住时退回用户目录
            string[] roots = new string[]
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "翻译结果"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Ollama翻译"),
                Path.Combine(Path.GetTempPath(), "Ollama翻译")
            };
            foreach (string root in roots)
            {
                try
                {
                    string d = Path.Combine(root, stamp);
                    Directory.CreateDirectory(d);
                    File.WriteAllText(Path.Combine(d, ".keep"), "");
                    File.Delete(Path.Combine(d, ".keep"));
                    _outDir = d;
                    break;
                }
                catch (Exception) { }
            }
            if (_outDir == null) _outDir = Path.Combine(Path.GetTempPath(), "Ollama翻译", stamp);

            BuildUi();
            AllowDrop = true;
            DragEnter += OnDragEnter;
            DragDrop += OnDragDrop;
            FormClosing += OnClosing;
            Shown += delegate
            {
                try { _split.SplitterDistance = Math.Max(200, _split.ClientSize.Width / 2); }
                catch (Exception) { }
            };
        }

        void BuildUi()
        {
            Panel top = new Panel();
            top.Dock = DockStyle.Top;
            top.Height = 64;
            top.BackColor = Ui.PanelBg;
            top.Paint += (s, e) =>
            {
                using (Pen p = new Pen(Ui.Line)) e.Graphics.DrawLine(p, 0, top.Height - 1, top.Width, top.Height - 1);
            };
            Controls.Add(top);

            Label t = Ui.Label("一键翻译", Ui.FStrong, FontStyle.Bold, Ui.Ink);
            t.Location = new Point(18, 18);
            top.Controls.Add(t);

            // 右侧模式区固定贴右，左侧标题区填充剩余宽度，两者永不重叠
            FlowLayoutPanel modeBar = new FlowLayoutPanel();
            modeBar.Dock = DockStyle.Right;
            modeBar.FlowDirection = FlowDirection.LeftToRight;
            modeBar.WrapContents = false;
            modeBar.AutoSize = true;
            modeBar.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            modeBar.BackColor = Color.Transparent;
            modeBar.Padding = new Padding(0, 16, 18, 0);
            top.Controls.Add(modeBar);

            // PDF / 图片的 OCR 语言：系统 OCR 只装了中英文，西里尔文必须走内置引擎
            Label ll = Ui.Label("识别语言", Ui.FSub, FontStyle.Regular, Ui.Muted);
            ll.Margin = new Padding(0, 7, 10, 0);
            modeBar.Controls.Add(ll);

            ComboBox lang = new CBox();
            lang.DropDownStyle = ComboBoxStyle.DropDownList;
            lang.FlatStyle = FlatStyle.Flat;
            lang.BackColor = ColorTranslator.FromHtml("#0e141b");
            lang.ForeColor = Ui.Ink;
            lang.Font = Ui.F(Ui.FSub, FontStyle.Regular);
            lang.Width = 116;
            lang.TabStop = false;
            lang.Margin = new Padding(0, 4, 18, 0);
            lang.Items.AddRange(new object[] { "自动识别", "中文", "英文", "俄文·西里尔" });
            lang.SelectedIndex = DocTranslate.OcrLang;
            lang.SelectedIndexChanged += delegate { DocTranslate.OcrLang = lang.SelectedIndex; };
            modeBar.Controls.Add(lang);
            Ui.Tip(lang, "扫描版 PDF / 图片的文字识别语言。俄语、乌兹别克语等西里尔文用内置引擎，不依赖系统语言包");

            Label ml = Ui.Label("翻译模式", Ui.FSub, FontStyle.Regular, Ui.Muted);
            ml.Margin = new Padding(0, 7, 12, 0);
            modeBar.Controls.Add(ml);

            _rdFast = MakeRadio("快翻");
            _rdFast.Margin = new Padding(0, 5, 12, 0);
            modeBar.Controls.Add(_rdFast);

            _rdPrecise = MakeRadio("精翻");
            _rdPrecise.Margin = new Padding(0, 5, 12, 0);
            _rdPrecise.Checked = true;
            modeBar.Controls.Add(_rdPrecise);

            Label tip = Ui.Label("显存不够自动降级", Ui.FSmall, FontStyle.Regular, Ui.Dim);
            tip.Margin = new Padding(4, 7, 0, 0);
            modeBar.Controls.Add(tip);

            Panel left = new Panel();
            left.Dock = DockStyle.Fill;
            left.BackColor = Color.Transparent;
            top.Controls.Add(left);
            left.BringToFront();

            t.Parent = left;
            t.Location = new Point(18, 18);

            _subLabel = Ui.Label("", Ui.FSub, FontStyle.Regular, Ui.Muted);
            _subLabel.AutoSize = false;
            _subLabel.AutoEllipsis = true;
            _subLabel.Location = new Point(120, 23);
            _subLabel.Size = new Size(400, 22);
            _subLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            left.Controls.Add(_subLabel);
            left.Resize += delegate
            {
                int w = left.ClientSize.Width - 120 - 8;
                if (w > 80) _subLabel.Width = w;
            };

            EventHandler modeChanged = delegate
            {
                _fast = _rdFast.Checked;
                UpdateModeText();
            };
            _rdFast.CheckedChanged += modeChanged;
            _rdPrecise.CheckedChanged += modeChanged;

            ToolTip modeTip = new ToolTip();
            modeTip.SetToolTip(_rdFast, "轻量模型 " + _light + "：出稿快，适合先看懂大意");
            modeTip.SetToolTip(_rdPrecise, "精翻模型 " + _precise + "：质量最好；显存不足会自动降级为快翻");

            UpdateModeText();

            _drop = new Panel();
            _drop.Dock = DockStyle.Top;
            _drop.Height = 104;
            _drop.Margin = new Padding(0);
            _drop.Paint += DrawDrop;
            _drop.AllowDrop = true;
            _drop.DragEnter += OnDragEnter;
            _drop.DragDrop += OnDragDrop;
            _drop.Cursor = Cursors.Hand;
            _drop.Click += (s, e) => PickFiles();
            Controls.Add(_drop);

            _list = new DarkList();
            _list.Dock = DockStyle.Top;
            _list.Height = 172;
            _list.View = View.Details;
            _list.FullRowSelect = true;
            _list.BackColor = Ui.PanelBg;
            _list.ForeColor = Ui.Ink;
            _list.BorderStyle = BorderStyle.None;
            _list.Font = Ui.F(Ui.FSub, FontStyle.Regular);
            _list.Columns.Add("文件", 290);
            _list.Columns.Add("格式", 64);
            _list.Columns.Add("状态", 320);
            _list.Columns.Add("输出", 320);
            Ui.DarkHeader(_list);
            // 末列随宽度伸缩，避免右侧露白边
            _list.Resize += delegate
            {
                int rest = _list.ClientSize.Width - 290 - 64 - 320 - 2;
                if (rest > 60) _list.Columns[3].Width = rest;
            };
            Controls.Add(_list);

            SplitContainer split = new SplitContainer();
            split.Dock = DockStyle.Fill;
            split.Orientation = Orientation.Vertical;
            split.FixedPanel = FixedPanel.Panel1;
            split.SplitterDistance = 480;
            split.BackColor = Ui.Bg;
            split.Panel1.BackColor = Ui.Bg;
            split.Panel2.BackColor = Ui.Bg;
            split.Panel1.Padding = new Padding(14, 28, 7, 8);
            split.Panel2.Padding = new Padding(7, 28, 14, 8);
            _split = split;
            Controls.Add(split);

            Label l1 = Ui.Label("识别原文（图片 / PDF 的 OCR 结果）", Ui.FSub, FontStyle.Bold, Ui.Muted);
            l1.AutoSize = true;
            split.Panel1.Controls.Add(l1);
            l1.Location = new Point(14, 6);
            _ocrBox = MakeBox(true);
            split.Panel1.Controls.Add(_ocrBox);
            _ocrBox.BringToFront();

            Label l2 = Ui.Label("译文", Ui.FSub, FontStyle.Bold, Ui.Muted);
            l2.AutoSize = true;
            split.Panel2.Controls.Add(l2);
            l2.Location = new Point(7, 6);
            _trBox = MakeBox(false);
            split.Panel2.Controls.Add(_trBox);
            _trBox.BringToFront();

            Panel bottom = new Panel();
            bottom.Dock = DockStyle.Bottom;
            bottom.Height = 96;
            bottom.BackColor = Ui.PanelBg;
            bottom.Paint += (s, e) =>
            {
                using (Pen p = new Pen(Ui.Line)) e.Graphics.DrawLine(p, 0, 0, bottom.Width, 0);
            };
            Controls.Add(bottom);

            _outLabel = Ui.Label("", Ui.FSmall, FontStyle.Regular, Ui.Dim);
            _outLabel.AutoSize = false;
            _outLabel.AutoEllipsis = true;
            _outLabel.Location = new Point(16, 10);
            _outLabel.Size = new Size(720, 22);
            _outLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            bottom.Controls.Add(_outLabel);
            bottom.Resize += delegate
            {
                int w = bottom.ClientSize.Width - 32;
                if (w > 100) _outLabel.Width = w;
            };

            // 左侧按钮区：流式自动排布，按钮之间保持固定间距，永不挤在一起
            FlowLayoutPanel btnRow = new FlowLayoutPanel();
            btnRow.FlowDirection = FlowDirection.LeftToRight;
            btnRow.WrapContents = false;
            btnRow.AutoSize = true;
            btnRow.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            btnRow.BackColor = Color.Transparent;
            btnRow.Location = new Point(16, 42);
            btnRow.Padding = new Padding(0);
            bottom.Controls.Add(btnRow);

            _btnStart = MakeButton("开始翻译", Ui.BtnPrimary);
            _btnStart.Size = new Size(124, Ui.BtnH);
            _btnStart.Margin = new Padding(0, 0, 10, 0);
            _btnStart.Click += (s, e) => StartWork();
            btnRow.Controls.Add(_btnStart);

            _btnPick = MakeButton("选择文件…", Ui.BtnSecondary);
            _btnPick.Size = new Size(110, Ui.BtnH);
            _btnPick.Margin = new Padding(0, 0, 10, 0);
            _btnPick.Click += (s, e) => PickFiles();
            btnRow.Controls.Add(_btnPick);

            _btnOpen = MakeButton("打开输出目录", Ui.BtnSecondary);
            _btnOpen.Size = new Size(126, Ui.BtnH);
            _btnOpen.Margin = new Padding(0, 0, 10, 0);
            _btnOpen.Click += (s, e) => OpenOutDir();
            btnRow.Controls.Add(_btnOpen);

            _btnClose = MakeButton("关闭", Ui.BtnSecondary);
            _btnClose.Size = new Size(88, Ui.BtnH);
            _btnClose.Margin = new Padding(0, 0, 10, 0);
            _btnClose.Click += (s, e) => Close();
            btnRow.Controls.Add(_btnClose);

            _btnStop = MakeButton("停止", Ui.BtnDanger);
            _btnStop.Size = new Size(88, Ui.BtnH);
            _btnStop.Margin = new Padding(0, 0, 10, 0);
            _btnStop.Visible = false;
            _btnStop.Click += (s, e) =>
            {
                DocTranslate.Cancel = true;
                _btnStop.Enabled = false;
                _btnStop.Text = "停止中…";
                SetStatus("正在停止…（当前这一段返回后停下，已翻部分会保存）", Ui.Warn);
            };
            btnRow.Controls.Add(_btnStop);

            // 状态文字固定贴右下，不再与按钮区抢位置
            _status = Ui.Label("把 .docx / .pdf / png / jpg 拖到上面的区域，然后点「开始翻译」", Ui.FSub, FontStyle.Regular, Ui.Muted);
            _status.AutoSize = false;
            _status.AutoEllipsis = true;
            _status.TextAlign = ContentAlignment.MiddleRight;
            _status.Size = new Size(500, 22);
            _status.Location = new Point(500, 50);
            _status.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            bottom.Controls.Add(_status);
            bottom.Resize += delegate
            {
                int x = btnRow.Width + 32;
                int w = bottom.ClientSize.Width - x - 16;
                if (w > 120) { _status.Location = new Point(x, 50); _status.Width = w; }
            };

            // WinForms 的 Dock 按 z-order 从后往前计算：索引大的先占边缘。
            // 目标顺序（从上到下）：顶栏 → 拖拽区 → 文件列表 → 预览 → 底部按钮条
            Controls.SetChildIndex(split, 0);
            Controls.SetChildIndex(_list, 1);
            Controls.SetChildIndex(_drop, 2);
            Controls.SetChildIndex(top, 3);
            Controls.SetChildIndex(bottom, 4);

            UpdateOutLabel();
        }

        Button MakeButton(string text, int style)
        {
            return Ui.Button(text, style);
        }

        RadioButton MakeRadio(string text)
        {
            RRadio r = new RRadio();
            r.Text = text;
            r.FlatStyle = FlatStyle.Flat;
            r.BackColor = Color.Transparent;
            r.ForeColor = Ui.Ink;
            r.Font = Ui.F(Ui.FBody, FontStyle.Bold);
            Size t = TextRenderer.MeasureText(text, r.Font);
            r.Size = new Size(22 + t.Width, 24);
            return r;
        }

        void UpdateModeText()
        {
            if (_subLabel == null) return;
            string m = _fast ? _light : _precise;
            _subLabel.Text = (_fast ? "快翻" : "精翻") + " · " + m + " · 译文另存新文件，不改原件";
        }

        TextBox MakeBox(bool readOnly)
        {
            TextBox b = new DarkBox();
            b.Dock = DockStyle.Fill;
            b.Multiline = true;
            b.ScrollBars = ScrollBars.Vertical;
            b.ReadOnly = readOnly;
            b.BackColor = Ui.PanelBg;
            b.ForeColor = readOnly ? Ui.Muted : Ui.Ink;
            b.BorderStyle = BorderStyle.FixedSingle;
            b.Font = Ui.F(Ui.FBody, FontStyle.Regular);
            return b;
        }

        void DrawDrop(object sender, PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle r = new Rectangle(14, 8, _drop.Width - 29, _drop.Height - 17);
            using (GraphicsPath gp = Ui.RoundRect(r, 10))
            using (Pen p = new Pen(Ui.Line, 1.2f))
            {
                p.DashStyle = DashStyle.Dash;
                e.Graphics.DrawPath(p, gp);
            }
            string s1 = "把文件拖到这里，或点击选择";
            string s2 = "支持 .docx / .pdf / .png / .jpg，可一次多个";
            using (Font f1 = Ui.F(Ui.FTitle, FontStyle.Bold))
            using (Font f2 = Ui.F(Ui.FSub, FontStyle.Regular))
            {
                SizeF a = e.Graphics.MeasureString(s1, f1);
                SizeF b = e.Graphics.MeasureString(s2, f2);
                float cx = _drop.Width / 2f;
                e.Graphics.DrawString(s1, f1, new SolidBrush(Ui.Ink), cx - a.Width / 2, 28);
                e.Graphics.DrawString(s2, f2, new SolidBrush(Ui.Muted), cx - b.Width / 2, 54);
            }
        }

        void UpdateOutLabel()
        {
            _outLabel.Text = "输出目录：" + _outDir;
        }

        void OnDragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy;
            else e.Effect = DragDropEffects.None;
        }

        void OnDragDrop(object sender, DragEventArgs e)
        {
            string[] files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files != null) AddFiles(files);
        }

        void PickFiles()
        {
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Multiselect = true;
                dlg.Filter = "可翻译文件|*.docx;*.pdf;*.png;*.jpg;*.jpeg|Word 文档|*.docx|PDF|*.pdf|图片|*.png;*.jpg;*.jpeg";
                if (dlg.ShowDialog(this) == DialogResult.OK) AddFiles(dlg.FileNames);
            }
        }

        void AddFiles(string[] files)
        {
            int added = 0, skipped = 0;
            foreach (string f in files)
            {
                string ext = Path.GetExtension(f).ToLowerInvariant();
                if (ext != ".docx" && ext != ".pdf" && ext != ".png" && ext != ".jpg" && ext != ".jpeg") { skipped++; continue; }
                if (!File.Exists(f)) { skipped++; continue; }
                if (_files.Contains(f)) continue;
                _files.Add(f);
                ListViewItem it = new ListViewItem(Path.GetFileName(f));
                it.SubItems.Add(ext.TrimStart('.'));
                it.SubItems.Add("等待");
                it.SubItems.Add("");
                it.Tag = f;
                _list.Items.Add(it);
                added++;
            }
            _status.Text = "已加入 " + added + " 个文件" + (skipped > 0 ? "，跳过 " + skipped + " 个不支持的文件" : "");
        }

        void OpenOutDir()
        {
            try
            {
                Directory.CreateDirectory(_outDir);
                Process.Start("explorer.exe", "\"" + _outDir + "\"");
            }
            catch (Exception ex) { MessageBox.Show(this, "打开目录失败：" + ex.Message, "提示"); }
        }

        void StartWork()
        {
            if (_running) return;
            if (_files.Count == 0) { MessageBox.Show(this, "先拖入或选择要翻译的文件。", "提示"); return; }
            _running = true;
            DocTranslate.Cancel = false;
            _btnStart.Enabled = false;
            _btnPick.Enabled = false;
            _rdFast.Enabled = false;
            _rdPrecise.Enabled = false;
            _btnClose.Visible = false;
            _btnStop.Text = "停止";
            _btnStop.Enabled = true;
            _btnStop.Visible = true;
            Thread t = new Thread(Work);
            t.IsBackground = true;
            t.Start();
        }

        void Work()
        {
            int ok = 0, fail = 0;
            bool stopped = false;
            Post(delegate { SetStatus("正在连接 Ollama 服务…", Ui.Warn); });

            if (!Ollama.Online())
            {
                try { Ollama.StartService(); } catch (Exception) { }
                for (int t = 0; t < 20 && !Ollama.Online(); t++) Thread.Sleep(500);
                if (!Ollama.Online())
                {
                    Post(delegate
                    {
                        _running = false;
                        _btnStart.Enabled = true;
                        _btnPick.Enabled = true;
                        _btnStop.Visible = false;
                        _btnClose.Visible = true;
                        _rdFast.Enabled = true;
                        _rdPrecise.Enabled = true;
                        SetStatus("Ollama 服务没在运行，先回主界面点「启动服务」再试", Ui.Bad);
                    });
                    return;
                }
            }

            bool fast = _fast;
            string model = fast ? _light : _precise;
            string note = "";

            // 选中的模型本机没有时，换成另一边，避免直接失败
            if (!Ollama.ModelExists(model))
            {
                string other = fast ? _precise : _light;
                if (!string.IsNullOrEmpty(other) && Ollama.ModelExists(other))
                {
                    note = "本机没有 " + model + "，已改用 " + other;
                    model = other;
                    fast = !fast;
                }
            }

            // 精翻前先看显存：注定放不下就直接降级，别让用户白等一分钟
            if (!fast)
            {
                string why;
                if (!CanLoadOnGpu(model, out why))
                {
                    if (!string.IsNullOrEmpty(_light) && Ollama.ModelExists(_light))
                    {
                        note = "显存不足（" + why + "），已自动降级为快翻";
                        model = _light;
                        fast = true;
                    }
                    else
                    {
                        note = "显存可能不足（" + why + "），仍继续尝试精翻";
                    }
                }
            }

            bool fastNow = fast;
            string noteNow = note;
            string loadText = "正在加载" + (fastNow ? "轻量" : "精翻") + "模型（首次约 1 分钟）…";
            Post(delegate
            {
                if (_fast != fastNow)
                {
                    _fast = fastNow;
                    _rdFast.Checked = fastNow;
                    _rdPrecise.Checked = !fastNow;
                    UpdateModeText();
                }
                SetStatus(noteNow.Length > 0 ? noteNow : loadText, Ui.Warn);
            });

            try
            {
                Ollama.SwitchModel(model, "30m");
            }
            catch (Exception ex)
            {
                if (!fast && !string.IsNullOrEmpty(_light) && Ollama.ModelExists(_light))
                {
                    string why = ex.Message;
                    model = _light;
                    fast = true;
                    Post(delegate { SetStatus("精翻模型加载失败，已改用快翻：" + why, Ui.Warn); });
                    try { Ollama.SwitchModel(_light, "30m"); }
                    catch (Exception ex2)
                    {
                        string e2 = ex2.Message;
                        Post(delegate { SetStatus("轻量模型也加载失败：" + e2, Ui.Bad); });
                    }
                }
                else
                {
                    string em = ex.Message;
                    Post(delegate { SetStatus("模型加载失败：" + em, Ui.Bad); });
                }
            }

            // 加载成功但大部分层被挤到内存，同样降级
            if (!fast)
            {
                RunningInfo loaded = Ollama.RunningOf(model);
                if (loaded != null && loaded.SizeBytes > 0 && loaded.VramBytes < loaded.SizeBytes * 0.85
                    && !string.IsNullOrEmpty(_light) && Ollama.ModelExists(_light))
                {
                    model = _light;
                    fast = true;
                    Post(delegate
                    {
                        _fast = true;
                        _rdFast.Checked = true;
                        _rdPrecise.Checked = false;
                        UpdateModeText();
                        SetStatus("精翻模型大部分被挤到内存，已自动降级为快翻", Ui.Warn);
                    });
                    try { Ollama.SwitchModel(_light, "30m"); } catch (Exception) { }
                }
            }

            for (int i = 0; i < _files.Count; i++)
            {
                if (DocTranslate.Cancel) { stopped = true; break; }
                string file = _files[i];
                string name = Path.GetFileName(file);
                string ext = Path.GetExtension(file).ToLowerInvariant();
                int idx = i;
                Post(delegate { SetRow(idx, "处理中…", ""); SetStatus(name + " 处理中…", Ui.Muted); });
                string outPath = "";
                try
                {
                    outPath = MakeOutPath(file);
                    string info;
                    if (ext == ".docx")
                    {
                        info = DocTranslate.TranslateDocx(model, file, outPath, delegate (string s) { SetRowStatus(idx, s); });
                    }
                    else if (ext == ".pdf")
                    {
                        info = DocTranslate.TranslatePdf(model, file, outPath, delegate (string s) { SetRowStatus(idx, s); });
                    }
                    else
                    {
                        Post(delegate { SetRowStatus(idx, "识别文字（OCR）…"); });
                        string ocr = DocTranslate.OcrImageFile(file);
                        Post(delegate { _ocrBox.Text = ocr; });

                        List<string> paras = new List<string>();
                        foreach (string p in Regex.Split(ocr, "\n"))
                            if (p.Trim().Length > 0) paras.Add(p.Trim());

                        // 每翻完一批就落盘一次，中途停止时已译部分保留
                        List<string> trDone = null;
                        Action<List<string>> checkpoint = delegate (List<string> partial)
                        {
                            trDone = partial;
                            try
                            {
                                List<DocLine> docLines = new List<DocLine>();
                                docLines.Add(new DocLine("识别原文（OCR）", 1));
                                foreach (string p in paras) docLines.Add(new DocLine(p, 0));
                                docLines.Add(new DocLine("译文", 1));
                                foreach (string s in partial) docLines.Add(new DocLine(s, 0));
                                DocTranslate.WriteDocx(outPath, docLines);
                            }
                            catch (Exception) { }
                        };

                        List<string> tr;
                        try
                        {
                            tr = DocTranslate.TranslateParagraphs(model, paras,
                                delegate (string s) { SetRowStatus(idx, s); }, checkpoint);
                        }
                        catch (OperationCanceledException)
                        {
                            if (trDone != null) checkpoint(trDone);
                            throw;
                        }

                        StringBuilder all = new StringBuilder();
                        foreach (string s in tr) { if (all.Length > 0) all.Append("\n"); all.Append(s); }
                        string trText = all.ToString();
                        Post(delegate { _trBox.Text = trText; });

                        List<DocLine> lines = new List<DocLine>();
                        lines.Add(new DocLine("识别原文（OCR）", 1));
                        foreach (string p in paras) lines.Add(new DocLine(p, 0));
                        lines.Add(new DocLine("译文", 1));
                        foreach (string s in tr) lines.Add(new DocLine(s, 0));
                        DocTranslate.WriteDocx(outPath, lines);
                        info = "识别 " + paras.Count + " 段并完成翻译";
                    }
                    string finalOut = outPath;
                    Post(delegate { SetRow(idx, "完成 · " + info, finalOut); });
                    ok++;
                }
                catch (OperationCanceledException)
                {
                    // 中断前已翻的部分已经写进输出文件，保留下来
                    string saved = outPath;
                    if (string.IsNullOrEmpty(saved) || !File.Exists(saved)) saved = "";
                    string savedCopy = saved;
                    string tip = saved == "" ? "已停止 · 尚未产生译文" : "已停止 · 已翻部分已保存";
                    Post(delegate { SetRow(idx, tip, savedCopy); });
                    stopped = true;
                    break;
                }
                catch (Exception ex)
                {
                    string msg = ex.Message;
                    if (msg.Length > 80) msg = msg.Substring(0, 80) + "…";
                    Post(delegate { SetRow(idx, "失败：" + msg, ""); });
                    fail++;
                }
            }

            Post(delegate
            {
                _running = false;
                _btnStart.Enabled = true;
                _btnPick.Enabled = true;
                _btnStop.Visible = false;
                _btnClose.Visible = true;
                _rdFast.Enabled = true;
                _rdPrecise.Enabled = true;
                if (stopped) SetStatus("已停止：完成 " + ok + " 个，失败 " + fail + " 个 · 已翻部分已保存到输出目录", Ui.Warn);
                else SetStatus("完成：成功 " + ok + " 个，失败 " + fail + " 个", fail > 0 ? Ui.Warn : Ui.Ok);
            });
        }

        // 估算显存能不能装下：总显存 - 已用 + 待卸载的其他驻留模型
        bool CanLoadOnGpu(string model, out string reason)
        {
            reason = "";
            try
            {
                Snapshot st = Ollama.GetSnapshot();
                foreach (RunningInfo r in st.Running)
                    if (string.Equals(r.Name, model, StringComparison.OrdinalIgnoreCase) && r.VramBytes > 0) return true;
                if (!st.Gpu.Valid) return true;

                long need = 0;
                foreach (ModelInfo m in st.Models)
                    if (string.Equals(m.Name, model, StringComparison.OrdinalIgnoreCase)) need = m.SizeBytes;
                if (need <= 0) return true;

                long free = ((long)st.Gpu.TotalMiB - st.Gpu.UsedMiB) * 1048576L;
                foreach (RunningInfo r in st.Running)
                    if (!string.Equals(r.Name, model, StringComparison.OrdinalIgnoreCase)) free += r.VramBytes;

                // 桌面本身会占用一部分显存，显存紧张时系统还会主动让出，
                // 所以这里只拦「明显放不下」的情况，真正的降级由加载后的显存占比判断
                if (free < need * 0.75)
                {
                    reason = string.Format("需 {0:0.0} GB，可用 {1:0.0} GB", need / 1e9, free / 1e9);
                    return false;
                }
                return true;
            }
            catch (Exception) { return true; }
        }

        string MakeOutPath(string src)
        {
            string baseName = Path.GetFileNameWithoutExtension(src);
            string p = Path.Combine(_outDir, baseName + "_译文.docx");
            int n = 2;
            while (File.Exists(p))
            {
                p = Path.Combine(_outDir, baseName + "_译文(" + n + ").docx");
                n++;
            }
            return p;
        }

        void Post(Action a)
        {
            try
            {
                if (IsDisposed || !IsHandleCreated) return;
                BeginInvoke(a);
            }
            catch (Exception) { }
        }

        void SetStatus(string text, Color color)
        {
            _status.Text = text;
            _status.ForeColor = color;
        }

        void SetRow(int idx, string status, string outPath)
        {
            if (idx < 0 || idx >= _list.Items.Count) return;
            _list.Items[idx].SubItems[2].Text = status;
            _list.Items[idx].SubItems[3].Text = outPath == null ? "" : Path.GetFileName(outPath);
        }

        void SetRowStatus(int idx, string status)
        {
            Post(delegate
            {
                SetRow(idx, status, _list.Items[idx].SubItems[3].Text);
                SetStatus(status, Ui.Muted);
            });
        }

        void OnClosing(object sender, FormClosingEventArgs e)
        {
            if (_running)
            {
                DialogResult r = MessageBox.Show(this, "翻译还在进行中，现在关闭会中断。确定关闭吗？", "提示",
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);
                if (r != DialogResult.OK) { e.Cancel = true; return; }
                DocTranslate.Cancel = true;
            }
        }
    }
}
