namespace Arbor.Symbols.Server;

public sealed class SymbolServerStatistics
{
    private long _cacheHits;
    private long _officialDownloads;
    private long _ilSpyGenerations;
    private long _notFound;

    public long CacheHits => Interlocked.Read(ref _cacheHits);
    public long OfficialDownloads => Interlocked.Read(ref _officialDownloads);
    public long IlSpyGenerations => Interlocked.Read(ref _ilSpyGenerations);
    public long NotFound => Interlocked.Read(ref _notFound);
    public long TotalRequests => CacheHits + OfficialDownloads + IlSpyGenerations + NotFound;

    public void RecordCacheHit() => Interlocked.Increment(ref _cacheHits);
    public void RecordOfficialDownload() => Interlocked.Increment(ref _officialDownloads);
    public void RecordIlSpyGeneration() => Interlocked.Increment(ref _ilSpyGenerations);
    public void RecordNotFound() => Interlocked.Increment(ref _notFound);
}
