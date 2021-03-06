﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Web.Mvc;
using Microsoft.WindowsAzure.StorageClient;

namespace NuGetGallery
{
    public class CloudBlobFileStorageService : IFileStorageService
    {
        private readonly ICloudBlobClient client;
        private readonly IConfiguration configuration;
        private readonly IDictionary<string, ICloudBlobContainer> containers = new Dictionary<string, ICloudBlobContainer>();

        public CloudBlobFileStorageService(ICloudBlobClient client, IConfiguration configuration)
        {
            this.client = client;
            this.configuration = configuration;

            PrepareContainer(Constants.PackagesFolderName, isPublic: true);
            PrepareContainer(Constants.DownloadsFolderName, isPublic: true);
            PrepareContainer(Constants.UploadsFolderName, isPublic: false);
        }

        public ActionResult CreateDownloadFileActionResult(
            string folderName,
            string fileName)
        {
            var container = GetContainer(folderName);
            var blob = container.GetBlobReference(fileName);

            var redirectUri = GetRedirectUri(blob.Uri);
            return new RedirectResult(redirectUri.OriginalString, false);
        }

        public void DeleteFile(
            string folderName,
            string fileName)
        {
            var container = GetContainer(folderName);
            var blob = container.GetBlobReference(fileName);
            blob.DeleteIfExists();
        }

        public bool FileExists(
            string folderName,
            string fileName)
        {
            
            var container = GetContainer(folderName);
            var blob = container.GetBlobReference(fileName);
            return blob.Exists();
        }

        ICloudBlobContainer GetContainer(string folderName)
        {
            return containers[folderName];
        }

        static string GetContentType(string folderName)
        {
            switch (folderName)
            {
                case Constants.PackagesFolderName:
                case Constants.UploadsFolderName:
                    return Constants.PackageContentType;
                case Constants.DownloadsFolderName:
                    return Constants.OctetStreamContentType;
                default:
                    throw new InvalidOperationException(String.Format(CultureInfo.CurrentCulture, "The folder name {0} is not supported.", folderName));
            }
        }

        public Stream GetFile(
            string folderName,
            string fileName)
        {
            if (String.IsNullOrWhiteSpace(folderName))
                throw new ArgumentNullException("folderName");
            if (String.IsNullOrWhiteSpace(fileName))
                throw new ArgumentNullException("fileName");

            var container = GetContainer(folderName);
            var blob = container.GetBlobReference(fileName);
            var stream = new MemoryStream();
            try
            {
                blob.DownloadToStream(stream);
            }
            catch (TestableStorageClientException ex)
            {
                stream.Dispose();
                if (ex.ErrorCode == StorageErrorCode.BlobNotFound)
                    return null;
                else
                    throw;
            }

            stream.Position = 0;
            return stream;
        }

        void PrepareContainer(
            string folderName,
            bool isPublic)
        {
            var container = client.GetContainerReference(folderName);
            container.CreateIfNotExist();

            if (isPublic)
                container.SetPermissions(new BlobContainerPermissions { PublicAccess = BlobContainerPublicAccessType.Blob });
            else
                container.SetPermissions(new BlobContainerPermissions { PublicAccess = BlobContainerPublicAccessType.Off });

            containers.Add(folderName, container);
        }

        public void SaveFile(
            string folderName,
            string fileName,
            Stream packageFile)
        {
            var container = GetContainer(folderName);
            var blob = container.GetBlobReference(fileName);
            blob.DeleteIfExists();
            blob.UploadFromStream(packageFile);
            blob.Properties.ContentType = GetContentType(folderName);
            blob.SetProperties();
        }

        private Uri GetRedirectUri(Uri blobUri)
        {
            if (!String.IsNullOrEmpty(configuration.AzureCdnHost))
            {
                // If a Cdn is specified, convert the blob url to an Azure Cdn url.
                UriBuilder builder = new UriBuilder(blobUri.Scheme, configuration.AzureCdnHost);
                builder.Path = blobUri.AbsolutePath;
                builder.Query = blobUri.Query;

                return builder.Uri;
            }
            return blobUri;
        }
    }
}