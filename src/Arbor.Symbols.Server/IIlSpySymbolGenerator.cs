namespace Arbor.Symbols.Server;

public interface IIlSpySymbolGenerator
{
    Task<bool> TryGeneratePdbAsync(string assemblyPath, string outputPdbPath, CancellationToken cancellationToken);
}
