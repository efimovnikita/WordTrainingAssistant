using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;

namespace WordTrainingAssistant.Archiver
{
    internal static class Program
    {
        private static async Task<int> Main(string[] args)
        {
            Option<IEnumerable<FileInfo>> filesOption = new("--files", "Paths to archived files");
            filesOption.AddAlias("-f");
            filesOption.IsRequired = true;
            filesOption.AllowMultipleArgumentsPerToken = true;
            
            RootCommand rootCommand = new("Application for archiving published assemblies.");
            rootCommand.AddOption(filesOption);

            rootCommand.SetHandler(ArchiveFiles, filesOption);
            
            return await rootCommand.InvokeAsync(args);
        }

        private static void ArchiveFiles(IEnumerable<FileInfo> files)
        {
            foreach (FileInfo fileInfo in files)
            {
                DirectoryInfo parent = Directory.GetParent(fileInfo.Directory!.FullName);
                if (parent == null)
                {
                    continue;
                }

                string dirName = parent.Name;

                if (fileInfo.DirectoryName == null)
                {
                    continue;
                }

                string destinationArchiveFileName = Path.Combine(parent.FullName, $"{dirName}.zip");
                if (File.Exists(destinationArchiveFileName))
                {
                    File.Delete(destinationArchiveFileName);
                }

                ZipFile.CreateFromDirectory(fileInfo.DirectoryName, destinationArchiveFileName);

                string directoryName = Path.GetDirectoryName(destinationArchiveFileName);
                if (directoryName == null)
                {
                    continue;
                }

                string operatingSystem = Environment.OSVersion.VersionString;
                if (operatingSystem.Contains("windows", StringComparison.OrdinalIgnoreCase))
                {
                    Process.Start(Environment.GetEnvironmentVariable("WINDIR") + @"\explorer.exe", directoryName);
                }
            }

            Console.WriteLine("OK");
        }
    }
}