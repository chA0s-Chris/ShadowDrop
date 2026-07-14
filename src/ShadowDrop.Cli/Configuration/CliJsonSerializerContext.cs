// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Configuration;

using ShadowDrop.Cli.Downloads;
using ShadowDrop.Cli.Results;
using ShadowDrop.Cli.Shares;
using ShadowDrop.Cli.Tokens;
using ShadowDrop.Cli.Updates;
using ShadowDrop.Cli.Uploads;
using ShadowDrop.Contracts;
using System.Text.Json.Serialization;

[JsonSerializable(typeof(CliConfigFile))]
[JsonSerializable(typeof(ShareManifestContract))]
[JsonSerializable(typeof(UploadCapabilitiesResponse))]
[JsonSerializable(typeof(UploadReservationResponse))]
[JsonSerializable(typeof(UploadResponse))]
[JsonSerializable(typeof(UploadMetadataPayload))]
[JsonSerializable(typeof(CreateShareCliRequest))]
[JsonSerializable(typeof(CreateShareCliResult))]
[JsonSerializable(typeof(ShareCleanupResultContract))]
[JsonSerializable(typeof(CreateUploadCredentialCliRequest))]
[JsonSerializable(typeof(CreateUploadCredentialCliResult))]
[JsonSerializable(typeof(UploadCredentialCliProjection))]
[JsonSerializable(typeof(UploadCredentialCliListResult))]
[JsonSerializable(typeof(TokenRevokeCliResult))]
[JsonSerializable(typeof(UploadCommandResult))]
[JsonSerializable(typeof(UploadFailure))]
[JsonSerializable(typeof(DirectHttpDownload))]
[JsonSerializable(typeof(CredentialDocument))]
[JsonSerializable(typeof(DownloadResumeMarker))]
[JsonSerializable(typeof(GitHubReleaseContract))]
[JsonSerializable(typeof(UpdateCheckRecord))]
internal sealed partial class CliJsonSerializerContext : JsonSerializerContext;
