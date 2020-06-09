using System;

namespace OrchardCore.FileStorage.S3
{
    public class S3Directory : IFileStoreEntry
    {
        public S3Directory(string path, DateTime lastModifiedUtc)
        {
            Path = path;
            LastModifiedUtc = lastModifiedUtc;
            // Use GetFileName rather than GetDirectoryName as GetDirectoryName requires a delimiter
            Name = System.IO.Path.GetFileName(path);
            DirectoryPath = Path.Length > Name.Length ? Path.Substring(0, Path.Length - Name.Length - 1) : "";
        }

        public string Path { get; }

        public string Name { get; }

        public string DirectoryPath { get; }

        public long Length => 0;

        public DateTime LastModifiedUtc { get; }

        public bool IsDirectory => true;
    }
}