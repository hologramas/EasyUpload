// Copyright 2015 Javier Flores Assad.
// All rights reserved.
// MIT license.

using System;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Threading.Tasks;
using Windows.Storage;

namespace EasyUpload
{
    [DataContract]
    public class UploadRecord : IUploadItem
    {
        public static async Task<UploadRecord> CreateFromFilesAsync(string name, IReadOnlyList<IStorageFile> files)
        {
            var record = new UploadRecord();
            record.Name = name;
            record.NumberOfFiles = (uint)files.Count();
            record.FileTokens = new List<string>();

            var filePropTasks = new List<Task<Windows.Storage.FileProperties.BasicProperties>>();
            files.ToList().ForEach(storageFile =>
            {
                filePropTasks.Add(storageFile.GetBasicPropertiesAsync().AsTask());
                record.FileTokens.Add(Windows.Storage.AccessCache.StorageApplicationPermissions.FutureAccessList.Add(storageFile));
            });

            await Task.WhenAll(filePropTasks);
            filePropTasks.ForEach(prop => record.TotalSizeInBytes += prop.Result.Size);

            return record;
        }

        public event EventHandler<int> OnUploadProgress;

        public async Task<IStorageFile> GetNextFileToUploadAsync()
        {
            if (this.NextFileUploadIndex >= this.FileTokens.Count)
            {
                return null;
            }
            return await Windows.Storage.AccessCache.StorageApplicationPermissions.FutureAccessList.GetFileAsync(this.FileTokens[this.NextFileUploadIndex]);
        }

        public void MarkNextFileUploadAsCompleted()
        {
            this.NextFileUploadIndex++;
            this.UploadSessionUrl = string.Empty;

            if (this.NextFileUploadIndex < this.FileTokens.Count)
            {
                return;
            }

            this.IsCompleted = true;
            this.UploadTimestamp = DateTimeOffset.Now;
            this.UploadProgressInBytes = this.TotalSizeInBytes;
            this.NextFileUploadIndex = this.FileTokens.Count;
            this.RaiseUploadProgressEvent();

            this.FileTokens.ForEach(path =>
            {
                Windows.Storage.AccessCache.StorageApplicationPermissions.FutureAccessList.Remove(path);
            });
        }

        public void RaiseUploadProgressEvent()
        {
            if (this.OnUploadProgress != null)
            {
                int progressPercent = 100;
                if (!this.IsCompleted)
                {
                    progressPercent = (int)((this.UploadProgressInBytes * 100) / this.TotalSizeInBytes);
                }

                this.OnUploadProgress(this, progressPercent);
            }
        }

        [DataMember]
        public string Name { get; set; }

        [DataMember]
        public uint NumberOfFiles { get; set; }

        [DataMember]
        public ulong TotalSizeInBytes { get; set; }

        [DataMember]
        public string ShareUrl { get; set; }

        [DataMember]
        public string UploadUrl { get; set; }

        [DataMember]
        public string UploadSessionUrl { get; set; }

        [DataMember]
        public DateTimeOffset UploadTimestamp { get; set; }

        [DataMember]
        public ulong UploadProgressInBytes { get; set; }

        [DataMember]
        public bool IsCompleted { get; private set; }

        [DataMember]
        private int NextFileUploadIndex { get; set; }

        [DataMember]
        private List<string> FileTokens { get; set; }

        public static string ConvertToJson<T>(T obj)
        {
            DataContractJsonSerializer serializer = new DataContractJsonSerializer(obj.GetType());
            using (System.IO.MemoryStream memory = new System.IO.MemoryStream())
            {
                serializer.WriteObject(memory, obj);
                return System.Text.Encoding.UTF8.GetString(memory.ToArray(), 0, (int)memory.Length);
            }
        }

        public static T ConvertFromJson<T>(string json)
        {
            T activator = Activator.CreateInstance<T>();
            using (System.IO.MemoryStream memory = new System.IO.MemoryStream(System.Text.Encoding.Unicode.GetBytes(json)))
            {
                DataContractJsonSerializer serializer = new DataContractJsonSerializer(activator.GetType());
                activator = (T)serializer.ReadObject(memory);
                return activator;
            }
        }
    }
}
