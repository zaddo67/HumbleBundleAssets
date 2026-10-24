using System;

namespace HumbleBundleAssets.Models
{
    public class FolderItem
    {
        public long Id { get; set; }
        public string FolderPath { get; set; } = string.Empty;
        public DateTime DateAdded { get; set; } = DateTime.Now;
        public int FileCount { get; set; }
    }
}
