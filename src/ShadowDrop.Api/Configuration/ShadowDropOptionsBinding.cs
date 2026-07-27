// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Configuration;

using Cronos;
using ShadowDrop.Api.Infrastructure.Storage;
using ShadowDrop.Api.Uploads;

public static class ShadowDropOptionsBinding
{
    public static ShadowDropOptions BindAndValidate(IConfiguration configuration, String contentRootPath)
    {
        var shadowDropSection = configuration.GetRequiredSection("ShadowDrop");
        var options = shadowDropSection.Get<ShadowDropOptions>()
                      ?? throw new InvalidOperationException("The 'ShadowDrop' configuration section is required.");

        if (options.Metadata.Provider == MetadataProvider.LiteDb && String.IsNullOrWhiteSpace(options.Metadata.LiteDbPath))
        {
            throw new InvalidOperationException("The configuration value 'ShadowDrop:Metadata:LiteDbPath' is required.");
        }

        if (options.Storage.Provider == BlobStorageProvider.FileSystem && String.IsNullOrWhiteSpace(options.Storage.LocalRoot))
        {
            throw new InvalidOperationException("The configuration value 'ShadowDrop:Storage:LocalRoot' is required.");
        }

        var mongoRequired = options.RequiresMongo;
        if (mongoRequired && String.IsNullOrWhiteSpace(options.Mongo.ConnectionString))
        {
            throw new InvalidOperationException("The configuration value 'ShadowDrop:Mongo:ConnectionString' is required by the selected provider.");
        }

        if (mongoRequired && String.IsNullOrWhiteSpace(options.Mongo.DatabaseName))
        {
            throw new InvalidOperationException("The configuration value 'ShadowDrop:Mongo:DatabaseName' is required by the selected provider.");
        }

        if (options.Storage.Provider == BlobStorageProvider.MongoGridFs
            && String.IsNullOrWhiteSpace(options.Storage.GridFsBucketName))
        {
            throw new InvalidOperationException("The configuration value 'ShadowDrop:Storage:GridFsBucketName' is required by the selected provider.");
        }

        if (options.Storage.Provider == BlobStorageProvider.S3)
        {
            ValidateS3(options);
        }

        if (String.IsNullOrWhiteSpace(options.Cleanup.CronExpression))
        {
            throw new InvalidOperationException("The configuration value 'ShadowDrop:Cleanup:CronExpression' is required.");
        }

        if (options.Upload.MaxBytes <= UploadLimitCalculator.MultipartEnvelopeAllowanceBytes)
        {
            throw new InvalidOperationException(
                $"The configuration value 'ShadowDrop:Upload:MaxBytes' must be greater than {UploadLimitCalculator.MultipartEnvelopeAllowanceBytes} "
                + "(the reserved multipart envelope allowance in bytes).");
        }

        CronExpression cleanupSchedule;
        try
        {
            cleanupSchedule = CronExpression.Parse(options.Cleanup.CronExpression, CronFormat.Standard);
        }
        catch (CronFormatException exception)
        {
            throw new InvalidOperationException("The configuration value 'ShadowDrop:Cleanup:CronExpression' must be a valid five-field cron expression.",
                                                exception);
        }

        if (cleanupSchedule.GetNextOccurrence(DateTimeOffset.UtcNow, TimeZoneInfo.Utc) is null)
        {
            throw new InvalidOperationException("The configuration value 'ShadowDrop:Cleanup:CronExpression' must produce at least one future occurrence.");
        }

        if (options.Metadata.Provider == MetadataProvider.LiteDb)
        {
            options.Metadata.LiteDbPath = ResolvePath(options.Metadata.LiteDbPath, contentRootPath);
            var metadataDirectory = Path.GetDirectoryName(options.Metadata.LiteDbPath)
                                    ?? throw new InvalidOperationException("The metadata database path must include a directory.");
            FileSystemAccessPermissions.EnsureOwnerOnlyDirectory(metadataDirectory);
        }

        if (options.Storage.Provider == BlobStorageProvider.FileSystem)
        {
            options.Storage.LocalRoot = ResolvePath(options.Storage.LocalRoot, contentRootPath);
            FileSystemAccessPermissions.EnsureOwnerOnlyDirectory(options.Storage.LocalRoot);
        }

        return options;
    }

    private static String ResolvePath(String configuredPath, String contentRootPath) =>
        Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.GetFullPath(Path.Combine(contentRootPath, configuredPath));

    private static void ValidateS3(ShadowDropOptions options)
    {
        var s3 = options.Storage.S3;
        if (String.IsNullOrWhiteSpace(s3.BucketName))
        {
            throw new InvalidOperationException("The configuration value 'ShadowDrop:Storage:S3:BucketName' is required by the selected provider.");
        }

        if (String.IsNullOrWhiteSpace(s3.Region))
        {
            throw new InvalidOperationException("The configuration value 'ShadowDrop:Storage:S3:Region' is required by the selected provider.");
        }

        var hasAccessKey = !String.IsNullOrWhiteSpace(s3.AccessKeyId);
        var hasSecretKey = !String.IsNullOrWhiteSpace(s3.SecretAccessKey);
        if (hasAccessKey != hasSecretKey)
        {
            throw new InvalidOperationException(
                "The configuration values 'ShadowDrop:Storage:S3:AccessKeyId' and 'ShadowDrop:Storage:S3:SecretAccessKey' must either both be supplied or both be omitted.");
        }

        if (!String.IsNullOrWhiteSpace(s3.ServiceEndpoint)
            && (!Uri.TryCreate(s3.ServiceEndpoint, UriKind.Absolute, out var endpoint)
                || (endpoint.Scheme != Uri.UriSchemeHttp && endpoint.Scheme != Uri.UriSchemeHttps)))
        {
            throw new InvalidOperationException(
                "The configuration value 'ShadowDrop:Storage:S3:ServiceEndpoint' must be an absolute HTTP or HTTPS URI.");
        }

        if (options.Upload.MaxBytes > S3BlobStorage.MaximumObjectSize)
        {
            throw new InvalidOperationException(
                $"The configuration value 'ShadowDrop:Upload:MaxBytes' cannot exceed {S3BlobStorage.MaximumObjectSize} when S3 storage is selected.");
        }

        s3.BucketName = s3.BucketName.Trim();
        s3.Region = s3.Region.Trim();
        s3.ServiceEndpoint = s3.ServiceEndpoint.Trim();
        s3.KeyPrefix = S3ObjectKey.NormalizePrefix(s3.KeyPrefix);
    }
}
