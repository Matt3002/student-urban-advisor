// ============================================================================
// ScoringService.cs - Calcolo unico dello Student Accessibility Score
// Unica implementazione della formula, usata da ranking, griglia di densita',
// raccomandazioni, clustering e privacy, cosi' che lo stesso punto abbia sempre
// lo stesso punteggio in tutta l'applicazione.
//
// 1) Conteggio servizi: una sola query PostGIS per un insieme di punti, con
//    ST_DWithin su geography (metri reali) entro RaggioMetri.
// 2) Sub-score 0-100 per fattore, lineari fino a una soglia di saturazione:
//    - trasporti: 100 a 0 m dalla fermata piu' vicina, 0 oltre DistanzaFermataMax;
//    - studio (biblioteche + sale studio), aree verdi, km di piste ciclabili,
//      residenze, mense, sedi: 100 * valore / soglia, troncato a 100.
// 3) Contesto orario: fuori dalla fascia diurna (8-20) i fattori legati a
//    servizi chiusi vengono ridotti e trasporti/residenze aumentati.
// 4) Punteggio finale: media pesata dei sub-score con i pesi del profilo
//    normalizzati a somma 1.
// ============================================================================
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using UrbanAdvisor.Api.Data;
using UrbanAdvisor.Api.Models;

namespace UrbanAdvisor.Api.Services
{
    public class ConteggiArea
    {
        public int Indice { get; set; }
        public double Lat { get; set; }
        public double Lon { get; set; }
        public int Biblioteche { get; set; }
        public int SaleStudio { get; set; }
        public int Fermate { get; set; }
        public int AreeVerdi { get; set; }
        public int Residenze { get; set; }
        public int Stazioni { get; set; }
        public int Mense { get; set; }
        public int Sedi { get; set; }
        public double KmPiste { get; set; }
        public double DistanzaFermataMetri { get; set; }

        public int TotalePoi => Biblioteche + SaleStudio + Fermate + AreeVerdi + Residenze + Stazioni + Mense + Sedi;
    }

    public class SubScores
    {
        public int Trasporti { get; set; }
        public int Studio { get; set; }
        public int AreeVerdi { get; set; }
        public int Mobilita { get; set; }
        public int Residenze { get; set; }
        public int Mense { get; set; }
        public int Sedi { get; set; }

        // Rappresentazione JSON con le chiavi usate dal front-end.
        public object PerApi() => new
        {
            trasporti = Trasporti, biblioteche = Studio, aree_verdi = AreeVerdi,
            mobilita = Mobilita, residenze = Residenze, mense = Mense, sedi = Sedi
        };
    }

    public class RisultatoScore
    {
        public int Punteggio { get; set; }
        public bool Diurna { get; set; }
        public string Fascia => Diurna ? "Diurna" : "Notturna";
        public SubScores Subscores { get; set; } = new();
        public List<string> Motivi { get; set; } = new();
        public string Dettaglio { get; set; } = "";
    }

    public record PesiProfilo(double Trasporti, double Studio, double AreeVerdi, double Mobilita,
                              double Residenze, double Mense, double Sedi)
    {
        // Pesi normalizzati a somma 1 a partire dal profilo (50 per ogni fattore se assente).
        public static PesiProfilo Da(ProfiloUtente? p)
        {
            double t = p?.PesoTrasporti ?? 50, b = p?.PesoBiblioteche ?? 50, v = p?.PesoAreeVerdi ?? 50;
            double m = p?.PesoMobilitaSostenibile ?? 50, r = p?.PesoResidenze ?? 50;
            double me = p?.PesoMense ?? 50, s = p?.PesoSedi ?? 50;
            double somma = t + b + v + m + r + me + s;
            if (somma <= 0) return new PesiProfilo(1.0 / 7, 1.0 / 7, 1.0 / 7, 1.0 / 7, 1.0 / 7, 1.0 / 7, 1.0 / 7);
            return new PesiProfilo(t / somma, b / somma, v / somma, m / somma, r / somma, me / somma, s / somma);
        }
    }

    public static class ScoringService
    {
        public const double RaggioMetri = 500;
        public const double DistanzaFermataMax = 1000;
        public const double SogliaStudio = 2, SogliaAreeVerdi = 3, SogliaKmPiste = 1.5;
        public const double SogliaResidenze = 2, SogliaMense = 1, SogliaSedi = 10;
        public const int OraInizioGiorno = 8, OraFineGiorno = 20;

