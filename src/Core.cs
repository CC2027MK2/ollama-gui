using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace OllamaSwitch
{
    public class ModelInfo
    {
        public string Name;
        public long SizeBytes;
        public string ParamSize;
        public string Quant;
        public string Family;
        public string ModifiedAt;
    }

    public class RunningInfo
    {
        public string Name;
        public string ExpiresAt;
        public DateTime ExpiresLocal = DateTime.MinValue;
        public long SizeBytes;
        public long VramBytes;
        public int ContextLength;
        public string ParamSize;
        public string Quant;
    }

    public class GpuInfo
    {
        public bool Valid;
        public string Name;
        public int UsedMiB;
        public int TotalMiB;
        public int Util;
        public int Temp;
        public double Power;
        public double PowerLimit;
        public string Raw;
    }

    public class Config
    {
        public string OllamaExe { get; set; }
        public string KeepAlive { get; set; }
        public string MainModel { get; set; }
        public string AltModel { get; set; }
        public string LightModel { get; set; }
        public string AsrPython { get; set; }
        public string AsrModel { get; set; }
        public string AsrScript { get; set; }
        public bool GameMode { get; set; }
        public string GameProcess { get; set; }

        public Config()
        {
            OllamaExe = "";
            KeepAlive = "-1";
            MainModel = "qwen3.5-9b-local:latest";
            AltModel = "translategemma:4b";
            LightModel = "qwen3.5-0.8b-local:latest";
            AsrPython = "";
            AsrModel = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "whisper-models", "faster-whisper-small");
            AsrScript = "";
            GameMode = true;
            GameProcess = "";
        }
    }

    public class Snapshot
    {
        public bool Online;
        public string Version = "";
        public string Error = "";
        public List<ModelInfo> Models = new List<ModelInfo>();
        public List<RunningInfo> Running = new List<RunningInfo>();
        public List<ModelInfo> Standby = new List<ModelInfo>();
        public GpuInfo Gpu = new GpuInfo();
        public Config Cfg = new Config();
        public string ExePath = "";
    }

    public static class Ollama
    {
        public const string BaseUrl = "http://127.0.0.1:11434";

        static readonly JavaScriptSerializer Ser = new JavaScriptSerializer();

        /* ---------------- 配置 ---------------- */
        public static string ConfigPath()
        {
            string dir = AppDomain.CurrentDomain.BaseDirectory;
            return Path.Combine(dir, "ollama-switcher-config.json");
        }

        public static Config LoadConfig()
        {
            try
            {
                if (File.Exists(ConfigPath()))
                {
                    string txt = File.ReadAllText(ConfigPath(), Encoding.UTF8);
                    Config c = Ser.Deserialize<Config>(txt);
                    if (c != null)
                    {
                        if (c.KeepAlive == null) c.KeepAlive = "-1";
                        if (c.OllamaExe == null) c.OllamaExe = "";
                        if (c.MainModel == null || c.MainModel == "") c.MainModel = "qwen3.5-9b-local:latest";
                        if (c.AltModel == null || c.AltModel == "") c.AltModel = "translategemma:4b";
                        if (c.LightModel == null || c.LightModel == "") c.LightModel = "qwen3.5-0.8b-local:latest";
                        if (c.AsrPython == null) c.AsrPython = "";
                        if (c.AsrModel == null || c.AsrModel == "") c.AsrModel = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "whisper-models", "faster-whisper-small");
                        if (c.AsrScript == null) c.AsrScript = "";
                        if (c.GameProcess == null) c.GameProcess = "";
                        return c;
                    }
                }
            }
            catch (Exception) { }
            return new Config();
        }

        public static bool SaveConfig(Config c)
        {
            try
            {
                File.WriteAllText(ConfigPath(), Ser.Serialize(c), new UTF8Encoding(false));
                return true;
            }
            catch (Exception) { return false; }
        }

        public static string DetectExe()
        {
            Config c = LoadConfig();
            string[] candidates = new string[]
            {
                c.OllamaExe,
                Environment.GetEnvironmentVariable("OLLAMA_EXE"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "ollama-bin", "ollama.exe"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ollama.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Ollama\ollama.exe")
            };
            foreach (string p in candidates)
            {
                try
                {
                    if (!string.IsNullOrEmpty(p) && File.Exists(p)) return p;
                }
                catch (Exception) { }
            }
            return "ollama.exe";
        }

        /* ---------------- HTTP ---------------- */
        static string Request(string path, string method, string body, int timeoutMs)
        {
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(BaseUrl + path);
            req.Method = method;
            req.Timeout = timeoutMs;
            req.ReadWriteTimeout = timeoutMs;
            req.Proxy = null;
            req.KeepAlive = false;
            if (body != null)
            {
                byte[] data = Encoding.UTF8.GetBytes(body);
                req.ContentType = "application/json; charset=utf-8";
                req.ContentLength = data.Length;
                using (Stream s = req.GetRequestStream()) s.Write(data, 0, data.Length);
            }
            using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
            using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
            {
                return sr.ReadToEnd();
            }
        }

        public static bool Online()
        {
            try { Request("/api/version", "GET", null, 2500); return true; }
            catch (Exception) { return false; }
        }

        public static string Chat(string model, string system, string user, int timeoutMs)
        {
            string content = ChatOnce(model, system, user, timeoutMs, true);
            if (content == null || content.Trim().Length == 0) content = ChatOnce(model, system, user, timeoutMs, false);
            if (content == null || content.Trim().Length == 0) throw new Exception("模型返回为空");
            return content;
        }

        // think=false：思考型模型（qwen3 系）默认会把算力花在 thinking 上，正文经常为空，翻译必须关掉
        static string ChatOnce(string model, string system, string user, int timeoutMs, bool noThink)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("{\"model\":\"").Append(Esc(model)).Append("\",");
            sb.Append("\"stream\":false,\"keep_alive\":\"30m\",");
            if (noThink) sb.Append("\"think\":false,");
            sb.Append("\"options\":{\"temperature\":0.1,\"num_ctx\":8192,\"num_predict\":4096},");
            sb.Append("\"messages\":[");
            if (!string.IsNullOrEmpty(system))
                sb.Append("{\"role\":\"system\",\"content\":\"").Append(Esc(system)).Append("\"},");
            sb.Append("{\"role\":\"user\",\"content\":\"").Append(Esc(user)).Append("\"}]}");
            string json;
            try { json = Request("/api/chat", "POST", sb.ToString(), timeoutMs); }
            catch (Exception) { if (noThink) return null; throw; }
            Dictionary<string, object> root = Obj(Ser.DeserializeObject(json));
            Dictionary<string, object> msg = Obj(root != null && root.ContainsKey("message") ? root["message"] : null);
            string content = Str(msg, "content");
            if (content == null) content = "";
            if (content.Trim().Length == 0)
            {
                // 某些模型只有 thinking 有内容，至少别丢
                string think = Str(msg, "thinking");
                if (!string.IsNullOrEmpty(think) && think.Trim().Length > 0) content = think;
            }
            return content;
        }

        static string Esc(string s)
        {
            if (s == null) return "";
            StringBuilder sb = new StringBuilder();
            foreach (char ch in s)
            {
                if (ch == '"' || ch == '\\') sb.Append('\\').Append(ch);
                else if (ch == '\n') sb.Append("\\n");
                else if (ch == '\r') sb.Append("\\r");
                else if (ch < ' ') { }
                else sb.Append(ch);
            }
            return sb.ToString();
        }

        static string KaJson(string ka)
        {
            if (string.IsNullOrEmpty(ka) || ka == "-1") return "-1";
            return "\"" + Esc(ka) + "\"";
        }

        static Dictionary<string, object> Obj(object o) { return o as Dictionary<string, object>; }

        static object[] Arr(object o) { return o as object[]; }

        static string Str(Dictionary<string, object> d, string key)
        {
            object v;
            if (d != null && d.TryGetValue(key, out v) && v != null) return Convert.ToString(v, CultureInfo.InvariantCulture);
            return "";
        }

        static long Lng(Dictionary<string, object> d, string key)
        {
            object v;
            if (d != null && d.TryGetValue(key, out v) && v != null)
            {
                try { return Convert.ToInt64(v, CultureInfo.InvariantCulture); }
                catch (Exception) { return 0; }
            }
            return 0;
        }

        /* ---------------- 数据 ---------------- */
        public static Snapshot GetSnapshot()
        {
            Snapshot st = new Snapshot();
            st.Cfg = LoadConfig();
            st.ExePath = DetectExe();

            try
            {
                string verJson = Request("/api/version", "GET", null, 3000);
                Dictionary<string, object> v = Obj(Ser.DeserializeObject(verJson));
                st.Version = Str(v, "version");
            }
            catch (Exception ex)
            {
                st.Online = false;
                st.Error = ex.Message;
                st.Gpu = GpuQuery();
                return st;
            }

            st.Online = true;

            try
            {
                string tagsJson = Request("/api/tags", "GET", null, 6000);
                Dictionary<string, object> root = Obj(Ser.DeserializeObject(tagsJson));
                object[] list = Arr(root != null && root.ContainsKey("models") ? root["models"] : null);
                if (list != null)
                {
                    foreach (object item in list)
                    {
                        Dictionary<string, object> m = Obj(item);
                        if (m == null) continue;
                        ModelInfo mi = new ModelInfo();
                        mi.Name = Str(m, "name");
                        mi.SizeBytes = Lng(m, "size");
                        mi.ModifiedAt = Str(m, "modified_at");
                        Dictionary<string, object> det = Obj(m.ContainsKey("details") ? m["details"] : null);
                        mi.ParamSize = Str(det, "parameter_size");
                        mi.Quant = Str(det, "quantization_level");
                        mi.Family = Str(det, "family");
                        st.Models.Add(mi);
                    }
                }
            }
            catch (Exception ex)
            {
                st.Error = ex.Message;
            }

            try
            {
                string psJson = Request("/api/ps", "GET", null, 6000);
                Dictionary<string, object> root = Obj(Ser.DeserializeObject(psJson));
                object[] list = Arr(root != null && root.ContainsKey("models") ? root["models"] : null);
                if (list != null)
                {
                    foreach (object item in list)
                    {
                        Dictionary<string, object> m = Obj(item);
                        if (m == null) continue;
                        RunningInfo ri = new RunningInfo();
                        ri.Name = Str(m, "name");
                        ri.ExpiresAt = Str(m, "expires_at");
                        ri.SizeBytes = Lng(m, "size");
                        ri.VramBytes = Lng(m, "size_vram");
                        ri.ContextLength = (int)Lng(m, "context_length");
                        Dictionary<string, object> det = Obj(m.ContainsKey("details") ? m["details"] : null);
                        ri.ParamSize = Str(det, "parameter_size");
                        ri.Quant = Str(det, "quantization_level");
                        ri.ExpiresLocal = ParseTime(ri.ExpiresAt);
                        st.Running.Add(ri);
                    }
                }
            }
            catch (Exception ex)
            {
                st.Error = ex.Message;
            }

            HashSet<string> runningNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (RunningInfo r in st.Running) runningNames.Add(r.Name);
            foreach (ModelInfo m in st.Models)
                if (!runningNames.Contains(m.Name)) st.Standby.Add(m);

            st.Gpu = GpuQuery();
            return st;
        }

        public static DateTime ParseTime(string iso)
        {
            if (string.IsNullOrEmpty(iso)) return DateTime.MinValue;
            try
            {
                return DateTime.Parse(iso, CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal).ToLocalTime();
            }
            catch (Exception) { return DateTime.MinValue; }
        }

        public static GpuInfo GpuQuery()
        {
            GpuInfo g = new GpuInfo();
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("nvidia-smi",
                    "--query-gpu=name,memory.used,memory.total,utilization.gpu,temperature.gpu,power.draw,power.limit --format=csv,noheader,nounits");
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.CreateNoWindow = true;
                using (Process p = Process.Start(psi))
                {
                    string outp = p.StandardOutput.ReadToEnd();
                    if (!p.WaitForExit(6000))
                    {
                        try { p.Kill(); } catch (Exception) { }
                    }
                    g.Raw = outp;
                    string line = outp.Replace("\r", "").Split('\n')[0];
                    if (string.IsNullOrEmpty(line)) return g;
                    string[] parts = line.Split(',');
                    if (parts.Length < 5) return g;
                    g.Name = parts[0].Trim();
                    g.UsedMiB = ToInt(parts[1]);
                    g.TotalMiB = ToInt(parts[2]);
                    g.Util = ToInt(parts[3]);
                    g.Temp = (int)Math.Round(ToDouble(parts[4]));
                    if (parts.Length > 5) g.Power = ToDouble(parts[5]);
                    if (parts.Length > 6) g.PowerLimit = ToDouble(parts[6]);
                    g.Valid = g.TotalMiB > 0;
                }
            }
            catch (Exception) { g.Valid = false; }
            return g;
        }

        static int ToInt(string s)
        {
            int v;
            if (int.TryParse(s.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out v)) return v;
            return 0;
        }

        static double ToDouble(string s)
        {
            double v;
            if (double.TryParse(s.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out v)) return v;
            return 0;
        }

        /* ---------------- 动作 ---------------- */
        public static void LoadModel(string model, string keepAlive)
        {
            string body = "{\"model\":\"" + Esc(model) + "\",\"keep_alive\":" + KaJson(keepAlive) + ",\"stream\":false}";
            Request("/api/generate", "POST", body, 300000);
        }

        public static void UnloadModel(string model)
        {
            string body = "{\"model\":\"" + Esc(model) + "\",\"keep_alive\":0,\"stream\":false}";
            Request("/api/generate", "POST", body, 30000);
        }

        // 查某个模型当前是否驻留，以及有多少层在显存里（判断是否被挤到内存）
        public static RunningInfo RunningOf(string model)
        {
            try
            {
                string psJson = Request("/api/ps", "GET", null, 6000);
                Dictionary<string, object> root = Obj(Ser.DeserializeObject(psJson));
                object[] list = Arr(root != null && root.ContainsKey("models") ? root["models"] : null);
                if (list != null)
                {
                    foreach (object item in list)
                    {
                        Dictionary<string, object> m = Obj(item);
                        if (m == null) continue;
                        if (!string.Equals(Str(m, "name"), model, StringComparison.OrdinalIgnoreCase)) continue;
                        RunningInfo r = new RunningInfo();
                        r.Name = Str(m, "name");
                        r.SizeBytes = Lng(m, "size");
                        r.VramBytes = Lng(m, "size_vram");
                        return r;
                    }
                }
            }
            catch (Exception) { }
            return null;
        }

        public static bool ModelExists(string model)
        {
            if (string.IsNullOrEmpty(model)) return false;
            try
            {
                string tagsJson = Request("/api/tags", "GET", null, 6000);
                Dictionary<string, object> root = Obj(Ser.DeserializeObject(tagsJson));
                object[] list = Arr(root != null && root.ContainsKey("models") ? root["models"] : null);
                if (list != null)
                {
                    foreach (object item in list)
                    {
                        Dictionary<string, object> m = Obj(item);
                        if (m != null && string.Equals(Str(m, "name"), model, StringComparison.OrdinalIgnoreCase)) return true;
                    }
                }
            }
            catch (Exception) { }
            return false;
        }

        public static void SetKeepAlive(string model, string keepAlive)
        {
            string body = "{\"model\":\"" + Esc(model) + "\",\"keep_alive\":" + KaJson(keepAlive) + ",\"stream\":false}";
            Request("/api/generate", "POST", body, 120000);
        }

        public static int SwitchModel(string target, string keepAlive)
        {
            string psJson = Request("/api/ps", "GET", null, 6000);
            Dictionary<string, object> root = Obj(Ser.DeserializeObject(psJson));
            object[] list = Arr(root != null && root.ContainsKey("models") ? root["models"] : null);
            int unloaded = 0;
            if (list != null)
            {
                foreach (object item in list)
                {
                    Dictionary<string, object> m = Obj(item);
                    if (m == null) continue;
                    string name = Str(m, "name");
                    if (string.Equals(name, target, StringComparison.OrdinalIgnoreCase)) continue;
                    try { UnloadModel(name); unloaded++; }
                    catch (Exception) { }
                }
            }
            LoadModel(target, keepAlive);
            return unloaded;
        }

        /* ---------------- 游戏模式 ---------------- */
        public static bool GameProcessRunning(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            Process[] procs;
            try { procs = Process.GetProcessesByName(name); }
            catch (Exception) { return false; }
            bool found = procs.Length > 0;
            foreach (Process p in procs) p.Dispose();
            return found;
        }

        public static int UnloadAll()
        {
            string psJson = Request("/api/ps", "GET", null, 6000);
            Dictionary<string, object> root = Obj(Ser.DeserializeObject(psJson));
            object[] list = Arr(root != null && root.ContainsKey("models") ? root["models"] : null);
            int n = 0;
            if (list != null)
            {
                foreach (object item in list)
                {
                    Dictionary<string, object> m = Obj(item);
                    if (m == null) continue;
                    try { UnloadModel(Str(m, "name")); n++; }
                    catch (Exception) { }
                }
            }
            return n;
        }

        public static void StartService()
        {
            string exe = DetectExe();
            ProcessStartInfo psi = new ProcessStartInfo(exe, "serve");
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.EnvironmentVariables["OLLAMA_HOST"] = "127.0.0.1:11434";
            if (string.IsNullOrEmpty(psi.EnvironmentVariables["OLLAMA_MODELS"]))
                psi.EnvironmentVariables["OLLAMA_MODELS"] = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "ollama-models");
            Process.Start(psi);
        }
    }
}
