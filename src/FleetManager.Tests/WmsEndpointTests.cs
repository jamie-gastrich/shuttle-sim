namespace FleetManager;

public class WmsEndpointTests
{
    [Fact]
    public void The_wms_url_must_be_an_http_or_https_url()
    {
        Assert.IsType<Uri>(WmsEndpoint.Parse("http://127.0.0.1:5090"));
        Assert.IsType<Uri>(WmsEndpoint.Parse("https://wms.example/api"));
        Assert.Throws<InvalidOperationException>(() => WmsEndpoint.Parse("ftp://wms.example/api"));
        Assert.Throws<InvalidOperationException>(() => WmsEndpoint.Parse("not-a-url"));
        Assert.Throws<InvalidOperationException>(() => WmsEndpoint.Parse(""));
    }
}