using System.Security.Cryptography;
using KotobaSenpai.Core.Errors;
using KotobaSenpai.Core.Updates;

namespace KotobaSenpai.Platform.Windows.Updates;

/// <summary>Applies the same trust and integrity checks at download and installation boundaries.</summary>
internal static class UpdatePackage
{
    internal static bool TryParseVersion(string? tag, out Version version)
    {
        var text = tag?.StartsWith('v') == true ? tag[1..] : tag;
        if (text is not null && text.Split('.').Length == 3
            && text.All(character => char.IsAsciiDigit(character) || character == '.')
            && Version.TryParse(text, out var parsed) && parsed.Build >= 0 && text == parsed.ToString(3))
        {
            version = parsed;
            return true;
        }
        version = new Version(0, 0, 0);
        return false;
    }

    internal static void ValidateRelease(AppRelease release)
    {
        if (!TryParseVersion(release.Tag, out var version) || version != release.Version
            || release.Size <= 0 || release.Sha256.Length != 64 || !release.Sha256.All(char.IsAsciiHexDigit)
            || release.DownloadUri != new Uri($"https://github.com/TreatTrick/KotobaSenpai/releases/download/{release.Tag}/{release.InstallerName}"))
            throw new AppUpdateException(ErrorCodes.AppUpdateReleaseUnavailable, "The release has no supported, verifiable official installer.");
    }

    internal static async Task<FileStream> OpenVerifiedAsync(DownloadedAppUpdate update, CancellationToken cancellationToken)
    {
        ValidateRelease(update.Release);
        var file = new FileStream(update.InstallerPath, FileMode.Open, FileAccess.Read, FileShare.Read,
            128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        try
        {
            if (file.Length != update.Release.Size
                || !Convert.ToHexString(await SHA256.HashDataAsync(file, cancellationToken).ConfigureAwait(false))
                    .Equals(update.Release.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new AppUpdateException(ErrorCodes.AppUpdateVerificationFailed, "The update package size or SHA-256 does not match the release.");
            return file;
        }
        catch
        {
            await file.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
}
