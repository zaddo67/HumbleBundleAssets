using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HumbleBundleAssets.Models;
using HumbleBundleAssets.Services;
using Xunit;

namespace HumbleBundleAssets.Tests
{
    public class CatalogTests : IDisposable
    {
        private readonly string _testRoot;
        private readonly string _testDbPath;
        private readonly DatabaseManager _dbManager;

        public CatalogTests()
        {
            _testRoot = Path.Combine(Path.GetTempPath(), "ZoneOfFun_Test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testRoot);
            _testDbPath = Path.Combine(_testRoot, "test_catalog.db");
            _dbManager = new DatabaseManager(_testDbPath);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_testRoot))
                {
                    Directory.Delete(_testRoot, true);
                }
            }
            catch { }
        }

        [Fact]
        public void DefaultDatabasePath_IsUnderZoneOfFunLocalLow()
        {
            var defaultPath = DatabaseManager.GetDefaultDatabasePath();
            Assert.Contains("AppData", defaultPath);
            Assert.Contains("LocalLow", defaultPath);
            Assert.Contains("Zone Of Fun", defaultPath);
            Assert.Contains("assets_catalog.db", defaultPath);
        }

        [Fact]
        public void FoldersAndSettings_ArePersistedCorrectly()
        {
            var sampleFolder = Path.Combine(_testRoot, "SampleAssets");
            Directory.CreateDirectory(sampleFolder);

            // Add folder
            var added = _dbManager.AddIndexedFolder(sampleFolder);
            Assert.True(added);

            // Adding duplicate returns false
            var addedDuplicate = _dbManager.AddIndexedFolder(sampleFolder);
            Assert.False(addedDuplicate);

            var folders = _dbManager.GetIndexedFolders();
            Assert.Single(folders);
            Assert.Equal(Path.GetFullPath(sampleFolder), folders[0].FolderPath);

            // Settings
            _dbManager.SetSetting("LastExtractFolder", @"D:\Unity\Assets");
            var settingVal = _dbManager.GetSetting("LastExtractFolder");
            Assert.Equal(@"D:\Unity\Assets", settingVal);

            // Remove folder
            var removed = _dbManager.RemoveIndexedFolder(sampleFolder);
            Assert.True(removed);
            Assert.Empty(_dbManager.GetIndexedFolders());
        }

        [Fact]
        public async Task CatalogService_IndexesLooseAndZippedAssets_Idempotently()
        {
            var assetsFolder = Path.Combine(_testRoot, "AssetBundleDir");
            Directory.CreateDirectory(assetsFolder);

            // 1. Create a loose image asset
            var loosePngPath = Path.Combine(assetsFolder, "hero_sprite.png");
            File.WriteAllBytes(loosePngPath, new byte[2048]); // 2 KB

            // 2. Create a loose sound asset
            var looseWavPath = Path.Combine(assetsFolder, "laser.wav");
            File.WriteAllBytes(looseWavPath, new byte[8192]); // 8 KB

            // 3. Create a zip archive with central directory containing audio, 3D model, and text
            var zipPath = Path.Combine(assetsFolder, "bundle_pack.zip");
            using (var zipStream = new FileStream(zipPath, FileMode.Create))
            using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create))
            {
                // Audio entry
                var audioEntry = archive.CreateEntry("Audio/music_theme.mp3");
                using (var writer = new StreamWriter(audioEntry.Open()))
                {
                    writer.Write("Fake MP3 stream content for test");
                }

                // 3D Model entry
                var modelEntry = archive.CreateEntry("Models/sword.fbx");
                using (var writer = new StreamWriter(modelEntry.Open()))
                {
                    writer.Write("Fake FBX model data");
                }

                // Text entry
                var readmeEntry = archive.CreateEntry("Docs/readme.txt");
                using (var writer = new StreamWriter(readmeEntry.Open()))
                {
                    writer.WriteLine("Hello Zone Of Fun Asset Catalog!");
                }

                // Non-supported entry (should be ignored)
                var exeEntry = archive.CreateEntry("Bin/unsupported.exe");
                using (var writer = new StreamWriter(exeEntry.Open()))
                {
                    writer.Write("exe");
                }
            }

            var catalogService = new CatalogService(_dbManager);

            // Run first catalog scan
            await catalogService.CatalogFoldersAsync(new[] { assetsFolder }, null, CancellationToken.None);

            var totalCount = _dbManager.GetTotalAssetCount();
            // Expected: hero_sprite.png, laser.wav, music_theme.mp3, sword.fbx, readme.txt = 5 assets
            Assert.Equal(5, totalCount);

            // Test search by partial keyword
            var searchSword = _dbManager.SearchAssets("sword");
            Assert.Single(searchSword);
            Assert.Equal("sword.fbx", searchSword[0].FileName);
            Assert.True(searchSword[0].IsZipArchive);
            Assert.Equal("Models/sword.fbx", searchSword[0].InnerPath);
            Assert.Equal("3D Models", searchSword[0].Category);
            Assert.False(searchSword[0].IsPreviewSupported); // FBX is not in preview formats

            // Test search by category Audio
            var audioAssets = _dbManager.SearchAssets(null, "Audio");
            Assert.Equal(2, audioAssets.Count); // laser.wav and music_theme.mp3
            Assert.Contains(audioAssets, a => a.FileName == "laser.wav" && !a.IsZipArchive);
            Assert.Contains(audioAssets, a => a.FileName == "music_theme.mp3" && a.IsZipArchive);

            // Test search by size filter
            var largeAssets = _dbManager.SearchAssets(null, minSizeBytes: 3000);
            Assert.Single(largeAssets);
            Assert.Equal("laser.wav", largeAssets[0].FileName);

            // IDEMPOTENCY TEST: Run cataloging again on same folder
            await catalogService.CatalogFoldersAsync(new[] { assetsFolder }, null, CancellationToken.None);
            var reTotalCount = _dbManager.GetTotalAssetCount();
            Assert.Equal(5, reTotalCount); // Must still be exactly 5, not 10!
        }

        [Fact]
        public async Task ExtractionService_ExtractsLooseAndZippedAssets()
        {
            var assetsFolder = Path.Combine(_testRoot, "AssetsSource");
            var extractDest = Path.Combine(_testRoot, "AssetsExtracted");
            Directory.CreateDirectory(assetsFolder);

            var looseTxt = Path.Combine(assetsFolder, "license.txt");
            File.WriteAllText(looseTxt, "MIT License Zone Of Fun");

            var zipPath = Path.Combine(assetsFolder, "pack.zip");
            using (var zipStream = new FileStream(zipPath, FileMode.Create))
            using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("Sub/coin.wav");
                using var writer = new StreamWriter(entry.Open());
                writer.Write("Coin sound data");
            }

            var item1 = new AssetItem
            {
                FileName = "license.txt",
                FilePath = looseTxt,
                IsZipArchive = false,
                FileType = ".txt"
            };

            var item2 = new AssetItem
            {
                FileName = "coin.wav",
                FilePath = zipPath,
                InnerPath = "Sub/coin.wav",
                IsZipArchive = true,
                FileType = ".wav"
            };

            var extractionService = new ExtractionService();
            var extracted = await extractionService.ExtractAssetsAsync(new[] { item1, item2 }, extractDest);

            Assert.Equal(2, extracted);
            Assert.True(File.Exists(Path.Combine(extractDest, "license.txt")));
            Assert.True(File.Exists(Path.Combine(extractDest, "Sub", "coin.wav")));
        }

        [Fact]
        public async Task PreviewService_ExtractsZipEntryToTempCacheAndLoadsText()
        {
            var assetsFolder = Path.Combine(_testRoot, "PreviewSource");
            Directory.CreateDirectory(assetsFolder);

            var zipPath = Path.Combine(assetsFolder, "text_archive.zip");
            using (var zipStream = new FileStream(zipPath, FileMode.Create))
            using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("Notes/changelog.txt");
                using var writer = new StreamWriter(entry.Open());
                writer.WriteLine("Version 1.0 - Humble Bundle Assets Tool Initial Release");
            }

            var asset = new AssetItem
            {
                FileName = "changelog.txt",
                FilePath = zipPath,
                InnerPath = "Notes/changelog.txt",
                IsZipArchive = true,
                FileType = ".txt"
            };

            var previewService = new PreviewService();
            var previewPath = await previewService.PreparePreviewFileAsync(asset);

            Assert.NotNull(previewPath);
            Assert.True(File.Exists(previewPath));

            var textContent = await previewService.LoadTextPreviewAsync(previewPath);
            Assert.Contains("Version 1.0", textContent);
        }

        [Fact]
        public void MainWindow_InstantiatesWithoutXamlParseException()
        {
            Exception? exception = null;
            var thread = new Thread(() =>
            {
                try
                {
                    if (System.Windows.Application.Current == null)
                    {
                        new App();
                    }
                    var window = new MainWindow();
                    Assert.NotNull(window);
                    window.Close();
                }
                catch (Exception ex)
                {
                    exception = ex;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();

            Assert.Null(exception);
        }
    }
}
