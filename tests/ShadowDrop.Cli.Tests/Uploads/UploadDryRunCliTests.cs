// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Uploads;

using FluentAssertions;
using NUnit.Framework;
using ShadowDrop.Cli;
using ShadowDrop.Cli.Downloads.Progress;
using ShadowDrop.Tests.Fakes;
using System.Text.Json;

[NonParallelizable]
public sealed class UploadDryRunCliTests
{
    private static readonly String[] ExpectedUncheckedValidations =
    [
        "serverAvailability",
        "authentication",
        "uploadCapabilities",
        "accountQuota",
        "serverFileSizeLimit"
    ];

    private String _rootDirectory;

    [Test]
    public async Task InvokeAsync_ShouldEmitCompleteJsonPlan_WithoutReadingConfigurationCreatingHttpOrWritingOutputs()
    {
        var inputDirectory = CreateDirectory("inputs");
        var alphaPath = CreateFile("inputs/alpha.bin", 3);
        var nestedPath = CreateFile("inputs/nested/zeta.bin", 5);
        CreateFile("inputs/nested/skipped.txt", 7);
        var queuePath = CreateFile("planned.queue.json", "existing queue");
        var secretsPath = CreateFile("planned.secrets.json", "existing secrets");
        var malformedConfigPath = CreateFile("config.json", "not-json");
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        var releaseClient = new StubUpdateReleaseClient("999.0.0");
        var cache = new InMemoryUpdateCheckCache();
        var httpClientCreations = 0;
        var services = CreateServices(standardOut,
                                      standardError,
                                      malformedConfigPath,
                                      releaseClient,
                                      cache,
                                      () => httpClientCreations++);

        var exitCode = await CliApplication.InvokeAsync([
                                                            "upload",
                                                            inputDirectory,
                                                            "--recursive",
                                                            "--include",
                                                            "**/*.bin",
                                                            "--queue-out",
                                                            queuePath,
                                                            "--secrets-out",
                                                            secretsPath,
                                                            "--input-root",
                                                            _rootDirectory,
                                                            "--force",
                                                            "--dry-run",
                                                            "--json",
                                                            "--no-banner"
                                                        ],
                                                        services,
                                                        CancellationToken.None);

        exitCode.Should().Be(0);
        standardError.ToString().Should().BeEmpty();
        using var document = JsonDocument.Parse(standardOut.ToString());
        var root = document.RootElement;
        root.EnumerateObject().Select(static property => property.Name).Should()
            .Equal("status", "files", "totals", "intendedOutputs", "uncheckedValidations", "errors");
        root.GetProperty("status").GetString().Should().Be("valid");
        var files = root.GetProperty("files").EnumerateArray().ToArray();
        files.Select(static file => file.GetProperty("sourcePath").GetString()).Should().Equal(alphaPath, nestedPath);
        files.Select(static file => file.GetProperty("plaintextBytes").GetInt64()).Should().Equal(3, 5);
        files.Select(static file => file.GetProperty("encryptedBytes").GetInt64()).Should().Equal(19, 21);
        files.Select(static file => file.GetProperty("queueDestination").GetString()).Should()
             .Equal("inputs/alpha.bin", "inputs/nested/zeta.bin");
        var totals = root.GetProperty("totals");
        totals.GetProperty("selectedFiles").GetInt32().Should().Be(2);
        totals.GetProperty("excludedFiles").GetInt32().Should().Be(1);
        totals.GetProperty("plaintextBytes").GetInt64().Should().Be(8);
        totals.GetProperty("encryptedBytes").GetInt64().Should().Be(40);
        root.GetProperty("intendedOutputs").GetProperty("queueFile").GetString().Should().Be(queuePath);
        root.GetProperty("intendedOutputs").GetProperty("secretsFile").GetString().Should().Be(secretsPath);
        root.GetProperty("uncheckedValidations").EnumerateArray().Select(static item => item.GetString()).Should()
            .Equal(ExpectedUncheckedValidations);
        root.GetProperty("errors").GetArrayLength().Should().Be(0);
        (await File.ReadAllTextAsync(queuePath)).Should().Be("existing queue");
        (await File.ReadAllTextAsync(secretsPath)).Should().Be("existing secrets");
        httpClientCreations.Should().Be(0);
        releaseClient.RequestCount.Should().Be(0);
        cache.WriteCount.Should().Be(0);
    }