        private const string SqlConteggi = @"
SELECT p.ord::int AS ""Indice"", p.lat AS ""Lat"", p.lon AS ""Lon"",
    (SELECT COUNT(*) FROM biblioteche x WHERE ST_DWithin(x.geog, p.g, @raggio))::int AS ""Biblioteche"",
    (SELECT COUNT(*) FROM sale_studio x WHERE ST_DWithin(x.geog, p.g, @raggio))::int AS ""SaleStudio"",
    (SELECT COUNT(*) FROM fermate_bus x WHERE ST_DWithin(x.geog, p.g, @raggio))::int AS ""Fermate"",
    (SELECT COUNT(*) FROM aree_verdi x WHERE ST_DWithin(x.geog, p.g, @raggio))::int AS ""AreeVerdi"",
    (SELECT COUNT(*) FROM residenze_universitarie x WHERE ST_DWithin(x.geog, p.g, @raggio))::int AS ""Residenze"",
    (SELECT COUNT(*) FROM stazioni_ferroviarie x WHERE ST_DWithin(x.geog, p.g, @raggio))::int AS ""Stazioni"",
    (SELECT COUNT(*) FROM mense x WHERE ST_DWithin(x.geog, p.g, @raggio))::int AS ""Mense"",
    (SELECT COUNT(*) FROM sedi_universitarie x WHERE ST_DWithin(x.geog, p.g, @raggio))::int AS ""Sedi"",
    COALESCE((SELECT SUM(ST_Length(ST_Intersection(x.geog, ST_Buffer(p.g, @raggio))))
              FROM piste_ciclabili x WHERE ST_DWithin(x.geog, p.g, @raggio)), 0) / 1000.0 AS ""KmPiste"",
    COALESCE((SELECT ST_Distance(x.geog, p.g) FROM fermate_bus x ORDER BY x.geog <-> p.g LIMIT 1), 99999) AS ""DistanzaFermataMetri""
FROM (
    SELECT u.lat, u.lon, u.ord, ST_SetSRID(ST_MakePoint(u.lon, u.lat), 4326)::geography AS g
    FROM unnest(@lats, @lons) WITH ORDINALITY AS u(lat, lon, ord)
) p
ORDER BY p.ord";

        // Conta i servizi attorno a ciascun punto con un'unica query spaziale (risultati nello stesso ordine dei punti).
        public static async Task<List<ConteggiArea>> ContaServiziAsync(
            UrbanAdvisorDbContext db, IReadOnlyList<(double Lat, double Lon)> punti, double raggioMetri = RaggioMetri)
        {
            if (punti.Count == 0) return new List<ConteggiArea>();

            var pLats = new NpgsqlParameter("lats", NpgsqlDbType.Array | NpgsqlDbType.Double) { Value = punti.Select(p => p.Lat).ToArray() };
            var pLons = new NpgsqlParameter("lons", NpgsqlDbType.Array | NpgsqlDbType.Double) { Value = punti.Select(p => p.Lon).ToArray() };
            var pRaggio = new NpgsqlParameter("raggio", NpgsqlDbType.Double) { Value = raggioMetri };

            var risultati = await db.Database
                .SqlQueryRaw<ConteggiArea>(SqlConteggi, pLats, pLons, pRaggio)
                .ToListAsync();
            return risultati.OrderBy(r => r.Indice).ToList();
        }

        // Conteggi per un singolo punto.
        public static async Task<ConteggiArea> ContaServiziAsync(
            UrbanAdvisorDbContext db, double lat, double lon, double raggioMetri = RaggioMetri)
        {
            var lista = await ContaServiziAsync(db, new List<(double, double)> { (lat, lon) }, raggioMetri);
            return lista[0];
        }

        // Indica se l'ora cade nella fascia diurna.
        public static bool IsDiurna(int ora) => ora >= OraInizioGiorno && ora < OraFineGiorno;

        // Sub-score lineare con saturazione: 100 quando il valore raggiunge la soglia.
        private static double Saturazione(double valore, double soglia) => Math.Min(100.0, 100.0 * valore / soglia);

