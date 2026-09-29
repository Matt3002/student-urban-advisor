// ============================================================================
// GeoUtils.cs - Funzioni geografiche di supporto
// Distanza di Haversine in metri (per i calcoli lato applicazione), costruzione
// di punti NTS in WGS84 e definizione della griglia di analisi su Bologna
// (bounding box 44.47-44.52 N, 11.30-11.38 E) usata da densita', raccomandazioni,
// clustering e isocrone. Le distanze nel database sono calcolate su geography.
// ============================================================================
using NetTopologySuite.Geometries;

namespace UrbanAdvisor.Api.Services
{
    public static class GeoUtils
    {
        public const double RaggioTerraMetri = 6371008.8;
        public const double MinLat = 44.47, MaxLat = 44.52;
        public const double MinLon = 11.30, MaxLon = 11.38;

        // Distanza ortodromica (formula di Haversine) tra due coordinate WGS84, in metri.
        public static double HaversineMetri(double lat1, double lon1, double lat2, double lon2)
        {
            double dLat = (lat2 - lat1) * Math.PI / 180.0;
            double dLon = (lon2 - lon1) * Math.PI / 180.0;
            double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                       Math.Cos(lat1 * Math.PI / 180.0) * Math.Cos(lat2 * Math.PI / 180.0) *
                       Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            return 2 * RaggioTerraMetri * Math.Asin(Math.Min(1.0, Math.Sqrt(a)));
        }

        // Distanza in metri tra due geometrie puntuali NTS (X = lon, Y = lat).
        public static double HaversineMetri(Geometry a, Geometry b)
        {
            var ca = a.Coordinate;
            var cb = b.Coordinate;
            return HaversineMetri(ca.Y, ca.X, cb.Y, cb.X);
        }

        // Crea un punto NTS con SRID 4326 a partire da latitudine e longitudine.
        public static Point Punto(double lat, double lon) => new Point(lon, lat) { SRID = 4326 };

        // Restituisce i centri delle celle di una griglia n x n sulla bounding box di analisi.
        public static List<(double Lat, double Lon)> CentriGriglia(int n)
        {
            double stepLat = (MaxLat - MinLat) / n;
            double stepLon = (MaxLon - MinLon) / n;
            var centri = new List<(double Lat, double Lon)>(n * n);
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                    centri.Add((MinLat + (i + 0.5) * stepLat, MinLon + (j + 0.5) * stepLon));
            return centri;
        }
    }
}
