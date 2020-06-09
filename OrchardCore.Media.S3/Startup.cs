using System;
using System.IO;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrchardCore.Environment.Shell;
using OrchardCore.Environment.Shell.Configuration;
using OrchardCore.FileStorage;
using OrchardCore.FileStorage.S3;
using OrchardCore.Media.Core;
using OrchardCore.Media.Core.Events;
using OrchardCore.Media.Events;
using OrchardCore.Modules;
using StartupBase = OrchardCore.Modules.StartupBase;

namespace OrchardCore.Media.S3
{    
    [Feature("OrchardCore.Media.S3.Storage")]
    public class Startup : StartupBase
    {
        private readonly ILogger _logger;
        private readonly IShellConfiguration _configuration;

        public Startup(ILogger<Startup> logger, IShellConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
        }

        public override int Order => 10;

        public override void ConfigureServices(IServiceCollection services)
        {
            services.AddTransient<IConfigureOptions<MediaS3StorageOptions>, MediaS3StorageOptionsConfiguration>();

            // Only replace default implementation if options are valid.
            var connectionString =
                _configuration[$"OrchardCore_Media_S3:{nameof(MediaS3StorageOptions.S3HostEndpoint)}"];
            var containerName =
                _configuration[$"OrchardCore_Media_S3:{nameof(MediaS3StorageOptions.S3BucketName)}"];
            var accessKey =
                _configuration[$"OrchardCore_Media_S3:{nameof(MediaS3StorageOptions.S3AccessKey)}"];
            var secretKey =
                _configuration[$"OrchardCore_Media_S3:{nameof(MediaS3StorageOptions.S3SecretKey)}"];

            if (!CheckOptions(connectionString, containerName, accessKey, secretKey, _logger)) return;
            // Register a media cache file provider.
            services.AddSingleton<IMediaFileStoreCacheFileProvider>(serviceProvider =>
            {
                var hostingEnvironment = serviceProvider.GetRequiredService<IWebHostEnvironment>();

                if (string.IsNullOrWhiteSpace(hostingEnvironment.WebRootPath))
                {
                    throw new Exception("The wwwroot folder for serving cache media files is missing.");
                }

                var mediaOptions = serviceProvider.GetRequiredService<IOptions<MediaOptions>>().Value;
                var shellOptions = serviceProvider.GetRequiredService<IOptions<ShellOptions>>();
                var shellSettings = serviceProvider.GetRequiredService<ShellSettings>();
                var logger = serviceProvider.GetRequiredService<ILogger<DefaultMediaFileStoreCacheFileProvider>>();

                var mediaCachePath = GetMediaCachePath(hostingEnvironment,
                    DefaultMediaFileStoreCacheFileProvider.AssetsCachePath, shellSettings);

                if (!Directory.Exists(mediaCachePath))
                {
                    Directory.CreateDirectory(mediaCachePath);
                }

                return new DefaultMediaFileStoreCacheFileProvider(logger, mediaOptions.AssetsRequestPath,
                    mediaCachePath);
            });

            // Replace the default media file provider with the media cache file provider.
            services.Replace(ServiceDescriptor.Singleton<IMediaFileProvider>(serviceProvider =>
                serviceProvider.GetRequiredService<IMediaFileStoreCacheFileProvider>()));

            // Register the media cache file provider as a file store cache provider.
            services.AddSingleton<IMediaFileStoreCache>(serviceProvider =>
                serviceProvider.GetRequiredService<IMediaFileStoreCacheFileProvider>());

            // Replace the default media file store with a blob file store.
            services.Replace(ServiceDescriptor.Singleton<IMediaFileStore>(serviceProvider =>
            {
                var mediaS3StorageOptions =
                    serviceProvider.GetRequiredService<IOptions<MediaS3StorageOptions>>().Value;
                var shellOptions = serviceProvider.GetRequiredService<IOptions<ShellOptions>>();
                var shellSettings = serviceProvider.GetRequiredService<ShellSettings>();
                var mediaOptions = serviceProvider.GetRequiredService<IOptions<MediaOptions>>().Value;
                var clock = serviceProvider.GetRequiredService<IClock>();
                var contentTypeProvider = serviceProvider.GetRequiredService<IContentTypeProvider>();
                var mediaEventHandlers = serviceProvider.GetServices<IMediaEventHandler>();
                var mediaCreatingEventHandlers = serviceProvider.GetServices<IMediaCreatingEventHandler>();
                var logger = serviceProvider.GetRequiredService<ILogger<DefaultMediaFileStore>>();

                var fileStore = new S3FileStore(mediaS3StorageOptions, clock, contentTypeProvider);

                var mediaPath = GetMediaPath(shellOptions.Value, shellSettings, mediaOptions.AssetsPath);

                var mediaUrlBase =
                    "/" + fileStore.Combine(shellSettings.RequestUrlPrefix, mediaOptions.AssetsRequestPath);

                var originalPathBase = serviceProvider.GetRequiredService<IHttpContextAccessor>()
                                           .HttpContext?.Features.Get<ShellContextFeature>()?.OriginalPathBase ??
                                       null;

                if (originalPathBase.HasValue)
                {
                    mediaUrlBase = fileStore.Combine(originalPathBase.Value, mediaUrlBase);
                }

                return new DefaultMediaFileStore(fileStore, mediaUrlBase, mediaOptions.CdnBaseUrl,
                    mediaEventHandlers, mediaCreatingEventHandlers, logger);
            }));

            services.AddSingleton<IMediaEventHandler, DefaultMediaFileStoreCacheEventHandler>();

            services.AddScoped<IModularTenantEvents, CreateMediaBucketEvent>();
        }

        private string GetMediaPath(ShellOptions shellOptions, ShellSettings shellSettings, string assetsPath)
        {
            return PathExtensions.Combine(shellOptions.ShellsApplicationDataPath, shellOptions.ShellsContainerName,
                shellSettings.Name, assetsPath);
        }

        private string GetMediaCachePath(IWebHostEnvironment hostingEnvironment, string assetsPath,
            ShellSettings shellSettings)
        {
            return PathExtensions.Combine(hostingEnvironment.WebRootPath, assetsPath, shellSettings.Name);
        }

        private static bool CheckOptions(string hostEndpoint, string bucketName, string accessKey, string secretKey,
            ILogger logger)
        {
            var optionsAreValid = true;

            if (string.IsNullOrWhiteSpace(hostEndpoint))
            {
                logger.LogError(
                    "S3 Media is enabled but not active because the 'HostEndpoint' is missing or empty in application configuration.");
                optionsAreValid = false;
            }

            if (string.IsNullOrWhiteSpace(bucketName))
            {
                logger.LogError(
                    "S3 Media is enabled but not active because the 'BucketName' is missing or empty in application configuration.");
                optionsAreValid = false;
            }

            if (string.IsNullOrWhiteSpace(accessKey))
            {
                logger.LogError(
                    "S3 Media is enabled but not active because the 'AccessKey' is missing or empty in application configuration.");
                optionsAreValid = false;
            }

            if (string.IsNullOrWhiteSpace(secretKey))
            {
                logger.LogError(
                    "S3 Media is enabled but not active because the 'SecretKey' is missing or empty in application configuration.");
                optionsAreValid = false;
            }

            return optionsAreValid;
        }
    }
}