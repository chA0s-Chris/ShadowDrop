// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Admin;

using ShadowDrop.Api.Configuration;
using ShadowDrop.Api.Infrastructure.Security;
using ShadowDrop.Api.Shares;
using ShadowDrop.Api.Uploads;

public static class AdminEndpoints
{
    public static WebApplication MapAdminEndpoints(this WebApplication app, ShadowDropOptions options)
    {
        if (options.ApiExposure.EnableAdminOperations)
        {
            var adminRoutes = app.MapGroup("/api/admin")
                                 .RequireAdminBearerToken();

            var managementRoutes = adminRoutes.MapGroup("/management");
            managementRoutes.MapGet("/ping", () => Results.Ok(new
            {
                Status = "management-skeleton"
            }));

            var shareRoutes = adminRoutes.MapGroup("/shares");
            shareRoutes.MapPost("/", CreateShareAsync);
            shareRoutes.MapPost("/cleanup", CleanupSharesAsync);
            shareRoutes.MapPost("/{shareId:guid}/revoke", RevokeShareAsync);

            var uploadRoutes = adminRoutes.MapGroup("/uploads");
            uploadRoutes.MapGet("/capabilities", GetUploadCapabilities);
            uploadRoutes.MapPost("/reservations", ReserveUploadAsync);
            uploadRoutes.MapPost("/", UploadAsync)
                        .DisableAntiforgery();
            uploadRoutes.MapGet("/{fileId:guid}", GetUploadedFileMetadataAsync);

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

    private static async Task<IResult> CreateShareAsync(CreateShareRequest? request,
                                                        CreateShareService createShareService,
                                                        ILoggerFactory loggerFactory,
                                                        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return Results.BadRequest(new
            {
                Error = "Invalid share request."
            });
        }

        try
        {
            var result = await createShareService.CreateAsync(request, cancellationToken);
            return Results.Created($"/api/admin/shares/{result.ShareId}", result);
        }
        catch (CreateShareValidationException exception)
        {
            loggerFactory.CreateLogger(typeof(AdminEndpoints))
                         .LogWarning(exception, "Share request validation failed");
            return Results.BadRequest(new
            {
                Error = "Invalid share request."
            });
        }
    }

    private static IResult GetUploadCapabilities(ShadowDropOptions options)
    {
        var maxFilePayloadBytes = UploadLimitCalculator.ResolveMaxFilePayloadBytes(options.Upload.MaxBytes);
        return Results.Ok(new UploadCapabilitiesResult(options.Upload.MaxBytes,
                                                       UploadLimitCalculator.MultipartEnvelopeAllowanceBytes,
                                                       maxFilePayloadBytes));
    }

    private static async Task<IResult> GetUploadedFileMetadataAsync(Guid fileId,
                                                                    IUploadedFileMetadataRepository repository,
                                                                    CancellationToken cancellationToken)
    {
        var record = await repository.GetAsync(fileId, cancellationToken);
        return record is null
            ? Results.NotFound()
            : Results.Ok(record);
    }

    private static async Task<IResult> ReserveUploadAsync(IUploadedFileMetadataRepository repository,
                                                          CancellationToken cancellationToken)
    {
        var fileId = await repository.ReserveFileIdAsync(cancellationToken);
        return Results.Created($"/api/admin/uploads/{fileId}", new UploadReservationResult(fileId));
    }

    private static async Task<IResult> RevokeShareAsync(Guid shareId,
                                                        ShareRevocationService shareRevocationService,
                                                        CancellationToken cancellationToken)
    {
        var revoked = await shareRevocationService.RevokeAsync(shareId, cancellationToken);
        return revoked ? Results.NoContent() : Results.NotFound();
    }

    private static async Task<IResult> UploadAsync(HttpRequest request,
                                                   UploadPersistenceService uploadPersistenceService,
                                                   ShadowDropOptions options,
                                                   ILoggerFactory loggerFactory,
                                                   CancellationToken cancellationToken)
    {
        try
        {
            var uploadRequest = await MultipartUploadRequestReader.ReadAsync(request, cancellationToken, options.Upload.MaxBytes);
            await using var encryptedContent = uploadRequest.EncryptedContent;
            var result = await uploadPersistenceService.PersistAsync(uploadRequest.Request, encryptedContent, cancellationToken);

            return Results.Created($"/api/admin/uploads/{result.FileId}", result);
        }
        catch (UploadValidationException exception)
        {
            loggerFactory.CreateLogger(typeof(AdminEndpoints))
                         .LogWarning(exception, "Upload request validation failed");
            return Results.BadRequest(new
            {
                Error = "Invalid upload request."
            });
        }
        catch (UploadPayloadTooLargeException exception)
        {
            loggerFactory.CreateLogger(typeof(AdminEndpoints))
                         .LogWarning(exception, "Upload request exceeded the configured size limit");
            return Results.Json(new
            {
                Error = "Upload payload too large."
            }, statusCode: StatusCodes.Status413PayloadTooLarge);
        }
    }
}
