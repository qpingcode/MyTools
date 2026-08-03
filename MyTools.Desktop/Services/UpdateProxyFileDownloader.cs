using System.Net;
using System.Net.Http;
using Velopack.Sources;

namespace MyTools.Desktop.Services;

internal class UpdateProxyFileDownloader(Uri? proxyUri) : HttpClientFileDownloader
{
    protected override HttpClientHandler CreateHttpClientHandler()
    {
        var handler = base.CreateHttpClientHandler();
        if (proxyUri == null)
        {
            handler.UseProxy = false;
            return handler;
        }

        handler.Proxy = new WebProxy(proxyUri);
        handler.UseProxy = true;
        return handler;
    }
}


