using System;
using Amazon;
using Fluid;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrchardCore.Environment.Shell;
using OrchardCore.Environment.Shell.Configuration;

namespace OrchardCore.Media.S3
{
    public class MediaS3StorageOptionsConfiguration : IConfigureOptions<MediaS3StorageOptions>
    {
        private readonly IShellConfiguration _shellConfiguration;
        private readonly ShellSettings _shellSettings;
        private readonly ILogger<MediaS3StorageOptions> _logger;

        public MediaS3StorageOptionsConfiguration(
            IShellConfiguration shellConfiguration,
            ShellSettings shellSettings,
            ILogger<MediaS3StorageOptions> logger
        )
        {
            _shellConfiguration = shellConfiguration;
            _shellSettings = shellSettings;
            _logger = logger;
        }
        
        public void Configure(MediaS3StorageOptions options)
        {
            var section = _shellConfiguration.GetSection("OrchardCore_Media_S3");
            
            options.S3BucketName = section.GetValue(nameof(options.S3BucketName), "orchardcoremedia");
            options.S3BasePath = section.GetValue(nameof(options.S3BasePath), string.Empty);
            options.S3HostEndpoint = section.GetValue(nameof(options.S3HostEndpoint), string.Empty);
            options.S3Region = section.GetValue(nameof(options.S3Region), RegionEndpoint.EUCentral1.SystemName);
            options.S3AccessKey = section.GetValue(nameof(options.S3AccessKey), string.Empty);
            options.S3SecretKey = section.GetValue(nameof(options.S3SecretKey), string.Empty);
            options.CreateContainer = section.GetValue(nameof(options.CreateContainer), true);

            var templateContext = new TemplateContext();
            templateContext.MemberAccessStrategy.Register<ShellSettings>();
            templateContext.MemberAccessStrategy.Register<MediaS3StorageOptions>();
            templateContext.SetValue("ShellSettings", _shellSettings);

            ParseContainerName(options, templateContext);
            ParseBasePath(options, templateContext);        
        }
        
        private void ParseContainerName(MediaS3StorageOptions options, TemplateContext templateContext)
        {
            // Use Fluid directly as this is transient and cannot invoke _liquidTemplateManager.
            try
            {
                var template = FluidTemplate.Parse(options.S3BucketName);

                // container name must be lowercase
                options.S3BucketName = template.Render(templateContext, NullEncoder.Default).ToLower();
                options.S3BucketName = options.S3BucketName.Replace("\r", string.Empty).Replace("\n", string.Empty);
            }
            catch (Exception e)
            {
                _logger.LogCritical(e, "Unable to parse S3 Bucket name.");
                throw;
            }
        }

        private void ParseBasePath(MediaS3StorageOptions options, TemplateContext templateContext)
        {
            try
            {
                var template = FluidTemplate.Parse(options.S3BasePath);

                options.S3BasePath = template.Render(templateContext, NullEncoder.Default);
                options.S3BasePath = options.S3BasePath.Replace("\r", String.Empty).Replace("\n", String.Empty);
            }
            catch (Exception e)
            {
                _logger.LogCritical(e, "Unable to parse Azure Media Storage base path.");
                throw;
            }
        }
    }
}