using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using HumbleBundleAssets.Models;

namespace HumbleBundleAssets.Services
{
    public class PreviewService
    {
        private readonly string _previewTempDir;

        public PreviewService()
        {
            _previewTempDir = Path.Combine(Path.GetTempPath(), "ZoneOfFun", "HumbleBundleAssets", "Previews");
            if (!Directory.Exists(_previewTempDir))
            {
                Directory.CreateDirectory(_previewTempDir);
            }
        }

        public async Task<string?> PreparePreviewFileAsync(AssetItem asset)
        {
            return await Task.Run(() =>
            {
                try
                {
                    if (!asset.IsZipArchive)
                    {
                        if (File.Exists(asset.FilePath))
                        {
                            return asset.FilePath;
                        }
                        return null;
                    }

                    if (!File.Exists(asset.FilePath))
                    {
                        return null;
                    }

                    // Create unique hash-based path to prevent conflicts and cache previously extracted previews
                    var key = $"{asset.FilePath}::{asset.InnerPath}";
                    using var sha = SHA256.Create();
                    var hashBytes = sha.ComputeHash(Encoding.UTF8.GetBytes(key));
                    var hashStr = Convert.ToHexString(hashBytes).Substring(0, 16);

                    var previewSubDir = Path.Combine(_previewTempDir, hashStr);
                    if (!Directory.Exists(previewSubDir))
                    {
                        Directory.CreateDirectory(previewSubDir);
                    }

                    var targetPreviewPath = Path.Combine(previewSubDir, asset.FileName);
                    if (File.Exists(targetPreviewPath))
                    {
                        return targetPreviewPath;
                    }

                    // Extract single entry
                    using var zipStream = new FileStream(asset.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);
                    var entry = archive.GetEntry(asset.InnerPath);
                    if (entry != null)
                    {
                        entry.ExtractToFile(targetPreviewPath, overwrite: true);
                        return targetPreviewPath;
                    }

                    return null;
                }
                catch (Exception)
                {
                    return null;
                }
            });
        }

        public async Task<string> LoadTextPreviewAsync(string filePath, int maxChars = 20000)
        {
            return await Task.Run(() =>
            {
                try
                {
                    if (!File.Exists(filePath)) return "(File not found)";

                    using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

                    var buffer = new char[maxChars];
                    int read = reader.ReadBlock(buffer, 0, maxChars);
                    var text = new string(buffer, 0, read);

                    if (stream.Position < stream.Length)
                    {
                        text += "\r\n\r\n... [Preview truncated: file is larger than 20 KB] ...";
                    }
                    return text;
                }
                catch (Exception ex)
                {
                    return $"Error loading text preview: {ex.Message}";
                }
            });
        }
    }
}
