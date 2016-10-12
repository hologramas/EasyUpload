// Copyright 2015 Javier Flores Assad.
// All rights reserved.
// MIT license.

using System;
using System.Net.Http;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Storage;
using Microsoft.OneDrive.Sdk;
using Microsoft.OneDrive.Sdk.Authentication;

namespace EasyUpload
{
    public class Uploader
    {
        private static readonly string oneDriveClientId = "00000000";
        private static readonly string oneDriveReturnUrl = "https://login.live.com/oauth20_desktop.srf";
        private static readonly string oneDriveApiBaseUrl = "https://api.onedrive.com/v1.0";
        private static readonly string[] oneDriveScopes = new string[] { "onedrive.readwrite", "onedrive.appfolder", "wl.signin" };
        private IOneDriveClient onedriveClient;
        private string authToken;

        public Uploader()
        {
        }

        public async Task AuthenticateClientAsync()
        {
            var msa = new MsaAuthenticationProvider(Uploader.oneDriveClientId, Uploader.oneDriveReturnUrl, Uploader.oneDriveScopes, new CredentialVault(Uploader.oneDriveClientId));
            if (!msa.IsAuthenticated)
            {
                await msa.RestoreMostRecentFromCacheOrAuthenticateUserAsync();
            }
            
            this.onedriveClient = new OneDriveClient(Uploader.oneDriveApiBaseUrl, msa);
            this.authToken = msa.CurrentAccountSession.AccessToken;
        }

        public async Task<string> CreateShareAsync(string name)
        {
            var share = await this.onedriveClient.Drive.Root.ItemWithPath(name).CreateLink("view").Request().PostAsync();
            return share.Link.WebUrl;
        }

        public async Task<bool> TryCreateFolderAsync(string name)
        {
            string[] sections = name.Split(new char[] { '/' }, StringSplitOptions.RemoveEmptyEntries);

            try
            {
                Item folderItem = new Item();
                folderItem.Name = sections[0];
                folderItem.Folder = new Folder();
                folderItem.Folder.ChildCount = 0;
                await this.onedriveClient.Drive.Root.Children.Request().AddAsync(folderItem);

                folderItem.Name = sections[1];
                await this.onedriveClient.Drive.Root.ItemWithPath(sections[0]).Children.Request().AddAsync(folderItem);
            }
            catch (Exception)
            {
                return false;
            }

            return true;
        }

        public async Task ContinueUploadAsync(string shareName, UploadRecord record)
        {
            if (string.IsNullOrEmpty(record.UploadUrl))
            {
                await this.TryCreateFolderAsync(shareName);
                record.UploadUrl = shareName;
            }

            string itemPath = string.Empty;
            while (!record.IsCompleted)
            {
                ulong progressBefore = record.UploadProgressInBytes;
                try
                {
                    itemPath = await this.UploadNextFileAsync(this.onedriveClient, this.authToken, record);
                    record.MarkNextFileUploadAsCompleted();
                }
                catch (Exception)
                {
                    if (!string.IsNullOrEmpty(record.UploadSessionUrl))
                    {
                        var httpClient = new HttpClient();
                        await httpClient.DeleteAsync(record.UploadSessionUrl);
                        record.UploadSessionUrl = null;
                        record.UploadProgressInBytes = progressBefore;
                    }
                    throw;
                }
            }

            if (string.IsNullOrEmpty(record.ShareUrl))
            {
                if (record.NumberOfFiles == 1)
                {
                    shareName = itemPath;
                }
                record.ShareUrl =  await this.CreateShareAsync(shareName);
            }
        }

        private async Task<string> UploadNextFileAsync(IOneDriveClient client, string token, UploadRecord record)
        {
            string itemPath = string.Empty;
            IStorageFile file = await record.GetNextFileToUploadAsync();
            if (string.IsNullOrEmpty(record.UploadSessionUrl))
            {
                var descriptor = new ChunkedUploadSessionDescriptor();
                descriptor.Name = file.Name;

                itemPath = record.UploadUrl + file.Name;
                UploadSession session = await this.onedriveClient.Drive.Root.ItemWithPath(itemPath).CreateSession(descriptor).Request().PostAsync();
                record.UploadSessionUrl = session.UploadUrl;
            }

            ulong totalSize = (await file.GetBasicPropertiesAsync()).Size;
            ulong rangeStart = 0;

            using (var stream = await file.OpenSequentialReadAsync())
            {
                while (rangeStart < totalSize)
                {
                    ulong rangeSize = (totalSize - rangeStart);
                    if (rangeSize > (5 * 1024 * 1024))
                    {
                        rangeSize = (5 * 1024 * 1024);
                    }

                    byte[] payload = new byte[rangeSize];
                    var buffer = await stream.ReadAsync(payload.AsBuffer(), (uint)payload.Length, Windows.Storage.Streams.InputStreamOptions.None);

                    var httpRequest = new HttpRequestMessage(HttpMethod.Put, record.UploadSessionUrl);
                    httpRequest.Headers.Add("Authorization", "bearer " + token);
                    httpRequest.Content = new ByteArrayContent(buffer.ToArray());
                    httpRequest.Content.Headers.Add("Content-Length", buffer.Length.ToString());
                    httpRequest.Content.Headers.Add("Content-Range", string.Format("bytes {0}-{1}/{2}", rangeStart, rangeStart + rangeSize - 1, totalSize));

                    var httpClient = new HttpClient();
                    var response = await httpClient.SendAsync(httpRequest);
                    if ((response.StatusCode == System.Net.HttpStatusCode.OK) ||
                        (response.StatusCode == System.Net.HttpStatusCode.Created))
                    {
                        record.UploadProgressInBytes += rangeSize;
                        record.RaiseUploadProgressEvent();
                        break;
                    }
                    else if (response.StatusCode != System.Net.HttpStatusCode.Accepted)
                    {
                        string message = await response.Content.ReadAsStringAsync();
                        throw new Exception(message);
                    }

                    rangeStart += rangeSize;
                    record.UploadProgressInBytes += rangeSize;
                    record.RaiseUploadProgressEvent();
                }
            }

            return itemPath;
        }

    }
}
