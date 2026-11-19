using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace OllamaSwitch
{
    public class SpeechForm : Form
    {
        static readonly string[] Exts = new string[] {
            ".wav", ".mp3", ".m4a", ".aac", ".flac", ".ogg", ".opus", ".wma", ".mp4", ".mkv", ".mov", ".webm"
        };

        const string PolishPrompt =
            "你是语音转写校对员，输入可能是中文或英文。只做两件事：纠正同音错别字 / 拼写错误，补全逗号和句号。\n" +
            "要求：\n" +
            "1. 不要换词，不要改句式，不要调整语气，不要增删信息；\n" +
            "2. 按语义分段，每段一行；\n" +
            "3. 直接输出整理后的文本，不要解释。";

        const string ResEngine = "asr_engine.exe";
        const string ResModelBin = "model.bin";
        const string ResConfig = "config.json";
        const string ResTokenizer = "tokenizer.json";
        const string ResVocab = "vocabulary.txt";

        static readonly JavaScriptSerializer Ser = new JavaScriptSerializer();

        readonly List<string> _files = new List<string>();
        readonly string _polishModel;
        readonly object _procLock = new object();

        string _outDir;
        string _python, _script, _modelDir, _engine;
        Process _proc;
        bool _running;
        bool _cancel;

        Panel _drop;
        ListView _list;
        SplitContainer _split;
        TextBox _srcBox, _outBox;
        Label _outLabel, _status, _subLabel;
        ComboBox _cbLang;
        Button _btnStart, _btnOpen, _btnPick, _btnClose, _btnStop;

        public SpeechForm(string polishModel)
        {
            _polishModel = polishModel;
            Text = "语音转文字";
            ClientSize = new Size(1020, 760);
            MinimumSize = new Size(860, 640);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Ui.Bg;
            ForeColor = Ui.Ink;
            Font = Ui.F(Ui.FBody, FontStyle.Regular);

            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string[] roots = new string[]
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "转写结果"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Ollama转写"),
                Path.Combine(Path.GetTempPath(), "Ollama转写")
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
            if (_outDir == null) _outDir = Path.Combine(Path.GetTempPath(), "Ollama转写", stamp);

            BuildUi();
            AllowDrop = true;
            DragEnter += OnDragEnter;
            DragDrop += OnDragDrop;
            FormClosing += OnClosing;
            Shown += delegate
            {
                try { _split.SplitterDistance = Math.Max(200, _split.ClientSize.Width / 2); }
                catch (Exception) { }
                // 下拉框默认拿到焦点时整块会变成系统高亮蓝，启动时把焦点移开
                ActiveControl = null;
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

            Label t = Ui.Label("语音转文字", Ui.FStrong, FontStyle.Bold, Ui.Ink);
            t.Location = new Point(18, 18);
            top.Controls.Add(t);

            // 右侧语言区固定贴右，左侧标题区填充剩余宽度，两者永不重叠
            FlowLayoutPanel langBar = new FlowLayoutPanel();
            langBar.Dock = DockStyle.Right;
            langBar.FlowDirection = FlowDirection.LeftToRight;
            langBar.WrapContents = false;
            langBar.AutoSize = true;
            langBar.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            langBar.BackColor = Color.Transparent;
            langBar.Padding = new Padding(0, 16, 18, 0);
            top.Controls.Add(langBar);

            Label ll = Ui.Label("音频语言", Ui.FSub, FontStyle.Regular, Ui.Muted);
            ll.Margin = new Padding(0, 7, 10, 0);
            langBar.Controls.Add(ll);

            _cbLang = new CBox();
            _cbLang.DropDownStyle = ComboBoxStyle.DropDownList;
            _cbLang.FlatStyle = FlatStyle.Flat;
            _cbLang.BackColor = ColorTranslator.FromHtml("#0e141b");
            _cbLang.ForeColor = Ui.Ink;
            _cbLang.Font = Ui.F(Ui.FBody, FontStyle.Regular);
            _cbLang.Items.AddRange(new object[] { "中文", "英文", "自动识别" });
            _cbLang.SelectedIndex = 0;
            _cbLang.Margin = new Padding(0, 4, 0, 0);
            _cbLang.Width = 116;
            langBar.Controls.Add(_cbLang);

            Panel left = new Panel();
            left.Dock = DockStyle.Fill;
            left.BackColor = Color.Transparent;
            top.Controls.Add(left);
            left.BringToFront();

            t.Parent = left;
            t.Location = new Point(18, 18);

            _subLabel = Ui.Label("Whisper small 本地识别 → " + _polishModel + " 润色", Ui.FSub, FontStyle.Regular, Ui.Muted);
            _subLabel.AutoSize = false;
            _subLabel.AutoEllipsis = true;
            _subLabel.Location = new Point(140, 23);
            _subLabel.Size = new Size(520, 22);
            _subLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            left.Controls.Add(_subLabel);
            left.Resize += delegate
            {
                int w = left.ClientSize.Width - 140 - 8;
                if (w > 80) _subLabel.Width = w;
            };

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
            _list.Height = 150;
            _list.View = View.Details;
            _list.FullRowSelect = true;
            _list.BackColor = Ui.PanelBg;
            _list.ForeColor = Ui.Ink;
            _list.BorderStyle = BorderStyle.None;
            _list.Font = Ui.F(Ui.FSub, FontStyle.Regular);
            _list.Columns.Add("文件", 290);
            _list.Columns.Add("格式", 64);
            _list.Columns.Add("状态", 330);
            _list.Columns.Add("输出", 310);
            Ui.DarkHeader(_list);
            // 末列随宽度伸缩，避免右侧露白边
            _list.Resize += delegate
            {
                int rest = _list.ClientSize.Width - 290 - 64 - 330 - 2;
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

            Label l1 = Ui.Label("识别原文（Whisper）", Ui.FSub, FontStyle.Bold, Ui.Muted);
            l1.AutoSize = true;
            split.Panel1.Controls.Add(l1);
            l1.Location = new Point(14, 6);
            _srcBox = MakeBox(true);
            split.Panel1.Controls.Add(_srcBox);
            _srcBox.BringToFront();

            Label l2 = Ui.Label("润色后文本（可编辑）", Ui.FSub, FontStyle.Bold, Ui.Muted);
            l2.AutoSize = true;
            split.Panel2.Controls.Add(l2);
            l2.Location = new Point(7, 6);
            _outBox = MakeBox(false);
            split.Panel2.Controls.Add(_outBox);
            _outBox.BringToFront();

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

            _btnStart = MakeButton("开始转写", Ui.BtnPrimary);
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
                _cancel = true;
                _btnStop.Enabled = false;
                _btnStop.Text = "停止中…";
                lock (_procLock)
                {
                    if (_proc != null)
                    {
                        try { _proc.Kill(); } catch (Exception) { }
                    }
                }
                SetStatus("正在停止…", Ui.Warn);
            };
            btnRow.Controls.Add(_btnStop);

            // 状态文字固定贴右下，不再与按钮区抢位置
            _status = Ui.Label("把音频拖到上面的区域，然后点「开始转写」", Ui.FSub, FontStyle.Regular, Ui.Muted);
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
            string s1 = "把音频拖到这里，或点击选择";
            string s2 = "支持 wav / mp3 / m4a / flac / ogg / mp4，可一次多个";
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
                dlg.Filter = "音频 / 视频|*.wav;*.mp3;*.m4a;*.aac;*.flac;*.ogg;*.opus;*.wma;*.mp4;*.mkv;*.mov;*.webm|所有文件|*.*";
                if (dlg.ShowDialog(this) == DialogResult.OK) AddFiles(dlg.FileNames);
            }
        }

        void AddFiles(string[] files)
        {
            int added = 0, skipped = 0;
            foreach (string f in files)
            {
                string ext = Path.GetExtension(f).ToLowerInvariant();
                if (Array.IndexOf(Exts, ext) < 0) { skipped++; continue; }
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

        string LangValue()
        {
            if (_cbLang.SelectedIndex == 1) return "en";
            if (_cbLang.SelectedIndex == 2) return "auto";
            return "zh";
        }

        void StartWork()
        {
            if (_running) return;
            if (_files.Count == 0) { MessageBox.Show(this, "先拖入或选择要转写的音频。", "提示"); return; }

            Config c = Ollama.LoadConfig();
            _python = DetectPython(c);
            _modelDir = string.IsNullOrEmpty(c.AsrModel) ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "whisper-models", "faster-whisper-small") : c.AsrModel;
            _script = string.IsNullOrEmpty(c.AsrScript) ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"asr\asr_transcribe.py") : c.AsrScript;

            string envMsg;
            Cursor = Cursors.WaitCursor;
            bool ok = PrepareEngine(out envMsg);
            Cursor = Cursors.Default;
            if (!ok)
            {
                MessageBox.Show(this, envMsg, "语音转文字环境未就绪", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _running = true;
            _cancel = false;
            _btnStart.Enabled = false;
            _btnPick.Enabled = false;
            _btnClose.Visible = false;
            _btnStop.Text = "停止";
            _btnStop.Enabled = true;
            _btnStop.Visible = true;
            Thread t = new Thread(Work);
            t.IsBackground = true;
            t.Start();
        }

        // 优先使用打包进 exe 的独立引擎；没有内嵌资源时退回本机 Python + 脚本
        bool PrepareEngine(out string msg)
        {
            msg = "";
            Assembly asm = Assembly.GetExecutingAssembly();
            using (Stream probe = asm.GetManifestResourceStream(ResEngine))
            {
                if (probe == null)
                {
                    _engine = null;
                    return CheckEnv(out msg);
                }
            }

            try
            {
                string root = Path.Combine(Path.GetTempPath(), "OllamaModelSwitcher", "asr-cache");
                _engine = Path.Combine(root, "asr_engine.exe");
                if (!Extract(asm, ResEngine, _engine))
                {
                    msg = "内置语音引擎释放失败，请检查磁盘权限。";
                    return false;
                }

                // 本机已有外部模型就直接用，省掉一次 480 MB 解压
                if (!File.Exists(Path.Combine(_modelDir, "model.bin")))
                {
                    SetStatus("首次使用，正在释放 Whisper 模型（约 480 MB）…", Ui.Warn);
                    Application.DoEvents();
                    string mdir = Path.Combine(root, "model");
                    if (!Extract(asm, ResModelBin, Path.Combine(mdir, "model.bin")) ||
                        !Extract(asm, ResConfig, Path.Combine(mdir, "config.json")) ||
                        !Extract(asm, ResTokenizer, Path.Combine(mdir, "tokenizer.json")) ||
                        !Extract(asm, ResVocab, Path.Combine(mdir, "vocabulary.txt")))
                    {
                        msg = "内置 Whisper 模型释放失败，请检查磁盘空间（需要约 500 MB）。";
                        return false;
                    }
                    _modelDir = mdir;
                }
                msg = "内置语音引擎";
                return true;
            }
            catch (Exception ex)
            {
                msg = "释放内置语音组件失败：" + ex.Message;
                return false;
            }
        }

        static bool Extract(Assembly asm, string name, string path)
        {
            using (Stream s = asm.GetManifestResourceStream(name))
            {
                if (s == null) return false;
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                if (File.Exists(path) && new FileInfo(path).Length == s.Length) return true;
                string tmp = path + ".tmp";
                using (FileStream fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
                    s.CopyTo(fs, 1048576);
                try { if (File.Exists(path)) File.Delete(path); }
                catch (Exception) { }
                File.Move(tmp, path);
                return true;
            }
        }

        bool CheckEnv(out string msg)
        {
            msg = "";
            if (!File.Exists(_script))
            {
                msg = "找不到转写脚本：\n" + _script + "\n\n请确认 exe 同级的 asr\\asr_transcribe.py 存在。";
                return false;
            }
            if (!File.Exists(Path.Combine(_modelDir, "model.bin")))
            {
                msg = "找不到 Whisper 模型：\n" + _modelDir +
                    "\n\n该目录里应包含 model.bin / config.json / tokenizer.json / vocabulary.txt。\n" +
                    "可在主界面「设置」里修改模型目录。";
                return false;
            }
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = _python;
                psi.Arguments = "-c \"import faster_whisper,sys;print(sys.version.split()[0])\"";
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.StandardOutputEncoding = Encoding.UTF8;
                psi.StandardErrorEncoding = Encoding.UTF8;
                psi.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
                using (Process p = Process.Start(psi))
                {
                    string o = p.StandardOutput.ReadToEnd();
                    string e = p.StandardError.ReadToEnd();
                    if (!p.WaitForExit(20000))
                    {
                        try { p.Kill(); } catch (Exception) { }
                        msg = "Python 检测超时，请确认 Python 可用。";
                        return false;
                    }
                    if (p.ExitCode != 0)
                    {
                        string detail = e.Trim().Length > 0 ? e.Trim() : o.Trim();
                        msg = "Python 无法加载 faster-whisper：\n" + detail +
                            "\n\n修复命令：python -m pip install faster-whisper";
                        return false;
                    }
                    msg = "Python " + o.Trim();
                    return true;
                }
            }
            catch (Exception ex)
            {
                msg = "找不到可用的 Python：\n" + _python + "\n" + ex.Message +
                    "\n\n请安装 Python 3.9+ 并执行：python -m pip install faster-whisper";
                return false;
            }
        }

        static string DetectPython(Config c)
        {
            if (!string.IsNullOrEmpty(c.AsrPython) && File.Exists(c.AsrPython)) return c.AsrPython;
            string env = Environment.GetEnvironmentVariable("ASR_PYTHON");
            if (!string.IsNullOrEmpty(env) && File.Exists(env)) return env;
            string local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"Programs\Python\Python313\python.exe");
            if (File.Exists(local)) return local;
            string local2 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"Programs\Python\Python312\python.exe");
            if (File.Exists(local2)) return local2;
            return "python.exe";
        }

        void Work()
        {
            int ok = 0, fail = 0;
            bool stopped = false;
            Post(delegate { SetStatus("正在检查环境…", Ui.Warn); });

            for (int i = 0; i < _files.Count; i++)
            {
                if (_cancel) { stopped = true; break; }
                string file = _files[i];
                string name = Path.GetFileName(file);
                int idx = i;
                Post(delegate { SetRow(idx, "识别中…", ""); SetStatus(name + " 识别中…", Ui.Muted); });

                try
                {
                    List<string> segs = RunAsr(file, idx);
                    if (_cancel) { stopped = true; Post(delegate { SetRow(idx, "已停止", ""); }); break; }

                    string raw = string.Join("\n", segs.ToArray());
                    string polished = raw;
                    string note = "";
                    if (!Ollama.Online())
                    {
                        note = "Ollama 未连接，已保存纯识别文本";
                    }
                    else if (!string.IsNullOrEmpty(_polishModel))
                    {
                        try
                        {
                            polished = Polish(segs, delegate (string s) { Post(delegate { SetStatus(name + " · " + s, Ui.Muted); }); });
                        }
                        catch (Exception ex)
                        {
                            note = "润色失败：" + ex.Message;
                            polished = raw;
                        }
                    }

                    string rawCopy = raw, polishedCopy = polished;
                    Post(delegate
                    {
                        _srcBox.Text = rawCopy;
                        _outBox.Text = polishedCopy;
                    });

                    string outDocx, outTxt;
                    SaveResult(file, raw, polished, note.Length == 0, out outDocx, out outTxt);
                    string info = "完成 · " + segs.Count + " 段" + (note.Length > 0 ? " · " + note : "");
                    string finalDocx = outDocx;
                    Post(delegate { SetRow(idx, info, finalDocx); });
                    ok++;
                }
                catch (OperationCanceledException)
                {
                    Post(delegate { SetRow(idx, "已停止", ""); });
                    stopped = true;
                    break;
                }
                catch (Exception ex)
                {
                    string msg = ex.Message;
                    if (msg.Length > 100) msg = msg.Substring(0, 100) + "…";
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
                if (stopped) SetStatus("已停止：完成 " + ok + " 个，失败 " + fail + " 个", Ui.Warn);
                else SetStatus("完成：成功 " + ok + " 个，失败 " + fail + " 个", fail > 0 ? Ui.Warn : Ui.Ok);
            });
        }

        List<string> RunAsr(string file, int idx)
        {
            List<string> texts = new List<string>();
            StringBuilder errSb = new StringBuilder();
            Process p = new Process();
            ProcessStartInfo psi = new ProcessStartInfo();
            if (!string.IsNullOrEmpty(_engine))
            {
                psi.FileName = _engine;
                psi.Arguments = Quote(_modelDir) + " " + Quote(file) + " " + Quote(LangValue());
            }
            else
            {
                psi.FileName = _python;
                psi.Arguments = Quote(_script) + " " + Quote(_modelDir) + " " + Quote(file) + " " + Quote(LangValue());
                psi.WorkingDirectory = Path.GetDirectoryName(_script);
            }
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.StandardOutputEncoding = Encoding.UTF8;
            psi.StandardErrorEncoding = Encoding.UTF8;
            psi.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
            p.StartInfo = psi;
            p.ErrorDataReceived += delegate (object s, DataReceivedEventArgs e)
            {
                if (e.Data != null) lock (errSb) errSb.AppendLine(e.Data);
            };
            lock (_procLock) _proc = p;

            try
            {
                p.Start();
                p.BeginErrorReadLine();
                double dur = 0;
                string line;
                while ((line = p.StandardOutput.ReadLine()) != null)
                {
                    if (_cancel) { try { p.Kill(); } catch (Exception) { } break; }
                    if (line.Trim().Length == 0) continue;
                    Dictionary<string, object> d = null;
                    try { d = Ser.DeserializeObject(line) as Dictionary<string, object>; }
                    catch (Exception) { }
                    if (d == null) continue;
                    string type = S(d, "type");
                    if (type == "start")
                    {
                        dur = D(d, "dur");
                    }
                    else if (type == "seg")
                    {
                        string txt = S(d, "text");
                        double end = D(d, "end");
                        if (txt.Length > 0)
                        {
                            texts.Add(txt);
                            string txtCopy = txt;
                            double durCopy = dur;
                            double endCopy = end;
                            int idxCopy = idx;
                            Post(delegate
                            {
                                _srcBox.AppendText(txtCopy + "\r\n");
                                SetRowStatus(idxCopy, "识别中 " + Clock(endCopy) + (durCopy > 0 ? " / " + Clock(durCopy) : "") + "…");
                            });
                        }
                    }
                    else if (type == "error")
                    {
                        throw new Exception(S(d, "message"));
                    }
                }
                p.WaitForExit();
                if (_cancel) throw new OperationCanceledException();
                if (p.ExitCode != 0)
                {
                    string e2;
                    lock (errSb) e2 = errSb.ToString().Trim();
                    if (e2.Length > 200) e2 = e2.Substring(e2.Length - 200);
                    throw new Exception("Whisper 退出码 " + p.ExitCode + (e2.Length > 0 ? "：" + e2 : ""));
                }
                return texts;
            }
            finally
            {
                lock (_procLock) _proc = null;
                try { if (!p.HasExited) p.Kill(); } catch (Exception) { }
                p.Dispose();
            }
        }

        string Polish(List<string> segs, Action<string> progress)
        {
            List<string> chunks = new List<string>();
            StringBuilder cur = new StringBuilder();
            foreach (string s in segs)
            {
                if (cur.Length > 0 && cur.Length + s.Length > 1800)
                {
                    chunks.Add(cur.ToString());
                    cur.Length = 0;
                }
                if (cur.Length > 0) cur.Append('\n');
                cur.Append(s);
            }
            if (cur.Length > 0) chunks.Add(cur.ToString());

            StringBuilder all = new StringBuilder();
            for (int i = 0; i < chunks.Count; i++)
            {
                progress("润色第 " + (i + 1) + "/" + chunks.Count + " 段…");
                string part = Ollama.Chat(_polishModel, PolishPrompt, chunks[i], 180000);
                if (all.Length > 0) all.Append('\n');
                all.Append(part.Trim());
            }
            return all.ToString();
        }

        void SaveResult(string src, string raw, string polished, bool withPolish, out string docxPath, out string txtPath)
        {
            string baseName = Path.GetFileNameWithoutExtension(src);
            docxPath = Unique(Path.Combine(_outDir, baseName + "_转写.docx"));
            txtPath = Unique(Path.Combine(_outDir, baseName + "_转写.txt"));

            List<DocLine> lines = new List<DocLine>();
            lines.Add(new DocLine("识别原文（Whisper）", 1));
            foreach (string l in raw.Split('\n'))
                if (l.Trim().Length > 0) lines.Add(new DocLine(l.Trim(), 0));
            if (withPolish && polished.Trim().Length > 0 && polished.Trim() != raw.Trim())
            {
                lines.Add(new DocLine("润色后文本（" + _polishModel + "）", 1));
                foreach (string l in polished.Split('\n'))
                    if (l.Trim().Length > 0) lines.Add(new DocLine(l.Trim(), 0));
            }
            DocTranslate.WriteDocx(docxPath, lines);
            File.WriteAllText(txtPath, withPolish ? polished : raw, new UTF8Encoding(false));
        }

        string Unique(string path)
        {
            if (!File.Exists(path)) return path;
            string dir = Path.GetDirectoryName(path);
            string name = Path.GetFileNameWithoutExtension(path);
            string ext = Path.GetExtension(path);
            int n = 2;
            string p = Path.Combine(dir, name + "(" + n + ")" + ext);
            while (File.Exists(p))
            {
                n++;
                p = Path.Combine(dir, name + "(" + n + ")" + ext);
            }
            return p;
        }

        static string Quote(string s)
        {
            return "\"" + s + "\"";
        }

        static string S(Dictionary<string, object> d, string key)
        {
            object v;
            if (d != null && d.TryGetValue(key, out v) && v != null) return Convert.ToString(v, CultureInfo.InvariantCulture);
            return "";
        }

        static double D(Dictionary<string, object> d, string key)
        {
            object v;
            if (d != null && d.TryGetValue(key, out v) && v != null)
            {
                double r;
                if (double.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture), NumberStyles.Any, CultureInfo.InvariantCulture, out r)) return r;
            }
            return 0;
        }

        static string Clock(double sec)
        {
            if (sec < 0) sec = 0;
            int s = (int)Math.Round(sec);
            if (s >= 3600) return string.Format("{0}:{1:00}:{2:00}", s / 3600, (s / 60) % 60, s % 60);
            return string.Format("{0:00}:{1:00}", s / 60, s % 60);
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
                DialogResult r = MessageBox.Show(this, "转写还在进行中，现在关闭会中断。确定关闭吗？", "提示",
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);
                if (r != DialogResult.OK) { e.Cancel = true; return; }
                _cancel = true;
                lock (_procLock)
                {
                    if (_proc != null) { try { _proc.Kill(); } catch (Exception) { } }
                }
            }
        }
    }
}
