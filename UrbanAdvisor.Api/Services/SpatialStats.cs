// ============================================================================
// SpatialStats.cs - Statistiche spaziali sulla griglia di analisi
// - Conteggio dei PoI per cella: celle rettangolari NON sovrapposte della
//   bounding box (ST_Intersects con l'envelope della cella), per categoria.
// - Indice di Moran globale con pesi di contiguita' "queen" (celle che
//   condividono un lato o un vertice, w = 1, poi standardizzati per riga):
//   I = (N / S0) * sum_ij w_ij z_i z_j / sum_i z_i^2, con z_i = x_i - media.
//   Valore atteso sotto ipotesi di casualita': E[I] = -1 / (N - 1).
//   Significativita' con test a permutazioni: i valori vengono rimescolati
//   casualmente tra le celle e p = (numero di I permutati >= I osservato + 1) /
//   (permutazioni + 1) (test a una coda per autocorrelazione positiva).
// ============================================================================
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using UrbanAdvisor.Api.Data;

namespace UrbanAdvisor.Api.Services
{
    public class ConteggioCella
    {
        public int I { get; set; }
        public int J { get; set; }
        public int Conteggio { get; set; }
    }

    public record RisultatoMoran(double MoranI, double Atteso, double PValue, double ZPermutazioni, int Permutazioni, string Interpretazione);

    public static class SpatialStats
    {
        public static readonly Dictionary<string, string[]> Indicatori = new()
        {
            ["biblioteche"] = new[] { "biblioteche", "salestudio" },
            ["fermate"] = new[] { "fermate" },
            ["sedi"] = new[] { "sedi" },
            ["areeverdi"] = new[] { "areeverdi" },
            ["residenze"] = new[] { "residenze" },
            ["mense"] = new[] { "mense" }
        };

        private const string SqlConteggioCelle = @"
SELECT c.i AS ""I"", c.j AS ""J"",
    (SELECT COUNT(*) FROM poi_tutti p WHERE p.categoria = ANY(@categorie) AND ST_Intersects(p.geom, c.env))::int AS ""Conteggio""
FROM (
    SELECT i, j, ST_MakeEnvelope(@minlon + j * @steplon, @minlat + i * @steplat,
                                 @minlon + (j + 1) * @steplon, @minlat + (i + 1) * @steplat, 4326) AS env
    FROM generate_series(0, @n - 1) AS i, generate_series(0, @n - 1) AS j
) c
ORDER BY c.i, c.j";

        // Numero di PoI delle categorie date in ciascuna cella n x n (stesso ordine di GeoUtils.CentriGriglia).
        public static async Task<double[]> ConteggiPerCellaAsync(UrbanAdvisorDbContext db, string[] categorie, int n)
        {
            var parametri = new object[]
            {
                new NpgsqlParameter("categorie", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = categorie },
                new NpgsqlParameter("minlat", NpgsqlDbType.Double) { Value = GeoUtils.MinLat },
                new NpgsqlParameter("minlon", NpgsqlDbType.Double) { Value = GeoUtils.MinLon },
                new NpgsqlParameter("steplat", NpgsqlDbType.Double) { Value = (GeoUtils.MaxLat - GeoUtils.MinLat) / n },
                new NpgsqlParameter("steplon", NpgsqlDbType.Double) { Value = (GeoUtils.MaxLon - GeoUtils.MinLon) / n },
                new NpgsqlParameter("n", NpgsqlDbType.Integer) { Value = n }
            };
            var celle = await db.Database.SqlQueryRaw<ConteggioCella>(SqlConteggioCelle, parametri).ToListAsync();
            var valori = new double[n * n];
            foreach (var c in celle) valori[c.I * n + c.J] = c.Conteggio;
            return valori;
        }

        // Pesi queen standardizzati per riga su una griglia n x n (liste di vicini e peso di ciascuno).
        private static (int[][] vicini, double[] peso) PesiQueen(int n)
        {
            var vicini = new int[n * n][];
            var peso = new double[n * n];
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                {
                    var lista = new List<int>();
                    for (int di = -1; di <= 1; di++)
                        for (int dj = -1; dj <= 1; dj++)
                        {
                            if (di == 0 && dj == 0) continue;
                            int ni = i + di, nj = j + dj;
                            if (ni >= 0 && ni < n && nj >= 0 && nj < n) lista.Add(ni * n + nj);
                        }
                    vicini[i * n + j] = lista.ToArray();
                    peso[i * n + j] = 1.0 / lista.Count;
                }
            return (vicini, peso);
        }

        // Indice di Moran per valori gia' centrati, con pesi queen standardizzati (S0 = N).
        private static double Moran(double[] z, int[][] vicini, double[] peso)
        {
            double num = 0, den = 0;
            for (int k = 0; k < z.Length; k++)
            {
                den += z[k] * z[k];
                double somma = 0;
                foreach (var v in vicini[k]) somma += z[v];
                num += z[k] * somma * peso[k];
            }
            return den == 0 ? 0 : num / den;
        }

        // Indice di Moran globale con test a permutazioni su una griglia n x n.
        public static RisultatoMoran MoranGriglia(double[] valori, int n, int permutazioni = 999, int seed = 42)
        {
            int N = valori.Length;
            double media = valori.Average();
            var z = valori.Select(v => v - media).ToArray();
            var (vicini, peso) = PesiQueen(n);
            double osservato = Moran(z, vicini, peso);
            double atteso = -1.0 / (N - 1);

            var rng = new Random(seed);
            var perm = (double[])z.Clone();
            var campioni = new double[permutazioni];
            int maggiori = 0;
            for (int p = 0; p < permutazioni; p++)
            {
                for (int k = N - 1; k > 0; k--)
                {
                    int r = rng.Next(k + 1);
                    (perm[k], perm[r]) = (perm[r], perm[k]);
                }
                campioni[p] = Moran(perm, vicini, peso);
                if (campioni[p] >= osservato) maggiori++;
            }
            double pValue = (maggiori + 1.0) / (permutazioni + 1.0);
            double mediaPerm = campioni.Average();
            double devPerm = Math.Sqrt(campioni.Average(c => (c - mediaPerm) * (c - mediaPerm)));
            double zPerm = devPerm > 0 ? (osservato - mediaPerm) / devPerm : 0;

            string interpretazione = pValue >= 0.05
                ? "Nessuna autocorrelazione significativa (p ≥ 0.05): distribuzione compatibile con il caso"
                : osservato > atteso
                    ? "Autocorrelazione positiva significativa: celle con valori simili tendono a essere vicine"
                    : "Valori sotto l'atteso: celle diverse tendono a essere vicine (dispersione)";

            return new RisultatoMoran(Math.Round(osservato, 4), Math.Round(atteso, 4), Math.Round(pValue, 4),
                Math.Round(zPerm, 2), permutazioni, interpretazione);
        }
    }
}
