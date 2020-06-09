using System;
using System.Net;
using System.Threading.Tasks;
using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Util;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrchardCore.Environment.Shell;
using OrchardCore.Modules;

namespace OrchardCore.Media.S3
{
    public class CreateMediaBucketEvent : ModularTenantEvents
    {
        private readonly MediaS3StorageOptions _options;
        private readonly ShellSettings _shellSettings;
        private readonly ILogger<CreateMediaBucketEvent> _logger;

        public CreateMediaBucketEvent(
            IOptions<MediaS3StorageOptions> options,
            ShellSettings shellSettings,
            ILogger<CreateMediaBucketEvent> logger
        )
        {
            _options = options.Value;
            _shellSettings = shellSettings;
            _logger = logger;
        }

        public override async Task ActivatingAsync()
        {
            // Only create container if options are valid.

            if (_shellSettings.State != Environment.Shell.Models.TenantState.Uninitialized &&
                !string.IsNullOrEmpty(_options.S3HostEndpoint) &&
                !string.IsNullOrEmpty(_options.S3BucketName) &&
                !string.IsNullOrEmpty(_options.S3AccessKey) &&
                !string.IsNullOrEmpty(_options.S3SecretKey) &&
                _options.CreateContainer
            )
            {
                _logger.LogDebug("Testing S3 Bucket {BucketName} existence", _options.S3BucketName);
                var clientConfig = new AmazonS3Config
                {
                    RegionEndpoint = RegionEndpoint.GetBySystemName(_options.S3Region),
                    ServiceURL = _options.S3HostEndpoint,
                    ForcePathStyle = true,
                    UseHttp = true,
                };
                var s3Client = new AmazonS3Client(_options.S3AccessKey, _options.S3SecretKey, clientConfig);
                try
                {
                    if (!await AmazonS3Util.DoesS3BucketExistV2Async(s3Client, _options.S3BucketName))
                    {
                        await s3Client.PutBucketAsync(new PutBucketRequest
                        {
                            BucketName = _options.S3BucketName,
                            BucketRegionName = _options.S3Region
                        });
                        var bucketLocation = await s3Client.GetBucketLocationAsync(new GetBucketLocationRequest
                        {
                            BucketName = _options.S3BucketName
                        });
                        _logger.LogDebug("S3 CreateBucket Request {ContainerName} send.", _options.S3BucketName);
                        if (bucketLocation.HttpStatusCode != HttpStatusCode.OK)
                            _logger.LogCritical(
                                "S3 Bucket not found {ContainerName}, after CreateBucket request was sent.",
                                _options.S3BucketName);
                        if (bucketLocation.Location != _options.S3Region)
                            _logger.LogCritical(
                                "S3 Bucket not found in specific Region {Region}, after CreateBucket request was sent.",
                                _options.S3Region);

                        var newPolicy = @"{ 
                        ""Version"": ""2012-10-17"",
                        ""Statement"":[{
                        ""Action"":[""s3:GetObject""], 
                        ""Effect"":""Allow"",
                        ""Principal"": {""AWS"": [""*""]},
                        ""Resource"":[""arn:aws:s3:::" + _options.S3BucketName + @"/*""],
                        ""Sid"":""""
                    }]}";
                        await s3Client.PutBucketPolicyAsync(new PutBucketPolicyRequest
                        {
                            BucketName = _options.S3BucketName,
                            Policy = newPolicy
                        });

                        var existingPolicy = await s3Client.GetBucketPolicyAsync(_options.S3BucketName);
                        if (existingPolicy.Policy != newPolicy)
                            _logger.LogCritical(
                                "S3 Bucket has this policy {ExistingPolicy}, instead of the expected Policy : {ExpectedPolicy}.",
                                newPolicy, existingPolicy.Policy);
                    }

                    _logger.LogDebug("S3 Bucket {ContainerName} created.", _options.S3BucketName);
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "Unable to create Azure Media Storage Container.");
                }
            }
        }
    }
}