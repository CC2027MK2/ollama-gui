using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace OllamaSwitch
{
    static class UiShot
    {
        [DllImport("user32.dll")]
        static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);

        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            string which = args.Length > 0 ? args[0] : "main";
            string outPath = args.Length > 1 ? args[1] : @".\ui_main.png";

            Form f;
            if (which == "translate") f = new TranslateForm("qwen3.5-9b-local:latest", "qwen3.5-0.8b-local:latest");
            else if (which == "speech") f = new SpeechForm("qwen3.5-0.8b-local:latest");
            else f = new MainForm();

            if (args.Length > 3)
            {
                int w, h;
                if (int.TryParse(args[2], out w) && int.TryParse(args[3], out h))
                    f.ClientSize = new Size(w, h);
            }
            f.StartPosition = FormStartPosition.Manual;
            f.Location = new Point(0, 0);
            f.Show();
            Timer t = new Timer();
            t.Interval = 3000;
            t.Tick += delegate
            {
                t.Stop();
                Application.DoEvents();
                using (Bitmap b = new Bitmap(f.Width, f.Height))
                {
                    using (Graphics g = Graphics.FromImage(b))
                    {
                        IntPtr dc = g.GetHdc();
                        PrintWindow(f.Handle, dc, 2);
                        g.ReleaseHdc(dc);
                    }
                    b.Save(outPath, ImageFormat.Png);
                }
                f.Close();
            };
            t.Start();
            Application.Run(f);
        }
    }
}
