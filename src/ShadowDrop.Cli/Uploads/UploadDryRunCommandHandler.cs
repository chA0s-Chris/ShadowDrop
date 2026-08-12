// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Uploads;

using ShadowDrop.Cli.Queues;
using ShadowDrop.Cli.Results;

internal sealed class UploadDryRunCommandHandler
{
    private static readonly IReadOnlyList<String> UncheckedValidations =
    [
        "serverAvailability",
        "authentication",
        "uploadCapabilities",
        "accountQuota",
        "serverFileSizeLimit"
    ];

    private readonly TextWriter _standardError;

    private readonly TextReader _standardInput;
    private readonly TextWriter _standardOut;

    public UploadDryRunCommandHandler(TextReader standardInput, TextWriter standardOut, TextWriter standardError)
    {
        _standardInput = standardInput;
        _standardOut = standardOut;
        _standardError = standardError;
    }

    public Task<Int32> ExecuteAsync(UploadCommandOptions options,
                                    Boolean interactive,
                                    Boolean tlsOptionsConflict,
                                    CancellationToken cancellationToken) =>
        ExecuteAsync(UploadDryRunPlanner.Create(options, _standardInput, interactive, tlsOptionsConflict), options.Json, cancellationToken);

    public Task<Int32> ExecuteAsync(UploadRawCommandOptions options,
                                    Boolean tlsOptionsConflict,
                                    CancellationToken cancellationToken) =>
        ExecuteAsync(UploadDryRunPlanner.Create(options, _standardInput, tlsOptionsConflict), options.Json, cancellationToken);

    private static UploadDryRunResult BuildInvalidResult(IReadOnlyList<UploadDryRunError> errors) =>
        new(UploadDryRunStatus.Invalid,
            [],
            new(0, 0, 0, 0),
            new(null, null),
            UncheckedValidations,
            errors.Select(static error => new UploadDryRunErrorResult(error.Message, error.Origin.Source, error.Origin.RecordNumber)).ToArray());

    private static UploadDryRunResult BuildValidResult(UploadDryRunPlan plan)
    {
        List<UploadDryRunFile> files = [];
        Int64 plaintextBytes = 0;
        Int64 encryptedBytes = 0;
        foreach (var file in plan.LocalPlan.Files)
        {
            QueueDestination? destination = null;
            plan.QueueDestinations?.TryGetValue(file.File.FullName, out destination);
            files.Add(new(file.File.FullName,
                          file.PlaintextLength,
                          file.EncryptedLength,
                          destination?.Path));
            plaintextBytes = checked(plaintextBytes + file.PlaintextLength);
            encryptedBytes = checked(encryptedBytes + file.EncryptedLength);
        }

        return new(UploadDryRunStatus.Valid,
                   files,
                   new(files.Count, plan.LocalPlan.ExcludedFileCount, plaintextBytes, encryptedBytes),
                   new(plan.QueueOut?.FullName, plan.SecretsOut?.FullName),
                   UncheckedValidations,
                   []);
    }

    private async Task<Int32> ExecuteAsync(UploadDryRunPlanningResult planningResult,
                                           Boolean json,
                                           CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = planningResult.IsValid
            ? BuildValidResult(planningResult.Plan ?? throw new InvalidOperationException("A valid planning result must contain a plan."))
            : BuildInvalidResult(planningResult.Errors);

        if (json)
        {
            await UploadDryRunResultWriter.WriteJsonAsync(_standardOut, result);
        }
        else
        {
            await UploadDryRunResultWriter.WritePlainAsync(_standardOut, _standardError, result);
        }

        return planningResult.IsValid ? 0 : 1;
    }
}
