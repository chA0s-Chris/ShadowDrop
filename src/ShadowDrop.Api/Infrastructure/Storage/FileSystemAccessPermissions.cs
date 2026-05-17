// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Infrastructure.Storage;

internal static class FileSystemAccessPermissions
{
    private const UnixFileMode OwnerOnlyDirectoryMode =
        UnixFileMode.UserRead |
        UnixFileMode.UserWrite |
        UnixFileMode.UserExecute;

    private const UnixFileMode OwnerOnlyFileMode =
        UnixFileMode.UserRead |
        UnixFileMode.UserWrite;

    public static void EnsureOwnerOnlyDirectory(String path)
    {
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(path);
            return;
        }

        Directory.CreateDirectory(path, OwnerOnlyDirectoryMode);
        File.SetUnixFileMode(path, OwnerOnlyDirectoryMode);
    }

    public static void EnsureOwnerOnlyFile(String path)
    {
        var directory = Path.GetDirectoryName(path)
                        ?? throw new InvalidOperationException("The file path must include a parent directory.");

        EnsureOwnerOnlyDirectory(directory);

        if (OperatingSystem.IsWindows())
        {
            using (File.Open(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite)) { }

            return;
        }

        try
        {
            using (new FileStream(path, new FileStreamOptions
                   {
                       Mode = FileMode.CreateNew,
                       Access = FileAccess.ReadWrite,
                       Share = FileShare.ReadWrite,
                       UnixCreateMode = OwnerOnlyFileMode
                   })) { }
        }
        catch (IOException) when (File.Exists(path)) { }

        File.SetUnixFileMode(path, OwnerOnlyFileMode);
    }
}
