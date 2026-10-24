using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.Data.Sqlite;
using HumbleBundleAssets.Models;

namespace HumbleBundleAssets.Services
{
    public class DatabaseManager
    {
        private string _connectionString = string.Empty;
        public string CurrentDbPath { get; private set; } = string.Empty;

        public static string GetDefaultDatabasePath()
        {
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var localLowDir = Path.Combine(userProfile, "AppData", "LocalLow", "Zone Of Fun", "HumbleBundleAssets");
            if (!Directory.Exists(localLowDir))
            {
                Directory.CreateDirectory(localLowDir);
            }
            return Path.Combine(localLowDir, "assets_catalog.db");
        }

        public DatabaseManager(string? dbPath = null)
        {
            var targetPath = string.IsNullOrWhiteSpace(dbPath) ? GetDefaultDatabasePath() : dbPath;
            SwitchDatabase(targetPath);
        }

        public void SwitchDatabase(string dbPath)
        {
            CurrentDbPath = Path.GetFullPath(dbPath);
            var parentDir = Path.GetDirectoryName(CurrentDbPath);
            if (!string.IsNullOrEmpty(parentDir) && !Directory.Exists(parentDir))
            {
                Directory.CreateDirectory(parentDir);
            }

            _connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = CurrentDbPath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Shared
            }.ToString();

            InitializeDatabase();
        }

        private SqliteConnection CreateConnection()
        {
            var connection = new SqliteConnection(_connectionString);
            connection.Open();
            return connection;
        }

