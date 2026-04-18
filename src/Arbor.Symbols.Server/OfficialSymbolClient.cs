using Arbor.Symbols.Core;

namespace Arbor.Symbols.Server;

public sealed class OfficialSymbolClient(HttpClient httpClient) : IOfficialSymbolClient
{
    public async Task<Stream?> TryDownloadAsync(SymbolResourceRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var relativeUri = SymbolResourcePathHelper.BuildRelativeUri(request);
            var response = await httpClient.GetAsync(relativeUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                response.Dispose();
                return null;
            }

            return await response.Content.ReadAsStreamAsync(cancellationToken);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }
}
