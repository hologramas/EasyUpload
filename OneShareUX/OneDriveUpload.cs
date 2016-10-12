using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.OneDrive.Sdk;
using Windows.Storage;

namespace OneShareUX
{
    internal sealed class OneDriveUpload
    {
        private static readonly string[] OneDriveScopes = new string[] { "onedrive.readwrite", "wl.offline_access", "wl.signin" };
        private static object s_clientLock = new object();
        private static IOneDriveClient s_client = null;

        public static async Task<string> CreateShareAsync(string name)
        {
            var client = await OneDriveUpload.GetOneDriveClientAsync();
            var share = await client.Drive.Root.ItemWithPath(name).CreateLink("view").Request().PostAsync();

            return share.Link.WebUrl;
        }

        private static async Task CreateFolderAsync(string name)
        {
            var client = await GetOneDriveClientAsync();

            string[] sections = name.Split(new char[] { '/' }, StringSplitOptions.RemoveEmptyEntries);

            try
            {
                Item masterItem = new Item();
                masterItem.Name = sections[0];
                masterItem.Folder = new Folder();
                masterItem.Folder.ChildCount = 0;
                await client.Drive.Root.Children.Request().AddAsync(masterItem);
            }
            catch(Exception)
            {

            }

            try
            {
                Item childItem = new Item();
                childItem.Name = sections[1];
                childItem.Folder = new Folder();
                childItem.Folder.ChildCount = 0;
                await client.Drive.Root.ItemWithPath(sections[0]).Children.Request().AddAsync(childItem);
            }
            catch(Exception)
            {

            }
        }
        
        public static async Task<string> UploadFilesAndCreateShareAsync(string shareName, IEnumerable<IStorageFile> files, IProgress<double> progress)
        {
            ulong totalSize = 0;
            foreach (var uploadFile in files)
            {
                totalSize += (await uploadFile.GetBasicPropertiesAsync()).Size;
            }

            await CreateFolderAsync(shareName);

            ulong uploadedSize = 0;
            foreach (var uploadFile in files)
            {
                ulong sizeInBytes = (await (uploadFile.GetBasicPropertiesAsync())).Size;
                if (sizeInBytes <= (5 * 1024 * 1024))
                {
                    await OneDriveUpload.UploadFileToShareAsync(shareName, uploadFile);
                    double partialPercent = (uploadedSize + sizeInBytes);
                    progress.Report((double)partialPercent / totalSize);
                }
                else
                {
                    var partialProgress = new Progress<double>((percent) =>
                    {
                        double partialPercent = (uploadedSize + (sizeInBytes * percent));
                        progress.Report(partialPercent / totalSize);
                    });

                    await OneDriveUpload.LargeUploadAsync(shareName, uploadFile, partialProgress);
                }

                uploadedSize += sizeInBytes;
            }

            string shareUrl;
            if (files.Count() == 1)
            {
                shareUrl = await OneDriveUpload.CreateShareAsync(shareName + files.First().Name);
            }
            else
            {
                shareUrl = await OneDriveUpload.CreateShareAsync(shareName);
            }
            return shareUrl;
        }

        public static async Task<string> UploadFileToShareAsync(string shareName, IStorageFile file)
        {
            string link = string.Empty;

            var client = await GetOneDriveClientAsync();
            using (var stream = await file.OpenStreamForReadAsync())
            {
                var item = await client.Drive.Root.ItemWithPath(shareName + file.Name).Content.Request().PutAsync<Item>(stream);
                link = item.WebUrl;
            }

            return link;
        }

        public static async Task LargeUploadAsync(string shareName, IStorageFile file, IProgress<double> progress)
        {
            ChunkedUploadSessionDescriptor descriptor = new ChunkedUploadSessionDescriptor();
            descriptor.Name = file.Name;

            var client = await OneDriveUpload.GetOneDriveClientAsync();
            var builder = client.Drive.Root.ItemWithPath(shareName + file.Name).CreateSession(descriptor);
            var session = await builder.Request().PostAsync();

            try
            {
                await UploadAllRangesAsync(session.UploadUrl, file, progress);
            }
            catch (Exception)
            {
                var httpClient = new HttpClient();
                await httpClient.DeleteAsync(session.UploadUrl);
                throw;
            }
        }

        private static async Task UploadAllRangesAsync(string partUploadUrl, IStorageFile file, IProgress<double> progress)
        {
            ulong totalSize = (await file.GetBasicPropertiesAsync()).Size;
            ulong rangeStart = 0;

            using (var stream = await file.OpenSequentialReadAsync())
            {
                string token = await OneDriveUpload.GetAuthTokenAsync();

                while (rangeStart < totalSize)
                {
                    ulong rangeSize = (totalSize - rangeStart);
                    if (rangeSize > (5 * 1024 * 1024))
                    {
                        rangeSize = (5 * 1024 * 1024);
                    }

                    byte[] payload = new byte[rangeSize];
                    var buffer = await stream.ReadAsync(payload.AsBuffer(), (uint)payload.Length, Windows.Storage.Streams.InputStreamOptions.None);

                    var httpRequest = new HttpRequestMessage(HttpMethod.Put, partUploadUrl);
                    httpRequest.Headers.Add("Authorization", "bearer " + token);
                    httpRequest.Content = new ByteArrayContent(buffer.ToArray());
                    httpRequest.Content.Headers.Add("Content-Length", buffer.Length.ToString());
                    httpRequest.Content.Headers.Add("Content-Range", string.Format("bytes {0}-{1}/{2}", rangeStart, rangeStart + rangeSize - 1, totalSize));

                    var client = new HttpClient();
                    var response = await client.SendAsync(httpRequest);
                    if ((response.StatusCode == System.Net.HttpStatusCode.OK) ||
                        (response.StatusCode == System.Net.HttpStatusCode.Created))
                    {
                        progress.Report(1.0);
                        break;
                    }
                    else if (response.StatusCode != System.Net.HttpStatusCode.Accepted)
                    {
                        string message = await response.Content.ReadAsStringAsync();
                        throw new Exception(message);
                    }

                    rangeStart += rangeSize;
                    progress.Report(((double)rangeStart) / totalSize);
                }
            }
        }

        public static async Task RequestAccessAsync()
        {
            await OneDriveUpload.GetOneDriveClientAsync();
        }

        public static async Task<IOneDriveClient> GetOneDriveClientAsync()
        {
            if (OneDriveUpload.s_client == null)
            {
                lock (OneDriveUpload.s_clientLock)
                {
                    if (OneDriveUpload.s_client == null)
                    {
                        OneDriveUpload.s_client = OneDriveClientExtensions.GetUniversalClient(OneDriveUpload.OneDriveScopes);
                    }
                }
            }

            if (!OneDriveUpload.s_client.IsAuthenticated)
            {
                await OneDriveUpload.s_client.AuthenticateAsync();
            }

            return OneDriveUpload.s_client;
        }

        public static async Task<string> GetAuthTokenAsync()
        {
            var session = await (await GetOneDriveClientAsync()).AuthenticateAsync();
            return session.AccessToken;
        }
    }
}
