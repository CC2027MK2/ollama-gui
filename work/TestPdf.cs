using System;

namespace OllamaSwitch
{
    static class TestPdf
    {
        [STAThread]
        static void Main(string[] args)
        {
            // 用法: testpdf.exe <model> <pdf> <outDocx> <ocrLang 0自动/1中/2英/3俄>
            string model = args[0];
            string src = args[1];
            string outDocx = args[2];
            DocTranslate.OcrLang = int.Parse(args[3]);
            DateTime t0 = DateTime.Now;
            string r = DocTranslate.TranslatePdf(model, src, outDocx, delegate(string s)
            {
                Console.WriteLine("[" + (DateTime.Now - t0).TotalSeconds.ToString("0") + "s] " + s);
            });
            Console.WriteLine("DONE " + r + " in " + (DateTime.Now - t0).TotalSeconds.ToString("0") + "s");
        }
    }
}
