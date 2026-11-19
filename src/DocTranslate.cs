using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Windows.Data.Pdf;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;

namespace OllamaSwitch
{
    public class DocLine
    {
        public string Text;
        public int Kind; // 0 正文, 1 标题, 2 原文对照
        public DocLine(string text, int kind) { Text = text; Kind = kind; }
    }

    public static class DocTranslate
    {
        public const string NsW = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        public const string Seg = "<|SEG|>";

        // 用户点「停止」时置 true，长循环里检查
        public static volatile bool Cancel;

        static void CheckCancel()
        {
            if (Cancel) throw new OperationCanceledException("已停止");
        }

        public const string SysPrompt =
            "你是严谨的文档翻译器。原文可能是俄语、乌兹别克语（西里尔字母）或英语，" +
            "译文一律用简体中文；只有原文本身是简体中文时才译成英文。" +
            "要求：只输出译文本身，不要解释、不要加引号、不要遗漏内容；" +
            "必须原样保留 " + Seg + " 标记，不得增加、删除或改变它们的顺序。";

        /* ---------------- 翻译调用 ---------------- */
        public static string TranslateBlock(string model, string text, int timeoutMs)
        {
            string res = Ollama.Chat(model, SysPrompt, text, timeoutMs);
            res = res.Trim();
            return res;
        }

        public static List<string> TranslateParagraphs(string model, List<string> paras, Action<string> status)
        {
            return TranslateParagraphs(model, paras, status, null);
        }

        // onProgress：每翻完一批回调一次当前已译出的全部段落，调用方可以随时把进度落盘，
        // 这样即使中途停止或出错，已经翻好的部分也不会丢
        public static List<string> TranslateParagraphs(string model, List<string> paras, Action<string> status, Action<List<string>> onProgress)
        {
            List<string> outp = new List<string>();

            // 先按“每批最多 6 段 / 1100 字”切好，进度才能算准
            List<List<string>> chunks = new List<List<string>>();
            int j = 0;
            while (j < paras.Count)
            {
                List<string> cur = new List<string>();
                int len = 0;
                while (j < paras.Count && cur.Count < 6 && (len < 1100 || cur.Count == 0))
                {
                    cur.Add(paras[j]);
                    len += paras[j].Length;
                    j++;
                }
                chunks.Add(cur);
            }
            if (status != null) status("共 " + paras.Count + " 段，分 " + chunks.Count + " 批翻译");

            for (int ci = 0; ci < chunks.Count; ci++)
            {
                CheckCancel();
                List<string> cur = chunks[ci];
                if (status != null) status("翻译第 " + (ci + 1) + "/" + chunks.Count + " 批（共 " + paras.Count + " 段）");

                string res = null;
                try { res = TranslateBlock(model, string.Join("\n" + Seg + "\n", cur.ToArray()), 300000); }
                catch (Exception) { res = null; }

                string[] parts = null;
                if (res != null)
                {
                    res = Regex.Replace(res, @"\s*<\|SEG\|>\s*", Seg);
                    parts = res.Split(new string[] { Seg }, StringSplitOptions.None);
                }

                if (parts != null && parts.Length == cur.Count)
                {
                    for (int k = 0; k < cur.Count; k++) outp.Add(CleanOut(parts[k]));
                }
                else
                {
                    // 分隔符没被正确保留：逐段翻译
                    for (int k = 0; k < cur.Count; k++)
                    {
                        CheckCancel();
                        if (status != null) status("逐段翻译 第 " + (outp.Count + 1) + "/" + paras.Count + " 段");
                        string one = null;
                        try { one = TranslateBlock(model, cur[k], 300000); }
                        catch (Exception) { one = cur[k]; }
                        outp.Add(CleanOut(one));
                        // 逐段回退时一批可能要很久，每段结束就落盘一次，中断也不丢
                        if (onProgress != null) onProgress(new List<string>(outp));
                    }
                }
                if (onProgress != null) onProgress(new List<string>(outp));
            }
            return outp;
        }

