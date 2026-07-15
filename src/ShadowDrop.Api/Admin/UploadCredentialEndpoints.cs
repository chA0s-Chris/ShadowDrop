// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Admin;

using ShadowDrop.Api.Infrastructure.Security;

public static class UploadCredentialEndpoints
{
    internal const Int32 DefaultPageSize = 50;
    internal const Int32 MaxPageSize = 200;

    public static RouteGroupBuilder MapUploadCredentialEndpoints(this RouteGroupBuilder adminRoutes)
    {
        ArgumentNullException.ThrowIfNull(adminRoutes);

        var credentialRoutes = adminRoutes.MapGroup("/upload-credentials");
        credentialRoutes.MapPost("/", CreateAsync);
        credentialRoutes.MapGet("/", ListAsync);
        credentialRoutes.MapGet("/{credentialId:guid}", InspectAsync);
        credentialRoutes.MapPost("/{credentialId:guid}/revoke", RevokeAsync);
        return adminRoutes;
    }

    private static async Task<IResult> CreateAsync(CreateUploadCredentialRequest? request,
                                                   UploadCredentialService uploadCredentialService,
                                                   ILoggerFactory loggerFactory,
                                                   CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return Results.BadRequest(new
            {
                Error = "Invalid upload credential request."
            });
        }

        try
        {
            var result = await uploadCredentialService.CreateAsync(new(request.Name,
                                                                       request.ExpiresAtUtc,
                                                                       request.MaxEncryptedFileBytes,
                                                                       request.MaxEncryptedShareBytes), cancellationToken);
            return Results.Created($"/api/admin/upload-credentials/{result.Credential.CredentialId}",
                                   new CreateUploadCredentialResult(UploadCredentialProjection.FromRecord(result.Credential),
                                                                    result.Token));
        }
        catch (UploadCredentialValidationException exception)
        {
            loggerFactory.CreateLogger(typeof(UploadCredentialEndpoints))
                         .LogWarning(exception, "Upload credential request validation failed");
            return Results.BadRequest(new
            {
                Error = "Invalid upload credential request."
            });
        }
    }

    private static async Task<IResult> InspectAsync(Guid credentialId,
                                                    IUploadCredentialRepository repository,
                                                    CancellationToken cancellationToken)
    {
        var record = await repository.GetAsync(credentialId, cancellationToken);
        return record is null
            ? Results.NotFound()
            : Results.Ok(UploadCredentialProjection.FromRecord(record));
    }

    private static async Task<IResult> ListAsync(String? cursor,
                                                 Int32? limit,
                                                 IUploadCredentialRepository repository,
                                                 CancellationToken cancellationToken)
    {
        UploadCredentialListCursor? listCursor = null;
        if (cursor is not null && !UploadCredentialListCursor.TryDecode(cursor, out listCursor))
        {
            return Results.BadRequest(new
            {
                Error = "Invalid cursor."
            });
        }

        if (limit is <= 0)
        {
            return Results.BadRequest(new
            {
                Error = "Invalid limit."
            });
        }

        var pageSize = Math.Min(limit ?? DefaultPageSize, MaxPageSize);
        var page = await repository.ListNewestFirstAsync(pageSize, listCursor, cancellationToken);
        return Results.Ok(new UploadCredentialListResult(page.Credentials.Select(UploadCredentialProjection.FromRecord).ToList(),
                                                         page.NextCursor?.Encode()));
    }

    private static async Task<IResult> RevokeAsync(Guid credentialId,
                                                   IUploadCredentialRepository repository,
                                                   TimeProvider timeProvider,
                                                   CancellationToken cancellationToken)
    {
        var record = await repository.RevokeAsync(credentialId, timeProvider.GetUtcNow(), cancellationToken);
        return record is null ? Results.NotFound() : Results.NoContent();
    }
}
