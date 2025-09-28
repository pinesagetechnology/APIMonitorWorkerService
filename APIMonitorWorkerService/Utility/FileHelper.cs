namespace APIMonitorWorkerService.Utility
{
    public static class FileHelper
    {
        public static FileType GetFileType(string fileName)
        {
            var extension = Path.GetExtension(fileName).ToLowerInvariant();
            return extension switch
            {
                ".json" => FileType.Json,
                ".jpg" or ".jpeg" or ".png" or ".bmp" or ".gif" => FileType.Image,
                _ => FileType.Other
            };
        }

    }
}
