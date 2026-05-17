// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Uploads;

public interface IBlobStorage
{
    Task DeleteIfExistsAsync(String blobKey, CancellationToken cancellationToken);

    Task<UploadBlobDescriptor> SaveAsync(Guid fileId, Stream encryptedContent, CancellationToken cancellationToken);
}
