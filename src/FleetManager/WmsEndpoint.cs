namespace FleetManager;

public static class WmsEndpoint
{
    public static Uri Parse(string baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new InvalidOperationException("Wms:BaseUrl must be set.");

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
            throw new InvalidOperationException("Wms:BaseUrl must be an absolute URL.");

        // Uri.TryCreate accepts ftp://, mailto: and file://. HttpClient then fails
        // with NotSupportedException, so we refuse those schemes at startup.
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("Wms:BaseUrl must be an http or https URL.");

        return uri;
    }
}