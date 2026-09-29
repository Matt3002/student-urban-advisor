// ============================================================================
// PrivacyService.cs - Metriche per la valutazione della privacy della posizione
// La perturbazione della posizione avviene nel browser (Frontend/app.js,
// Laplace planare o gaussiana): il server riceve solo coordinate gia'
// perturbate. Per l'esperimento di valutazione il client invia la posizione
// reale e i campioni perturbati, e il server calcola:
// - Privacy Perturbation: distanza (m) tra posizione reale e pubblicata;
// - Quality of Service: perdita di score |score_reale - score_perturbato| e
//   recall dei servizi vicini, cioe' la frazione dei PoI entro RaggioMetri
//   dalla posizione reale che risultano entro RaggioMetri anche da quella
//   perturbata.
// ============================================================================
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using UrbanAdvisor.Api.Data;

namespace UrbanAdvisor.Api.Services
{
    public class RecallServizi
    {
        public int Indice { get; set; }
        public int Reali { get; set; }
        public int Comuni { get; set; }

        public double Recall => Reali == 0 ? 1.0 : (double)Comuni / Reali;
    }

    public static class PrivacyService
    {
        private const string SqlRecall = @"
SELECT u.ord::int AS ""Indice"",
    (SELECT COUNT(*) FROM poi_tutti p WHERE ST_DWithin(p.geog, r.g, @raggio))::int AS ""Reali"",
    (SELECT COUNT(*) FROM poi_tutti p
     WHERE ST_DWithin(p.geog, r.g, @raggio)
       AND ST_DWithin(p.geog, ST_SetSRID(ST_MakePoint(u.lon, u.lat), 4326)::geography, @raggio))::int AS ""Comuni""
FROM (SELECT ST_SetSRID(ST_MakePoint(@rlon, @rlat), 4326)::geography AS g) r,
     unnest(@lats, @lons) WITH ORDINALITY AS u(lat, lon, ord)
ORDER BY u.ord";

        // Recall dei servizi vicini per ciascuna posizione perturbata rispetto a quella reale.
        public static async Task<List<RecallServizi>> RecallAsync(
            UrbanAdvisorDbContext db, double lat, double lon, IReadOnlyList<(double Lat, double Lon)> perturbati,
            double raggioMetri = ScoringService.RaggioMetri)
        {
            if (perturbati.Count == 0) return new List<RecallServizi>();
            var parametri = new object[]
            {
                new NpgsqlParameter("rlat", NpgsqlDbType.Double) { Value = lat },
                new NpgsqlParameter("rlon", NpgsqlDbType.Double) { Value = lon },
                new NpgsqlParameter("lats", NpgsqlDbType.Array | NpgsqlDbType.Double) { Value = perturbati.Select(p => p.Lat).ToArray() },
                new NpgsqlParameter("lons", NpgsqlDbType.Array | NpgsqlDbType.Double) { Value = perturbati.Select(p => p.Lon).ToArray() },
                new NpgsqlParameter("raggio", NpgsqlDbType.Double) { Value = raggioMetri }
            };
            var lista = await db.Database.SqlQueryRaw<RecallServizi>(SqlRecall, parametri).ToListAsync();
            return lista.OrderBy(r => r.Indice).ToList();
        }
    }
}
