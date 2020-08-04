using PuppeteerSharp;
using PuppeteerSharp.Media;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Eos.Html2Pdf
{
    public static class Html2PdfHelper
    {
        private const string default_chrome_path = @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe";

        public static byte[] Convert(byte[] httpSource, string executablePath = default_chrome_path)
        {
            return Convert(ConvertAsync(httpSource, executablePath));
        }

        public static byte[] Convert(string httpPath, string executablePath = default_chrome_path)
        {
            return Convert(ConvertAsync(httpPath, executablePath));
        }

        private static byte[] Convert(Task<byte[]> task)
        {
            if (!task.Wait(TimeSpan.FromSeconds(60)) || task.Exception != null)
                throw new Exception($"В процессе конвертирования документа http в pdf произошла ошибка. {task.Exception.Message}", task.Exception);
            return task.Result;
        }

        public static async Task<byte[]> ConvertAsync(byte[] httpSource, string executablePath = default_chrome_path)
        {
            var htmlPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.html");
            try
            {
                File.WriteAllBytes(htmlPath, httpSource);
                return await ConvertAsync(htmlPath, executablePath).ConfigureAwait(false);
            }
            finally
            {
                DeleteFile(htmlPath);
            }
        }

        public static async Task<byte[]> ConvertAsync(string httpPath, string executablePath = default_chrome_path)
        {
            using (var browser = await Puppeteer.LaunchAsync(new LaunchOptions
            {
                ExecutablePath = executablePath,
                Headless = true
            }).ConfigureAwait(false))
            {
                var page = await browser.NewPageAsync();

                await page.GoToAsync(httpPath).ConfigureAwait(false);

                var pdfOptions = new PdfOptions
                {
                    Format = PaperFormat.A4,
                    DisplayHeaderFooter = false
                };

                return await page.PdfDataAsync(pdfOptions).ConfigureAwait(false);
            }
        }

        private static void DeleteFile(string fileName)
        {
            var fileInfo = new FileInfo(fileName);
            if (!fileInfo.Exists)
                return;
            try
            {
                fileInfo.Attributes = FileAttributes.Normal;
                fileInfo.Delete();
            }
            catch { }
        }
    }
}
