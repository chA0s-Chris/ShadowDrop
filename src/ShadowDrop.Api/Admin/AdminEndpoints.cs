// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Admin;

using Microsoft.Extensions.Primitives;
using ShadowDrop.Api.Configuration;
using ShadowDrop.Api.Infrastructure.Security;
using ShadowDrop.Api.Shares;
using ShadowDrop.Api.Status;
using ShadowDrop.Contracts;

public static class AdminEndpoints
{
    public static WebApplication MapAdminEndpoints(this WebApplication app, ShadowDropOptions options)
    {
        if (options.ApiExposure.EnableAdminOperations)
        {
            app.MapGet("/api/admin/status", GetStatusAsync)
               .WithName("AdminServerStatus")
               .WithMetadata(new OperationalAuditMetadata("admin-status-view"))
               .AddEndpointFilter<OperationalAuditEndpointFilter>()
               .AddEndpointFilter<AdminBearerTokenEndpointFilter>();

            app.MapGet("/api/admin/shares", ListSharesAsync)
               .WithName("AdminShareList")
               .WithMetadata(new OperationalAuditMetadata("admin-share-list"))
               .AddEndpointFilter<OperationalAuditEndpointFilter>()
               .AddEndpointFilter<OperationalUnauthorizedResultFilter>()
               .AddEndpointFilter<AdminBearerTokenEndpointFilter>();

            app.MapGet("/api/admin/shares/{shareId}", InspectShareAsync)
               .WithName("AdminShareInspect")
               .WithMetadata(new OperationalAuditMetadata(OperationalAuditEndpointFilter.ShareInspectionOperation))
               .AddEndpointFilter<OperationalAuditEndpointFilter>()
               .AddEndpointFilter<OperationalUnauthorizedResultFilter>()
               .AddEndpointFilter<AdminBearerTokenEndpointFilter>();

            var adminRoutes = app.MapGroup("/api/admin")
                                 .RequireAdminBearerToken();

            var managementRoutes = adminRoutes.MapGroup("/management");
            managementRoutes.MapGet("/ping", () => Results.Ok(new
            {
                Status = "management-skeleton"
            }));

            var shareRoutes = adminRoutes.MapGroup("/shares");
            shareRoutes.MapPost("/cleanup", CleanupSharesAsync);
            shareRoutes.MapPost("/{shareId:guid}/revoke", RevokeShareAsync);

            adminRoutes.MapUploadCredentialEndpoints();
        }

        return app;
    }

    private static async Task<IResult> CleanupSharesAsync(ShareCleanupRunner cleanupRunner,
                                                          CancellationToken cancellationToken)
    {
        var result = await cleanupRunner.RunIfIdleAsync(cancellationToken);
        return Results.Ok(result);
    }

    private static IResult Error(String reason, Int32 statusCode) =>
        Results.Json(new OperationalErrorContract(reason),
                     OperationalStatusJsonSerializerContext.Default.OperationalErrorContract,
                     statusCode: statusCode);

    private static async Task<IResult> GetStatusAsync(OperationalStatusService service, CancellationToken cancellationToken) =>
        StatusEndpoints.ToResult(await service.GetAdminAsync(cancellationToken));

    private static async Task<IResult> InspectShareAsync(
        HttpContext context,
        ShareInspectionService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var includeFilenames = ParseIncludeFilenames(context.Request.Query["includeFilenames"]);
            context.Items[OperationalAuditEndpointFilter.FilenamesIncludedItemKey] = includeFilenames;
            if (!Guid.TryParse(context.Request.RouteValues["shareId"]?.ToString(), out var shareId)
                || shareId == Guid.Empty)
            {
                return Error(OperationalErrorReasons.InvalidRequest, StatusCodes.Status400BadRequest);
            }

            var inspection = await service.GetAsync(shareId, includeFilenames, cancellationToken);
            return inspection is null
                ? Error(OperationalErrorReasons.NotFound, StatusCodes.Status404NotFound)
                : Results.Json(inspection, OperationalStatusJsonSerializerContext.Default.ShareInspectionContract);
        }
        catch (OperationalValidationException exception)
        {
            return Error(exception.Reason, StatusCodes.Status400BadRequest);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return Error(OperationalErrorReasons.OperationFailed, StatusCodes.Status500InternalServerError);
        }
    }

    private static async Task<IResult> ListSharesAsync(
        HttpRequest request,
        ShareListService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var page = await service.GetAsync(request.Query["status"],
                                              request.Query["pageSize"],
                                              request.Query["cursor"],
                                              cancellationToken);
            return Results.Json(page, OperationalStatusJsonSerializerContext.Default.ShareListPageContract);
        }
        catch (OperationalValidationException exception)
        {
            return Error(exception.Reason, StatusCodes.Status400BadRequest);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return Error(OperationalErrorReasons.OperationFailed, StatusCodes.Status500InternalServerError);
        }
    }

    private static Boolean ParseIncludeFilenames(StringValues values)
    {
        if (values.Count == 0)
        {
            return false;
        }

        if (values.Count != 1)
        {
            throw new OperationalValidationException(OperationalErrorReasons.InvalidRequest);
        }

        return values[0] switch
        {
            "true" => true,
            "false" => false,
            _ => throw new OperationalValidationException(OperationalErrorReasons.InvalidRequest)
        };
    }

    private static async Task<IResult> RevokeShareAsync(Guid shareId,
                                                        ShareRevocationService shareRevocationService,
                                                        CancellationToken cancellationToken)
    {
        var revoked = await shareRevocationService.RevokeAsync(shareId, cancellationToken);
        return revoked ? Results.NoContent() : Results.NotFound();
    }
}