        static string CleanOut(string s)
        {
            if (s == null) return "";
            s = s.Trim();
            s = Regex.Replace(s, @"^\s*```[a-zA-Z]*\s*", "");
            s = Regex.Replace(s, @"\s*```\s*$", "");
            // 逐段回退时模型偶尔会把分隔标记也带出来，清掉
            s = s.Replace(Seg, "").Trim();
            return s.Trim();
        }

        /* ---------------- Word ---------------- */
        public static string TranslateDocx(string model, string src, string outPath, Action<string> status)
        {
            File.Copy(src, outPath, true);

            XmlDocument doc;
            using (FileStream fs = new FileStream(outPath, FileMode.Open, FileAccess.Read))
            using (ZipArchive za = new ZipArchive(fs, ZipArchiveMode.Read))
            {
                ZipArchiveEntry e = za.GetEntry("word/document.xml");
                if (e == null) throw new Exception("不是有效的 .docx 文件（缺少 word/document.xml）");
                using (Stream s = e.Open())
                {
                    doc = new XmlDocument();
                    doc.PreserveWhitespace = true;
                    doc.Load(s);
                }
            }

            XmlNamespaceManager ns = new XmlNamespaceManager(doc.NameTable);
            ns.AddNamespace("w", NsW);

            List<List<XmlElement>> tsets = new List<List<XmlElement>>();
            List<string> texts = new List<string>();
            foreach (XmlNode n in doc.SelectNodes("//w:p", ns))
            {
                List<XmlElement> ts = new List<XmlElement>();
                foreach (XmlNode t in n.SelectNodes(".//w:t", ns)) ts.Add((XmlElement)t);
                if (ts.Count == 0) continue;
                StringBuilder sb = new StringBuilder();
                foreach (XmlElement t in ts) sb.Append(t.InnerText);
                string text = sb.ToString();
                if (text.Trim().Length == 0) continue;
                tsets.Add(ts);
                texts.Add(text);
            }
            if (texts.Count == 0) throw new Exception("文档里没有可翻译的文字");

            // 检查点：每翻完一批就把译文写回输出 docx（没翻到的段落先保留原文），
            // 中途停止或出错时已翻部分不会丢
            List<string> lastPartial = new List<string>();
            Action<List<string>> checkpoint = delegate (List<string> partial)
            {
                try
                {
                    lastPartial = partial;
                    for (int i = 0; i < tsets.Count; i++)
                        Distribute(tsets[i], i < partial.Count ? partial[i] : texts[i]);
                    SaveDocXml(outPath, doc);
                }
                catch (Exception) { }
            };

            List<string> trans = null;
            try
            {
                trans = TranslateParagraphs(model, texts, status, checkpoint);
            }
            catch (OperationCanceledException)
            {
                // 已翻部分已由检查点落盘，这里再补写一次最后的进度
                checkpoint(lastPartial);
                throw;
            }

            for (int i = 0; i < tsets.Count; i++)
                Distribute(tsets[i], trans[i]);

            SaveDocXml(outPath, doc);

            return "已翻译 " + texts.Count + " 个段落（含表格）";
        }

        static void SaveDocXml(string outPath, XmlDocument doc)
        {
            byte[] xmlBytes;
            using (MemoryStream ms = new MemoryStream())
            {
                XmlWriterSettings st = new XmlWriterSettings();
                st.Encoding = new UTF8Encoding(false);
                st.OmitXmlDeclaration = false;
                st.Indent = false;
                using (XmlWriter w = XmlWriter.Create(ms, st)) doc.Save(w);
                xmlBytes = ms.ToArray();
            }

            using (FileStream fs = new FileStream(outPath, FileMode.Open, FileAccess.ReadWrite))
            using (ZipArchive za = new ZipArchive(fs, ZipArchiveMode.Update))
            {
                ZipArchiveEntry old = za.GetEntry("word/document.xml");
                string full = old.FullName;
                old.Delete();
                ZipArchiveEntry ne = za.CreateEntry(full, CompressionLevel.Optimal);
                using (Stream s = ne.Open()) s.Write(xmlBytes, 0, xmlBytes.Length);
            }
        }

