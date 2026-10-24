using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using HumbleBundleAssets.Models;
using HumbleBundleAssets.Services;

namespace HumbleBundleAssets.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly DatabaseManager _dbManager;
        private readonly CatalogService _catalogService;
        private readonly ExtractionService _extractionService;
        private readonly PreviewService _previewService;

        private CancellationTokenSource? _catalogCts;
        private CancellationTokenSource? _extractionCts;
        private readonly MediaPlayer _mediaPlayer = new();
        private readonly DispatcherTimer _audioTimer = new();

        public MainViewModel()
        {
            _dbManager = new DatabaseManager();
            _catalogService = new CatalogService(_dbManager);
            _extractionService = new ExtractionService();
            _previewService = new PreviewService();

            InitCommands();
            InitAudioTimer();

            LoadDatabaseState();
        }

        #region Navigation State

        private int _selectedTabIndex = 0; // 0 = Setup, 1 = Search
        public int SelectedTabIndex
        {
            get => _selectedTabIndex;
            set
            {
                if (_selectedTabIndex != value)
                {
                    _selectedTabIndex = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsSetupTabSelected));
                    OnPropertyChanged(nameof(IsSearchTabSelected));
                }
            }
        }

        public bool IsSetupTabSelected
        {
            get => SelectedTabIndex == 0;
            set
            {
                if (value && SelectedTabIndex != 0)
                {
                    SelectedTabIndex = 0;
                }
            }
        }

        public bool IsSearchTabSelected
        {
            get => SelectedTabIndex == 1;
            set
            {
                if (value && SelectedTabIndex != 1)
                {
                    SelectedTabIndex = 1;
                }
            }
        }

        #endregion

        #region Database Header State

        public string ActiveDatabasePath => _dbManager.CurrentDbPath;
        public string ActiveDatabaseName => Path.GetFileName(_dbManager.CurrentDbPath);

        private int _totalCatalogedAssets;
        public int TotalCatalogedAssets
        {
            get => _totalCatalogedAssets;
            set { _totalCatalogedAssets = value; OnPropertyChanged(); }
        }

        #endregion

        #region Setup Tab State

        public ObservableCollection<FolderItem> IndexedFolders { get; } = new();

        private FolderItem? _selectedFolder;
        public FolderItem? SelectedFolder
        {
            get => _selectedFolder;
            set { _selectedFolder = value; OnPropertyChanged(); }
        }

        private bool _isCataloging;
        public bool IsCataloging
        {
            get => _isCataloging;
            set
            {
                if (_isCataloging != value)
                {
                    _isCataloging = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(CanStartCatalog));
                }
            }
        }

        public bool CanStartCatalog => !_isCataloging && IndexedFolders.Count > 0;

        #endregion

        #region Search Tab State

        private string _searchKeyword = string.Empty;
        public string SearchKeyword
        {
            get => _searchKeyword;
            set { _searchKeyword = value; OnPropertyChanged(); }
        }

        public ObservableCollection<string> Categories { get; } = new()
        {
            "All Categories",
            "Images",
            "Audio",
            "3D Models",
            "Fonts",
            "Text / Data",
            "Unity Packages"
        };

        private string _selectedCategory = "All Categories";
        public string SelectedCategory
        {
            get => _selectedCategory;
            set
            {
                if (_selectedCategory != value)
                {
                    _selectedCategory = value;
                    OnPropertyChanged();
                    UpdateFileTypesForCategory();
                }
            }
        }

        public ObservableCollection<string> FileTypes { get; } = new() { "All" };

        private string _selectedFileType = "All";
        public string SelectedFileType
        {
            get => _selectedFileType;
            set { _selectedFileType = value; OnPropertyChanged(); }
        }

        private string _minSizeText = string.Empty;
        public string MinSizeText
        {
            get => _minSizeText;
            set { _minSizeText = value; OnPropertyChanged(); }
        }

        public ObservableCollection<string> SizeUnits { get; } = new() { "KB", "MB" };

        private string _minSizeUnit = "KB";
        public string MinSizeUnit
        {
            get => _minSizeUnit;
            set { _minSizeUnit = value; OnPropertyChanged(); }
        }

        private string _maxSizeText = string.Empty;
        public string MaxSizeText
        {
            get => _maxSizeText;
            set { _maxSizeText = value; OnPropertyChanged(); }
        }

        private string _maxSizeUnit = "MB";
        public string MaxSizeUnit
        {
            get => _maxSizeUnit;
            set { _maxSizeUnit = value; OnPropertyChanged(); }
        }

        public ObservableCollection<AssetItem> SearchResults { get; } = new();

        private AssetItem? _selectedAsset;
        public AssetItem? SelectedAsset
        {
            get => _selectedAsset;
            set
            {
                if (_selectedAsset != value)
                {
                    _selectedAsset = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(HasSelectedAsset));
                    TriggerPreviewForAsset(value);
                }
            }
        }

        public bool HasSelectedAsset => SelectedAsset != null;

        private int _selectedAssetsCount;
        public int SelectedAssetsCount
        {
            get => _selectedAssetsCount;
            set { _selectedAssetsCount = value; OnPropertyChanged(); }
        }

        #endregion

        #region Preview State

        private bool _isPreviewLoading;
        public bool IsPreviewLoading
        {
            get => _isPreviewLoading;
            set { _isPreviewLoading = value; OnPropertyChanged(); }
        }

        private bool _isImagePreview;
        public bool IsImagePreview
        {
            get => _isImagePreview;
            set { _isImagePreview = value; OnPropertyChanged(); }
        }

        private ImageSource? _previewImageSource;
        public ImageSource? PreviewImageSource
        {
            get => _previewImageSource;
            set { _previewImageSource = value; OnPropertyChanged(); }
        }

        private bool _isAudioPreview;
        public bool IsAudioPreview
        {
            get => _isAudioPreview;
            set { _isAudioPreview = value; OnPropertyChanged(); }
        }

        private bool _isAudioPlaying;
        public bool IsAudioPlaying
        {
            get => _isAudioPlaying;
            set { _isAudioPlaying = value; OnPropertyChanged(); }
        }

        private double _audioPositionSeconds;
        public double AudioPositionSeconds
        {
            get => _audioPositionSeconds;
            set
            {
                if (Math.Abs(_audioPositionSeconds - value) > 0.05)
                {
                    _audioPositionSeconds = value;
                    OnPropertyChanged();
                }
            }
        }

        private double _audioDurationSeconds = 1;
        public double AudioDurationSeconds
        {
            get => _audioDurationSeconds;
            set { _audioDurationSeconds = value; OnPropertyChanged(); }
        }

        private string _audioTimeDisplay = "00:00 / 00:00";
        public string AudioTimeDisplay
        {
            get => _audioTimeDisplay;
            set { _audioTimeDisplay = value; OnPropertyChanged(); }
        }

        private bool _isTextPreview;
        public bool IsTextPreview
        {
            get => _isTextPreview;
            set { _isTextPreview = value; OnPropertyChanged(); }
        }

        private string _textPreviewContent = string.Empty;
        public string TextPreviewContent
        {
            get => _textPreviewContent;
            set { _textPreviewContent = value; OnPropertyChanged(); }
        }

        private bool _isNoPreview;
        public bool IsNoPreview
        {
            get => _isNoPreview;
            set { _isNoPreview = value; OnPropertyChanged(); }
        }

        private string _noPreviewMessage = "Select an item to view its details and preview.";
        public string NoPreviewMessage
        {
            get => _noPreviewMessage;
            set { _noPreviewMessage = value; OnPropertyChanged(); }
        }

        #endregion

        #region Footer & Progress State

        private string _statusMessage = "Ready";
        public string StatusMessage
        {
            get => _statusMessage;
            set { _statusMessage = value; OnPropertyChanged(); }
        }

        private bool _isProgressIndeterminate;
        public bool IsProgressIndeterminate
        {
            get => _isProgressIndeterminate;
            set { _isProgressIndeterminate = value; OnPropertyChanged(); }
        }

        private double _progressValue;
        public double ProgressValue
        {
            get => _progressValue;
            set { _progressValue = value; OnPropertyChanged(); }
        }

        #endregion

        #region Commands

        public RelayCommand NavigateToSetupCommand { get; private set; } = null!;
        public RelayCommand NavigateToSearchCommand { get; private set; } = null!;
        public RelayCommand OpenDatabaseCommand { get; private set; } = null!;
        public RelayCommand NewDatabaseCommand { get; private set; } = null!;
        public RelayCommand AddFolderCommand { get; private set; } = null!;
        public RelayCommand RemoveFolderCommand { get; private set; } = null!;
        public RelayCommand StartCatalogCommand { get; private set; } = null!;
        public RelayCommand CancelCatalogCommand { get; private set; } = null!;
        public RelayCommand SearchCommand { get; private set; } = null!;
        public RelayCommand ResetFiltersCommand { get; private set; } = null!;
        public RelayCommand SelectAllCommand { get; private set; } = null!;
        public RelayCommand ClearSelectionCommand { get; private set; } = null!;
        public RelayCommand ExtractSelectedCommand { get; private set; } = null!;
        public RelayCommand ExtractCurrentItemCommand { get; private set; } = null!;
        public RelayCommand OpenExplorerCommand { get; private set; } = null!;
        public RelayCommand PlayAudioCommand { get; private set; } = null!;
        public RelayCommand PauseAudioCommand { get; private set; } = null!;
        public RelayCommand StopAudioCommand { get; private set; } = null!;

        private void InitCommands()
        {
            NavigateToSetupCommand = new RelayCommand(() => SelectedTabIndex = 0);
            NavigateToSearchCommand = new RelayCommand(() => SelectedTabIndex = 1);

            OpenDatabaseCommand = new RelayCommand(ExecuteOpenDatabase);
            NewDatabaseCommand = new RelayCommand(ExecuteNewDatabase);

            AddFolderCommand = new RelayCommand(ExecuteAddFolder);
            RemoveFolderCommand = new RelayCommand(ExecuteRemoveFolder, () => SelectedFolder != null);
            StartCatalogCommand = new RelayCommand(ExecuteStartCatalog, () => CanStartCatalog);
            CancelCatalogCommand = new RelayCommand(ExecuteCancelCatalog, () => IsCataloging);

            SearchCommand = new RelayCommand(ExecuteSearch);
            ResetFiltersCommand = new RelayCommand(ExecuteResetFilters);

            SelectAllCommand = new RelayCommand(() => SetAllSelection(true));
            ClearSelectionCommand = new RelayCommand(() => SetAllSelection(false));
            ExtractSelectedCommand = new RelayCommand(ExecuteExtractSelected, () => SelectedAssetsCount > 0);
            ExtractCurrentItemCommand = new RelayCommand(ExecuteExtractCurrentItem, () => SelectedAsset != null);
            OpenExplorerCommand = new RelayCommand(ExecuteOpenExplorer, () => SelectedAsset != null);

            PlayAudioCommand = new RelayCommand(ExecutePlayAudio);
            PauseAudioCommand = new RelayCommand(ExecutePauseAudio);
            StopAudioCommand = new RelayCommand(ExecuteStopAudio);
        }

        #endregion

        #region Database Loading & Switching

        private void LoadDatabaseState()
        {
            OnPropertyChanged(nameof(ActiveDatabasePath));
            OnPropertyChanged(nameof(ActiveDatabaseName));

            IndexedFolders.Clear();
            var folders = _dbManager.GetIndexedFolders();
            foreach (var f in folders)
            {
                IndexedFolders.Add(f);
            }

            TotalCatalogedAssets = _dbManager.GetTotalAssetCount();
            StatusMessage = $"Ready. Active Database: {ActiveDatabaseName} ({TotalCatalogedAssets:N0} assets).";

            UpdateFileTypesForCategory();
            ExecuteSearch();
        }

        private void ExecuteOpenDatabase()
        {
            var dialog = new OpenFileDialog
            {
                Title = "Open SQLite Assets Database",
                Filter = "SQLite Database (*.db;*.sqlite)|*.db;*.sqlite|All Files (*.*)|*.*",
                InitialDirectory = Path.GetDirectoryName(_dbManager.CurrentDbPath)
            };

            if (dialog.ShowDialog() == true)
            {
                _dbManager.SwitchDatabase(dialog.FileName);
                LoadDatabaseState();
            }
        }

        private void ExecuteNewDatabase()
        {
            var dialog = new SaveFileDialog
            {
                Title = "Create New SQLite Assets Database",
                Filter = "SQLite Database (*.db)|*.db",
                FileName = "new_assets_catalog.db",
                InitialDirectory = Path.GetDirectoryName(_dbManager.CurrentDbPath)
            };

            if (dialog.ShowDialog() == true)
            {
                _dbManager.SwitchDatabase(dialog.FileName);
                LoadDatabaseState();
            }
        }

        #endregion

        #region Setup Actions

        private void ExecuteAddFolder()
        {
            var dialog = new OpenFolderDialog
            {
                Title = "Select Folder to Index",
                Multiselect = false
            };

            if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.FolderName))
            {
                var folderPath = dialog.FolderName;
                if (_dbManager.AddIndexedFolder(folderPath))
                {
                    IndexedFolders.Clear();
                    foreach (var f in _dbManager.GetIndexedFolders())
                    {
                        IndexedFolders.Add(f);
                    }
                    OnPropertyChanged(nameof(CanStartCatalog));
                    StatusMessage = $"Added folder: {folderPath}";
                }
                else
                {
                    StatusMessage = $"Folder is already in the list: {folderPath}";
                }
            }
        }

        private void ExecuteRemoveFolder()
        {
            if (SelectedFolder == null) return;
            var path = SelectedFolder.FolderPath;
            if (_dbManager.RemoveIndexedFolder(path))
            {
                IndexedFolders.Remove(SelectedFolder);
                SelectedFolder = null;
                TotalCatalogedAssets = _dbManager.GetTotalAssetCount();
                OnPropertyChanged(nameof(CanStartCatalog));
                StatusMessage = $"Removed folder and associated assets: {path}";
                ExecuteSearch();
            }
        }

        private async void ExecuteStartCatalog()
        {
            if (IndexedFolders.Count == 0 || IsCataloging) return;

            IsCataloging = true;
            IsProgressIndeterminate = true;
            _catalogCts = new CancellationTokenSource();

            var foldersToScan = IndexedFolders.Select(f => f.FolderPath).ToList();
            var progress = new Progress<CatalogProgress>(p =>
            {
                StatusMessage = string.IsNullOrEmpty(p.CurrentFile)
                    ? p.StatusMessage
                    : $"Cataloging: {p.CurrentFile} ({p.CatalogedAssets:N0} assets found)";
            });

            try
            {
                StatusMessage = "Cataloging started on background thread...";
                await _catalogService.CatalogFoldersAsync(foldersToScan, progress, _catalogCts.Token);
            }
            catch (Exception ex)
            {
                StatusMessage = $"Cataloging error: {ex.Message}";
            }
            finally
            {
                IsCataloging = false;
                IsProgressIndeterminate = false;
                _catalogCts?.Dispose();
                _catalogCts = null;

                TotalCatalogedAssets = _dbManager.GetTotalAssetCount();
                ExecuteSearch();
            }
        }

        private void ExecuteCancelCatalog()
        {
            if (_catalogCts != null && !_catalogCts.IsCancellationRequested)
            {
                _catalogCts.Cancel();
                StatusMessage = "Cancelling cataloging process...";
            }
        }

        #endregion

        #region Search & Selection

        private void UpdateFileTypesForCategory()
        {
            var currentType = SelectedFileType;
            FileTypes.Clear();
            FileTypes.Add("All");

            IEnumerable<string> extensions = SelectedCategory switch
            {
                "Images" => new[] { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".tga", ".psd" },
                "Audio" => new[] { ".wav", ".mp3", ".ogg", ".flac", ".aiff" },
                "3D Models" => new[] { ".fbx", ".obj", ".blend", ".dae" },
                "Fonts" => new[] { ".ttf", ".otf" },
                "Text / Data" => new[] { ".txt", ".json", ".csv", ".md", ".cs", ".shader", ".xml", ".yaml", ".ini" },
                "Unity Packages" => new[] { ".unitypackage" },
                _ => CatalogService.SupportedExtensions.OrderBy(x => x)
            };

            foreach (var ext in extensions)
            {
                FileTypes.Add(ext);
            }

            if (FileTypes.Contains(currentType))
            {
                SelectedFileType = currentType;
            }
            else
            {
                SelectedFileType = "All";
            }
        }

        private void ExecuteSearch()
        {
            long? minSizeBytes = null;
            if (double.TryParse(MinSizeText, out var minVal) && minVal > 0)
            {
                minSizeBytes = (long)(minVal * (MinSizeUnit == "MB" ? 1024 * 1024 : 1024));
            }

            long? maxSizeBytes = null;
            if (double.TryParse(MaxSizeText, out var maxVal) && maxVal > 0)
            {
                maxSizeBytes = (long)(maxVal * (MaxSizeUnit == "MB" ? 1024 * 1024 : 1024));
            }

            var results = _dbManager.SearchAssets(
                SearchKeyword,
                SelectedCategory,
                SelectedFileType,
                minSizeBytes,
                maxSizeBytes);

            SearchResults.Clear();
            foreach (var item in results)
            {
                item.PropertyChanged += Item_PropertyChanged;
                SearchResults.Add(item);
            }

            UpdateSelectionCount();
            StatusMessage = $"Search returned {SearchResults.Count:N0} assets.";
        }

        private void ExecuteResetFilters()
        {
            SearchKeyword = string.Empty;
            SelectedCategory = "All Categories";
            SelectedFileType = "All";
            MinSizeText = string.Empty;
            MaxSizeText = string.Empty;
            ExecuteSearch();
        }

        private void Item_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(AssetItem.IsSelected))
            {
                UpdateSelectionCount();
            }
        }

        private void SetAllSelection(bool isSelected)
        {
            foreach (var item in SearchResults)
            {
                item.IsSelected = isSelected;
            }
            UpdateSelectionCount();
        }

        private void UpdateSelectionCount()
        {
            SelectedAssetsCount = SearchResults.Count(x => x.IsSelected);
            ExtractSelectedCommand.RaiseCanExecuteChanged();
        }

        #endregion

        #region Extraction

        private async void ExecuteExtractSelected()
        {
            var selectedItems = SearchResults.Where(x => x.IsSelected).ToList();
            if (selectedItems.Count == 0) return;

            await ExtractItemsInternal(selectedItems);
        }

        private async void ExecuteExtractCurrentItem()
        {
            if (SelectedAsset == null) return;
            await ExtractItemsInternal(new List<AssetItem> { SelectedAsset });
        }

        private async Task ExtractItemsInternal(List<AssetItem> items)
        {
            // Retrieve last saved folder from database settings
            var defaultFolder = _dbManager.GetSetting("LastExtractFolder");
            if (string.IsNullOrEmpty(defaultFolder) || !Directory.Exists(defaultFolder))
            {
                defaultFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            }

            var dialog = new OpenFolderDialog
            {
                Title = $"Select Destination Folder for {items.Count} Asset(s)",
                InitialDirectory = defaultFolder
            };

            if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.FolderName))
            {
                var destination = dialog.FolderName;
                // Save chosen destination to database settings
                _dbManager.SetSetting("LastExtractFolder", destination);

                IsProgressIndeterminate = true;
                StatusMessage = $"Extracting {items.Count} items to {destination}...";

                var progress = new Progress<string>(msg => StatusMessage = msg);
                _extractionCts = new CancellationTokenSource();

                try
                {
                    var count = await _extractionService.ExtractAssetsAsync(items, destination, progress, _extractionCts.Token);
                    StatusMessage = $"Successfully extracted {count} items to: {destination}";
                }
                catch (Exception ex)
                {
                    StatusMessage = $"Extraction failed: {ex.Message}";
                }
                finally
                {
                    IsProgressIndeterminate = false;
                    _extractionCts?.Dispose();
                    _extractionCts = null;
                }
            }
        }

        private void ExecuteOpenExplorer()
        {
            if (SelectedAsset == null) return;
            try
            {
                var target = SelectedAsset.FilePath;
                if (File.Exists(target))
                {
                    Process.Start("explorer.exe", $"/select,\"{target}\"");
                }
                else if (Directory.Exists(SelectedAsset.FullContainerDirectory))
                {
                    Process.Start("explorer.exe", $"\"{SelectedAsset.FullContainerDirectory}\"");
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Could not open Explorer: {ex.Message}";
            }
        }

        #endregion

        #region Preview Logic

        private async void TriggerPreviewForAsset(AssetItem? asset)
        {
            ResetPreviewState();

            if (asset == null)
            {
                IsNoPreview = true;
                NoPreviewMessage = "No item selected.";
                return;
            }

            if (!asset.IsPreviewSupported)
            {
                IsNoPreview = true;
                NoPreviewMessage = $"Preview is not supported for {asset.FileType} files.\nUse 'Extract Item' or 'Open in Explorer' to work with this asset in Unity.";
                return;
            }

            IsPreviewLoading = true;
            try
            {
                var previewFilePath = await _previewService.PreparePreviewFileAsync(asset);
                if (string.IsNullOrEmpty(previewFilePath) || !File.Exists(previewFilePath))
                {
                    IsNoPreview = true;
                    NoPreviewMessage = "Could not extract or locate file for preview.";
                    return;
                }

                var ext = asset.FileType.ToLowerInvariant();

                // Image Preview
                if (ext is ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp")
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.UriSource = new Uri(previewFilePath, UriKind.Absolute);
                    bmp.EndInit();
                    bmp.Freeze();

                    PreviewImageSource = bmp;
                    IsImagePreview = true;
                }
                // Audio Preview
                else if (ext is ".wav" or ".mp3")
                {
                    _mediaPlayer.Open(new Uri(previewFilePath, UriKind.Absolute));
                    IsAudioPreview = true;
                    AudioTimeDisplay = "00:00 / --:--";
                }
                // Text Preview
                else if (ext is ".txt" or ".json" or ".csv" or ".md" or ".cs" or ".shader" or ".xml" or ".yaml" or ".ini")
                {
                    TextPreviewContent = await _previewService.LoadTextPreviewAsync(previewFilePath);
                    IsTextPreview = true;
                }
                else
                {
                    IsNoPreview = true;
                    NoPreviewMessage = $"No preview available for {asset.FileType}.";
                }
            }
            catch (Exception ex)
            {
                IsNoPreview = true;
                NoPreviewMessage = $"Error generating preview: {ex.Message}";
            }
            finally
            {
                IsPreviewLoading = false;
            }
        }

        private void ResetPreviewState()
        {
            ExecuteStopAudio();
            PreviewImageSource = null;
            TextPreviewContent = string.Empty;
            IsImagePreview = false;
            IsAudioPreview = false;
            IsTextPreview = false;
            IsNoPreview = false;
        }

        private void InitAudioTimer()
        {
            _audioTimer.Interval = TimeSpan.FromMilliseconds(250);
            _audioTimer.Tick += (s, e) =>
            {
                if (_mediaPlayer.NaturalDuration.HasTimeSpan)
                {
                    var dur = _mediaPlayer.NaturalDuration.TimeSpan;
                    var pos = _mediaPlayer.Position;
                    AudioDurationSeconds = dur.TotalSeconds > 0 ? dur.TotalSeconds : 1;
                    AudioPositionSeconds = pos.TotalSeconds;
                    AudioTimeDisplay = $"{pos:mm\\:ss} / {dur:mm\\:ss}";

                    if (pos >= dur)
                    {
                        ExecuteStopAudio();
                    }
                }
            };

            _mediaPlayer.MediaOpened += (s, e) =>
            {
                if (_mediaPlayer.NaturalDuration.HasTimeSpan)
                {
                    var dur = _mediaPlayer.NaturalDuration.TimeSpan;
                    AudioDurationSeconds = dur.TotalSeconds > 0 ? dur.TotalSeconds : 1;
                    AudioTimeDisplay = $"00:00 / {dur:mm\\:ss}";
                }
            };
        }

        private void ExecutePlayAudio()
        {
            if (IsAudioPreview)
            {
                _mediaPlayer.Play();
                _audioTimer.Start();
                IsAudioPlaying = true;
            }
        }

        private void ExecutePauseAudio()
        {
            if (IsAudioPreview)
            {
                _mediaPlayer.Pause();
                _audioTimer.Stop();
                IsAudioPlaying = false;
            }
        }

        private void ExecuteStopAudio()
        {
            try
            {
                _mediaPlayer.Stop();
            }
            catch { }
            _audioTimer.Stop();
            IsAudioPlaying = false;
            AudioPositionSeconds = 0;
            if (_mediaPlayer.NaturalDuration.HasTimeSpan)
            {
                AudioTimeDisplay = $"00:00 / {_mediaPlayer.NaturalDuration.TimeSpan:mm\\:ss}";
            }
        }

        public void SeekAudio(double seconds)
        {
            if (IsAudioPreview)
            {
                _mediaPlayer.Position = TimeSpan.FromSeconds(seconds);
            }
        }

        #endregion

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion
    }
}
