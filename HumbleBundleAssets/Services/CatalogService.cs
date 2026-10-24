using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using HumbleBundleAssets.Models;

namespace HumbleBundleAssets.Services
{
    public class CatalogService
    {
        private readonly DatabaseManager _dbManager;

        public static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            // Images
            ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".tga", ".psd",
            // Audio
            ".wav", ".mp3", ".ogg", ".flac", ".aiff",
            // 3D Models
            ".fbx", ".obj", ".blend", ".dae",
            // Fonts
            ".ttf", ".otf",
            // Text / Data / Unity Scripts & Shaders
            ".txt", ".json", ".csv", ".md", ".cs", ".shader", ".xml", ".yaml", ".ini",
            // Unity Packages
            ".unitypackage"
        };

        public CatalogService(DatabaseManager dbManager)
        {
            _dbManager = dbManager;
        }

        public async Task CatalogFoldersAsync(
            IReadOnlyList<string> folders,
            IProgress<CatalogProgress>? progress,
            CancellationToken cancellationToken)
        {
            await Task.Run(() =>
            {
                var progressReport = new CatalogProgress();
                int totalProcessedFiles = 0;
                int totalCatalogedAssets = 0;

                foreach (var folder in folders)
                {
                    if (cancellationToken.IsCancellationRequested) break;
                    if (!Directory.Exists(folder)) continue;

                    progressReport.CurrentFolder = folder;
                    progressReport.StatusMessage = $"Scanning folder: {folder}";
                    progress?.Report(progressReport);

                    // Idempotent: clear previous records for this root folder before re-cataloging
                    _dbManager.ClearAssetsForRootFolder(folder);

                    var batch = new List<AssetItem>(1000);

                    void FlushBatch()
                    {
                        if (batch.Count > 0)
                        {
                            _dbManager.InsertAssetsBatch(batch, folder);
                            batch.Clear();
                        }
                    }

                    try
                    {
                        var dirQueue = new Queue<string>();
                        dirQueue.Enqueue(folder);

                        while (dirQueue.Count > 0)
                        {
                            if (cancellationToken.IsCancellationRequested) break;

                            var currentDir = dirQueue.Dequeue();

                            // Enqueue subdirectories safely
                            try
                            {
                                foreach (var subDir in Directory.EnumerateDirectories(currentDir))
                                {
                                    if (cancellationToken.IsCancellationRequested) break;
                                    // Skip hidden or system folders if needed
                                    var dirInfo = new DirectoryInfo(subDir);
                                    if (!dirInfo.Attributes.HasFlag(FileAttributes.Hidden) &&
                                        !dirInfo.Attributes.HasFlag(FileAttributes.System))
                                    {
                                        dirQueue.Enqueue(subDir);
                                    }
                                }
                            }
                            catch (UnauthorizedAccessException) { /* ignore restricted dirs */ }
                            catch (Exception) { /* continue */ }

                            // Enumerate files
                            IEnumerable<string>? files = null;
                            try
                            {
                                files = Directory.EnumerateFiles(currentDir);
                            }
                            catch (UnauthorizedAccessException) { /* ignore restricted files */ }
                            catch (Exception) { /* continue */ }

                            if (files == null) continue;

                            foreach (var filePath in files)
                            {
                                if (cancellationToken.IsCancellationRequested) break;

                                totalProcessedFiles++;
                                var ext = Path.GetExtension(filePath);
                                if (string.IsNullOrEmpty(ext)) continue;

                                progressReport.CurrentFile = Path.GetFileName(filePath);
                                progressReport.ProcessedFiles = totalProcessedFiles;
                                progressReport.CatalogedAssets = totalCatalogedAssets;

                                if (totalProcessedFiles % 50 == 0)
                                {
                                    progress?.Report(progressReport);
                                }

                                if (ext.Equals(".zip", StringComparison.OrdinalIgnoreCase))
                                {
                                    // Inspect ZIP central directory
                                    try
                                    {
                                        using var zipFileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                                        using var archive = new ZipArchive(zipFileStream, ZipArchiveMode.Read);

                                        foreach (var entry in archive.Entries)
                                        {
                                            if (cancellationToken.IsCancellationRequested) break;

                                            // Skip directories inside zip
                                            if (entry.FullName.EndsWith("/") || entry.FullName.EndsWith("\\") || string.IsNullOrEmpty(entry.Name))
                                            {
                                                continue;
                                            }

                                            var entryExt = Path.GetExtension(entry.Name);
                                            if (SupportedExtensions.Contains(entryExt))
                                            {
                                                var asset = new AssetItem
                                                {
                                                    FileName = entry.Name,
                                                    FilePath = filePath,
                                                    InnerPath = entry.FullName,
                                                    IsZipArchive = true,
                                                    FileType = AssetItem.NormalizeExtension(entryExt),
                                                    FileSize = entry.Length,
                                                    DateModified = entry.LastWriteTime.DateTime
                                                };

                                                batch.Add(asset);
                                                totalCatalogedAssets++;

                                                if (batch.Count >= 1000)
                                                {
                                                    FlushBatch();
                                                }
                                            }
                                        }
                                    }
                                    catch (Exception)
                                    {
                                        // Corrupted zip or locked file: safely skip
                                    }
                                }
                                else if (SupportedExtensions.Contains(ext))
                                {
                                    // Loose asset file
                                    try
                                    {
                                        var fileInfo = new FileInfo(filePath);
                                        var asset = new AssetItem
                                        {
                                            FileName = fileInfo.Name,
                                            FilePath = fileInfo.FullName,
                                            InnerPath = string.Empty,
                                            IsZipArchive = false,
                                            FileType = AssetItem.NormalizeExtension(ext),
                                            FileSize = fileInfo.Length,
                                            DateModified = fileInfo.LastWriteTime
                                        };

                                        batch.Add(asset);
                                        totalCatalogedAssets++;

                                        if (batch.Count >= 1000)
                                        {
                                            FlushBatch();
                                        }
                                    }
                                    catch (Exception)
                                    {
                                        // Unreadable file: skip
                                    }
                                }
                            }
                        }

                        FlushBatch();
                    }
                    catch (Exception ex)
                    {
                        progressReport.StatusMessage = $"Error scanning {folder}: {ex.Message}";
                        progress?.Report(progressReport);
                    }
                }

                progressReport.ProcessedFiles = totalProcessedFiles;
                progressReport.CatalogedAssets = totalCatalogedAssets;
                progressReport.StatusMessage = cancellationToken.IsCancellationRequested
                    ? "Cataloging cancelled."
                    : $"Completed: {totalCatalogedAssets:N0} assets cataloged from {totalProcessedFiles:N0} files scanned.";
                progress?.Report(progressReport);

            }, cancellationToken);
        }
    }
}
