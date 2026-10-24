namespace HumbleBundleAssets.Models
{
    public class CatalogProgress
    {
        public string CurrentFolder { get; set; } = string.Empty;
        public string CurrentFile { get; set; } = string.Empty;
        public int ProcessedFiles { get; set; }
        public int CatalogedAssets { get; set; }
        public bool IsIndeterminate { get; set; } = true;
        public double Percentage { get; set; }
        public string StatusMessage { get; set; } = string.Empty;
    }
}
