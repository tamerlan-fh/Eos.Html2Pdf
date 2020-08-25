using System.Threading.Tasks;

namespace PuppeteerSharp
{
    public static class Puppeteer
    {
        internal const int DefaultTimeout = 30_000;

        public static Task<Browser> LaunchAsync(LaunchOptions options)
            => new Launcher().LaunchAsync(options);
    }
}