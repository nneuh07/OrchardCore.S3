using OrchardCore.FileStorage.S3;

namespace OrchardCore.Media.S3
{
    public class MediaS3StorageOptions : S3Settings
    {
        public bool CreateContainer { get; set; }
    }
}