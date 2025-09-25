using System.Security.Cryptography;

namespace APIMonitorWorkerService.Utility
{
    public static class FileHelper
    {
        public static async Task<string> CalculateFileHashAsync(string filePath)
        {
            using var sha256 = SHA256.Create();
            using var stream = File.OpenRead(filePath);
            var hashBytes = await Task.Run(() => sha256.ComputeHash(stream));
            return Convert.ToHexString(hashBytes);
        }

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
