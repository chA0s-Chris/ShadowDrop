// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Uploads;

using FluentAssertions;
using NUnit.Framework;
using ShadowDrop.Cli.Uploads;
using ShadowDrop.Cli.Uploads.Progress;
using System.Net;
using System.Text;

public sealed class LocalUploadPlannerTests
{
    private String _rootDirectory;

    [Test]
    public void Create_ShouldCaptureStableNumbersAndEncryptedSizes()
    {
        var first = CreateFile("first.bin", 1);
        var second = CreateFile("second.bin", LocalUploadPlanner.ChunkSize + 1L);

        var result = LocalUploadPlanner.Create([
            UploadSelection.FromCommandLine(first),
            new(second, new("list.txt", 7), "nested/second.bin")
        ]);

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
        result.Plan.Should().NotBeNull();
        var plan = result.Plan;
        plan.Files.Select(static file => file.FileNumber).Should().Equal(1, 2);
        plan.Files.Select(static file => file.PlaintextLength).Should().Equal(1, LocalUploadPlanner.ChunkSize + 1L);
        plan.Files.Select(static file => file.ChunkCount).Should().Equal(1, 2);
        plan.Files.Select(static file => file.EncryptedLength).Should().Equal(17, LocalUploadPlanner.ChunkSize + 33L);
        plan.Files[0].Origin.Should().Be(UploadSelectionOrigin.CommandLine);
        plan.Files[1].Origin.Should().Be(new UploadSelectionOrigin("list.txt", 7));
        plan.Files[1].DirectoryRelativePath.Should().Be("nested/second.bin");
    }

    [Test]
    public void Create_ShouldPreflightTheCompleteBatch()
    {
        var empty = CreateFile("empty.bin", 0);
        var missing = new FileInfo(Path.Combine(_rootDirectory, "missing.bin"));

        var result = LocalUploadPlanner.Create([
            UploadSelection.FromCommandLine(empty),
            UploadSelection.FromCommandLine(missing)
        ]);

        result.IsValid.Should().BeFalse();
        result.Plan.Should().BeNull();
        result.Errors.Select(static error => (error.FileNumber, error.Message)).Should().Equal(
            (1, "File is empty."),
            (2, "File is missing."));
    }

    [Test]
    public void Create_ShouldRejectDuplicatePathsBeforeFilePreflight()
    {
        var missing = new FileInfo(Path.Combine(_rootDirectory, "missing.bin"));

        var result = LocalUploadPlanner.Create([
            UploadSelection.FromCommandLine(missing),
            UploadSelection.FromCommandLine(new(missing.FullName))
        ]);

        result.IsValid.Should().BeFalse();
        result.Plan.Should().BeNull();
        result.Errors.Should().ContainSingle()
              .Which.Message.Should().Be("File was selected more than once.");
    }

    [Test]
    public async Task ExecuteAsync_ShouldRevalidateTheCapturedLengthBeforeReservation()
    {
        var file = CreateFile("changing.bin", 16);
        var planningResult = LocalUploadPlanner.Create([UploadSelection.FromCommandLine(file)]);
        planningResult.Plan.Should().NotBeNull();
        var handler = new MutatingCapabilitiesHandler(file.FullName);
        using var httpClient = new HttpClient(handler);

        var result = await new UploadCommandExecutor(httpClient).ExecuteAsync(planningResult.Plan,
                                                                              new("https://shadowdrop.test"),
                                                                              "upload-token",
                                                                              NullUploadProgressReporter.Instance,
                                                                              CancellationToken.None);

        result.AllSucceeded.Should().BeFalse();
        result.Files.Should().ContainSingle()
              .Which.ErrorMessage.Should().Be("changing.bin changed while preparing the upload.");
        handler.RequestCount.Should().Be(1, "a changed file must fail before reserving a file ID");
    }

    [SetUp]
    public void SetUp()
    {
        _rootDirectory = Path.Combine(TestContext.CurrentContext.WorkDirectory,
                                      "artifacts",
                                      "local-upload-planner-tests",
                                      Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_rootDirectory);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_rootDirectory))
        {
            Directory.Delete(_rootDirectory, true);
        }
    }

    private FileInfo CreateFile(String relativePath, Int64 length)
    {
        var path = Path.Combine(_rootDirectory, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
        stream.SetLength(length);
        return new(path);
    }

    private sealed class MutatingCapabilitiesHandler(String filePath) : HttpMessageHandler
    {
        public Int32 RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            request.Method.Should().Be(HttpMethod.Get);
            request.RequestUri?.AbsolutePath.Should().Be("/api/uploads/capabilities");
            using (var stream = new FileStream(filePath, FileMode.Append, FileAccess.Write))
            {
                stream.WriteByte(42);
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"maxFilePayloadBytes\":10485760}", Encoding.UTF8, "application/json")
            });
        }
    }
}