        // Applica la formula dello Student Accessibility Score ai conteggi di un'area.
        public static RisultatoScore CalcolaScore(ConteggiArea c, int ora, PesiProfilo w)
        {
            bool diurna = IsDiurna(ora);

            double sT = Math.Clamp(100.0 * (1 - c.DistanzaFermataMetri / DistanzaFermataMax), 0, 100);
            double sB = Saturazione(c.Biblioteche + c.SaleStudio, SogliaStudio);
            double sV = Saturazione(c.AreeVerdi, SogliaAreeVerdi);
            double sM = Saturazione(c.KmPiste, SogliaKmPiste);
            double sR = Saturazione(c.Residenze, SogliaResidenze);
            double sMe = Saturazione(c.Mense, SogliaMense);
            double sS = Saturazione(c.Sedi, SogliaSedi);

            if (!diurna)
            {
                sB *= 0.3;
                sT = Math.Min(100, sT * 1.3);
                sR = Math.Min(100, sR * 1.5);
                sMe *= 0.3;
                sS *= 0.2;
            }

            var sub = new SubScores
            {
                Trasporti = (int)Math.Round(sT), Studio = (int)Math.Round(sB), AreeVerdi = (int)Math.Round(sV),
                Mobilita = (int)Math.Round(sM), Residenze = (int)Math.Round(sR),
                Mense = (int)Math.Round(sMe), Sedi = (int)Math.Round(sS)
            };

            double finale = sT * w.Trasporti + sB * w.Studio + sV * w.AreeVerdi + sM * w.Mobilita +
                            sR * w.Residenze + sMe * w.Mense + sS * w.Sedi;

            var motivi = new List<string>();
            if (sub.Trasporti >= 50) motivi.Add($"area ben collegata (fermata a {(int)c.DistanzaFermataMetri} m)");
            if (sub.Studio >= 50) motivi.Add($"alta densità di biblioteche/sale studio ({c.Biblioteche + c.SaleStudio})");
            if (sub.AreeVerdi >= 50) motivi.Add($"ricca di aree verdi ({c.AreeVerdi})");
            if (sub.Mobilita >= 50) motivi.Add($"presenza di piste ciclabili ({c.KmPiste:0.0} km)");
            if (sub.Residenze >= 50) motivi.Add($"vicina a residenze universitarie ({c.Residenze})");
            if (sub.Mense >= 50) motivi.Add($"buona offerta di mense/punti ristoro ({c.Mense})");
            if (sub.Sedi >= 50) motivi.Add($"alta concentrazione di sedi universitarie ({c.Sedi})");
            if (motivi.Count == 0) motivi.Add("area con servizi limitati");

            var fattori = new List<(string nome, int score, double peso)>
            {
                ("Trasporti pubblici", sub.Trasporti, w.Trasporti),
                ("Biblioteche/Sale studio", sub.Studio, w.Studio),
                ("Aree verdi", sub.AreeVerdi, w.AreeVerdi),
                ("Mobilità sostenibile", sub.Mobilita, w.Mobilita),
                ("Residenze universitarie", sub.Residenze, w.Residenze),
                ("Mense/Punti ristoro", sub.Mense, w.Mense),
                ("Sedi universitarie", sub.Sedi, w.Sedi)
            }.OrderByDescending(f => f.score * f.peso)
             .Select(f => $"{f.nome}: {(f.score >= 70 ? "alto" : f.score >= 40 ? "medio" : "basso")} ({f.score}/100, peso {(int)Math.Round(f.peso * 100)}%)");

            string fascia = diurna ? "Diurna" : "Notturna";
            string dettaglio = $"{(diurna ? "🌞" : "🌙")} Fascia {fascia}. " +
                               $"Fermata bus più vicina: {(int)c.DistanzaFermataMetri} m. " +
                               $"Nel raggio di {(int)RaggioMetri} m: {c.Biblioteche} biblioteche, {c.SaleStudio} sale studio, " +
                               $"{c.AreeVerdi} aree verdi, {c.KmPiste:0.0} km di piste ciclabili, {c.Residenze} residenze, " +
                               $"{c.Mense} mense/ristori, {c.Sedi} sedi universitarie. " +
                               string.Join(" | ", fattori);

            return new RisultatoScore
            {
                Punteggio = Math.Clamp((int)Math.Round(finale), 0, 100),
                Diurna = diurna,
                Subscores = sub,
                Motivi = motivi,
                Dettaglio = dettaglio
            };
        }
    }
}
