// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Admin;

using ShadowDrop.Api.CompositionRoot;
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

            var uploadRoutes = adminRoutes.MapGroup("/uploads");
            uploadRoutes.MapPost("/reservations", ReserveUploadAsync);
            uploadRoutes.MapPost("/", UploadAsync)
                        .RequireRateLimiting(RateLimiting.UploadRateLimitPolicyName)
                        .DisableAntiforgery();
            uploadRoutes.MapGet("/{fileId:guid}", GetUploadedFileMetadataAsync);
        }

        return app;
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
                         .LogWarning(exception, "Share request validation failed.");
            return Results.BadRequest(new
            {
                Error = "Invalid share request."
            });
        }
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

    private static async Task<IResult> UploadAsync(HttpRequest request,
                                                   UploadPersistenceService uploadPersistenceService,
                                                   ILoggerFactory loggerFactory,
                                                   CancellationToken cancellationToken)
    {
        try
        {
            var uploadRequest = await MultipartUploadRequestReader.ReadAsync(request, cancellationToken);
            await using var encryptedContent = uploadRequest.EncryptedContent;
            var result = await uploadPersistenceService.PersistAsync(uploadRequest.Request, encryptedContent, cancellationToken);

            return Results.Created($"/api/admin/uploads/{result.FileId}", result);
        }
        catch (UploadValidationException exception)
        {
            loggerFactory.CreateLogger(typeof(AdminEndpoints))
                         .LogWarning(exception, "Upload request validation failed.");
            return Results.BadRequest(new
            {
                Error = "Invalid upload request."
            });
        }
        catch (UploadPayloadTooLargeException exception)
        {
            loggerFactory.CreateLogger(typeof(AdminEndpoints))
                         .LogWarning(exception, "Upload request exceeded the configured size limit.");
            return Results.Json(new
            {
                Error = "Upload payload too large."
            }, statusCode: StatusCodes.Status413PayloadTooLarge);
        }
    }
}
