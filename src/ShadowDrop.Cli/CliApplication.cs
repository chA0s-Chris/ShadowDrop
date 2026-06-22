// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli;

using ShadowDrop.Cli.Downloads;
using ShadowDrop.Cli.Interactive;
using ShadowDrop.Cli.Uploads;
using System.CommandLine;
using System.CommandLine.Help;

internal static class CliApplication
{
    public static Task<Int32> RunAsync(String[] args, CancellationToken cancellationToken)
    {
        var services = CliApplicationServices.CreateDefault();
        return InvokeAsync(args, services, cancellationToken);
    }

    internal static RootCommand CreateRootCommand(CliApplicationServices services, CancellationToken cancellationToken) => CreateCommandModel().RootCommand;

    internal static Task<Int32> InvokeAsync(String[] args, CliApplicationServices services, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(services);

        var commandModel = CreateCommandModel();
        var parseResult = commandModel.RootCommand.Parse(args);
        if (parseResult.Action is HelpAction)
        {
            return parseResult.InvokeAsync(new()
            {
                Output = services.StandardOut,
                Error = services.StandardError
            }, cancellationToken);
        }

        return ExecuteAsync(parseResult, services, commandModel, cancellationToken);
    }

    private static CliCommandModel CreateCommandModel()
    {
        var filesArgument = new Argument<FileInfo[]>("files")
        {
            Description = "One or more local files to encrypt and upload.",
            Arity = ArgumentArity.ZeroOrMore
        };

        var serverOption = new Option<String?>("--server-url")
        {
            Description = "ShadowDrop server URL. Overrides environment variables and config values."
        };

        var uploadTokenOption = new Option<String?>("--upload-token")
        {
            Description =
                "Upload authorization token. Prefer environment variables or config files for sensitive deployments because CLI flags may be visible to process inspection tools."
        };

        var expiresInOption = new Option<String?>("--expires-in")
        {
            Description = "Share expiration as <amount><unit>, e.g. 7d, 12h, or 30m. Defaults to 7d."
        };

        var directHttpOption = new Option<Boolean>("--direct-http")
        {
            Description = "Create a direct-HTTP share instead of the default separate-key share."
        };

        var generateDownloadTokenOption = new Option<Boolean>("--download-token")
        {
            Description = "Generate a download bearer token (separate-key shares only)."
        };

        var secretsOutOption = new Option<FileInfo?>("--secrets-out")
        {
            Description = "Write download credentials to a dedicated file instead of stdout."
        };

        var jsonOption = new Option<Boolean>("--json")
        {
            Description = "Emit the result as a single JSON object on stdout."
        };

        var forceOption = new Option<Boolean>("--force")
        {
            Description = "Allow overwriting an existing --secrets-out file."
        };

        var uploadInteractiveOption = new Option<Boolean>("--interactive")
        {
            Description = "Run the guided Spectre.Console upload and share workflow."
        };

        var shareTokenArgument = new Argument<String?>("share-token")
        {
            Description = "Public share token or share URL."
        };
        shareTokenArgument.Arity = ArgumentArity.ZeroOrOne;

        var fileOption = new Option<String?>("--file")
        {
            Description = "Download only the selected file id from the share."
        };

        var queueOption = new Option<FileInfo?>("--queue")
        {
            Description = "Download files from a shared queue JSON file."
        };

        var shareKeyOption = new Option<String?>("--share-key")
        {
            Description = "Share key as lowercase hexadecimal key material or secret:<hex>."
        };

        var shareKeyFileOption = new Option<FileInfo?>("--share-key-file")
        {
            Description = "Read the share key from a local file. --share-key takes precedence."
        };

        var bearerTokenOption = new Option<String?>("--bearer-token")
        {
            Description = "Optional download bearer token. This is sourced only from this CLI argument."
        };

        var downloadInteractiveOption = new Option<Boolean>("--interactive")
        {
            Description = "Run the guided Spectre.Console download workflow."
        };

        var downloadCommand = new Command("download", "Download encrypted content and decrypt it locally.");
        downloadCommand.Arguments.Add(shareTokenArgument);
        downloadCommand.Options.Add(serverOption);
        downloadCommand.Options.Add(fileOption);
        downloadCommand.Options.Add(queueOption);
        downloadCommand.Options.Add(shareKeyOption);
        downloadCommand.Options.Add(shareKeyFileOption);
        downloadCommand.Options.Add(bearerTokenOption);
        downloadCommand.Options.Add(downloadInteractiveOption);

        var uploadCommand = new Command("upload", "Encrypt local files and upload encrypted content to ShadowDrop.");
        uploadCommand.Arguments.Add(filesArgument);
        uploadCommand.Options.Add(serverOption);
        uploadCommand.Options.Add(uploadTokenOption);
        uploadCommand.Options.Add(expiresInOption);
        uploadCommand.Options.Add(directHttpOption);
        uploadCommand.Options.Add(generateDownloadTokenOption);
        uploadCommand.Options.Add(secretsOutOption);
        uploadCommand.Options.Add(jsonOption);
        uploadCommand.Options.Add(forceOption);
        uploadCommand.Options.Add(uploadInteractiveOption);
        var rootCommand = new RootCommand("ShadowDrop CLI");
        rootCommand.Subcommands.Add(downloadCommand);
        rootCommand.Subcommands.Add(uploadCommand);
        return new(rootCommand,
                   shareTokenArgument,
                   filesArgument,
                   serverOption,
                   fileOption,
                   queueOption,
                   shareKeyOption,
                   shareKeyFileOption,
                   bearerTokenOption,
                   uploadTokenOption,
                   expiresInOption,
                   directHttpOption,
                   generateDownloadTokenOption,
                   secretsOutOption,
                   jsonOption,
                   forceOption,
                   uploadInteractiveOption,
                   downloadInteractiveOption);
    }

