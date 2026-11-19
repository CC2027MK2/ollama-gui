using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Windows.Data.Pdf;
using Windows.Storage;

static class Probe
{
    static T Wait<T>(Windows.Foundation.IAsyncOperation<T> op)
    {
        while (op.Status == Windows.Foundation.AsyncStatus.Started) System.Threading.Thread.Sleep(20);
        if (op.Status == Windows.Foundation.AsyncStatus.Error) throw op.ErrorCode;
        return op.GetResults();
    }

    static void Wait(Windows.Foundation.IAsyncAction op)
    {
        while (op.Status == Windows.Foundation.AsyncStatus.Started) System.Threading.Thread.Sleep(20);
        if (op.Status == Windows.Foundation.AsyncStatus.Error) throw op.ErrorCode;
    }

    static int Main(string[] args)
    {
        // 用法: OcrProbe <pdf> <pageIndex> <outPng> [lang] [tessDir] [dataDir] [psm]
        string pdfPath = args[0];
        uint idx = uint.Parse(args[1]);
        string png = args[2];
        string lang = args.Length > 3 ? args[3] : "rus+eng";
        string tess = args.Length > 4 ? args[4] : @".\tessmin\tesseract.exe";
        string data = args.Length > 5 ? args[5] : @".\tessdata";
        string psm = args.Length > 6 ? args[6] : "3";

        Console.OutputEncoding = Encoding.UTF8;

        StorageFile f = Wait(StorageFile.GetFileFromPathAsync(pdfPath));
        PdfDocument doc = Wait(PdfDocument.LoadFromFileAsync(f));
        Console.WriteLine("pages=" + doc.PageCount);
        using (PdfPage page = doc.GetPage(idx))
        {
            PdfPageRenderOptions opt = new PdfPageRenderOptions();
            opt.DestinationWidth = 1500;
            StorageFolder folder = Wait(StorageFolder.GetFolderFromPathAsync(Path.GetDirectoryName(png)));
            StorageFile outFile = Wait(folder.CreateFileAsync(Path.GetFileName(png), CreationCollisionOption.ReplaceExisting));
            using (var os = Wait(outFile.OpenAsync(FileAccessMode.ReadWrite)))
                Wait(page.RenderToStreamAsync(os, opt));
        }
        Console.WriteLine("rendered " + new FileInfo(png).Length + " bytes");

        ProcessStartInfo si = new ProcessStartInfo(tess);
        si.Arguments = "\"" + png + "\" stdout -l " + lang + " --tessdata-dir \"" + data + "\" --psm " + psm;
        si.UseShellExecute = false;
        si.RedirectStandardOutput = true;
        si.RedirectStandardError = true;
        si.StandardOutputEncoding = Encoding.UTF8;
        si.StandardErrorEncoding = Encoding.UTF8;
        si.CreateNoWindow = true;
        Stopwatch sw = Stopwatch.StartNew();
        using (Process p = Process.Start(si))
        {
            string o = p.StandardOutput.ReadToEnd();
            string e = p.StandardError.ReadToEnd();
            p.WaitForExit();
            sw.Stop();
            Console.WriteLine("--- tesseract exit=" + p.ExitCode + " " + sw.ElapsedMilliseconds + "ms");
            if (e.Trim().Length > 0) Console.WriteLine("[stderr] " + e.Substring(0, Math.Min(600, e.Length)));
            File.WriteAllText(png + ".txt", o, new UTF8Encoding(false));
            Console.WriteLine(o.Length > 4000 ? o.Substring(0, 4000) : o);
        }
        return 0;
    }
}
