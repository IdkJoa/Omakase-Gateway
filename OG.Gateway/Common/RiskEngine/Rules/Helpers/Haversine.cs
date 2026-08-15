using System;

namespace Application.Common.RiskEngine.Rules.Helpers;

public static class Haversine
{
    private const double EarthRadiusKm = 6371.0;

    public static double Distance(double lat1, double lon1, double lat2, double lon2)
    {
        var dLat = ToRadians(lat2 - lat1);
        var dLon = ToRadians(lon2 - lon1);

        var rLat1 = ToRadians(lat1);
        var rLat2 = ToRadians(lat2);

        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2) * Math.Cos(rLat1) * Math.Cos(rLat2);

        var c = 2 * Math.Asin(Math.Sqrt(a));

        return EarthRadiusKm * c;
    }

    private static double ToRadians(double angle)
    {
        return Math.PI * angle / 180.0;
    }
}
