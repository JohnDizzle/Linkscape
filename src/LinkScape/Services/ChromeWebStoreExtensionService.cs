using Microsoft.Web.WebView2.Core;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace LinkScape.Services;

internal sealed record ChromeWebStoreExtensionPackage(
    string ExtensionId,
    string Name,
    string Version,
    string ManifestVersion,
    IReadOnlyList<string> Permissions,
    string Folder);

internal sealed record InstalledChromeExtension(
    string Id,
    string Name,
    string Version,
    bool IsEnabled,
    string? PopupUrl);

internal static partial class ChromeWebStoreExtensionService
{
    private const int MaximumPackageBytes = 100 * 1024 * 1024;
    private const int MaximumExtractedBytes = 250 * 1024 * 1024;
    private const int MaximumFiles = 20_000;
    private static readonly HttpClient DownloadClient = CreateDownloadClient();

    [GeneratedRegex(@"^[a-p]{32}$", RegexOptions.CultureInvariant)]
    private static partial Regex ExtensionIdPattern();

    [GeneratedRegex(
        @"^https://chromewebstore\.google\.com/detail/(?:[^/?#]+/)?(?<id>[a-p]{32})(?:[/?#]|$)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex StorePagePattern();

    public static bool TryGetExtensionId(string? storePageUrl, out string extensionId)
    {
        var match = StorePagePattern().Match(storePageUrl ?? string.Empty);
        extensionId = match.Success
            ? match.Groups["id"].Value.ToLowerInvariant()
            : string.Empty;
        return match.Success;
    }

    public static async Task<ChromeWebStoreExtensionPackage> DownloadAndPrepareAsync(
        string extensionId,
        CancellationToken cancellationToken = default)
    {
        extensionId = extensionId.ToLowerInvariant();
        if (!ExtensionIdPattern().IsMatch(extensionId))
        {
            throw new InvalidDataException("The Chrome Web Store extension ID is invalid.");
        }

        var requestUri = new Uri(
            "https://clients2.google.com/service/update2/crx" +
            "?response=redirect&prodversion=131.0.0.0&acceptformat=crx3" +
            $"&x=id%3D{extensionId}%26installsource%3Dondemand%26uc");
        using var response = await DownloadClient.GetAsync(
            requestUri,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        var declaredLength = response.Content.Headers.ContentLength;
        if (declaredLength is > MaximumPackageBytes)
        {
            throw new InvalidDataException("The extension package is larger than LinkScape allows.");
        }

        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var packageStream = new MemoryStream();
        var buffer = new byte[81920];
        while (true)
        {
            var read = await responseStream.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            if (packageStream.Length + read > MaximumPackageBytes)
            {
                throw new InvalidDataException("The extension package is larger than LinkScape allows.");
            }

            await packageStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        var package = packageStream.ToArray();
        var verifiedCrx = VerifyCrx3(package, extensionId);
        var destination = Path.Combine(
            Windows.Storage.ApplicationData.Current.LocalFolder.Path,
            "BrowserExtensions",
            "ChromeWebStore",
            extensionId);
        var pending = $"{destination}.pending";

        ResetOwnedDirectory(pending);
        Directory.CreateDirectory(pending);
        try
        {
            ExtractArchive(package.AsMemory(verifiedCrx.ZipOffset), pending);
            var manifestPath = Path.Combine(pending, "manifest.json");
            if (!File.Exists(manifestPath))
            {
                throw new InvalidDataException("The extension package does not contain manifest.json.");
            }

            // AddBrowserExtensionAsync installs an unpacked directory. Preserve
            // the verified CRX public key so Chromium derives the same extension
            // ID used by the Chrome Web Store instead of generating a new one.
            var manifestObject = JsonNode.Parse(
                    await File.ReadAllTextAsync(manifestPath, cancellationToken))
                ?.AsObject() ??
                throw new InvalidDataException("The extension manifest is invalid.");
            manifestObject["key"] = Convert.ToBase64String(verifiedCrx.PublicKey);
            await File.WriteAllTextAsync(
                manifestPath,
                manifestObject.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
                cancellationToken);

            // Read the manifest into memory so no file handle remains open when
            // the verified staging directory is moved into its versioned home.
            var manifestBytes = await File.ReadAllBytesAsync(
                manifestPath,
                cancellationToken);
            using var manifest = JsonDocument.Parse(manifestBytes);
            var root = manifest.RootElement;
            var manifestVersion = root.TryGetProperty("manifest_version", out var manifestVersionNode)
                ? manifestVersionNode.GetInt32()
                : 0;
            if (manifestVersion != 3)
            {
                throw new InvalidDataException(
                    $"LinkScape currently accepts Manifest V3 extensions; this package uses Manifest V{manifestVersion}.");
            }

            var name = ReadManifestText(root, "name", extensionId);
            var version = ReadManifestText(root, "version", "Unknown");
            var permissions = ReadPermissions(root);
            var versionedDestination = Path.Combine(
                destination,
                $"{SanitizePathSegment(version)}-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path.GetDirectoryName(versionedDestination)!);
            Directory.Move(pending, versionedDestination);

            return new ChromeWebStoreExtensionPackage(
                extensionId,
                name,
                version,
                $"Manifest V{manifestVersion}",
                permissions,
                versionedDestination);
        }
        catch
        {
            ResetOwnedDirectory(pending);
            throw;
        }
    }

    public static async Task<CoreWebView2BrowserExtension> InstallAsync(
        CoreWebView2Profile profile,
        ChromeWebStoreExtensionPackage package)
    {
        var installed = await profile.GetBrowserExtensionsAsync();
        var previous = installed.FirstOrDefault(extension =>
            string.Equals(extension.Id, package.ExtensionId, StringComparison.Ordinal) ||
            string.Equals(extension.Name, package.Name, StringComparison.OrdinalIgnoreCase));
        if (previous is not null)
        {
            await previous.RemoveAsync();
        }

        return await profile.AddBrowserExtensionAsync(package.Folder);
    }

    public static async Task<IReadOnlyList<InstalledChromeExtension>> GetInstalledAsync(
        CoreWebView2Profile profile)
    {
        var installed = await profile.GetBrowserExtensionsAsync();
        var storeRoot = GetStoreRoot();
        if (!Directory.Exists(storeRoot))
        {
            return [];
        }

        var results = new List<InstalledChromeExtension>();
        foreach (var extension in installed)
        {
            var extensionRoot = Path.Combine(storeRoot, extension.Id);
            var manifestPath = Directory.Exists(extensionRoot)
                ? FindNewestManifest(extensionRoot)
                : FindManifestByExtensionName(storeRoot, extension.Name);
            if (manifestPath is null)
            {
                continue;
            }

            try
            {
                using var manifest = JsonDocument.Parse(
                    await File.ReadAllBytesAsync(manifestPath));
                var root = manifest.RootElement;
                var name = ReadManifestText(root, "name", extension.Id);
                var version = ReadManifestText(root, "version", "Unknown");
                var popupPath = ReadPopupPath(root);
                results.Add(new InstalledChromeExtension(
                    extension.Id,
                    name,
                    version,
                    extension.IsEnabled,
                    string.IsNullOrWhiteSpace(popupPath)
                        ? null
                        : $"chrome-extension://{extension.Id}/{popupPath.TrimStart('/')}"));
            }
            catch (JsonException)
            {
                // Ignore damaged metadata. WebView2 remains the source of truth
                // for whether the extension itself is installed.
            }
        }

        return results.OrderBy(extension => extension.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string? FindNewestManifest(string extensionRoot) =>
        Directory.EnumerateFiles(extensionRoot, "manifest.json", SearchOption.AllDirectories)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();

    private static string? FindManifestByExtensionName(string storeRoot, string extensionName)
    {
        foreach (var manifestPath in Directory.EnumerateFiles(
                     storeRoot,
                     "manifest.json",
                     SearchOption.AllDirectories)
                 .OrderByDescending(File.GetLastWriteTimeUtc))
        {
            try
            {
                using var manifest = JsonDocument.Parse(File.ReadAllBytes(manifestPath));
                if (string.Equals(
                        ReadManifestText(manifest.RootElement, "name", string.Empty),
                        extensionName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return manifestPath;
                }
            }
            catch (JsonException)
            {
            }
        }

        return null;
    }

    public static async Task SetEnabledAsync(
        CoreWebView2Profile profile,
        string extensionId,
        bool enabled)
    {
        var extension = (await profile.GetBrowserExtensionsAsync()).FirstOrDefault(candidate =>
            string.Equals(candidate.Id, extensionId, StringComparison.Ordinal));
        if (extension is null)
        {
            throw new InvalidOperationException("The extension is no longer installed.");
        }

        await extension.EnableAsync(enabled);
    }

    public static async Task RemoveAsync(
        CoreWebView2Profile profile,
        string extensionId)
    {
        var extension = (await profile.GetBrowserExtensionsAsync()).FirstOrDefault(candidate =>
            string.Equals(candidate.Id, extensionId, StringComparison.Ordinal));
        if (extension is null)
        {
            return;
        }

        await extension.RemoveAsync();
    }

    private sealed record VerifiedCrx3(int ZipOffset, byte[] PublicKey);

    private static VerifiedCrx3 VerifyCrx3(byte[] package, string expectedExtensionId)
    {
        if (package.Length < 12 ||
            package[0] != (byte)'C' ||
            package[1] != (byte)'r' ||
            package[2] != (byte)'2' ||
            package[3] != (byte)'4')
        {
            throw new InvalidDataException("The downloaded file is not a Chrome extension package.");
        }

        var version = BinaryPrimitives.ReadUInt32LittleEndian(package.AsSpan(4, 4));
        if (version != 3)
        {
            throw new InvalidDataException($"LinkScape currently supports CRX3 packages; this is CRX{version}.");
        }

        var headerLength = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(package.AsSpan(8, 4)));
        if (headerLength <= 0 || headerLength > package.Length - 12)
        {
            throw new InvalidDataException("The CRX3 header is invalid.");
        }

        var header = package.AsSpan(12, headerLength);
        var signedHeader = ReadLengthDelimitedField(header, 10000)
            ?? throw new InvalidDataException("The CRX3 package has no signed header.");
        var crxId = ReadLengthDelimitedField(signedHeader, 1)
            ?? throw new InvalidDataException("The CRX3 package has no signed extension ID.");
        if (crxId.Length != 16 || !string.Equals(
                EncodeExtensionId(crxId),
                expectedExtensionId,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException("The downloaded extension ID does not match the Store page.");
        }

        var archiveOffset = 12 + headerLength;
        var signedData = BuildSignedData(signedHeader, package.AsSpan(archiveOffset));
        byte[]? verifiedPublicKey = null;
        foreach (var proof in ReadMessages(header, 2))
        {
            if (VerifyRsaProof(proof, signedData, crxId))
            {
                verifiedPublicKey = ReadLengthDelimitedField(proof, 1);
                break;
            }
        }

        if (verifiedPublicKey is null)
        {
            foreach (var proof in ReadMessages(header, 3))
            {
                if (VerifyEcdsaProof(proof, signedData, crxId))
                {
                    verifiedPublicKey = ReadLengthDelimitedField(proof, 1);
                    break;
                }
            }
        }

        if (verifiedPublicKey is null)
        {
            throw new InvalidDataException("The Chrome extension signature could not be verified.");
        }

        return new VerifiedCrx3(archiveOffset, verifiedPublicKey);
    }

    private static byte[] BuildSignedData(ReadOnlySpan<byte> signedHeader, ReadOnlySpan<byte> archive)
    {
        var prefix = Encoding.ASCII.GetBytes("CRX3 SignedData\0");
        var result = new byte[prefix.Length + 4 + signedHeader.Length + archive.Length];
        prefix.CopyTo(result, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(
            result.AsSpan(prefix.Length, 4),
            checked((uint)signedHeader.Length));
        signedHeader.CopyTo(result.AsSpan(prefix.Length + 4));
        archive.CopyTo(result.AsSpan(prefix.Length + 4 + signedHeader.Length));
        return result;
    }

    private static bool VerifyRsaProof(ReadOnlySpan<byte> proof, byte[] signedData, byte[] crxId)
    {
        var publicKey = ReadLengthDelimitedField(proof, 1);
        var signature = ReadLengthDelimitedField(proof, 2);
        if (publicKey is null || signature is null || !PublicKeyMatchesId(publicKey, crxId))
        {
            return false;
        }

        try
        {
            using var rsa = RSA.Create();
            rsa.ImportSubjectPublicKeyInfo(publicKey, out _);
            return rsa.VerifyData(
                signedData,
                signature,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    private static bool VerifyEcdsaProof(ReadOnlySpan<byte> proof, byte[] signedData, byte[] crxId)
    {
        var publicKey = ReadLengthDelimitedField(proof, 1);
        var signature = ReadLengthDelimitedField(proof, 2);
        if (publicKey is null || signature is null || !PublicKeyMatchesId(publicKey, crxId))
        {
            return false;
        }

        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(publicKey, out _);
            return ecdsa.VerifyData(signedData, signature, HashAlgorithmName.SHA256);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    private static bool PublicKeyMatchesId(byte[] publicKey, byte[] crxId)
    {
        var digest = SHA256.HashData(publicKey);
        return CryptographicOperations.FixedTimeEquals(digest.AsSpan(0, 16), crxId);
    }

    private static IReadOnlyList<byte[]> ReadMessages(
        ReadOnlySpan<byte> message,
        int fieldNumber)
    {
        var matches = new List<byte[]>();
        var offset = 0;
        while (offset < message.Length)
        {
            var key = ReadVarint(message, ref offset);
            var wireType = (int)(key & 7);
            var field = checked((int)(key >> 3));
            if (wireType == 2)
            {
                var length = checked((int)ReadVarint(message, ref offset));
                if (length < 0 || offset + length > message.Length)
                {
                    throw new InvalidDataException("The CRX3 protobuf header is malformed.");
                }

                if (field == fieldNumber)
                {
                    matches.Add(message.Slice(offset, length).ToArray());
                }

                offset += length;
            }
            else
            {
                SkipProtobufValue(message, ref offset, wireType);
            }
        }

        return matches;
    }

    private static byte[]? ReadLengthDelimitedField(ReadOnlySpan<byte> message, int fieldNumber) =>
        ReadMessages(message, fieldNumber).FirstOrDefault();

    private static ulong ReadVarint(ReadOnlySpan<byte> message, ref int offset)
    {
        ulong result = 0;
        for (var shift = 0; shift < 64 && offset < message.Length; shift += 7)
        {
            var value = message[offset++];
            result |= (ulong)(value & 0x7f) << shift;
            if ((value & 0x80) == 0)
            {
                return result;
            }
        }

        throw new InvalidDataException("The CRX3 protobuf header is malformed.");
    }

    private static void SkipProtobufValue(ReadOnlySpan<byte> message, ref int offset, int wireType)
    {
        switch (wireType)
        {
            case 0:
                _ = ReadVarint(message, ref offset);
                break;
            case 1:
                offset += 8;
                break;
            case 2:
                offset += checked((int)ReadVarint(message, ref offset));
                break;
            case 5:
                offset += 4;
                break;
            default:
                throw new InvalidDataException("The CRX3 protobuf header uses an unsupported field.");
        }

        if (offset > message.Length)
        {
            throw new InvalidDataException("The CRX3 protobuf header is malformed.");
        }
    }

    private static void ExtractArchive(ReadOnlyMemory<byte> archiveBytes, string destination)
    {
        using var archiveStream = new MemoryStream(archiveBytes.ToArray(), writable: false);
        using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Read);
        if (archive.Entries.Count > MaximumFiles)
        {
            throw new InvalidDataException("The extension contains too many files.");
        }

        long extractedBytes = 0;
        var destinationRoot = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
        foreach (var entry in archive.Entries)
        {
            extractedBytes += entry.Length;
            if (extractedBytes > MaximumExtractedBytes)
            {
                throw new InvalidDataException("The unpacked extension is larger than LinkScape allows.");
            }

            var targetPath = Path.GetFullPath(Path.Combine(destination, entry.FullName));
            if (!targetPath.StartsWith(destinationRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The extension contains an unsafe file path.");
            }

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(targetPath);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            entry.ExtractToFile(targetPath, overwrite: true);
        }
    }

    private static IReadOnlyList<string> ReadPermissions(JsonElement manifest)
    {
        var permissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddStringArray(manifest, "permissions", permissions);
        AddStringArray(manifest, "host_permissions", permissions);
        return permissions.Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static void AddStringArray(
        JsonElement manifest,
        string propertyName,
        HashSet<string> destination)
    {
        if (!manifest.TryGetProperty(propertyName, out var values) ||
            values.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var value in values.EnumerateArray())
        {
            if (value.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(value.GetString()))
            {
                destination.Add(value.GetString()!);
            }
        }
    }

    private static string ReadManifestText(
        JsonElement manifest,
        string propertyName,
        string fallback) =>
        manifest.TryGetProperty(propertyName, out var value) &&
        value.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(value.GetString()) &&
        !value.GetString()!.StartsWith("__MSG_", StringComparison.Ordinal)
            ? value.GetString()!
            : fallback;

    private static string? ReadPopupPath(JsonElement manifest)
    {
        foreach (var actionName in new[] { "action", "browser_action", "page_action" })
        {
            if (manifest.TryGetProperty(actionName, out var action) &&
                action.ValueKind == JsonValueKind.Object &&
                action.TryGetProperty("default_popup", out var popup) &&
                popup.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(popup.GetString()))
            {
                return popup.GetString();
            }
        }

        return null;
    }

    private static string EncodeExtensionId(ReadOnlySpan<byte> bytes)
    {
        Span<char> result = stackalloc char[bytes.Length * 2];
        var index = 0;
        foreach (var value in bytes)
        {
            result[index++] = (char)('a' + (value >> 4));
            result[index++] = (char)('a' + (value & 0x0f));
        }

        return new string(result);
    }

    private static string SanitizePathSegment(string value)
    {
        foreach (var invalidCharacter in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(invalidCharacter, '_');
        }

        return string.IsNullOrWhiteSpace(value) ? "unknown" : value;
    }

    private static void ResetOwnedDirectory(string folder)
    {
        var chromeStoreRoot = Path.GetFullPath(GetStoreRoot()) + Path.DirectorySeparatorChar;
        var target = Path.GetFullPath(folder);
        if (!target.StartsWith(chromeStoreRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Refusing to modify a folder outside extension storage.");
        }

        if (Directory.Exists(target))
        {
            Directory.Delete(target, recursive: true);
        }
    }

    private static string GetStoreRoot() => Path.Combine(
        Windows.Storage.ApplicationData.Current.LocalFolder.Path,
        "BrowserExtensions",
        "ChromeWebStore");

    private static HttpClient CreateDownloadClient()
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = true,
            AutomaticDecompression =
                DecompressionMethods.Brotli |
                DecompressionMethods.Deflate |
                DecompressionMethods.GZip
        };
        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(60)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
            "Chrome/131.0.0.0 Safari/537.36");
        return client;
    }
}
