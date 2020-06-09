using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Transfer;
using Microsoft.AspNetCore.StaticFiles;
using OrchardCore.Modules;

namespace OrchardCore.FileStorage.S3
{
    public class S3FileStore : IFileStore
    {
        private const string DirectoryMarkerFileName = "OrchardCore.Media.txt";

        private readonly S3Settings _s3Settings;
        private readonly IClock _clock;
        private readonly AmazonS3Client _s3Client;
        private readonly IContentTypeProvider _contentTypeProvider;
        private readonly string _basePrefix;

        public S3FileStore(S3Settings s3Settings, IClock clock, IContentTypeProvider contentTypeProvider)
        {
            _s3Settings = s3Settings;
            _clock = clock;
            _contentTypeProvider = contentTypeProvider;

            var clientConfig = new AmazonS3Config
            {
                RegionEndpoint = RegionEndpoint.GetBySystemName(s3Settings.S3Region),
                ServiceURL = s3Settings.S3HostEndpoint,
                UseHttp = true,
                ForcePathStyle = true,
            };
            _s3Client = new AmazonS3Client(s3Settings.S3AccessKey, s3Settings.S3SecretKey, clientConfig);
            if (!string.IsNullOrEmpty(_s3Settings.S3BasePath))
            {
                _basePrefix = NormalizePrefix(_s3Settings.S3BasePath);
            }
        }

        public async Task<IFileStoreEntry> GetFileInfoAsync(string path)
        {
            try
            {
                var metaData = await _s3Client.GetObjectMetadataAsync(new GetObjectMetadataRequest
                {
                    BucketName = _s3Settings.S3BucketName,
                    Key = GetCompletePath(path)
                });
                return new S3File(path, metaData.ContentLength, metaData.LastModified);
            }
            catch (Exception)
            {
                return null;
            }
        }

        public async Task<IFileStoreEntry> GetDirectoryInfoAsync(string path)
        {
            if (path == string.Empty)
            {
                return new S3Directory(path, _clock.UtcNow);
            }

            var listResponse = await _s3Client.ListObjectsAsync(new ListObjectsRequest
            {
                BucketName = _s3Settings.S3BucketName,
                Prefix = GetCompletePath(path),
                MaxKeys = 1
            });
            return listResponse.S3Objects.Any() ? new S3Directory(path, _clock.UtcNow) : null;
        }

        public async Task<IEnumerable<IFileStoreEntry>> GetDirectoryContentAsync(string path = null,
            bool includeSubDirectories = false)
        {
            var results = new List<IFileStoreEntry>();
            var listObjectsRequest = new ListObjectsRequest
            {
                BucketName = _s3Settings.S3BucketName,
                Prefix = NormalizePrefix(GetCompletePath(path)),
                Delimiter = "/"
            };
            var listObjects = await _s3Client.ListObjectsAsync(listObjectsRequest);
            foreach (var prefix in listObjects.CommonPrefixes)
            {
                var folderPath = prefix;
                if (!string.IsNullOrEmpty(_basePrefix))
                {
                    folderPath = folderPath.Substring(_basePrefix.Length - 1);
                }

                folderPath = folderPath.TrimEnd('/');
                results.Add(new S3Directory(folderPath, _clock.UtcNow));
            }

            foreach (var s3Object in listObjects.S3Objects)
            {
                var decode = WebUtility.UrlDecode(s3Object.Key);
                var itemName = Path.GetFileName(decode);
                if (includeSubDirectories || itemName != DirectoryMarkerFileName)
                {
                    path = string.IsNullOrEmpty(path) ? "/" : path;
                    var itemPath = this.Combine(path, itemName);
                    results.Add(new S3File(itemPath, s3Object.Size, s3Object.LastModified));
                }
            }
            // results.AddRange(from s3Object in listObjects.S3Objects
            //     let itemName = Path.GetFileName(WebUtility.UrlDecode(s3Object.Key))
            //     where string.IsNullOrEmpty(itemName) && itemName != DirectoryMarkerFileName
            //     let itemPath = this.Combine(path, itemName)
            //     select new S3File(itemPath, s3Object.Size, s3Object.LastModified));

            return results
                .OrderByDescending(x => x.IsDirectory)
                .ToArray();
        }

        public async Task<bool> TryCreateDirectoryAsync(string path)
        {
            try
            {
                await _s3Client.GetObjectMetadataAsync(new GetObjectMetadataRequest
                {
                    BucketName = _s3Settings.S3BucketName,
                    Key = GetCompletePath(path)
                });
                throw new FileStoreException(
                    $"Cannot create directory because the path '{path}' already exists and is a file.");
            }
            catch (Exception)
            {
                await using var stream =
                    new MemoryStream(Encoding.UTF8.GetBytes(
                        "This is a directory marker file created by Orchard Core. It is safe to delete it."));
                var uploadRequest = new TransferUtilityUploadRequest
                {
                    InputStream = stream,
                    Key = this.Combine(GetCompletePath(path), DirectoryMarkerFileName),
                    BucketName = _s3Settings.S3BucketName,
                };
                var fileTransferUtility = new TransferUtility(_s3Client);
                await fileTransferUtility.UploadAsync(uploadRequest);
            }

            return true;
        }

