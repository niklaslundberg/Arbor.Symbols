namespace Arbor.Symbols.Core;

public sealed record SymbolResourceRequest(string RequestedFileName, string Identifier, string ResourceFileName)
{
    public string RelativePath => $"{RequestedFileName}/{Identifier}/{ResourceFileName}";
}
