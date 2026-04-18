using System.Buffers.Binary;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Arbor.Symbols.Core;

public static class SymbolResourcePathHelper
{
    public static bool TryCreateAssemblyRequest(string assemblyPath, out SymbolResourceRequest request)
    {
        request = default!;

        if (!File.Exists(assemblyPath))
        {
            return false;
        }

        var fileName = Path.GetFileName(assemblyPath);

        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);

        if (!peReader.HasMetadata || peReader.PEHeaders.PEHeader is null)
        {
            return false;
        }

        var timestamp = unchecked((uint)peReader.PEHeaders.CoffHeader.TimeDateStamp);
        var imageSize = unchecked((uint)peReader.PEHeaders.PEHeader.SizeOfImage);
        var identifier = $"{timestamp:X8}{imageSize:X}";

        request = new SymbolResourceRequest(fileName, identifier, fileName);
        return true;
    }

    public static bool TryCreateAssociatedPdbRequest(string assemblyPath, out SymbolResourceRequest request)
    {
        request = default!;

        if (!File.Exists(assemblyPath))
        {
            return false;
        }

        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);

        foreach (var entry in peReader.ReadDebugDirectory())
        {
            if (entry.Type != DebugDirectoryEntryType.CodeView)
            {
                continue;
            }

            var codeViewData = peReader.ReadCodeViewDebugDirectoryData(entry);
            var pdbFileName = Path.GetFileName(codeViewData.Path);
            var identifier = $"{codeViewData.Guid:N}{codeViewData.Age:X}".ToUpperInvariant();

            request = new SymbolResourceRequest(pdbFileName, identifier, pdbFileName);
            return true;
        }

        return false;
    }

    public static bool TryCreatePortablePdbRequest(string pdbPath, out SymbolResourceRequest request)
    {
        request = default!;

        if (!File.Exists(pdbPath))
        {
            return false;
        }

        try
        {
            using var stream = File.OpenRead(pdbPath);
            using var metadataReaderProvider = MetadataReaderProvider.FromPortablePdbStream(stream, MetadataStreamOptions.PrefetchMetadata);

            var debugMetadataHeader = metadataReaderProvider.GetMetadataReader().DebugMetadataHeader;
            if (debugMetadataHeader is null)
            {
                return false;
            }

            var debugId = debugMetadataHeader.Id;
            var idBytes = debugId.ToArray();

            if (idBytes.Length < 20)
            {
                return false;
            }

            var guid = new Guid(idBytes.AsSpan(0, 16));
            var stamp = BinaryPrimitives.ReadUInt32LittleEndian(idBytes.AsSpan(16, 4));

            var identifier = $"{guid:N}{stamp:X8}".ToUpperInvariant();
            var fileName = Path.GetFileName(pdbPath);

            request = new SymbolResourceRequest(fileName, identifier, fileName);
            return true;
        }
        catch (BadImageFormatException)
        {
            return false;
        }
    }

    public static string GetCachePath(string cacheRootDirectory, SymbolResourceRequest request)
    {
        Directory.CreateDirectory(cacheRootDirectory);

        var fullRoot = Path.GetFullPath(cacheRootDirectory);
        var combinedPath = Path.Combine(cacheRootDirectory, request.RequestedFileName, request.Identifier, request.ResourceFileName);
        var fullPath = Path.GetFullPath(combinedPath);

        if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Resolved symbol cache path escaped cache root.");
        }

        return fullPath;
    }

    public static string BuildRelativeUri(SymbolResourceRequest request)
    {
        return $"{Uri.EscapeDataString(request.RequestedFileName)}/{Uri.EscapeDataString(request.Identifier)}/{Uri.EscapeDataString(request.ResourceFileName)}";
    }
}