        private void InitializeDatabase()
        {
            using var conn = CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                PRAGMA journal_mode = WAL;
                PRAGMA synchronous = NORMAL;

                CREATE TABLE IF NOT EXISTS Folders (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    FolderPath TEXT UNIQUE NOT NULL,
                    DateAdded TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS Settings (
                    Key TEXT PRIMARY KEY,
                    Value TEXT
                );

                CREATE TABLE IF NOT EXISTS Assets (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    FileName TEXT NOT NULL,
                    FilePath TEXT NOT NULL,
                    InnerPath TEXT,
                    IsZipArchive INTEGER NOT NULL,
                    FileType TEXT NOT NULL,
                    FileSize INTEGER NOT NULL,
                    DateModified TEXT NOT NULL,
                    RootFolder TEXT NOT NULL
                );

                CREATE INDEX IF NOT EXISTS idx_assets_filename ON Assets(FileName COLLATE NOCASE);
                CREATE INDEX IF NOT EXISTS idx_assets_filetype ON Assets(FileType COLLATE NOCASE);
                CREATE INDEX IF NOT EXISTS idx_assets_filesize ON Assets(FileSize);
                CREATE INDEX IF NOT EXISTS idx_assets_rootfolder ON Assets(RootFolder);
                CREATE INDEX IF NOT EXISTS idx_assets_filepath ON Assets(FilePath);
            ";
            cmd.ExecuteNonQuery();
        }

        #region Folders & Settings

        public List<FolderItem> GetIndexedFolders()
        {
            var list = new List<FolderItem>();
            using var conn = CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Id, FolderPath, DateAdded FROM Folders ORDER BY DateAdded ASC;";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new FolderItem
                {
                    Id = reader.GetInt64(0),
                    FolderPath = reader.GetString(1),
                    DateAdded = DateTime.TryParse(reader.GetString(2), out var dt) ? dt : DateTime.Now
                });
            }
            return list;
        }

        public bool AddIndexedFolder(string folderPath)
        {
            var normalized = Path.GetFullPath(folderPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            using var conn = CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT OR IGNORE INTO Folders (FolderPath, DateAdded) VALUES (@path, @date);";
            cmd.Parameters.AddWithValue("@path", normalized);
            cmd.Parameters.AddWithValue("@date", DateTime.UtcNow.ToString("O"));
            return cmd.ExecuteNonQuery() > 0;
        }

        public bool RemoveIndexedFolder(string folderPath)
        {
            var normalized = Path.GetFullPath(folderPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            using var conn = CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM Folders WHERE FolderPath = @path COLLATE NOCASE;";
            cmd.Parameters.AddWithValue("@path", normalized);
            var deleted = cmd.ExecuteNonQuery() > 0;

            // Also clean up assets indexed under this folder
            using var cleanCmd = conn.CreateCommand();
            cleanCmd.CommandText = "DELETE FROM Assets WHERE RootFolder = @path COLLATE NOCASE;";
            cleanCmd.Parameters.AddWithValue("@path", normalized);
            cleanCmd.ExecuteNonQuery();

            return deleted;
        }

        public string? GetSetting(string key, string? defaultValue = null)
        {
            using var conn = CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Value FROM Settings WHERE Key = @key;";
            cmd.Parameters.AddWithValue("@key", key);
            var val = cmd.ExecuteScalar();
            return val != null && val != DBNull.Value ? Convert.ToString(val) : defaultValue;
        }

        public void SetSetting(string key, string value)
        {
            using var conn = CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO Settings (Key, Value) VALUES (@key, @value) ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value;";
            cmd.Parameters.AddWithValue("@key", key);
            cmd.Parameters.AddWithValue("@value", value);
            cmd.ExecuteNonQuery();
        }

        #endregion

        #region Cataloging & Assets Management

        public void ClearAssetsForRootFolder(string rootFolder)
        {
            var normalized = Path.GetFullPath(rootFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            using var conn = CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM Assets WHERE RootFolder = @root COLLATE NOCASE;";
            cmd.Parameters.AddWithValue("@root", normalized);
            cmd.ExecuteNonQuery();
        }

        public void InsertAssetsBatch(IReadOnlyList<AssetItem> assets, string rootFolder)
        {
            if (assets == null || assets.Count == 0) return;
            var normalizedRoot = Path.GetFullPath(rootFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            using var conn = CreateConnection();
            using var tx = conn.BeginTransaction();
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
                INSERT INTO Assets (FileName, FilePath, InnerPath, IsZipArchive, FileType, FileSize, DateModified, RootFolder)
                VALUES (@FileName, @FilePath, @InnerPath, @IsZipArchive, @FileType, @FileSize, @DateModified, @RootFolder);
            ";

            var pFileName = cmd.Parameters.Add("@FileName", SqliteType.Text);
            var pFilePath = cmd.Parameters.Add("@FilePath", SqliteType.Text);
            var pInnerPath = cmd.Parameters.Add("@InnerPath", SqliteType.Text);
            var pIsZipArchive = cmd.Parameters.Add("@IsZipArchive", SqliteType.Integer);
            var pFileType = cmd.Parameters.Add("@FileType", SqliteType.Text);
            var pFileSize = cmd.Parameters.Add("@FileSize", SqliteType.Integer);
            var pDateModified = cmd.Parameters.Add("@DateModified", SqliteType.Text);
            var pRootFolder = cmd.Parameters.Add("@RootFolder", SqliteType.Text);

            pRootFolder.Value = normalizedRoot;

            foreach (var item in assets)
            {
                pFileName.Value = item.FileName;
                pFilePath.Value = item.FilePath;
                pInnerPath.Value = item.InnerPath ?? string.Empty;
                pIsZipArchive.Value = item.IsZipArchive ? 1 : 0;
                pFileType.Value = item.FileType;
                pFileSize.Value = item.FileSize;
                pDateModified.Value = item.DateModified.ToString("O");
                cmd.ExecuteNonQuery();
            }

            tx.Commit();
        }

        public int GetTotalAssetCount()
        {
            using var conn = CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM Assets;";
            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        public List<string> GetDistinctFileTypes()
        {
            var list = new List<string>();
            using var conn = CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT DISTINCT FileType FROM Assets WHERE FileType IS NOT NULL AND FileType != '' ORDER BY FileType ASC;";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(reader.GetString(0));
            }
            return list;
        }

        public List<AssetItem> SearchAssets(
            string? keyword,
            string? category,
            string? fileType = null,
            long? minSizeBytes = null,
            long? maxSizeBytes = null,
            int limit = 5000)
        {
            return SearchAssets(keyword, SearchMode.All, category, fileType, minSizeBytes, maxSizeBytes, limit);
        }

        public List<AssetItem> SearchAssets(
            string? keyword = null,
            SearchMode searchMode = SearchMode.All,
            string? category = null,
            string? fileType = null,
            long? minSizeBytes = null,
            long? maxSizeBytes = null,
            int limit = 5000)
        {
            var results = new List<AssetItem>();
            using var conn = CreateConnection();

            conn.CreateFunction<string, string, string, string, bool>(
                "MATCH_SEARCH",
                (fileName, innerPath, q, m) =>
                {
                    var mode = Enum.TryParse<SearchMode>(m, out var parsed) ? parsed : SearchMode.All;
                    return SearchMatcher.Matches(fileName, innerPath, q, mode);
                });

            using var cmd = conn.CreateCommand();

            var sb = new StringBuilder("SELECT Id, FileName, FilePath, InnerPath, IsZipArchive, FileType, FileSize, DateModified FROM Assets WHERE 1=1 ");

            if (!string.IsNullOrWhiteSpace(keyword))
            {
                sb.Append(" AND MATCH_SEARCH(FileName, InnerPath, @keyword, @searchMode) = 1 ");
                cmd.Parameters.AddWithValue("@keyword", keyword.Trim());
                cmd.Parameters.AddWithValue("@searchMode", searchMode.ToString());
            }

            if (!string.IsNullOrWhiteSpace(fileType) && fileType != "All")
            {
                var normExt = AssetItem.NormalizeExtension(fileType);
                sb.Append(" AND FileType = @fileType COLLATE NOCASE ");
                cmd.Parameters.AddWithValue("@fileType", normExt);
            }
            else if (!string.IsNullOrWhiteSpace(category) && category != "All Categories")
            {
                var extensions = GetExtensionsForCategory(category);
                if (extensions.Count > 0)
                {
                    var paramNames = new List<string>();
                    for (int i = 0; i < extensions.Count; i++)
                    {
                        var pName = $"@catExt_{i}";
                        paramNames.Add(pName);
                        cmd.Parameters.AddWithValue(pName, extensions[i]);
                    }
                    sb.Append($" AND FileType IN ({string.Join(",", paramNames)}) ");
                }
            }

            if (minSizeBytes.HasValue && minSizeBytes.Value > 0)
            {
                sb.Append(" AND FileSize >= @minSize ");
                cmd.Parameters.AddWithValue("@minSize", minSizeBytes.Value);
            }

            if (maxSizeBytes.HasValue && maxSizeBytes.Value > 0)
            {
                sb.Append(" AND FileSize <= @maxSize ");
                cmd.Parameters.AddWithValue("@maxSize", maxSizeBytes.Value);
            }

            sb.Append(" ORDER BY FileName ASC ");
            if (limit > 0)
            {
                sb.Append($" LIMIT {limit} ");
            }

            cmd.CommandText = sb.ToString();

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                results.Add(new AssetItem
                {
                    Id = reader.GetInt64(0),
                    FileName = reader.GetString(1),
                    FilePath = reader.GetString(2),
                    InnerPath = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                    IsZipArchive = reader.GetInt32(4) == 1,
                    FileType = reader.GetString(5),
                    FileSize = reader.GetInt64(6),
                    DateModified = DateTime.TryParse(reader.GetString(7), out var dt) ? dt : DateTime.MinValue
                });
            }

            return results;
        }

        private static List<string> GetExtensionsForCategory(string category)
        {
            return category switch
            {
                "Images" => new List<string> { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".tga", ".psd" },
                "Audio" => new List<string> { ".wav", ".mp3", ".ogg", ".flac", ".aiff" },
                "3D Models" => new List<string> { ".fbx", ".obj", ".blend", ".dae" },
                "Fonts" => new List<string> { ".ttf", ".otf" },
                "Text / Data" => new List<string> { ".txt", ".json", ".csv", ".md", ".cs", ".shader", ".xml", ".yaml", ".ini" },
                "Unity Packages" => new List<string> { ".unitypackage" },
                _ => new List<string>()
            };
        }

        #endregion
    }
}
