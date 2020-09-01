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
                    var htmlContent = File.ReadAllText(file.FullName);
                    var filename = $"{file.Name}_{Guid.NewGuid()}.pdf";
                    File.WriteAllBytes(Path.Combine(targetDir.FullName, filename), Eos.Html2Pdf.Html2PdfHelper.ConvertFromContent(htmlContent));
                    Console.WriteLine($"{filename} created!");
                }

                Console.WriteLine("done!");
            }
            catch (Exception ex)
            {
                Console.WriteLine();
                Console.WriteLine(ex.Message);
                Console.WriteLine(ex.InnerException?.Message);
                Console.WriteLine(ex.InnerException?.InnerException?.Message);
            }
            Console.WriteLine("Press any key to exit");
            Console.ReadKey();
        }
    }
}
