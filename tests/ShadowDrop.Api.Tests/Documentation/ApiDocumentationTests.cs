// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Documentation;

using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using ShadowDrop.Api;
using System.Text.RegularExpressions;

public sealed partial class ApiDocumentationTests
{
    [Test]
    public void ApiDocumentation_ShouldListEveryRegisteredRoutePattern()
    {
        using var factory = new DocumentationApiFactory();
        var routePatterns = factory.Services
                                   .GetServices<EndpointDataSource>()
                                   .SelectMany(source => source.Endpoints)
                                   .OfType<RouteEndpoint>()
                                   .Select(endpoint => NormalizeRoutePattern(endpoint.RoutePattern.RawText))
                                   .Distinct()
                                   .ToList();
        routePatterns.Should().Contain("/health/live", "enumerating the endpoint data sources must yield the API's routes");

        var documentation = File.ReadAllText(LocateApiDocumentation());
        var documentedRoutePatterns = EndpointTableRowRegex().Matches(documentation)
                                                             .Select(match => match.Groups["route"].Value)
                                                             .ToHashSet(StringComparer.Ordinal);

        var undocumentedRoutePatterns = routePatterns.Where(pattern => !documentedRoutePatterns.Contains(pattern))
                                                     .ToList();
        undocumentedRoutePatterns.Should().BeEmpty("every registered route pattern must appear in docs/API.md");
    }

    [GeneratedRegex(@"^\|\s*`(?:CONNECT|DELETE|GET|HEAD|OPTIONS|PATCH|POST|PUT|TRACE)`\s*\|\s*`(?<route>/[^`]+)`\s*\|",
                    RegexOptions.CultureInvariant | RegexOptions.Multiline)]
    private static partial Regex EndpointTableRowRegex();

    private static String LocateApiDocumentation()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "docs", "API.md");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("docs/API.md was not found in any directory above the test base directory.");
    }

    private static String NormalizeRoutePattern(String? rawText)
    {
        rawText.Should().NotBeNullOrWhiteSpace("every registered endpoint must expose a raw route pattern");
        var pattern = rawText.StartsWith('/') ? rawText : $"/{rawText}";
        return pattern.Length > 1 ? pattern.TrimEnd('/') : pattern;
    }

    private sealed class DocumentationApiFactory : WebApplicationFactory<Program>
    {
        private readonly Dictionary<String, String?> _previousValues = [];
        private readonly String _rootDirectory;

        public DocumentationApiFactory()
        {
            _rootDirectory = Path.Combine(AppContext.BaseDirectory, "artifacts", $"api-documentation-{Guid.NewGuid():N}");
            Directory.CreateDirectory(_rootDirectory);
            SetEnvironmentVariable("SHADOWDROP_BOOTSTRAP_ADMIN_TOKEN", "api-documentation-test-token");
            SetEnvironmentVariable("ShadowDrop__Metadata__LiteDbPath", Path.Combine(_rootDirectory, "metadata", "shadowdrop.db"));
            SetEnvironmentVariable("ShadowDrop__Storage__LocalRoot", Path.Combine(_rootDirectory, "storage"));
            SetEnvironmentVariable("ShadowDrop__ApiExposure__EnableAdminOperations", "true");
            SetEnvironmentVariable("ShadowDrop__ApiExposure__EnableUploads", "true");
            SetEnvironmentVariable("ShadowDrop__ApiExposure__EnablePublicDownloads", "true");
        }

        protected override void Dispose(Boolean disposing)
        {
            if (disposing)
            {
                foreach (var (key, value) in _previousValues)
                {
                    Environment.SetEnvironmentVariable(key, value);
                }
            }

            base.Dispose(disposing);
            if (Directory.Exists(_rootDirectory))
            {
                Directory.Delete(_rootDirectory, true);
            }
        }

        private void SetEnvironmentVariable(String key, String? value)
        {
            _previousValues[key] = Environment.GetEnvironmentVariable(key);
            Environment.SetEnvironmentVariable(key, value);
        }
    }
}
