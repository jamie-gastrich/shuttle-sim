namespace FleetCore;

public enum WmsDeliveryResult
{
    Delivered,
    Retry,
    Permanent,
}

public static class WmsDeliveryOutcome
{
    public static WmsDeliveryResult FromStatus(int? statusCode)
    {
        if (statusCode is null)
            return WmsDeliveryResult.Retry;

        var code = statusCode.Value;
        if (code is >= 200 and < 300)
            return WmsDeliveryResult.Delivered;
        if (code is 408 or 429)
            return WmsDeliveryResult.Retry;
        if (code >= 500)
            return WmsDeliveryResult.Retry;
        if (code >= 400)
            return WmsDeliveryResult.Permanent;
        return WmsDeliveryResult.Retry;
    }
}
