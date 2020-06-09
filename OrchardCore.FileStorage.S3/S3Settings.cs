namespace OrchardCore.FileStorage.S3
{
    public class S3Settings
    {
        public string S3SecretKey { get; set; } = null!;
        public string S3AccessKey { get; set; } = null!;
        public string S3HostEndpoint { get; set; } = null!;
        public string S3BucketName { get; set; } = null!;
        public string S3BasePath { get; set; } = null!;
        public string S3Region { get; set; } = null!;
    }
}