        static void Distribute(List<XmlElement> ts, string text)
        {
            if (text == null) return;
            text = text.Trim();
            int[] lens = new int[ts.Count];
            int total = 0;
            for (int i = 0; i < ts.Count; i++) { lens[i] = ts[i].InnerText.Length; total += lens[i]; }
            if (total <= 0) total = ts.Count;
            int pos = 0;
            for (int i = 0; i < ts.Count; i++)
            {
                int take;
                if (i == ts.Count - 1) take = text.Length - pos;
                else take = (int)Math.Round((double)text.Length * (total == ts.Count ? 1 : lens[i]) / (total == ts.Count ? ts.Count : total));
                if (take < 0) take = 0;
                if (pos + take > text.Length) take = text.Length - pos;
                string part = text.Substring(pos, take);
                pos += take;
                XmlElement t = ts[i];
                t.InnerText = part;
                t.SetAttribute("space", "http://www.w3.org/XML/1998/namespace", "preserve");
            }
        }

        /* ---------------- PDF ---------------- */
        public static string TranslatePdf(string model, string src, string outPath, Action<string> status)
        {
            List<string> pageTexts = PdfPages(src, status);
            List<DocLine> lines = new List<DocLine>();
            int empty = 0;
            int donePages = 0;

            // 每翻完一页就把目前的结果写进输出 docx，中途停止也能保留已翻的页
            Action flush = delegate
            {
                try
                {
                    List<DocLine> snapshot = new List<DocLine>(lines);
                    snapshot.Add(new DocLine("（未完成：已翻 " + donePages + " 页，共 " + pageTexts.Count + " 页）", 2));
                    WriteDocx(outPath, snapshot);
                }
                catch (Exception) { }
            };

            try
            {
                for (int i = 0; i < pageTexts.Count; i++)
                {
                    CheckCancel();
                    string pt = pageTexts[i];
                    if (pt.Trim().Length == 0) { empty++; donePages++; continue; }
                    lines.Add(new DocLine("第 " + (i + 1) + " 页", 1));
                    List<string> paras = new List<string>(Regex.Split(pt, "\n"));
                    List<string> keep = new List<string>();
                    foreach (string p in paras) if (p.Trim().Length > 0) keep.Add(p.Trim());
                    List<string> tr = TranslateParagraphs(model, keep, status);
                    for (int k = 0; k < keep.Count; k++)
                    {
                        lines.Add(new DocLine(tr[k], 0));
                        lines.Add(new DocLine("原文：" + keep[k], 2));
                    }
                    donePages++;
                    flush();
                }
            }
            catch (OperationCanceledException)
            {
                flush();
                throw;
            }

            if (lines.Count == 0) throw new Exception("PDF 没有识别出文字（可能是加密或纯图片型 PDF）");
            WriteDocx(outPath, lines);
            return "共 " + pageTexts.Count + " 页" + (empty > 0 ? "（其中 " + empty + " 页无文字）" : "");
        }

