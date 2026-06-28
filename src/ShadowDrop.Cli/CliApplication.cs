// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli;

using ShadowDrop.Cli.Downloads;
using ShadowDrop.Cli.Interactive;
using ShadowDrop.Cli.Queues;
using ShadowDrop.Cli.Shares;
using ShadowDrop.Cli.Tls;
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

        var caCertOption = new Option<String?>("--cacert")
        {
            Description =
                "Trust a PEM-encoded certificate (a self-signed server certificate or private CA) as an additional anchor. "
                + "The presented chain is still validated, so this is the safe way to talk to a reverse proxy that terminates TLS with a self-signed certificate. "
                + "Overrides SHADOWDROP_CACERT."
        };

        var insecureOption = new Option<Boolean>("--insecure", "-k")
        {
            Description =
                "Disable TLS certificate validation entirely. UNSAFE: this re-enables man-in-the-middle attacks and may expose upload and download tokens. "
                + "Prefer --cacert for self-signed or private-CA setups. Also enabled by SHADOWDROP_INSECURE=1|true|yes."
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
            Description = "Allow overwriting an existing --secrets-out or queue output file."
        };

        var queueOutOption = new Option<FileInfo?>("--queue-out")
        {
            Description = "Also write a download queue file after the share is created."
        };

        var embedSecretsOption = new Option<Boolean>("--embed-secrets")
        {
            Description = "Embed download credentials into the generated queue (self-contained and sensitive)."
        };

        var nameOption = new Option<String?>("--name")
        {
            Description = "Recipient-facing display name for the uploaded file. Requires exactly one file; "
                          + "use --display-name <path>=<name> for multiple files."
        };

        var uploadDisplayNameOption = new Option<String[]>("--display-name")
        {
            Description = "Map a file to a recipient-facing display name as <path>=<name>. Repeat for multiple files.",
            Arity = ArgumentArity.ZeroOrMore
        };

        var shareDisplayNameOption = new Option<String[]>("--display-name")
        {
            Description = "Map a file id to a recipient-facing display name as <file-id>=<name>. Repeat for multiple files.",
            Arity = ArgumentArity.ZeroOrMore
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

        var outputRootOption = new Option<DirectoryInfo?>("--output-root")
        {
            Description = "Root directory for relative outputPath values in a download queue."
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
        downloadCommand.Options.Add(caCertOption);
        downloadCommand.Options.Add(insecureOption);
        downloadCommand.Options.Add(fileOption);
        downloadCommand.Options.Add(queueOption);
        downloadCommand.Options.Add(outputRootOption);
        downloadCommand.Options.Add(shareKeyOption);
        downloadCommand.Options.Add(shareKeyFileOption);
        downloadCommand.Options.Add(bearerTokenOption);
        downloadCommand.Options.Add(downloadInteractiveOption);

        var uploadCommand = new Command("upload", "Encrypt local files and upload encrypted content to ShadowDrop.");
        uploadCommand.Arguments.Add(filesArgument);
        uploadCommand.Options.Add(serverOption);
        uploadCommand.Options.Add(caCertOption);
        uploadCommand.Options.Add(insecureOption);
        uploadCommand.Options.Add(uploadTokenOption);
        uploadCommand.Options.Add(expiresInOption);
        uploadCommand.Options.Add(directHttpOption);
        uploadCommand.Options.Add(generateDownloadTokenOption);
        uploadCommand.Options.Add(secretsOutOption);
        uploadCommand.Options.Add(queueOutOption);
        uploadCommand.Options.Add(embedSecretsOption);
        uploadCommand.Options.Add(nameOption);
        uploadCommand.Options.Add(uploadDisplayNameOption);
        uploadCommand.Options.Add(jsonOption);
        uploadCommand.Options.Add(forceOption);
        uploadCommand.Options.Add(uploadInteractiveOption);

        var rawFilesArgument = new Argument<FileInfo[]>("files")
        {
            Description = "One or more local files to encrypt and upload.",
            Arity = ArgumentArity.OneOrMore
        };

        var uploadRawCommand = new Command("raw", "Encrypt and upload files without creating a share; reports file IDs and the share key.");
        uploadRawCommand.Arguments.Add(rawFilesArgument);
        uploadRawCommand.Options.Add(serverOption);
        uploadRawCommand.Options.Add(caCertOption);
        uploadRawCommand.Options.Add(insecureOption);
        uploadRawCommand.Options.Add(uploadTokenOption);
        uploadRawCommand.Options.Add(secretsOutOption);
        uploadRawCommand.Options.Add(jsonOption);
        uploadRawCommand.Options.Add(forceOption);
        uploadCommand.Subcommands.Add(uploadRawCommand);

        // `upload` is both a leaf (end-to-end upload) and a group (`upload raw`). A no-op action marks it
        // invokable without a subcommand; actual execution is routed by the manual dispatch in ExecuteAsync.
        uploadCommand.SetAction(static _ => 0);

        var queueTokenArgument = new Argument<String?>("share-token")
        {
            Description = "Public share token or share URL of the share to queue."
        };
        queueTokenArgument.Arity = ArgumentArity.ZeroOrOne;

        var queueCreateOutOption = new Option<FileInfo?>("--out")
        {
            Description = "Path to write the queue file."
        };

        var queueCreateCommand = new Command("create", "Create a download queue from an existing share.");
        queueCreateCommand.Arguments.Add(queueTokenArgument);
        queueCreateCommand.Options.Add(serverOption);
        queueCreateCommand.Options.Add(caCertOption);
        queueCreateCommand.Options.Add(insecureOption);
        queueCreateCommand.Options.Add(queueCreateOutOption);
        queueCreateCommand.Options.Add(shareKeyOption);
        queueCreateCommand.Options.Add(shareKeyFileOption);
        queueCreateCommand.Options.Add(bearerTokenOption);
        queueCreateCommand.Options.Add(embedSecretsOption);
        queueCreateCommand.Options.Add(forceOption);

        var queueCommand = new Command("queue", "Create and manage download queues.");
        queueCommand.Subcommands.Add(queueCreateCommand);

        var shareFileIdsArgument = new Argument<String[]>("file-ids")
        {
            Description = "One or more previously uploaded file IDs, in the desired download order.",
            Arity = ArgumentArity.OneOrMore
        };

        var shareIdArgument = new Argument<String?>("share-id")
        {
            Description = "Internal share ID to revoke."
        };

        var shareCreateCommand = new Command("create", "Create a share from previously uploaded file IDs.");
        shareCreateCommand.Arguments.Add(shareFileIdsArgument);
        shareCreateCommand.Options.Add(serverOption);
        shareCreateCommand.Options.Add(caCertOption);
        shareCreateCommand.Options.Add(insecureOption);
        shareCreateCommand.Options.Add(uploadTokenOption);
        shareCreateCommand.Options.Add(expiresInOption);
        shareCreateCommand.Options.Add(directHttpOption);
        shareCreateCommand.Options.Add(generateDownloadTokenOption);
        shareCreateCommand.Options.Add(secretsOutOption);
        shareCreateCommand.Options.Add(shareDisplayNameOption);
        shareCreateCommand.Options.Add(jsonOption);
        shareCreateCommand.Options.Add(forceOption);

        var shareRevokeCommand = new Command("revoke", "Revoke a share by internal share ID.");
        shareRevokeCommand.Arguments.Add(shareIdArgument);
        shareRevokeCommand.Options.Add(serverOption);
        shareRevokeCommand.Options.Add(caCertOption);
        shareRevokeCommand.Options.Add(insecureOption);
        shareRevokeCommand.Options.Add(uploadTokenOption);

        var shareCommand = new Command("share", "Create and manage shares.");
        shareCommand.Subcommands.Add(shareCreateCommand);
        shareCommand.Subcommands.Add(shareRevokeCommand);

        var rootCommand = new RootCommand("ShadowDrop CLI");
        rootCommand.Subcommands.Add(downloadCommand);
        rootCommand.Subcommands.Add(uploadCommand);
        rootCommand.Subcommands.Add(queueCommand);
        rootCommand.Subcommands.Add(shareCommand);
        return new(rootCommand,
                   shareTokenArgument,
                   filesArgument,
                   serverOption,
                   caCertOption,
                   insecureOption,
                   fileOption,
                   queueOption,
                   outputRootOption,
                   shareKeyOption,
                   shareKeyFileOption,
                   bearerTokenOption,
                   uploadTokenOption,
                   expiresInOption,
                   directHttpOption,
                   generateDownloadTokenOption,
                   secretsOutOption,
                   queueOutOption,
                   embedSecretsOption,
                   nameOption,
                   uploadDisplayNameOption,
                   shareDisplayNameOption,
                   jsonOption,
                   forceOption,
                   uploadInteractiveOption,
                   downloadInteractiveOption,
                   queueCreateCommand,
                   queueTokenArgument,
                   queueCreateOutOption,
                   uploadRawCommand,
                   rawFilesArgument,
                   shareCreateCommand,
                   shareFileIdsArgument,
                   shareRevokeCommand,
                   shareIdArgument);
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

        var tlsOptions = services.ConfigurationResolver.ResolveTls(parseResult.GetValue(commandModel.CaCertOption),
                                                                   parseResult.GetValue(commandModel.InsecureOption));

        if (tlsOptions.CaCertPath is not null && tlsOptions.Insecure)
        {
            await services.StandardError.WriteLineAsync("The --cacert and --insecure options cannot be combined. Choose one.");
            return 1;
        }

        if (tlsOptions.Insecure)
        {
            await services.StandardError.WriteLineAsync(
                "WARNING: TLS certificate validation is disabled (--insecure). The connection is vulnerable to interception and your upload/download tokens may be exposed. Prefer --cacert for self-signed or private-CA setups.");
        }

        HttpClient httpClient;
        try
        {
            httpClient = services.HttpClientFactory(tlsOptions);
        }
        catch (CliTlsConfigurationException exception)
        {
            await services.StandardError.WriteLineAsync(exception.Message);
            return 1;
        }

        if (parseResult.CommandResult.Command == commandModel.QueueCreateCommand)
        {
            var queueOptions = new QueueCreateCommandOptions(parseResult.GetValue(commandModel.QueueTokenArgument),
                                                             parseResult.GetValue(commandModel.ServerOption),
                                                             parseResult.GetValue(commandModel.QueueCreateOutOption),
                                                             parseResult.GetValue(commandModel.ShareKeyOption),
                                                             parseResult.GetValue(commandModel.ShareKeyFileOption),
                                                             parseResult.GetValue(commandModel.BearerTokenOption),
                                                             parseResult.GetValue(commandModel.EmbedSecretsOption),
                                                             parseResult.GetValue(commandModel.ForceOption));

            return await new QueueCreateCommandHandler(services.ConfigurationResolver,
                                                       httpClient,
                                                       services.StandardOut,
                                                       services.StandardError).ExecuteAsync(queueOptions, cancellationToken);
        }

        if (parseResult.CommandResult.Command == commandModel.UploadRawCommand)
        {
            var rawOptions = new UploadRawCommandOptions(parseResult.GetValue(commandModel.RawFilesArgument) ?? [],
                                                         parseResult.GetValue(commandModel.ServerOption),
                                                         parseResult.GetValue(commandModel.UploadTokenOption),
                                                         parseResult.GetValue(commandModel.SecretsOutOption),
                                                         parseResult.GetValue(commandModel.JsonOption),
                                                         parseResult.GetValue(commandModel.ForceOption));

            return await new UploadRawCommandHandler(services.ConfigurationResolver,
                                                     httpClient,
                                                     services.StandardOut,
                                                     services.StandardError).ExecuteAsync(rawOptions, cancellationToken);
        }

        if (parseResult.CommandResult.Command == commandModel.ShareCreateCommand)
        {
            var shareOptions = new ShareCreateCommandOptions(parseResult.GetValue(commandModel.ShareFileIdsArgument) ?? [],
                                                             parseResult.GetValue(commandModel.ServerOption),
                                                             parseResult.GetValue(commandModel.UploadTokenOption),
                                                             parseResult.GetValue(commandModel.ExpiresInOption),
                                                             parseResult.GetValue(commandModel.DirectHttpOption),
                                                             parseResult.GetValue(commandModel.GenerateDownloadTokenOption),
                                                             parseResult.GetValue(commandModel.SecretsOutOption),
                                                             parseResult.GetValue(commandModel.JsonOption),
                                                             parseResult.GetValue(commandModel.ForceOption),
                                                             parseResult.GetValue(commandModel.ShareDisplayNameOption) ?? []);

            return await new ShareCreateCommandHandler(services.ConfigurationResolver,
                                                       httpClient,
                                                       services.StandardOut,
                                                       services.StandardError,
                                                       services.TimeProvider).ExecuteAsync(shareOptions, cancellationToken);
        }

        if (parseResult.CommandResult.Command == commandModel.ShareRevokeCommand)
        {
            var revokeOptions = new ShareRevokeCommandOptions(parseResult.GetValue(commandModel.ShareIdArgument),
                                                              parseResult.GetValue(commandModel.ServerOption),
                                                              parseResult.GetValue(commandModel.UploadTokenOption));

            return await new ShareRevokeCommandHandler(services.ConfigurationResolver,
                                                       httpClient,
                                                       services.StandardOut,
                                                       services.StandardError).ExecuteAsync(revokeOptions, cancellationToken);
        }

        var commandName = parseResult.CommandResult.Command.Name;
        if (String.Equals(commandName, "download", StringComparison.Ordinal))
        {
            var options = new DownloadCommandOptions(parseResult.GetValue(commandModel.ShareTokenArgument),
                                                     parseResult.GetValue(commandModel.ServerOption),
                                                     parseResult.GetValue(commandModel.FileOption),
                                                     parseResult.GetValue(commandModel.QueueOption),
                                                     parseResult.GetValue(commandModel.OutputRootOption),
                                                     parseResult.GetValue(commandModel.ShareKeyOption),
                                                     parseResult.GetValue(commandModel.ShareKeyFileOption),
                                                     parseResult.GetValue(commandModel.BearerTokenOption));

            if (options.OutputRoot is not null && options.QueuePath is null)
            {
                await services.StandardError.WriteLineAsync("The --output-root option requires --queue.");
                return 1;
            }

            if (parseResult.GetValue(commandModel.DownloadInteractiveOption))
            {
                return await new InteractiveDownloadCommandHandler(services.ConfigurationResolver,
                                                                   httpClient,
                                                                   services.InteractiveSession,
                                                                   services.StandardError).ExecuteAsync(options, cancellationToken);
            }

            var downloadHandler = new DownloadCommandHandler(services.ConfigurationResolver,
                                                             httpClient,
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
                                                     parseResult.GetValue(commandModel.QueueOutOption),
                                                     parseResult.GetValue(commandModel.EmbedSecretsOption),
                                                     parseResult.GetValue(commandModel.JsonOption),
                                                     parseResult.GetValue(commandModel.ForceOption),
                                                     parseResult.GetValue(commandModel.NameOption),
                                                     parseResult.GetValue(commandModel.UploadDisplayNameOption) ?? []);

        if (parseResult.GetValue(commandModel.UploadInteractiveOption))
        {
            return await new InteractiveUploadCommandHandler(services.ConfigurationResolver,
                                                             httpClient,
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
                                              httpClient,
                                              services.StandardOut,
                                              services.StandardError,
                                              services.TimeProvider).ExecuteAsync(uploadOptions, cancellationToken);
    }

    private sealed record CliCommandModel(
        RootCommand RootCommand,
        Argument<String?> ShareTokenArgument,
        Argument<FileInfo[]> FilesArgument,
        Option<String?> ServerOption,
        Option<String?> CaCertOption,
        Option<Boolean> InsecureOption,
        Option<String?> FileOption,
        Option<FileInfo?> QueueOption,
        Option<DirectoryInfo?> OutputRootOption,
        Option<String?> ShareKeyOption,
        Option<FileInfo?> ShareKeyFileOption,
        Option<String?> BearerTokenOption,
        Option<String?> UploadTokenOption,
        Option<String?> ExpiresInOption,
        Option<Boolean> DirectHttpOption,
        Option<Boolean> GenerateDownloadTokenOption,
        Option<FileInfo?> SecretsOutOption,
        Option<FileInfo?> QueueOutOption,
        Option<Boolean> EmbedSecretsOption,
        Option<String?> NameOption,
        Option<String[]> UploadDisplayNameOption,
        Option<String[]> ShareDisplayNameOption,
        Option<Boolean> JsonOption,
        Option<Boolean> ForceOption,
        Option<Boolean> UploadInteractiveOption,
        Option<Boolean> DownloadInteractiveOption,
        Command QueueCreateCommand,
        Argument<String?> QueueTokenArgument,
        Option<FileInfo?> QueueCreateOutOption,
        Command UploadRawCommand,
        Argument<FileInfo[]> RawFilesArgument,
        Command ShareCreateCommand,
        Argument<String[]> ShareFileIdsArgument,
        Command ShareRevokeCommand,
        Argument<String?> ShareIdArgument);
}
