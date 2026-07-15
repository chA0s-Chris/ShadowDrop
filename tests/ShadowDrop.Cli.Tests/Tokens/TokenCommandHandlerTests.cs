// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Tokens;

using FluentAssertions;
using NUnit.Framework;
using ShadowDrop.Cli.Configuration;
using ShadowDrop.Cli.Tokens;
using ShadowDrop.Tests.Fakes;
using System.Net;
using System.Text.Json;

public sealed class TokenCommandHandlerTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-07-14T12:00:00Z");

    [Test]
    public async Task TokenCreate_ShouldEmitSingleJsonObjectOnStdout_WhenJsonIsRequested()
    {
        var credentialId = Guid.NewGuid();
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ => new(HttpStatusCode.Created)
        {
            Content = new StringContent(CreationResponseJson(credentialId))
        }));
        var handler = CreateCreateHandler(httpClient, standardOut, standardError);

        var exitCode = await handler.ExecuteAsync(new("automation", null, null, null, null, null, true), CancellationToken.None);

        exitCode.Should().Be(0);
        var stdoutLines = ReadLines(standardOut);
        stdoutLines.Should().HaveCount(1, "--json must emit exactly one JSON value on stdout");
        using var document = JsonDocument.Parse(stdoutLines[0]);
        document.RootElement.GetProperty("token").GetString().Should().Be("sdu1.selector.secret");
        document.RootElement.GetProperty("credential").GetProperty("credentialId").GetGuid().Should().Be(credentialId);
        standardError.ToString().Should().NotContain("sdu1.selector.secret", "the one-time secret belongs on stdout only");
    }

    [Test]
    public async Task TokenCreate_ShouldPrintTokenOnStdout_AndOneTimeWarningOnStderr()
    {
        var credentialId = Guid.NewGuid();
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ => new(HttpStatusCode.Created)
        {
            Content = new StringContent(CreationResponseJson(credentialId))
        }));
        var handler = CreateCreateHandler(httpClient, standardOut, standardError);

        var exitCode = await handler.ExecuteAsync(new("automation", "12h", 1024, 4096, null, null, false), CancellationToken.None);

        exitCode.Should().Be(0);
        ReadLines(standardOut).Should().Equal($"credential-id:{credentialId}", "token:sdu1.selector.secret");
        standardError.ToString().Should().Contain("displayed once");
        standardError.ToString().Should().NotContain("sdu1.selector.secret");
    }

    [Test]
    public async Task TokenCreate_ShouldRejectInvalidInputsBeforeAnyHttpCall()
    {
        using var httpClient = new HttpClient(new NeverCalledHandler());
        const String nameError = "Credential name invalid or missing. Provide --name with 1 to 100 characters and no control characters.";
        var scenarios = new (TokenCreateCommandOptions Options, String ExpectedError)[]
        {
            (new(null, null, null, null, null, null, false), nameError),
            (new("   ", null, null, null, null, null, false), nameError),
            (new(new String('n', 101), null, null, null, null, null, false), nameError),
            (new("escape\u001bsequence", null, null, null, null, null, false), nameError),
            (new("name", "soon", null, null, null, null, false), "Expiration invalid. Use <amount><unit> such as 30d, 12h, or 45m."),
            (new("name", "3000000d", null, null, null, null, false), "Expiration invalid. Use <amount><unit> such as 30d, 12h, or 45m."),
            (new("name", null, 0, null, null, null, false), "The --max-file-bytes value must be positive."),
            (new("name", null, null, -5, null, null, false), "The --max-share-bytes value must be positive.")
        };

        foreach (var (options, expectedError) in scenarios)
        {
            var standardOut = new StringWriter();
            var standardError = new StringWriter();
            var handler = CreateCreateHandler(httpClient, standardOut, standardError);

            var exitCode = await handler.ExecuteAsync(options, CancellationToken.None);

            exitCode.Should().Be(1);
            standardOut.ToString().Should().BeEmpty();
            standardError.ToString().Trim().Should().Be(expectedError);
        }
    }

    [Test]
    public async Task TokenCreate_ShouldRequireAdminToken_WithoutFallingBackToUploadToken()
    {
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        using var httpClient = new HttpClient(new NeverCalledHandler());
        var resolver = FakeConfiguration.Resolver("https://shadowdrop.test", "upload-token-only");
        var handler = new TokenCreateCommandHandler(resolver, httpClient, standardOut, standardError, new FixedTimeProvider(Now));

        var exitCode = await handler.ExecuteAsync(new("automation", null, null, null, null, null, false), CancellationToken.None);

        exitCode.Should().Be(1);
        standardOut.ToString().Should().BeEmpty();
        standardError.ToString().Trim().Should().Be(AdminConfiguration.MissingAdminTokenError);
    }

    [Test]
    public async Task TokenCreate_ShouldResolveAdminTokenWithFlagOverEnvironmentOverConfigFile()
    {
        var configFilePath = Path.Combine(Path.GetTempPath(), $"shadowdrop-cli-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(configFilePath,
                                     """{"serverUrl":"https://shadowdrop.test","uploadToken":"upload-token","adminToken":"config-admin-token"}""");
        try
        {
            (await CaptureAuthorizationAsync(FakeConfiguration.Resolver(configFilePath: configFilePath), null))
                .Should().Be("Bearer config-admin-token", "the config-file adminToken applies when nothing else is set");

            var resolverWithEnvironment = FakeConfiguration.Resolver(configFilePath: configFilePath, adminToken: "env-admin-token");
            (await CaptureAuthorizationAsync(resolverWithEnvironment, null))
                .Should().Be("Bearer env-admin-token", "SHADOWDROP_ADMIN_TOKEN beats the config file");
            (await CaptureAuthorizationAsync(resolverWithEnvironment, "flag-admin-token"))
                .Should().Be("Bearer flag-admin-token", "--admin-token beats the environment");
        }
        finally
        {
            File.Delete(configFilePath);
        }
    }

    [Test]
    public async Task TokenInspect_ShouldValidateCredentialId_AndPrintDetails()
    {
        var credentialId = Guid.NewGuid();
        var invalidOut = new StringWriter();
        var invalidError = new StringWriter();
        using var neverCalledClient = new HttpClient(new NeverCalledHandler());
        var invalidHandler = new TokenInspectCommandHandler(CreateResolver(), neverCalledClient, invalidOut, invalidError);
        (await invalidHandler.ExecuteAsync(new("not-a-guid", null, null, false), CancellationToken.None)).Should().Be(1);
        invalidError.ToString().Trim().Should().Be("Credential id invalid or missing.");

        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ => new(HttpStatusCode.OK)
        {
            Content = new StringContent(ProjectionJson(credentialId))
        }));
        var handler = new TokenInspectCommandHandler(CreateResolver(), httpClient, standardOut, standardError);

        var exitCode = await handler.ExecuteAsync(new(credentialId.ToString(), null, null, false), CancellationToken.None);

        exitCode.Should().Be(0);
        var lines = ReadLines(standardOut);
        lines.Should().Contain($"credential-id:{credentialId}");
        lines.Should().Contain("name:automation");
        lines.Should().Contain("capability:upload-and-share");
        lines.Should().Contain("max-file-bytes:1024");
        lines.Should().Contain("max-share-bytes:-");
        standardError.ToString().Should().BeEmpty();
    }

    [Test]
    public async Task TokenList_ShouldPrintOneLinePerCredential_AndTheNextCursor()
    {
        var credentialId = Guid.NewGuid();
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ => new(HttpStatusCode.OK)
        {
            Content = new StringContent($$"""{"credentials":[{{ProjectionJson(credentialId)}}],"nextCursor":"cursor-a"}""")
        }));
        var handler = new TokenListCommandHandler(CreateResolver(), httpClient, standardOut, standardError);

        var exitCode = await handler.ExecuteAsync(new(null, null, null, null, false), CancellationToken.None);

        exitCode.Should().Be(0);
        var lines = ReadLines(standardOut);
        lines.Should().HaveCount(2);
        lines[0].Should().StartWith($"credential:{credentialId} created=2026-07-14T12:00:00Z expires=- revoked=- last-used=-");
        lines[0].Should().EndWith("name=automation");
        lines[1].Should().Be("next-cursor:cursor-a");
        standardError.ToString().Should().BeEmpty();
    }

    [Test]
    public async Task TokenList_ShouldRejectNonPositiveLimit_AndEmitSingleJsonValue()
    {
        using var neverCalledClient = new HttpClient(new NeverCalledHandler());
        var invalidError = new StringWriter();
        var invalidHandler = new TokenListCommandHandler(CreateResolver(), neverCalledClient, new StringWriter(), invalidError);
        (await invalidHandler.ExecuteAsync(new(null, 0, null, null, false), CancellationToken.None)).Should().Be(1);
        invalidError.ToString().Trim().Should().Be("The --limit value must be positive.");

        var standardOut = new StringWriter();
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ => new(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"credentials":[],"nextCursor":null}""")
        }));
        var handler = new TokenListCommandHandler(CreateResolver(), httpClient, standardOut, new StringWriter());

        (await handler.ExecuteAsync(new(null, null, null, null, true), CancellationToken.None)).Should().Be(0);

        var stdoutLines = ReadLines(standardOut);
        stdoutLines.Should().HaveCount(1);
        using var document = JsonDocument.Parse(stdoutLines[0]);
        document.RootElement.GetProperty("credentials").GetArrayLength().Should().Be(0);
    }

    [Test]
    public async Task TokenOutput_ShouldCollapseControlCharactersInCredentialNames()
    {
        var credential = new UploadCredentialCliProjection(Guid.NewGuid(),
                                                           "automation\ncredential:injected\u001b",
                                                           Now,
                                                           null,
                                                           null,
                                                           null,
                                                           "upload-and-share",
                                                           null,
                                                           null);

        var listLine = TokenOutput.FormatListLine(credential);
        var details = new StringWriter();
        await TokenOutput.WriteDetailsAsync(details, credential);

        listLine.Should().NotContain("\n").And.NotContain("\u001b");
        listLine.Should().EndWith("name=automation credential:injected ");
        details.ToString().Should().NotContain("\u001b");
        ReadLines(details).Should().HaveCount(9).And.Contain("name:automation credential:injected ");
    }

    [Test]
    public async Task TokenRevoke_ShouldReportSuccessOnStdout()
    {
        var credentialId = Guid.NewGuid();
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ => new(HttpStatusCode.NoContent)));
        var handler = new TokenRevokeCommandHandler(CreateResolver(), httpClient, standardOut, standardError);

        var exitCode = await handler.ExecuteAsync(new(credentialId.ToString(), null, null, false), CancellationToken.None);

        exitCode.Should().Be(0);
        ReadLines(standardOut).Should().Equal($"token-revoked:{credentialId}");
        standardError.ToString().Should().BeEmpty();

        var jsonOut = new StringWriter();
        using var jsonClient = new HttpClient(new StubHttpMessageHandler(_ => new(HttpStatusCode.NoContent)));
        var jsonHandler = new TokenRevokeCommandHandler(CreateResolver(), jsonClient, jsonOut, new StringWriter());
        (await jsonHandler.ExecuteAsync(new(credentialId.ToString(), null, null, true), CancellationToken.None)).Should().Be(0);
        using var document = JsonDocument.Parse(ReadLines(jsonOut).Should().ContainSingle().Subject);
        document.RootElement.GetProperty("credentialId").GetGuid().Should().Be(credentialId);
    }

    private static async Task<String?> CaptureAuthorizationAsync(CliConfigurationResolver resolver, String? adminTokenOverride)
    {
        String? capturedAuthorization = null;
        using var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            capturedAuthorization = request.Headers.Authorization?.ToString();
            return new(HttpStatusCode.NoContent);
        }));
        var handler = new TokenRevokeCommandHandler(resolver, httpClient, new StringWriter(), new StringWriter());

        (await handler.ExecuteAsync(new(Guid.NewGuid().ToString(), null, adminTokenOverride, false), CancellationToken.None)).Should().Be(0);
        return capturedAuthorization;
    }

    private static TokenCreateCommandHandler CreateCreateHandler(HttpClient httpClient, StringWriter standardOut, StringWriter standardError) =>
        new(CreateResolver(), httpClient, standardOut, standardError, new FixedTimeProvider(Now));

    private static CliConfigurationResolver CreateResolver() =>
        FakeConfiguration.Resolver("https://shadowdrop.test", "upload-token", adminToken: "admin-token");

    private static String CreationResponseJson(Guid credentialId) =>
        $$"""{"credential":{{ProjectionJson(credentialId)}},"token":"sdu1.selector.secret"}""";

    private static String ProjectionJson(Guid credentialId) =>
        $$"""
          {"credentialId":"{{credentialId}}","name":"automation","createdAtUtc":"2026-07-14T12:00:00Z",
          "expiresAtUtc":null,"revokedAtUtc":null,"lastUsedAtUtc":null,"capability":"upload-and-share",
          "maxEncryptedFileBytes":1024,"maxEncryptedShareBytes":null}
          """;

    private static String[] ReadLines(StringWriter writer) =>
        writer.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class NeverCalledHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new AssertionException("HTTP client should not have been called.");
    }
}