        public static List<string> PdfPages(string path, Action<string> status)
        {
            string tmp = Path.Combine(Path.GetTempPath(), "ollama-tr-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);
            List<string> pages = new List<string>();
            try
            {
                StorageFile file = Wait(StorageFile.GetFileFromPathAsync(path));
                PdfDocument doc = Wait(PdfDocument.LoadFromFileAsync(file));
                StorageFolder folder = Wait(StorageFolder.GetFolderFromPathAsync(tmp));
                int total = (int)doc.PageCount;
                int mode = OcrLang;

                // 每批渲染 workers 页再并行识别：西里尔文走内置 Tesseract，一页约 3 秒，
                // 多进程并行后整本 PDF 的耗时才降得下来
                int workers = Math.Max(2, Math.Min(8, Environment.ProcessorCount / 2));
                for (int start = 0; start < total; start += workers)
                {
                    CheckCancel();
                    int end = Math.Min(total, start + workers);
                    string[] batch = new string[end - start];
                    for (int i = start; i < end; i++)
                    {
                        CheckCancel();
                        if (status != null) status("渲染 PDF 第 " + (i + 1) + "/" + total + " 页");
                        string name = "p" + i + ".png";
                        using (PdfPage page = doc.GetPage((uint)i))
                        {
                            PdfPageRenderOptions opt = new PdfPageRenderOptions();
                            opt.DestinationWidth = 1500;
                            StorageFile outFile = Wait(folder.CreateFileAsync(name, CreationCollisionOption.ReplaceExisting));
                            using (var os = Wait(outFile.OpenAsync(FileAccessMode.ReadWrite)))
                            {
                                Wait(page.RenderToStreamAsync(os, opt));
                            }
                        }
                        batch[i - start] = Path.Combine(tmp, name);
                    }

                    // 自动模式只对第一页判一次文字体系，整本沿用
                    if (mode == 0) mode = DetectMode(batch[0], status);

                    string[] res = new string[batch.Length];
                    int useMode = mode;
                    Parallel.For(0, batch.Length, delegate(int k)
                    {
                        try { res[k] = OcrPageText(batch[k], useMode, null); }
                        catch (Exception) { res[k] = ""; }
                    });
                    for (int k = 0; k < res.Length; k++) pages.Add(res[k]);
                    if (status != null) status("识别文字 " + end + "/" + total + " 页");
                }
            }
            finally
            {
                try { Directory.Delete(tmp, true); } catch (Exception) { }
            }
            return pages;
        }

        /* ---------------- 图片 OCR ---------------- */
        public static string OcrImageFile(string path)
        {
            int mode = OcrLang;
            if (mode == 0) mode = DetectMode(path, null);
            string text = OcrPageText(path, mode, null);
            if (text == null || text.Trim().Length == 0) throw new Exception("图片里没有识别出文字");
            return text;
        }

        /* ---------------- OCR 引擎选择 ----------------
         * 0 自动 / 1 中文 / 2 英文 / 3 俄文（西里尔字母）
         * 系统自带的 OCR 只装了中英文，西里尔文会被认成一堆形似的拉丁字母（乱码），
         * 所以西里尔文改走 exe 内置的 Tesseract */
        public static int OcrLang = 0;

        static string OcrPageText(string path, int mode, Action<string> status)
        {
            if (mode == 3)
            {
                string ru = TessOcr(path, "rus+eng", status);
                if (ru != null && ru.Trim().Length > 0) return CleanOcrLeader(ru);
                // 内置引擎缺失或没认出字：退回系统引擎，至少不空着
            }
            return SystemOcr(path, mode == 2 ? "en-US" : "zh-Hans-CN", mode == 2 ? "zh-Hans-CN" : "en-US");
        }

        /* 目录行的点线常被 OCR 认成一串形似字符（"ce ecceeec…"、"ииииии"），
         * 这里只处理“以页码结尾、页码前是垃圾碎片”的行，正文不动 */
        static string CleanOcrLeader(string text)
        {
            string[] lines = text.Replace("\r", "").Split('\n');
            for (int i = 0; i < lines.Length; i++) lines[i] = CleanLeaderLine(lines[i]);
            return string.Join("\n", lines);
        }

        static string CleanLeaderLine(string line)
        {
            string s = line.TrimEnd();
            string[] parts = Regex.Split(s, @"\s+");
            if (parts.Length < 3) return line;
            int pi = parts.Length - 1;
            if (!Regex.IsMatch(parts[pi], @"^[0-9]{1,3}$")) return line;
            int cut = pi;
            while (cut > 0 && LeaderJunk(parts[cut - 1])) cut--;
            if (cut == pi) return line;
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < cut; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(parts[i]);
            }
            string body = sb.ToString().Trim();
            if (body.Length < 3) return line;
            return body + " …… " + parts[pi];
        }

