using System.IO.Compression;
using ILogger = Serilog.ILogger;

namespace ZipSplitterWorker;

public class Worker(ILogger logger, string zipFilesPath) : BackgroundService
{
    private string ZipFilesPath { get; } = zipFilesPath;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.Information("Starting zip processing...");
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(ZipFilesPath) && Directory.Exists(ZipFilesPath))
                    ProcessZipFiles(ZipFilesPath);
                else
                    logger.Warning("Invalid path or directory does not exist.");
            }
            catch (Exception ex)
            {
                logger.Error(ex, "An error occurred while processing zip files.");
            }

            await Task.Delay(10000, stoppingToken); // Delay for 10 seconds before repeating
        }
    }

    private void ProcessZipFiles(string directoryPath)
    {
        var zipFiles = Directory.GetFiles(directoryPath, "*.zip");

        foreach (var zipFile in zipFiles)
            try
            {
                var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(zipFile);
                var finalFolder = Path.Combine(directoryPath, $"{fileNameWithoutExtension}-Folder");

                logger.Information($"Processing {zipFile}...");

                // Step 1: Extract zip file recursively directly into final folder
                ExtractZipFileRecursively(zipFile, finalFolder);
                logger.Information($"Extracted {zipFile} into final folder: {finalFolder}");

                // Step 2: Create split zip files from the final folder
                CreateSplitZip(finalFolder, Path.Combine(finalFolder, $"{fileNameWithoutExtension}.part"),
                    100 * 1024 * 1024);
                logger.Information($"Created split zip files for {zipFile}");

                // Step 3: Delete old filee
                File.Delete(zipFile);
                Directory.Delete(Path.Combine(finalFolder, fileNameWithoutExtension), true);
                logger.Information($"Deleted original zip file: {zipFile}");
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"An error occurred while processing {zipFile}");
            }
    }

    private static void ExtractZipFileRecursively(string zipFilePath, string destinationFolder)
    {
        if (!Directory.Exists(destinationFolder)) Directory.CreateDirectory(destinationFolder);

        using var archive = ZipFile.OpenRead(zipFilePath);
        foreach (var entry in archive.Entries)
        {
            var entryPath = Path.Combine(destinationFolder, entry.FullName);

            if (string.IsNullOrEmpty(entry.Name)) // It's a directory
            {
                Directory.CreateDirectory(entryPath);
            }
            else
            {
                var entryDir = Path.GetDirectoryName(entryPath)!;
                if (!Directory.Exists(entryDir))
                    Directory.CreateDirectory(entryDir);

                entry.ExtractToFile(entryPath, true);

                // If the extracted file is another ZIP, process it recursively
                if (Path.GetExtension(entryPath).Equals(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    var nestedDestination = Path.Combine(destinationFolder,
                        Path.GetFileNameWithoutExtension(entry.FullName));
                    ExtractZipFileRecursively(entryPath, nestedDestination);

                    // Delete the nested zip file after extraction
                    File.Delete(entryPath);
                }
            }
        }
    }

    private static void CreateSplitZip(string sourceDir, string outputFilePrefix, long partSizeBytes)
    {
        var files = Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories);
        var partNumber = 1;
        long currentPartSize = 0;
        FileStream? partFile = null;
        ZipArchive? partArchive = null;

        foreach (var file in files)
        {
            var fileInfo = new FileInfo(file);

            // Se il file è più grande del partSizeBytes, va in un part da solo
            if (fileInfo.Length > partSizeBytes)
            {
                // Chiudi il part corrente
                partArchive?.Dispose();
                partFile?.Dispose();
                var partFileName = $"{outputFilePrefix}{partNumber}.zip";
                using (var largeFile = new FileStream(partFileName, FileMode.Create, FileAccess.Write))
                using (var largeArchive = new ZipArchive(largeFile, ZipArchiveMode.Create))
                {
                    var relativePath = Path.GetRelativePath(sourceDir, file);
                    var entry = largeArchive.CreateEntry(relativePath);
                    using (var entryStream = entry.Open())
                    using (var fileStream = new FileStream(file, FileMode.Open, FileAccess.Read))
                    {
                        fileStream.CopyTo(entryStream);
                    }
                }

                partNumber++;
                currentPartSize = 0;
                partArchive = null;
                partFile = null;
                continue;
            }

            // Se aggiungere il file corrente sfora il limite, chiudi il part corrente
            if (currentPartSize + fileInfo.Length > partSizeBytes)
            {
                partArchive?.Dispose();
                partFile?.Dispose();
                partNumber++;
                currentPartSize = 0;
            }

            // Se non esiste ancora, apri un nuovo part
            if (currentPartSize == 0)
            {
                var partFileName = $"{outputFilePrefix}{partNumber}.zip";
                partFile = new FileStream(partFileName, FileMode.Create, FileAccess.Write);
                partArchive = new ZipArchive(partFile, ZipArchiveMode.Create);
            }

            // Aggiungi il file al part corrente
            var relPath = Path.GetRelativePath(sourceDir, file);
            var zipEntry = partArchive!.CreateEntry(relPath);
            using (var entryStream = zipEntry.Open())
            using (var fileStream = new FileStream(file, FileMode.Open, FileAccess.Read))
            {
                fileStream.CopyTo(entryStream);
            }

            currentPartSize += fileInfo.Length;
        }

        // Chiudi l'ultimo part
        partArchive?.Dispose();
        partFile?.Dispose();
    }
}