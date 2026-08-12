// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Uploads;

using ShadowDrop.Cli.Output;
using ShadowDrop.Cli.Queues;
using ShadowDrop.Cli.Shares;
using System.Collections.Immutable;

internal sealed record UploadDryRunPlan(
    LocalUploadPlan LocalPlan,
    IReadOnlyDictionary<String, QueueDestination>? QueueDestinations,
    FileInfo? QueueOut,
    FileInfo? SecretsOut);

internal sealed record UploadDryRunPlanningResult(
    UploadDryRunPlan? Plan,
    ImmutableArray<UploadDryRunError> Errors)
{
    public Boolean IsValid => Plan is not null && Errors.IsEmpty;
}

internal sealed record UploadDryRunError(String Message, UploadSelectionOrigin Origin);

internal static class UploadDryRunPlanner
{
    public static UploadDryRunPlanningResult Create(UploadCommandOptions options,
                                                    TextReader standardInput,
                                                    Boolean interactive,
                                                    Boolean tlsOptionsConflict)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(standardInput);

        if (interactive)
        {
            return Invalid("--dry-run cannot be combined with --interactive.");
        }

        if (tlsOptionsConflict)
        {
            return Invalid("The --cacert and --insecure options cannot be combined. Choose one.");
        }

        if (!UploadCommandOptionsValidator.TryValidateLocalOptionCombinations(options, out var optionError))
        {
            return Invalid(optionError);
        }

        var workingDirectory = options.WorkingDirectory ?? Directory.GetCurrentDirectory();
        var inputResolution = ResolveInputs(options.Files,
                                            options.InputPaths ?? [],
                                            options.Recursive,
                                            options.IncludePatterns ?? [],
                                            options.ExcludePatterns ?? [],
                                            options.FilesFrom ?? [],
                                            workingDirectory,
                                            standardInput);
        if (!inputResolution.IsValid)
        {
            return FromInputErrors(inputResolution.Errors);
        }

        var planningResult = LocalUploadPlanner.Create(inputResolution.Selections, inputResolution.ExcludedFileCount);
        if (!planningResult.IsValid)
        {
            return FromLocalPlanningErrors(planningResult.Errors);
        }

        var localPlan = planningResult.Plan ?? throw new InvalidOperationException("A valid planning result must contain a plan.");
        var resolvedFiles = localPlan.Files.Select(static file => file.File).ToArray();
        options = options with
        {
            Files = resolvedFiles
        };
        if (!DisplayNameResolver.TryResolveForUpload(resolvedFiles,
                                                     options.DisplayName,
                                                     options.DisplayNameMappings,
                                                     out var displayNames,
                                                     out var displayNameError,
                                                     workingDirectory))
        {
            return Invalid(displayNameError!);
        }

        if (!UploadCommandHandler.TryResolveQueueDestinationPlan(options, displayNames, out var queueDestinations, out var queueError))
        {
            return Invalid(queueError);
        }

        if (!ShareOptions.TryValidate(options.ExpiresIn, options.DirectHttp, options.GenerateDownloadToken, out _, out var shareError))
        {
            return Invalid(shareError!);
        }

        if (!TryValidateOutputs(options.SecretsOut, options.QueueOut, options.Force, out var outputError))
        {
            return Invalid(outputError!);
        }

        return new(new(localPlan, queueDestinations, options.QueueOut, options.SecretsOut), []);
    }

    public static UploadDryRunPlanningResult Create(UploadRawCommandOptions options,
                                                    TextReader standardInput,
                                                    Boolean tlsOptionsConflict)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(standardInput);

        if (tlsOptionsConflict)
        {
            return Invalid("The --cacert and --insecure options cannot be combined. Choose one.");
        }

        if (!UploadInputOptionsValidator.TryValidate(options.Recursive,
                                                     options.IncludePatterns,
                                                     options.ExcludePatterns,
                                                     options.FilesFrom,
                                                     out var optionError))
        {
            return Invalid(optionError);
        }

        var inputResolution = ResolveInputs(options.Files,
                                            options.InputPaths ?? [],
                                            options.Recursive,
                                            options.IncludePatterns ?? [],
                                            options.ExcludePatterns ?? [],
                                            options.FilesFrom ?? [],
                                            options.WorkingDirectory ?? Directory.GetCurrentDirectory(),
                                            standardInput);
        if (!inputResolution.IsValid)
        {
            return FromInputErrors(inputResolution.Errors);
        }

        var planningResult = LocalUploadPlanner.Create(inputResolution.Selections, inputResolution.ExcludedFileCount);
        if (!planningResult.IsValid)
        {
            return FromLocalPlanningErrors(planningResult.Errors);
        }

        if (!TryValidateOutputs(options.SecretsOut, null, options.Force, out var outputError))
        {
            return Invalid(outputError!);
        }

        var localPlan = planningResult.Plan ?? throw new InvalidOperationException("A valid planning result must contain a plan.");
        return new(new(localPlan, null, null, options.SecretsOut), []);
    }

    private static UploadDryRunPlanningResult FromInputErrors(ImmutableArray<UploadInputError> errors) =>
        new(null, [.. errors.Select(static error => new UploadDryRunError(error.Message, error.Origin))]);

    private static UploadDryRunPlanningResult FromLocalPlanningErrors(ImmutableArray<LocalUploadPlanningError> errors) =>
        new(null, [.. errors.Select(static error => new UploadDryRunError(error.Message, error.Origin))]);

    private static UploadDryRunPlanningResult Invalid(String message) =>
        new(null, [new(message, UploadSelectionOrigin.CommandLine)]);

    private static UploadInputResolution ResolveInputs(IReadOnlyList<FileInfo> files,
                                                       IReadOnlyList<String> inputPaths,
                                                       Boolean recursive,
                                                       IReadOnlyList<String> includePatterns,
                                                       IReadOnlyList<String> excludePatterns,
                                                       IReadOnlyList<String> filesFrom,
                                                       String workingDirectory,
                                                       TextReader standardInput) =>
        UploadInputResolver.Resolve(files,
                                    inputPaths,
                                    recursive,
                                    includePatterns,
                                    excludePatterns,
                                    filesFrom,
                                    workingDirectory,
                                    standardInput);

    private static Boolean TryValidateOutputs(FileInfo? secretsOut,
                                              FileInfo? queueOut,
                                              Boolean force,
                                              out String? error)
    {
        foreach (var output in new[]
                 {
                     secretsOut,
                     queueOut
                 })
        {
            if (output is null)
            {
                continue;
            }

            try
            {
                AtomicFileWriter.EnsureWritable(output, force);
            }
            catch (AtomicFileException exception)
            {
                error = exception.Message;
                return false;
            }
        }

        error = null;
        return true;
    }
}