    [Test]
    public async Task InvokeAsync_ShouldEmitRawJsonPlan_WithNullDestinationsAndNoDisplayNames()
    {
        var filePath = CreateFile("raw.bin", 9);
        var secretsPath = Path.Combine(_rootDirectory, "raw-secrets.json");
        var standardOut = new StringWriter();

        var exitCode = await CliApplication.InvokeAsync([
                                                            "upload",
                                                            "raw",
                                                            filePath,
                                                            "--secrets-out",
                                                            secretsPath,
                                                            "--dry-run",
                                                            "--json",
                                                            "--no-banner"
                                                        ],
                                                        CreateServices(standardOut, new()),
                                                        CancellationToken.None);

        exitCode.Should().Be(0);
        using var document = JsonDocument.Parse(standardOut.ToString());
        var root = document.RootElement;
        var file = root.GetProperty("files").EnumerateArray().Single();
        file.GetProperty("sourcePath").GetString().Should().Be(filePath);
        file.GetProperty("plaintextBytes").GetInt64().Should().Be(9);
        file.GetProperty("encryptedBytes").GetInt64().Should().Be(25);
        file.GetProperty("queueDestination").ValueKind.Should().Be(JsonValueKind.Null);
        root.GetProperty("intendedOutputs").GetProperty("queueFile").ValueKind.Should().Be(JsonValueKind.Null);
        root.GetProperty("intendedOutputs").GetProperty("secretsFile").GetString().Should().Be(secretsPath);
        File.Exists(secretsPath).Should().BeFalse();
    }

    [Test]
    public async Task InvokeAsync_ShouldEmitStructuredJsonFailure_WithoutExposingPartialPlan()
    {
        var validPath = CreateFile("valid.bin", 4);
        var listPath = CreateFile("inputs.txt", $"{validPath}\nmissing.bin\n");
        var standardOut = new StringWriter();

        var exitCode = await CliApplication.InvokeAsync([
                                                            "upload",
                                                            "--files-from",
                                                            listPath,
                                                            "--queue-out",
                                                            Path.Combine(_rootDirectory, "queue.json"),
                                                            "--dry-run",
                                                            "--json",
                                                            "--no-banner"
                                                        ],
                                                        CreateServices(standardOut, new()),
                                                        CancellationToken.None);

        exitCode.Should().Be(1);
        using var document = JsonDocument.Parse(standardOut.ToString());
        var root = document.RootElement;
        root.GetProperty("status").GetString().Should().Be("invalid");
        root.GetProperty("files").GetArrayLength().Should().Be(0);
        root.GetProperty("totals").EnumerateObject().Select(static property => property.Value.GetInt64()).Should().OnlyContain(value => value == 0);
        root.GetProperty("intendedOutputs").EnumerateObject().Should().OnlyContain(static property => property.Value.ValueKind == JsonValueKind.Null);
        var error = root.GetProperty("errors").EnumerateArray().Single();
        error.GetProperty("message").GetString().Should().Contain("File is missing");
        error.GetProperty("source").GetString().Should().Be(listPath);
        error.GetProperty("recordNumber").GetInt32().Should().Be(2);
    }