    private static async Task<Int32> ExecuteAsync(ParseResult parseResult, CliApplicationServices services, CliCommandModel commandModel,
                                                  CancellationToken cancellationToken)
    {
        if (parseResult.Errors.Count > 0)
        {
            foreach (var error in parseResult.Errors)
            {
                await services.StandardError.WriteLineAsync(error.Message);
            }

            return 1;
        }

        var commandName = parseResult.CommandResult.Command.Name;
        if (String.Equals(commandName, "download", StringComparison.Ordinal))
        {
            var options = new DownloadCommandOptions(parseResult.GetValue(commandModel.ShareTokenArgument),
                                                     parseResult.GetValue(commandModel.ServerOption),
                                                     parseResult.GetValue(commandModel.FileOption),
                                                     parseResult.GetValue(commandModel.QueueOption),
                                                     parseResult.GetValue(commandModel.ShareKeyOption),
                                                     parseResult.GetValue(commandModel.ShareKeyFileOption),
                                                     parseResult.GetValue(commandModel.BearerTokenOption));

            if (parseResult.GetValue(commandModel.DownloadInteractiveOption))
            {
                return await new InteractiveDownloadCommandHandler(services.ConfigurationResolver,
                                                                   services.HttpClient,
                                                                   services.InteractiveSession,
                                                                   services.StandardError).ExecuteAsync(options, cancellationToken);
            }

            var downloadHandler = new DownloadCommandHandler(services.ConfigurationResolver,
                                                             services.HttpClient,
                                                             services.StandardOutStream,
                                                             services.StandardError);
            return await downloadHandler.ExecuteAsync(options, cancellationToken);
        }

        if (!String.Equals(commandName, "upload", StringComparison.Ordinal))
        {
            await services.StandardError.WriteLineAsync("A command is required.");
            return 1;
        }

        var uploadOptions = new UploadCommandOptions(parseResult.GetValue(commandModel.FilesArgument) ?? [],
                                                     parseResult.GetValue(commandModel.ServerOption),
                                                     parseResult.GetValue(commandModel.UploadTokenOption),
                                                     parseResult.GetValue(commandModel.ExpiresInOption),
                                                     parseResult.GetValue(commandModel.DirectHttpOption),
                                                     parseResult.GetValue(commandModel.GenerateDownloadTokenOption),
                                                     parseResult.GetValue(commandModel.SecretsOutOption),
                                                     parseResult.GetValue(commandModel.JsonOption),
                                                     parseResult.GetValue(commandModel.ForceOption));

        if (parseResult.GetValue(commandModel.UploadInteractiveOption))
        {
            return await new InteractiveUploadCommandHandler(services.ConfigurationResolver,
                                                             services.HttpClient,
                                                             services.InteractiveSession,
                                                             services.StandardOut,
                                                             services.StandardError,
                                                             services.TimeProvider).ExecuteAsync(uploadOptions, cancellationToken);
        }

        if (uploadOptions.Files.Length == 0)
        {
            await services.StandardError.WriteLineAsync("Required argument missing for command: 'upload'.");
            return 1;
        }

        return await new UploadCommandHandler(services.ConfigurationResolver,
                                              services.HttpClient,
                                              services.StandardOut,
                                              services.StandardError,
                                              services.TimeProvider).ExecuteAsync(uploadOptions, cancellationToken);
    }

    private sealed record CliCommandModel(
        RootCommand RootCommand,
        Argument<String?> ShareTokenArgument,
        Argument<FileInfo[]> FilesArgument,
        Option<String?> ServerOption,
        Option<String?> FileOption,
        Option<FileInfo?> QueueOption,
        Option<String?> ShareKeyOption,
        Option<FileInfo?> ShareKeyFileOption,
        Option<String?> BearerTokenOption,
        Option<String?> UploadTokenOption,
        Option<String?> ExpiresInOption,
        Option<Boolean> DirectHttpOption,
        Option<Boolean> GenerateDownloadTokenOption,
        Option<FileInfo?> SecretsOutOption,
        Option<Boolean> JsonOption,
        Option<Boolean> ForceOption,
        Option<Boolean> UploadInteractiveOption,
        Option<Boolean> DownloadInteractiveOption);
}
