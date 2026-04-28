using Arbor.Symbols.Core;

namespace Arbor.Symbols.Server;

public static class UiEndpoints
{
    public static IResult Dashboard(SymbolServerStatistics statistics, SymbolStorage storage)
    {
        var diskBytes = storage.GetDiskUsageBytes();
        var cached = storage.GetCachedSymbols();

        var rows = new System.Text.StringBuilder();
        foreach (var entry in cached.OrderByDescending(e => e.LastModifiedUtc))
        {
            var escapedFileName = System.Text.Json.JsonSerializer.Serialize(entry.RequestedFileName);
            var escapedIdentifier = System.Text.Json.JsonSerializer.Serialize(entry.Identifier);
            var escapedResourceFileName = System.Text.Json.JsonSerializer.Serialize(entry.ResourceFileName);
            rows.Append($"""
                <tr class="cache-row" data-name="{System.Web.HttpUtility.HtmlEncode(entry.RequestedFileName.ToLowerInvariant())}">
                  <td>{System.Web.HttpUtility.HtmlEncode(entry.RequestedFileName)}</td>
                  <td class="mono">{System.Web.HttpUtility.HtmlEncode(entry.Identifier)}</td>
                  <td>{System.Web.HttpUtility.HtmlEncode(entry.ResourceFileName)}</td>
                  <td class="right">{FormatBytes(entry.SizeBytes)}</td>
                  <td class="right">{entry.LastModifiedUtc:yyyy-MM-dd HH:mm:ss} UTC</td>
                  <td class="center">
                    <button class="btn-delete" onclick="deleteEntry({escapedFileName},{escapedIdentifier},{escapedResourceFileName},this)">Delete</button>
                  </td>
                </tr>
                """);
        }

        var html = $$"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
              <meta charset="utf-8"/>
              <meta name="viewport" content="width=device-width, initial-scale=1"/>
              <title>Arbor.Symbols Dashboard</title>
              <style>
                *, *::before, *::after { box-sizing: border-box; margin: 0; padding: 0; }
                body { font-family: system-ui, sans-serif; background: #f5f5f5; color: #222; }
                header { background: #1a1a2e; color: #fff; padding: 1rem 2rem; }
                header h1 { font-size: 1.4rem; font-weight: 600; }
                main { max-width: 1200px; margin: 2rem auto; padding: 0 1rem; }
                h2 { font-size: 1.1rem; margin-bottom: 1rem; color: #333; }
                .section { background: #fff; border-radius: 8px; padding: 1.5rem; margin-bottom: 1.5rem; box-shadow: 0 1px 4px rgba(0,0,0,.08); }
                .stats-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(160px, 1fr)); gap: 1rem; }
                .stat-card { background: #f0f4ff; border-radius: 8px; padding: 1rem; text-align: center; }
                .stat-card .value { font-size: 2rem; font-weight: 700; color: #1a1a2e; }
                .stat-card .label { font-size: 0.8rem; color: #555; margin-top: .25rem; }
                .disk { font-size: 1.5rem; font-weight: 700; color: #1a1a2e; }
                .disk-label { font-size: 0.85rem; color: #555; margin-top: .25rem; }
                .search-bar { width: 100%; padding: .5rem .75rem; font-size: 1rem; border: 1px solid #ccc; border-radius: 6px; margin-bottom: 1rem; }
                table { width: 100%; border-collapse: collapse; font-size: .9rem; }
                th { background: #1a1a2e; color: #fff; padding: .6rem .8rem; text-align: left; font-weight: 500; }
                td { padding: .55rem .8rem; border-bottom: 1px solid #eee; }
                tr:last-child td { border-bottom: none; }
                tr:hover td { background: #f8f8ff; }
                .right { text-align: right; }
                .center { text-align: center; }
                .mono { font-family: monospace; font-size: .8rem; }
                .btn-delete { background: #dc3545; color: #fff; border: none; border-radius: 4px; padding: .3rem .7rem; cursor: pointer; font-size: .8rem; }
                .btn-delete:hover { background: #b02a37; }
                .btn-delete:disabled { background: #aaa; cursor: default; }
                .empty { color: #888; text-align: center; padding: 2rem; }
              </style>
            </head>
            <body>
              <header>
                <h1>Arbor.Symbols Dashboard</h1>
              </header>
              <main>
                <div class="section">
                  <h2>Statistics</h2>
                  <div class="stats-grid">
                    <div class="stat-card"><div class="value">{{statistics.TotalRequests}}</div><div class="label">Total Requests</div></div>
                    <div class="stat-card"><div class="value">{{statistics.CacheHits}}</div><div class="label">Cache Hits</div></div>
                    <div class="stat-card"><div class="value">{{statistics.OfficialDownloads}}</div><div class="label">Downloaded</div></div>
                    <div class="stat-card"><div class="value">{{statistics.IlSpyGenerations}}</div><div class="label">ILSpy Generated</div></div>
                    <div class="stat-card"><div class="value">{{statistics.NotFound}}</div><div class="label">Not Found</div></div>
                  </div>
                </div>

                <div class="section">
                  <h2>Disk Usage</h2>
                  <div class="disk">{{FormatBytes(diskBytes)}}</div>
                  <div class="disk-label">{{cached.Count}} cached symbol(s)</div>
                </div>

                <div class="section">
                  <h2>Cached Symbols</h2>
                  <input class="search-bar" type="search" id="search" placeholder="Search by file name…" oninput="filterTable(this.value)"/>
                  <table>
                    <thead>
                      <tr>
                        <th>File</th>
                        <th>Identifier</th>
                        <th>Resource</th>
                        <th class="right">Size</th>
                        <th class="right">Last Modified</th>
                        <th class="center">Action</th>
                      </tr>
                    </thead>
                    <tbody id="cache-body">
                      {{(cached.Count == 0 ? "<tr><td colspan=\"6\" class=\"empty\">No cached symbols</td></tr>" : rows.ToString())}}
                    </tbody>
                  </table>
                </div>
              </main>
              <script>
                function filterTable(query) {
                  const q = query.toLowerCase();
                  document.querySelectorAll('#cache-body .cache-row').forEach(row => {
                    row.style.display = row.dataset.name.includes(q) ? '' : 'none';
                  });
                }

                async function deleteEntry(fileName, identifier, resourceFileName, btn) {
                  if (!confirm('Delete ' + fileName + '/' + identifier + '/' + resourceFileName + '?')) return;
                  btn.disabled = true;
                  const url = '/ui/cache/' + encodeURIComponent(fileName) + '/' + encodeURIComponent(identifier) + '/' + encodeURIComponent(resourceFileName);
                  const resp = await fetch(url, { method: 'DELETE' });
                  if (resp.ok) {
                    btn.closest('tr').remove();
                  } else {
                    alert('Delete failed: ' + resp.status);
                    btn.disabled = false;
                  }
                }
              </script>
            </body>
            </html>
            """;

        return Results.Content(html, "text/html");
    }

    public static IResult DeleteCacheEntry(
        string requestedFileName,
        string identifier,
        string resourceFileName,
        SymbolStorage storage)
    {
        var request = new SymbolResourceRequest(requestedFileName, identifier, resourceFileName);
        return storage.TryDelete(request) ? Results.Ok() : Results.NotFound();
    }

    private static string FormatBytes(long bytes)
    {
        return bytes switch
        {
            < 1024 => $"{bytes} B",
            < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
            < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
            _ => $"{bytes / (1024.0 * 1024 * 1024):F2} GB"
        };
    }
}