        public async Task<bool> TryDeleteFileAsync(string path)
        {
            try
            {
                await _s3Client.DeleteObjectAsync(new DeleteObjectRequest
                {
                    BucketName = _s3Settings.S3BucketName,
                    Key = GetCompletePath(path)
                });
                return true;
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                return false;
            }
        }

        public async Task<bool> TryDeleteDirectoryAsync(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                throw new FileStoreException("Cannot delete the root directory.");
            }

            var request = new ListObjectsRequest
            {
                BucketName = _s3Settings.S3BucketName,
                Prefix = this.Combine(_basePrefix, path)
            };
            var listObjectsResponse = await _s3Client.ListObjectsAsync(request);
            var deleteObjectsRequest = new DeleteObjectsRequest
            {
                BucketName = _s3Settings.S3BucketName
            };
            foreach (var entry in listObjectsResponse.S3Objects)
            {
                deleteObjectsRequest.AddKey(entry.Key);
            }

            await _s3Client.DeleteObjectsAsync(deleteObjectsRequest);

            return true;
        }

        public async Task MoveFileAsync(string oldPath, string newPath)
        {
            await CopyFileAsync(oldPath, newPath);
            await TryDeleteFileAsync(oldPath);
        }

        public async Task CopyFileAsync(string srcPath, string dstPath)
        {
            if (srcPath == dstPath)
            {
                throw new ArgumentException(
                    $"The values for {nameof(srcPath)} and {nameof(dstPath)} must not be the same.");
            }

            try
            {
                await _s3Client.GetObjectMetadataAsync(new GetObjectMetadataRequest
                {
                    BucketName = _s3Settings.S3BucketName,
                    Key = GetCompletePath(srcPath)
                });
            }
            catch (Exception e)
            {
                throw new FileStoreException($"Cannot copy file from '{srcPath}' because it does not exists, with message {e.Message}.");
            }



            try
            {
                await _s3Client.GetObjectMetadataAsync(new GetObjectMetadataRequest
                {
                    BucketName = _s3Settings.S3BucketName,
                    Key = GetCompletePath(dstPath)
                });
                throw new FileStoreException($"Cannot copy file to '{dstPath}' because it already exists, with message.");
            }
            catch
            {
                // ignored
            }

            try
            {          
                var request = new CopyObjectRequest
                {
                    SourceBucket = _s3Settings.S3BucketName,
                    SourceKey = GetCompletePath(srcPath),
                    DestinationBucket = _s3Settings.S3BucketName,
                    DestinationKey = GetCompletePath(dstPath)
                };
                var response = await _s3Client.CopyObjectAsync(request);
                if(response.LastModified != null)
                {
                    throw new FileStoreException(
                        $"Error while copying file '{srcPath}'; copy operation failed with status {response.HttpStatusCode}.");
                }
            }
            catch (AmazonS3Exception e)
            {
                throw new FileStoreException(
                    $"Error while copying file '{srcPath}'; copy operation failed with exception {e.Message}.");
            }
            catch (Exception e)
            {
                throw new FileStoreException(
                    $"Error while copying file '{srcPath}'; copy operation failed with exception {e.Message}.");
            }
        }

        public async Task<Stream> GetFileStreamAsync(string path)
        {
            try
            {
                var transferUtility = new TransferUtility(_s3Client);
                return await transferUtility.OpenStreamAsync(_s3Settings.S3BucketName, GetCompletePath(path));
            }
            catch (Exception)
            {
                throw new FileStoreException($"Cannot get file stream because the file '{path}' does not exist.");
            }
        }

        public async Task<Stream> GetFileStreamAsync(IFileStoreEntry fileStoreEntry)
        {
            return await GetFileStreamAsync(fileStoreEntry.Path);
        }

        public async Task<string> CreateFileFromStreamAsync(string path, Stream inputStream, bool overwrite = false)
        {
            try
            {
                await _s3Client.GetObjectMetadataAsync(new GetObjectMetadataRequest
                {
                    BucketName = _s3Settings.S3BucketName,
                    Key = GetCompletePath(path)
                });
                throw new FileStoreException($"Cannot create file '{path}' because it already exists.");
            }
            catch (Exception)
            {
                _contentTypeProvider.TryGetContentType(path, out var contentType);
                var uploadRequest = new TransferUtilityUploadRequest
                {
                    InputStream = inputStream,
                    ContentType = contentType ?? "application/octet-stream",
                    Key = GetCompletePath(path),
                    BucketName = _s3Settings.S3BucketName,
                };
                var fileTransferUtility = new TransferUtility(_s3Client);
                await fileTransferUtility.UploadAsync(uploadRequest);
                return path;
            }
        }

        private string GetCompletePath(string path)
        {
            return this.Combine(_basePrefix, path);
        }

        /// <summary>
        /// Blob prefix requires a trailing slash except when loading the root of the container.
        /// </summary>
        private static string NormalizePrefix(string prefix)
        {
            prefix = prefix.Trim('/') + '/';
            return prefix.Length == 1 ? string.Empty : prefix;
        }
    }
}
