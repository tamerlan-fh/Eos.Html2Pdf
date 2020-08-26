using PuppeteerSharp;
using PuppeteerSharp.Media;
using System;
using System.Text;
using System.Threading.Tasks;

namespace Eos.Html2Pdf
{
    public static class Html2PdfHelper
    {
        private const string default_chrome_path = @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe";

        /// <summary>
        /// Конвертирование на основе содержимого файла html
        /// </summary>
        /// <param name="htmlContent">содержимое файла html</param>
        /// <param name="executablePath">абсолютный путь к исполняемому файлу chrome.exe</param>
        /// <returns>содержимое результата в формате pdf в байтовом представлении</returns>
        public static byte[] ConvertFromContent(string htmlContent, string executablePath = default_chrome_path)
        {
            return GetAsyncResult(ConvertFromContentAsync(htmlContent, executablePath));
        }

        /// <summary>
        /// Конвертирование на основе содержимого файла html
        /// </summary>
        /// <param name="htmlContent">содержимое файла html</param>
        /// <param name="executablePath">абсолютный путь к исполняемому файлу chrome.exe</param>
        /// <returns>содержимое результата в формате pdf в байтовом представлении</returns>
        public static byte[] ConvertFromContent(byte[] htmlContent, string executablePath = default_chrome_path)
        {
            return ConvertFromContent(Encoding.UTF8.GetString(htmlContent), executablePath);
        }

        /// <summary>
        /// Конвертирование на основе содержимого файла html
        /// </summary>
        /// <param name="htmlContent">содержимое файла html</param>
        /// <param name="executablePath">абсолютный путь к исполняемому файлу chrome.exe</param>
        /// <returns>содержимое результата в формате pdf в байтовом представлении</returns>
        public static async Task<byte[]> ConvertFromContentAsync(string htmlContent, string executablePath = default_chrome_path)
        {
            using (var browser = await Puppeteer.LaunchAsync(new LaunchOptions
            {
                ExecutablePath = executablePath,
                Headless = true
            }).ConfigureAwait(false))
            {
                using (var page = await browser.NewPageAsync())
                {
                    await page.SetContentAsync(htmlContent).ConfigureAwait(false);

                    var pdfOptions = new PdfOptions
                    {
                        Format = PaperFormat.A4,
                        DisplayHeaderFooter = false
                    };

                    return await page.PdfDataAsync(pdfOptions).ConfigureAwait(false);
                }
            }
        }

        private static byte[] GetAsyncResult(Task<byte[]> task)
        {
            if (!task.Wait(TimeSpan.FromSeconds(60)) || task.Exception != null)
                throw new Exception($"В процессе конвертирования документа http в pdf произошла ошибка. {task.Exception?.Message}", task.Exception);
            return task.Result;
        }
    }
}
