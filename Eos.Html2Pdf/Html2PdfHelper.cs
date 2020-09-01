using PuppeteerSharp;
using PuppeteerSharp.Media;
using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading.Tasks;

namespace Eos.Html2Pdf
{
    public static class Html2PdfHelper
    {
        public const string DEFAULT_CHROME_PATH = @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe";

        /// <summary>
        /// Конвертирование на основе содержимого файла html
        /// </summary>
        /// <param name="htmlContent">содержимое файла html</param>
        /// <param name="executablePath">абсолютный путь к исполняемому файлу chrome.exe</param>
        /// <returns>содержимое результата в формате pdf в байтовом представлении</returns>
        public static byte[] ConvertFromContent(string htmlContent, string executablePath = DEFAULT_CHROME_PATH)
        {
            return GetAsyncResult(ConvertFromContentAsync(htmlContent, executablePath));
        }

        /// <summary>
        /// Конвертирование на основе содержимого файла html
        /// </summary>
        /// <param name="htmlContent">содержимое файла html</param>
        /// <param name="executablePath">абсолютный путь к исполняемому файлу chrome.exe</param>
        /// <returns>содержимое результата в формате pdf в байтовом представлении</returns>
        public static byte[] ConvertFromContent(byte[] htmlContent, string executablePath = DEFAULT_CHROME_PATH)
        {
            return ConvertFromContent(Encoding.UTF8.GetString(htmlContent), executablePath);
        }

        /// <summary>
        /// Конвертирование на основе содержимого файла html
        /// </summary>
        /// <param name="htmlContent">содержимое файла html</param>
        /// <param name="executablePath">абсолютный путь к исполняемому файлу chrome.exe</param>
        /// <returns>содержимое результата в формате pdf в байтовом представлении</returns>
        public static async Task<byte[]> ConvertFromContentAsync(string htmlContent, string executablePath = DEFAULT_CHROME_PATH)
        {
            using (var browser = await Puppeteer.LaunchAsync(new LaunchOptions
            {
                ExecutablePath = executablePath,
                Headless = true,
                WebSocketFactory = async (uri, socketOptions, cancellationToken) =>
                {
                    var client = SystemClientWebSocket.CreateClientWebSocket();
                    if (client is System.Net.WebSockets.Managed.ClientWebSocket managed)
                    {
                        managed.Options.KeepAliveInterval = TimeSpan.FromSeconds(0);
                        await managed.ConnectAsync(uri, cancellationToken);
                    }
                    else
                    {
                        var coreSocket = client as ClientWebSocket;
                        coreSocket.Options.KeepAliveInterval = TimeSpan.FromSeconds(0);
                        await coreSocket.ConnectAsync(uri, cancellationToken);
                    }

                    return client;
                },
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
