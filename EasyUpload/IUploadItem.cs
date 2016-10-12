// Copyright 2015 Javier Flores Assad.
// All rights reserved.
// MIT license.

using System;

namespace EasyUpload
{
    public interface IUploadItem
    {
        string Name { get; }
        uint NumberOfFiles { get; }
        ulong TotalSizeInBytes { get; }
        string ShareUrl { get; }
        string UploadUrl { get; }
        string UploadSessionUrl { get; }
        DateTimeOffset UploadTimestamp { get; }
        ulong UploadProgressInBytes { get; }
        bool IsCompleted { get; }
    }
}