        static bool LeaderJunk(string t)
        {
            if (t.Length == 0) return false;
            int dots = 0, letters = 0, latin = 0;
            bool same = true;
            char first = '\0';
            foreach (char c in t)
            {
                if (c == '.' || c == '·' || c == '•' || c == '…' || c == '_' || c == '-') dots++;
                else if (char.IsLetter(c))
                {
                    letters++;
                    if (c < 128) latin++;
                    if (first == '\0') first = c;
                    else if (c != first) same = false;
                }
                else if (c != ' ') same = false;
            }
            if (letters == 0) return dots >= 2;                     // 纯点线
            if (dots > 0 && letters <= 2) return true;              // "..___.." 里夹一两个字
            if (t.Length <= 3) return latin == letters || same;      // "ce" "oc" "ии"
            int run = 1, best = 1;
            for (int i = 1; i < t.Length; i++)
            {
                if (char.IsLetter(t[i]) && t[i] == t[i - 1]) { run++; if (run > best) best = run; }
                else run = 1;
            }
            if (best >= 4) return true;                              // "ииииии" "ccccccc"
            HashSet<char> set = new HashSet<char>();
            foreach (char c in t.ToLowerInvariant()) if (char.IsLetter(c)) set.Add(c);
            return letters >= 4 && set.Count <= 3;                   // "ccscssss" 这类低字符种类碎片
        }

        static int DetectMode(string png, Action<string> status)
        {
            string script = TessScript(png, status);
            if (script == null) return 1; // 没有内置引擎：保持原来只用系统中文引擎的行为
            if (script == "Cyrillic" || script == "Armenian" || script == "Georgian" || script == "Greek") return 3;
            if (script == "Han" || script == "HanS" || script == "HanT") return 1;
            return 2;
        }

