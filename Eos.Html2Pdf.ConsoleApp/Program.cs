using System;
using System.IO;

namespace Eos.Html2Pdf.ConsoleApp
{
    class Program
    {
        static void Main(string[] args)
        {
            try
            {
                var files = new[]
                {
                    new FileInfo(@"sources\ответ_0.html"),
                    new FileInfo(@"sources\ответ_1.html"),
                    new FileInfo(@"sources\ответ_43.html")
                };
                var targetDir = new DirectoryInfo("targets");
                if (!targetDir.Exists)
                    Directory.CreateDirectory(targetDir.FullName);

                foreach (var file in files)
                {
                    if (!file.Exists)
                    {
                        Console.WriteLine($"File {file.FullName} not exist");
                        continue;
                    }
                    var bytes = File.ReadAllBytes(file.FullName);
                    File.WriteAllBytes(Path.Combine(targetDir.FullName, $"{file.Name}_{Guid.NewGuid()}.pdf"), Eos.Html2Pdf.Html2PdfHelper.Convert(bytes));
                }

                Console.WriteLine("done!");
            }
            catch (Exception ex)
            {
                Console.WriteLine();
                Console.WriteLine(ex.Message);
            }
            Console.WriteLine("Press any key to exit");
            Console.ReadKey();
        }
    }
}
