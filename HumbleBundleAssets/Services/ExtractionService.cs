using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using HumbleBundleAssets.Models;

namespace HumbleBundleAssets.Services
{
    public class ExtractionService
    {
        public async Task<int> ExtractAssetsAsync(
            IEnumerable<AssetItem> assets,
            string destinationFolder,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            return await Task.Run(() =>
            {
                if (!Directory.Exists(destinationFolder))
                {
                    Directory.CreateDirectory(destinationFolder);
                }

                int extractedCount = 0;

                foreach (var asset in assets)
                {
                    if (cancellationToken.IsCancellationRequested) break;

                    try
                    {
                        progress?.Report($"Extracting {asset.FileName}...");

                        string targetFilePath;
                        if (asset.IsZipArchive && !string.IsNullOrEmpty(asset.InnerPath))
                        {
                            // Create subfolder structure or safe file path
                            var safeRelative = asset.InnerPath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
                            targetFilePath = Path.Combine(destinationFolder, safeRelative);
                            var parent = Path.GetDirectoryName(targetFilePath);
                            if (!string.IsNullOrEmpty(parent) && !Directory.Exists(parent))
                            {
                                Directory.CreateDirectory(parent);
                            }

                            // Extract from zip
                            if (File.Exists(asset.FilePath))
                            {
                                using var zipStream = new FileStream(asset.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                                using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);
                                var entry = archive.GetEntry(asset.InnerPath);
                                if (entry != null)
                                {
                                    entry.ExtractToFile(targetFilePath, overwrite: true);
                                    extractedCount++;
                                }
                            }
                        }
                        else
                        {
                            // Loose file: copy directly
                            if (File.Exists(asset.FilePath))
                            {
                                targetFilePath = Path.Combine(destinationFolder, asset.FileName);
                                File.Copy(asset.FilePath, targetFilePath, overwrite: true);
                                extractedCount++;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        progress?.Report($"Error extracting {asset.FileName}: {ex.Message}");
                    }
                }

                return extractedCount;
            }, cancellationToken);
        }
    }
}
