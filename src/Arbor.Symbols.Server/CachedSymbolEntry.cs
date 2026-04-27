namespace Arbor.Symbols.Server;

public sealed record CachedSymbolEntry(
    string RequestedFileName,
    string Identifier,
    string ResourceFileName,
    long SizeBytes,
    DateTime LastModifiedUtc);
