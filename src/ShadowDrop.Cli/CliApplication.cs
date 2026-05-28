// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli;

using ShadowDrop.Cli.Downloads;
using ShadowDrop.Cli.Uploads;
using System.CommandLine;

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
        if (IsHelpRequest(args))
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
            Arity = ArgumentArity.OneOrMore
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

        var outputSecretOption = new Option<Boolean>("--output-secret")
        {
            Description = "Emit the generated share secret as the final stdout line after full success only."
        };

        var shareIdArgument = new Argument<String?>("share-id")
        {
            Description = "Share identifier or public share URL."
        };
        shareIdArgument.Arity = ArgumentArity.ZeroOrOne;

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

        var downloadCommand = new Command("download", "Download encrypted content and decrypt it locally.");
        downloadCommand.Arguments.Add(shareIdArgument);
        downloadCommand.Options.Add(serverOption);
        downloadCommand.Options.Add(fileOption);
        downloadCommand.Options.Add(queueOption);
        downloadCommand.Options.Add(shareKeyOption);
        downloadCommand.Options.Add(shareKeyFileOption);
        downloadCommand.Options.Add(bearerTokenOption);

        var uploadCommand = new Command("upload", "Encrypt local files and upload encrypted content to ShadowDrop.");
        uploadCommand.Arguments.Add(filesArgument);
        uploadCommand.Options.Add(serverOption);
        uploadCommand.Options.Add(uploadTokenOption);
        uploadCommand.Options.Add(outputSecretOption);
        var rootCommand = new RootCommand("ShadowDrop CLI");
        rootCommand.Subcommands.Add(downloadCommand);
        rootCommand.Subcommands.Add(uploadCommand);
        return new(rootCommand,
                   downloadCommand,
                   uploadCommand,
                   shareIdArgument,
                   filesArgument,
                   serverOption,
                   fileOption,
                   queueOption,
                   shareKeyOption,
                   shareKeyFileOption,
                   bearerTokenOption,
                   uploadTokenOption,
                   outputSecretOption);
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
            var downloadHandler = new DownloadCommandHandler(services.ConfigurationResolver,
                                                             services.HttpClient,
                                                             services.StandardOutStream,
                                                             services.StandardError);
            return await downloadHandler.ExecuteAsync(new(parseResult.GetValue(commandModel.ShareIdArgument),
                                                          parseResult.GetValue(commandModel.ServerOption),
                                                          parseResult.GetValue(commandModel.FileOption),
                                                          parseResult.GetValue(commandModel.QueueOption),
                                                          parseResult.GetValue(commandModel.ShareKeyOption),
                                                          parseResult.GetValue(commandModel.ShareKeyFileOption),
                                                          parseResult.GetValue(commandModel.BearerTokenOption)),
                                                      cancellationToken);
        }

        if (!String.Equals(commandName, "upload", StringComparison.Ordinal))
        {
            await services.StandardError.WriteLineAsync("A command is required.");
            return 1;
        }

        if (parseResult.GetResult(commandModel.FilesArgument) is null)
        {
            await services.StandardError.WriteLineAsync("Required argument missing for command: 'upload'.");
            return 1;
        }

        var handler = new UploadCommandHandler(services.ConfigurationResolver,
                                               services.HttpClient,
                                               services.StandardOut,
                                               services.StandardError);

        return await handler.ExecuteAsync(new(parseResult.GetValue(commandModel.FilesArgument) ?? [],
                                              parseResult.GetValue(commandModel.ServerOption),
                                              parseResult.GetValue(commandModel.UploadTokenOption),
                                              parseResult.GetValue(commandModel.OutputSecretOption)),
                                          cancellationToken);
    }

    private static Boolean IsHelpFlag(String value) =>
        String.Equals(value, "--help", StringComparison.Ordinal) || String.Equals(value, "-h", StringComparison.Ordinal);

    private static Boolean IsHelpRequest(String[] args)
    {
        var reachedEndOfOptions = false;

        foreach (var arg in args)
        {
            if (String.Equals(arg, "--", StringComparison.Ordinal))
            {
                reachedEndOfOptions = true;
                continue;
            }

            if (!reachedEndOfOptions && IsHelpFlag(arg))
            {
                return true;
            }
        }

        return false;
    }

    private sealed record CliCommandModel(
        RootCommand RootCommand,
        Command DownloadCommand,
        Command UploadCommand,
        Argument<String?> ShareIdArgument,
        Argument<FileInfo[]> FilesArgument,
        Option<String?> ServerOption,
        Option<String?> FileOption,
        Option<FileInfo?> QueueOption,
        Option<String?> ShareKeyOption,
        Option<FileInfo?> ShareKeyFileOption,
        Option<String?> BearerTokenOption,
        Option<String?> UploadTokenOption,
        Option<Boolean> OutputSecretOption);
}
