using System;

namespace OrchardCore.FileStorage.S3
{
    public class S3File : IFileStoreEntry
    {
        private readonly long? _length;
        private readonly DateTimeOffset? _lastModified;

        public S3File(string path, long? length, DateTimeOffset? lastModified)
        {
            Path = path;
            Name = System.IO.Path.GetFileName(Path);

            DirectoryPath = Name == Path ? "" : Path.Substring(0, Path.Length - Name.Length - 1);

            _length = length;
            _lastModified = lastModified;
        }

        public string Path { get; }

        public string Name { get; }

        public string DirectoryPath { get; }

        public long Length => _length.GetValueOrDefault();

        public DateTime LastModifiedUtc => _lastModified.GetValueOrDefault().UtcDateTime;

        public bool IsDirectory => false;
    }
}