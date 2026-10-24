using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

namespace HumbleBundleAssets.Models
{
    public class AssetItem : INotifyPropertyChanged
    {
        private bool _isSelected;

        public long Id { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string InnerPath { get; set; } = string.Empty;
        public bool IsZipArchive { get; set; }
        public string FileType { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public DateTime DateModified { get; set; }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged();
                }
            }
        }

        public string Category => GetCategoryForExtension(FileType);

        public bool IsPreviewSupported => CheckPreviewSupport(FileType);

        public string FormattedFileSize
        {
            get
            {
                if (FileSize < 1024)
                    return $"{FileSize} B";
                if (FileSize < 1024 * 1024)
                    return $"{FileSize / 1024.0:F1} KB";
                if (FileSize < 1024 * 1024 * 1024)
                    return $"{FileSize / (1024.0 * 1024.0):F2} MB";
                return $"{FileSize / (1024.0 * 1024.0 * 1024.0):F2} GB";
            }
        }

        public string DisplayLocation
        {
            get
            {
                if (IsZipArchive)
                {
                    var zipName = Path.GetFileName(FilePath);
                    return string.IsNullOrEmpty(InnerPath) ? zipName : $"{zipName} > {InnerPath}";
                }
                return FilePath;
            }
        }

        public string FullContainerDirectory => Path.GetDirectoryName(FilePath) ?? string.Empty;

        public static string NormalizeExtension(string ext)
        {
            if (string.IsNullOrWhiteSpace(ext)) return string.Empty;
            ext = ext.Trim().ToLowerInvariant();
            if (!ext.StartsWith(".")) ext = "." + ext;
            return ext;
        }

        public static string GetCategoryForExtension(string ext)
        {
            ext = NormalizeExtension(ext);
            return ext switch
            {
                ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".tga" or ".psd" => "Images",
                ".wav" or ".mp3" or ".ogg" or ".flac" or ".aiff" => "Audio",
                ".fbx" or ".obj" or ".blend" or ".dae" => "3D Models",
                ".ttf" or ".otf" => "Fonts",
                ".txt" or ".json" or ".csv" or ".md" or ".cs" or ".shader" or ".xml" or ".yaml" or ".ini" => "Text / Data",
                ".unitypackage" => "Unity Packages",
                _ => "Other"
            };
        }

        public static bool CheckPreviewSupport(string ext)
        {
            ext = NormalizeExtension(ext);
            return ext switch
            {
                ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" => true,
                ".wav" or ".mp3" => true,
                ".txt" or ".json" or ".csv" or ".md" or ".cs" or ".shader" or ".xml" or ".yaml" or ".ini" => true,
                _ => false
            };
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