    [Test]
    public async Task InvokeAsync_ShouldIdentifyEveryFailingCommandLineFile()
    {
        var missingFirst = Path.Combine(_rootDirectory, "missing-first.bin");
        var present = CreateFile("present.bin", 3);
        var missingSecond = Path.Combine(_rootDirectory, "missing-second.bin");
        String[] arguments =
        [
            "upload",
            missingFirst,
            present,
            missingSecond,
            "--dry-run",
            "--no-banner"
        ];
        var jsonOut = new StringWriter();
        var plainError = new StringWriter();

        var jsonExit = await CliApplication.InvokeAsync([.. arguments, "--json"], CreateServices(jsonOut, new()), CancellationToken.None);
        var plainExit = await CliApplication.InvokeAsync(arguments, CreateServices(new(), plainError), CancellationToken.None);

        jsonExit.Should().Be(1);
        plainExit.Should().Be(1);
        using var document = JsonDocument.Parse(jsonOut.ToString());
        var errors = document.RootElement.GetProperty("errors").EnumerateArray().ToArray();
        errors.Select(static error => error.GetProperty("message").GetString()).Should().Equal(
            $"{missingFirst}: File is missing.",
            $"{missingSecond}: File is missing.");
        errors.Select(static error => error.GetProperty("source").GetString()).Should().AllBe("commandLine");
        plainError.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Should().Equal(
            $"{missingFirst}: File is missing.",
            $"{missingSecond}: File is missing.");
    }

