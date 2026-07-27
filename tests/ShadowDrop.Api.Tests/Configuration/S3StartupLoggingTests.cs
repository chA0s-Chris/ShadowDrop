// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Configuration;

using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using NUnit.Framework;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using ShadowDrop.Api.CompositionRoot;

[TestFixture]
public sealed class S3StartupLoggingTests
{
    [Test]
    public async Task PrepareStartupAsync_ShouldLogEffectiveS3SettingsWithoutCredentials()
    {
        var root = Path.Combine(Path.GetTempPath(), $"shadowdrop-s3-logging-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                ContentRootPath = root
            });
            builder.Configuration.AddInMemoryCollection(new Dictionary<String, String?>
            {
                ["ShadowDrop:Metadata:Provider"] = "LiteDb",
                ["ShadowDrop:Metadata:LiteDbPath"] = Path.Combine(root, "metadata", "shadowdrop.db"),
                ["ShadowDrop:Storage:Provider"] = "S3",
                ["ShadowDrop:Storage:S3:BucketName"] = "audit-bucket",
                ["ShadowDrop:Storage:S3:Region"] = "us-east-1",
                ["ShadowDrop:Storage:S3:ServiceEndpoint"] = "https://objects.example.test",
                ["ShadowDrop:Storage:S3:KeyPrefix"] = "tenant/archive",
                ["ShadowDrop:Storage:S3:UsePathStyle"] = "true",
                ["ShadowDrop:Storage:S3:AccessKeyId"] = "DO-NOT-LOG-ACCESS",
                ["ShadowDrop:Storage:S3:SecretAccessKey"] = "DO-NOT-LOG-SECRET",
                ["ShadowDrop:Cleanup:CronExpression"] = "0 * * * *",
                ["ShadowDrop:Upload:MaxBytes"] = "4294967296",
                ["ShadowDrop:ApiExposure:EnableAdminOperations"] = "false",
                ["ShadowDrop:ApiExposure:EnableUploads"] = "false",
                ["ShadowDrop:ApiExposure:EnablePublicDownloads"] = "false"
            });
            var sink = new RecordingSink();
            await using var logger = new LoggerConfiguration().MinimumLevel.Debug().WriteTo.Sink(sink).CreateLogger();
            await using var app = builder.ConfigureServices(logger).Build();

            _ = await app.PrepareStartupAsync(logger, CancellationToken.None);

            var rendered = String.Join('\n', sink.Events.Select(static logEvent => logEvent.RenderMessage()));
            rendered.Should().Contain("audit-bucket")
                    .And.Contain("https://objects.example.test")
                    .And.Contain("tenant/archive")
                    .And.Contain("static configuration")
                    .And.NotContain("DO-NOT-LOG-ACCESS")
                    .And.NotContain("DO-NOT-LOG-SECRET");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private sealed class RecordingSink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];

        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }
}