        static string SystemOcr(string path, string first, string second)
        {
            StorageFile file = Wait(StorageFile.GetFileFromPathAsync(path));
            using (var stream = Wait(file.OpenAsync(FileAccessMode.Read)))
            {
                BitmapDecoder dec = Wait(BitmapDecoder.CreateAsync(stream));
                SoftwareBitmap bmp = Wait(dec.GetSoftwareBitmapAsync());
                OcrResult res = null;
                OcrEngine eng = OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language(first));
                if (eng != null) res = Wait(eng.RecognizeAsync(bmp));
                if (res == null || res.Lines.Count == 0)
                {
                    OcrEngine eng2 = OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language(second));
                    if (eng2 != null) res = Wait(eng2.RecognizeAsync(bmp));
                }
                if (res == null) throw new Exception("系统 OCR 引擎不可用（需要在系统里安装中文/英文语言包）");
                if (res.Lines.Count == 0) return "";
                return LinesToParagraphs(res);
            }
        }

        /* ---------------- 内置 Tesseract（西里尔文 OCR） ---------------- */
        const string ResOcr = "ocr.zip";
        static string _tessDir;
        static readonly object _tessLock = new object();

        // 首次用到西里尔文识别时，把打包在 exe 里的 Tesseract 解压到临时目录（之后复用）
        public static string TessDir(Action<string> status)
        {
            lock (_tessLock)
            {
                if (_tessDir != null) return _tessDir;
                Assembly asm = Assembly.GetExecutingAssembly();
                using (Stream probe = asm.GetManifestResourceStream(ResOcr))
                {
                    if (probe == null) return null;
                    string root = Path.Combine(Path.GetTempPath(), "OllamaModelSwitcher", "ocr-" + probe.Length);
                    string exe = Path.Combine(root, "tesseract.exe");
                    string stamp = Path.Combine(root, ".ok");
                    if (!File.Exists(stamp) || !File.Exists(exe))
                    {
                        if (status != null) status("首次使用：正在释放内置西里尔文识别引擎…");
                        try { if (Directory.Exists(root)) Directory.Delete(root, true); }
                        catch (Exception) { }
                        Directory.CreateDirectory(root);
                        probe.Position = 0;
                        using (ZipArchive za = new ZipArchive(probe, ZipArchiveMode.Read))
                        {
                            foreach (ZipArchiveEntry e in za.Entries)
                            {
                                string dst = Path.Combine(root, e.FullName.Replace('/', Path.DirectorySeparatorChar));
                                if (e.Name.Length == 0) { Directory.CreateDirectory(dst); continue; }
                                string dir = Path.GetDirectoryName(dst);
                                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                                using (Stream s = e.Open())
                                using (FileStream fs = new FileStream(dst, FileMode.Create, FileAccess.Write, FileShare.None))
                                    s.CopyTo(fs, 1048576);
                            }
                        }
                        File.WriteAllText(stamp, "ok");
                    }
                    _tessDir = root;
                    return root;
                }
            }
        }

        static string TessRun(string dir, string args)
        {
            ProcessStartInfo si = new ProcessStartInfo(Path.Combine(dir, "tesseract.exe"));
            si.Arguments = args;
            si.UseShellExecute = false;
            si.CreateNoWindow = true;
            si.RedirectStandardOutput = true;
            si.RedirectStandardError = true;
            si.StandardOutputEncoding = Encoding.UTF8;
            si.StandardErrorEncoding = Encoding.UTF8;
            using (Process p = Process.Start(si))
            {
                string o = p.StandardOutput.ReadToEnd();
                p.StandardError.ReadToEnd();
                p.WaitForExit();
                return o == null ? "" : o;
            }
        }

        // 返回识别出的文字；没有内置引擎时返回 null
        static string TessOcr(string png, string lang, Action<string> status)
        {
            string dir = TessDir(status);
            if (dir == null) return null;
            string args = "\"" + png + "\" stdout -l " + lang +
                          " --tessdata-dir \"" + Path.Combine(dir, "tessdata") + "\" --psm 3";
            string o = TessRun(dir, args);
            o = o.Replace("\r\n", "\n").Trim();
            return o.Length == 0 ? null : o;
        }

        // 用 Tesseract 的文字体系检测（OSD）判断这一页是哪种文字
        static string TessScript(string png, Action<string> status)
        {
            string dir;
            try { dir = TessDir(status); }
            catch (Exception) { return null; }
            if (dir == null) return null;
            string args = "\"" + png + "\" - --psm 0 -l osd --tessdata-dir \"" + Path.Combine(dir, "tessdata") + "\"";
            string o = "";
            try { o = TessRun(dir, args); }
            catch (Exception) { return null; }
            Match m = Regex.Match(o, @"Script:\s*(\w+)");
            return m.Success ? m.Groups[1].Value : null;
        }

        static string LinesToParagraphs(OcrResult res)
        {
            List<string> ps = new List<string>();
            StringBuilder cur = new StringBuilder();
            double prevBottom = -1;
            double lineH = 14;
            foreach (OcrLine line in res.Lines)
            {
                if (line.Words.Count == 0) continue;
                double top = double.MaxValue;
                double bottom = 0;
                foreach (OcrWord w in line.Words)
                {
                    double t = w.BoundingRect.Y;
                    double b = w.BoundingRect.Y + w.BoundingRect.Height;
                    if (t < top) top = t;
                    if (b > bottom) bottom = b;
                }
                if (bottom > top) lineH = bottom - top;
                string text = Clean(line.Text);
                if (text.Length == 0) continue;
                bool newPara = prevBottom < 0 || (top - prevBottom) > Math.Max(6, lineH * 0.75);
                if (newPara)
                {
                    if (cur.Length > 0) { ps.Add(cur.ToString()); cur.Length = 0; }
                    cur.Append(text);
                }
                else
                {
                    char last = cur.Length > 0 ? cur[cur.Length - 1] : ' ';
                    char first = text[0];
                    if (NeedSpace(last, first)) cur.Append(' ');
                    cur.Append(text);
                }
                prevBottom = bottom;
            }
            if (cur.Length > 0) ps.Add(cur.ToString());
            return string.Join("\n", ps.ToArray());
        }

        static bool NeedSpace(char a, char b)
        {
            if (IsCjk(a) || IsCjk(b)) return false;
            if (a == ' ' || b == ' ') return false;
            return true;
        }

        static bool IsCjk(char c)
        {
            return (c >= 0x4e00 && c <= 0x9fff) || (c >= 0x3000 && c <= 0x303f) || (c >= 0xff00 && c <= 0xffef);
        }

        public static string Clean(string s)
        {
            if (s == null) return "";
            s = Regex.Replace(s, @"(?<=[\u4e00-\u9fff])\s+(?=[\u4e00-\u9fff])", "");
            s = s.Replace('\uFF0C', '\uFF0C');
            return s.Trim();
        }

        /* ---------------- 生成 docx ---------------- */
        const string ContentTypes =
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
            "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
            "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
            "<Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/>" +
            "</Types>";

        const string RootRels =
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/>" +
            "</Relationships>";

        public static void WriteDocx(string path, List<DocLine> lines)
        {
            StringBuilder b = new StringBuilder();
            b.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            b.Append("<w:document xmlns:w=\"").Append(NsW).Append("\"><w:body>");
            foreach (DocLine l in lines) b.Append(ParaXml(l));
            b.Append("<w:sectPr><w:pgSz w:w=\"11906\" w:h=\"16838\"/>");
            b.Append("<w:pgMar w:top=\"1247\" w:right=\"1247\" w:bottom=\"1247\" w:left=\"1247\"/></w:sectPr>");
            b.Append("</w:body></w:document>");

            using (FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (ZipArchive za = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                AddEntry(za, "[Content_Types].xml", ContentTypes);
                AddEntry(za, "_rels/.rels", RootRels);
                AddEntry(za, "word/document.xml", b.ToString());
            }
        }

        static string ParaXml(DocLine l)
        {
            string text = XmlEsc(l.Text == null ? "" : l.Text);
            if (l.Kind == 1)
                return "<w:p><w:pPr><w:spacing w:before=\"280\" w:after=\"140\"/></w:pPr><w:r><w:rPr>" +
                       "<w:rFonts w:ascii=\"Microsoft YaHei\" w:eastAsia=\"Microsoft YaHei\"/><w:b/><w:sz w:val=\"30\"/><w:szCs w:val=\"30\"/>" +
                       "</w:rPr><w:t xml:space=\"preserve\">" + text + "</w:t></w:r></w:p>";
            if (l.Kind == 2)
                return "<w:p><w:pPr><w:spacing w:after=\"160\"/></w:pPr><w:r><w:rPr>" +
                       "<w:rFonts w:ascii=\"Microsoft YaHei\" w:eastAsia=\"Microsoft YaHei\"/><w:i/><w:color w:val=\"808080\"/><w:sz w:val=\"18\"/><w:szCs w:val=\"18\"/>" +
                       "</w:rPr><w:t xml:space=\"preserve\">" + text + "</w:t></w:r></w:p>";
            return "<w:p><w:pPr><w:spacing w:after=\"120\" w:line=\"300\" w:lineRule=\"auto\"/></w:pPr><w:r><w:rPr>" +
                   "<w:rFonts w:ascii=\"Microsoft YaHei\" w:eastAsia=\"Microsoft YaHei\"/><w:sz w:val=\"22\"/><w:szCs w:val=\"22\"/>" +
                   "</w:rPr><w:t xml:space=\"preserve\">" + text + "</w:t></w:r></w:p>";
        }

        static void AddEntry(ZipArchive za, string name, string content)
        {
            ZipArchiveEntry e = za.CreateEntry(name, CompressionLevel.Optimal);
            using (Stream s = e.Open())
            {
                byte[] d = new UTF8Encoding(false).GetBytes(content);
                s.Write(d, 0, d.Length);
            }
        }

        static string XmlEsc(string s)
        {
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
        }

        /* ---------------- WinRT 同步等待 ---------------- */
        static T Wait<T>(IAsyncOperation<T> op)
        {
            while (op.Status == AsyncStatus.Started) Thread.Sleep(20);
            if (op.Status == AsyncStatus.Error) throw op.ErrorCode;
            return op.GetResults();
        }

        static void Wait(IAsyncAction op)
        {
            while (op.Status == AsyncStatus.Started) Thread.Sleep(20);
            if (op.Status == AsyncStatus.Error) throw op.ErrorCode;
        }
    }
}