    [Test]
    public async Task InvokeAsync_ShouldRejectInteractiveDryRun_AsStructuredJson()
    {
        var standardOut = new StringWriter();

        var exitCode = await CliApplication.InvokeAsync(["upload", "--interactive", "--dry-run", "--json", "--no-banner"],
                                                        CreateServices(standardOut, new()),
                                                        CancellationToken.None);

        exitCode.Should().Be(1);
        using var document = JsonDocument.Parse(standardOut.ToString());
        var error = document.RootElement.GetProperty("errors").EnumerateArray().Single();
        error.GetProperty("message").GetString().Should().Be("--dry-run cannot be combined with --interactive.");
        error.GetProperty("source").GetString().Should().Be("commandLine");
        error.GetProperty("recordNumber").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [TestCase("upload")]
    [TestCase("raw")]
    public async Task InvokeAsync_ShouldReportAnOnlyExcludedLinkDirectoryWithoutInvalidatingTheJsonPlan(String command)
    {
        var inputDirectory = CreateDirectory("inputs");
        var visiblePath = CreateFile("visible.bin", 3);
        var outsidePath = CreateFile("outside/secret.bin", 3);
        var linkPath = Path.Combine(inputDirectory, "linked.bin");
        File.CreateSymbolicLink(linkPath, outsidePath);
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        String[] arguments = command == "raw"
            ? ["upload", "raw", visiblePath, inputDirectory, "--recursive", "--dry-run", "--json", "--no-banner"]
            : ["upload", visiblePath, inputDirectory, "--recursive", "--dry-run", "--json", "--no-banner"];

        var exitCode = await CliApplication.InvokeAsync(arguments, CreateServices(standardOut, standardError), CancellationToken.None);

        exitCode.Should().Be(0);
        standardError.ToString().Should().Contain(linkPath).And.Contain("will not be uploaded");
        using var document = JsonDocument.Parse(standardOut.ToString());
        document.RootElement.GetProperty("status").GetString().Should().Be("valid");
        document.RootElement.GetProperty("files").EnumerateArray().Single().GetProperty("sourcePath").GetString().Should().Be(visiblePath);
        document.RootElement.GetProperty("totals").GetProperty("excludedFiles").GetInt32().Should().Be(1);
    }

    [Test]
    public async Task InvokeAsync_ShouldValidateLocalOptionAndOutputConflicts_AsStructuredFailures()
    {
        var filePath = CreateFile("conflict.bin", 3);
        var existingPath = CreateFile("existing.queue.json", "existing");

        foreach (var arguments in new[]
                 {
                     new[]
                     {
                         "upload",
                         filePath,
                         "--direct-http",
                         "--queue-out",
                         "queue.json",
                         "--dry-run",
                         "--json",
                         "--no-banner"
                     },
                     new[]
                     {
                         "upload",
                         filePath,
                         "--queue-out",
                         existingPath,
                         "--dry-run",
                         "--json",
                         "--no-banner"
                     }
                 })
        {
            var standardOut = new StringWriter();
            var exitCode = await CliApplication.InvokeAsync(arguments, CreateServices(standardOut, new()), CancellationToken.None);

            exitCode.Should().Be(1);
            using var document = JsonDocument.Parse(standardOut.ToString());
            document.RootElement.GetProperty("status").GetString().Should().Be("invalid");
            document.RootElement.GetProperty("errors").GetArrayLength().Should().Be(1);
        }

        (await File.ReadAllTextAsync(existingPath)).Should().Be("existing");
    }

    [Test]
    public async Task InvokeAsync_ShouldWriteStablePlainContract()
    {
        var filePath = CreateFile("plain.bin", 2);
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        var releaseClient = new StubUpdateReleaseClient("999.0.0");
        var cache = new InMemoryUpdateCheckCache();

        var exitCode = await CliApplication.InvokeAsync(["upload", "raw", filePath, "--dry-run", "--no-banner"],
                                                        CreateServices(standardOut, standardError, releaseClient: releaseClient, cache: cache),
                                                        CancellationToken.None);

        exitCode.Should().Be(0);
        standardError.ToString().Should().BeEmpty();
        standardOut.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Should().Equal([
            "dry-run-status:valid",
            $"file:{filePath}",
            "plaintext-bytes:2",
            "encrypted-bytes:18",
            "selected-files:1",
            "excluded-files:0",
            "total-plaintext-bytes:2",
            "total-encrypted-bytes:18",
            .. ExpectedUncheckedValidations.Select(static validation => $"unchecked-validation:{validation}")
        ]);
        releaseClient.RequestCount.Should().Be(0);
        cache.WriteCount.Should().Be(0);
    }

    [Test]
    public async Task InvokeAsync_ShouldWriteUploadQueueDestination_InPlainContract()
    {
        var filePath = CreateFile("nested/upload.bin", 6);
        var queuePath = Path.Combine(_rootDirectory, "plain.queue.json");
        var standardOut = new StringWriter();

        var exitCode = await CliApplication.InvokeAsync([
                                                            "upload", filePath,
                                                            "--queue-out", queuePath,
                                                            "--input-root", _rootDirectory,
                                                            "--dry-run",
                                                            "--no-banner"
                                                        ],
                                                        CreateServices(standardOut, new()),
                                                        CancellationToken.None);

        exitCode.Should().Be(0);
        standardOut.ToString().Should().Contain($"file:{filePath}{Environment.NewLine}")
                   .And.Contain($"queue-destination:nested/upload.bin{Environment.NewLine}")
                   .And.Contain($"intended-queue-file:{queuePath}{Environment.NewLine}");
        File.Exists(queuePath).Should().BeFalse();
    }

    [SetUp]
    public void SetUp()
    {
        _rootDirectory = Path.Combine(TestContext.CurrentContext.WorkDirectory,
                                      "artifacts",
                                      "upload-dry-run-tests",
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

    private static CliApplicationServices CreateServices(StringWriter standardOut,
                                                         StringWriter standardError,
                                                         String? configFilePath = null,
                                                         StubUpdateReleaseClient? releaseClient = null,
                                                         InMemoryUpdateCheckCache? cache = null,
                                                         Action? onHttpClientCreation = null) =>
        new(FakeConfiguration.Resolver(configFilePath: configFilePath),
            _ =>
            {
                onHttpClientCreation?.Invoke();
                throw new AssertionException("HTTP client should not have been created.");
            },
            standardOut,
            standardError,
            new FakeInteractiveSession(),
            TimeProvider.System,
            new PlainDownloadProgressReporterFactory(standardOut, standardError, TimeProvider.System),
            FixedTerminalCapabilityProvider.Plain)
        {
            UpdateServices = FakeUpdateServices.Create(releaseClient, cache)
        };

    private String CreateDirectory(String relativePath)
    {
        var path = Path.Combine(_rootDirectory, relativePath);
        Directory.CreateDirectory(path);
        return path;
    }

    private String CreateFile(String relativePath, Int32 length)
    {
        var content = new String('x', length);
        return CreateFile(relativePath, content);
    }

    private String CreateFile(String relativePath, String content)
    {
        var path = Path.Combine(_rootDirectory, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }
}
