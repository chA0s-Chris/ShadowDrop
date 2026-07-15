// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Uploads;

using ShadowDrop.Api.Configuration;
using ShadowDrop.Api.Infrastructure.Security;

public static class UploadEndpoints
{
    public static WebApplication MapUploadEndpoints(this WebApplication app, ShadowDropOptions options)
    {
        if (!options.ApiExposure.UploadsEnabled)
        {
            return app;
        }

        var routes = app.MapGroup("/api/uploads")
                        .RequireUploadOrAdminBearerToken();
        routes.MapGet("/capabilities", GetCapabilities);
        routes.MapPost("/reservations", ReserveAsync);
        routes.MapPost("/", UploadAsync)
              .DisableAntiforgery();
        routes.MapGet("/{fileId:guid}", GetMetadataAsync);
        return app;
    }

    private static IResult GetCapabilities(HttpContext httpContext, ShadowDropOptions options)
    {
        var authorizationContext = httpContext.GetUploadAuthorizationContext();
        var maxFilePayloadBytes = ResolveEffectiveMaxFilePayloadBytes(options, authorizationContext);
        return Results.Ok(new UploadCapabilitiesResult(options.Upload.MaxBytes,
                                                       UploadLimitCalculator.MultipartEnvelopeAllowanceBytes,
                                                       maxFilePayloadBytes,
                                                       authorizationContext.MaxEncryptedShareBytes));
    }

    private static async Task<IResult> GetMetadataAsync(Guid fileId,
                                                        HttpContext httpContext,
                                                        IUploadedFileMetadataRepository repository,
                                                        CancellationToken cancellationToken)
    {
        var authorizationContext = httpContext.GetUploadAuthorizationContext();
        var record = await repository.GetAsync(fileId, cancellationToken);
        if (record is null
            || (!authorizationContext.IsBootstrapAdmin
                && record.OwnerCredentialId != authorizationContext.CredentialId))
        {
            return Results.NotFound();
        }

        return Results.Ok(UploadedFileProjection.FromRecord(record));
    }

    private static async Task<IResult> ReserveAsync(HttpContext httpContext,
                                                    IUploadedFileMetadataRepository repository,
                                                    CancellationToken cancellationToken)
    {
        var authorizationContext = httpContext.GetUploadAuthorizationContext();
        var fileId = authorizationContext.CredentialId is { } ownerCredentialId
            ? await repository.ReserveFileIdAsync(ownerCredentialId, cancellationToken)
            : await repository.ReserveFileIdAsync(cancellationToken);
        return Results.Created($"/api/uploads/{fileId}", new UploadReservationResult(fileId));
    }

    private static Int64 ResolveEffectiveMaxFilePayloadBytes(ShadowDropOptions options,
                                                             UploadCredentialAuthorizationContext authorizationContext)
    {
        var serverLimit = UploadLimitCalculator.ResolveMaxFilePayloadBytes(options.Upload.MaxBytes);
        return authorizationContext.MaxEncryptedFileBytes is { } credentialLimit
            ? Math.Min(serverLimit, credentialLimit)
            : serverLimit;
    }

    private static async Task<IResult> UploadAsync(HttpRequest request,
                                                   HttpContext httpContext,
                                                   UploadPersistenceService uploadPersistenceService,
                                                   ShadowDropOptions options,
                                                   ILoggerFactory loggerFactory,
                                                   CancellationToken cancellationToken)
    {
        try
        {
            var authorizationContext = httpContext.GetUploadAuthorizationContext();
            var maxFilePayloadBytes = ResolveEffectiveMaxFilePayloadBytes(options, authorizationContext);
            var uploadRequest = await MultipartUploadRequestReader.ReadAsync(request,
                                                                             cancellationToken,
                                                                             options.Upload.MaxBytes,
                                                                             maxEncryptedFileBytes: maxFilePayloadBytes);
            await using var encryptedContent = uploadRequest.EncryptedContent;
            var result = await uploadPersistenceService.PersistAsync(uploadRequest.Request,
                                                                     encryptedContent,
                                                                     authorizationContext,
                                                                     cancellationToken);
            return Results.Created($"/api/uploads/{result.FileId}", result);
        }
        catch (UploadValidationException exception)
        {
            loggerFactory.CreateLogger(typeof(UploadEndpoints))
                         .LogWarning(exception, "Upload request validation failed");
            return Results.BadRequest(new
            {
                Error = "Invalid upload request."
            });
        }
        catch (UploadPayloadTooLargeException exception)
        {
            loggerFactory.CreateLogger(typeof(UploadEndpoints))
                         .LogWarning(exception, "Upload request exceeded the effective size limit");
            return Results.Json(new
            {
                Error = "Upload payload too large."
            }, statusCode: StatusCodes.Status413PayloadTooLarge);
        }
    }
}
