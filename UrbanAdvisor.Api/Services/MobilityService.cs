// ============================================================================
// MobilityService.cs - Tempi di percorrenza multimodali (piedi, bici, TPL)
// Piedi e bici: distanza di Haversine moltiplicata per un fattore di deviazione
// stradale (1.3) e velocita' medie di 5 e 15 km/h.
// Trasporto pubblico (GTFS TPER): si cercano le corse DIRETTE (senza cambi) che
// passano da una fermata entro RaggioPedonaleMetri dall'origine e, piu' avanti
// nella stessa corsa, da una fermata entro lo stesso raggio dalla destinazione,
// con partenza nella fascia oraria e nel tipo di giorno richiesti.
// Tempo = piedi fino alla fermata + attesa media (meta' dell'intervallo tra le
// corse della coppia di fermate) + tempo a bordo reale da stop_times + piedi
// dalla fermata alla destinazione. Si sceglie la coppia di fermate piu' rapida.
// Il tempo "con trasporto pubblico" e' il minimo tra bus e percorso a piedi.
// ============================================================================
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using UrbanAdvisor.Api.Data;
using UrbanAdvisor.Api.Models;

namespace UrbanAdvisor.Api.Services
{
    public class ConnessioneTpl
    {
        public string StopDa { get; set; } = "";
        public string? NomeDa { get; set; }
        public double DistDa { get; set; }
        public string StopA { get; set; } = "";
        public string? NomeA { get; set; }
        public double DistA { get; set; }
        public int Corse { get; set; }
        public double BordoMin { get; set; }
        public string? Linee { get; set; }
        public double TotaleMin { get; set; }
    }

    public static class MobilityService
    {
        public const double FattoreDeviazione = 1.3;
        public const double VelocitaPiediMetriMin = 5000.0 / 60.0;
        public const double VelocitaBiciMetriMin = 15000.0 / 60.0;
        public const double RaggioPedonaleMetri = 500;

        private const string SqlConnessione = @"
WITH o AS (
    SELECT f.stop_id, f.nome, ST_Distance(f.geog, ST_SetSRID(ST_MakePoint(@olon, @olat), 4326)::geography) AS d
    FROM gtfs_fermate f
    WHERE ST_DWithin(f.geog, ST_SetSRID(ST_MakePoint(@olon, @olat), 4326)::geography, @raggio)
), d AS (
    SELECT f.stop_id, f.nome, ST_Distance(f.geog, ST_SetSRID(ST_MakePoint(@dlon, @dlat), 4326)::geography) AS d
    FROM gtfs_fermate f
    WHERE ST_DWithin(f.geog, ST_SetSRID(ST_MakePoint(@dlon, @dlat), 4326)::geography, @raggio)
), corse AS (
    SELECT a.stop_id AS da, b.stop_id AS a_, a.trip_id, t.route_id, (b.arr_sec - a.dep_sec) AS bordo_sec
    FROM o
    JOIN gtfs_stop_times a ON a.stop_id = o.stop_id
    JOIN gtfs_trips t ON t.trip_id = a.trip_id
         AND CASE @tipo WHEN 'sabato' THEN t.sabato WHEN 'festivo' THEN t.festivo ELSE t.feriale END
    JOIN gtfs_stop_times b ON b.trip_id = a.trip_id AND b.stop_sequence > a.stop_sequence
    JOIN d ON d.stop_id = b.stop_id
    WHERE (a.dep_sec / 3600) % 24 = @ora
), coppie AS (
    SELECT da, a_, COUNT(DISTINCT trip_id)::int AS n, AVG(bordo_sec) / 60.0 AS bordo_min,
           string_agg(DISTINCT route_id, ', ') AS linee
    FROM corse
    GROUP BY da, a_
)
SELECT c.da AS ""StopDa"", o.nome AS ""NomeDa"", o.d AS ""DistDa"",
       c.a_ AS ""StopA"", d.nome AS ""NomeA"", d.d AS ""DistA"",
       c.n AS ""Corse"", c.bordo_min::float8 AS ""BordoMin"", c.linee AS ""Linee"",
       (o.d * @dev / @vpiedi + 30.0 / c.n + c.bordo_min + d.d * @dev / @vpiedi)::float8 AS ""TotaleMin""
FROM coppie c
JOIN o ON o.stop_id = c.da
JOIN d ON d.stop_id = c.a_
ORDER BY ""TotaleMin""
LIMIT 1";

        // Tempo a piedi (minuti) per una distanza in linea d'aria in metri.
        public static double MinutiPiedi(double metri) => metri * FattoreDeviazione / VelocitaPiediMetriMin;

        // Tempo in bici (minuti) per una distanza in linea d'aria in metri.
        public static double MinutiBici(double metri) => metri * FattoreDeviazione / VelocitaBiciMetriMin;

        // Sede universitaria (esclusi i musei) piu' vicina a una posizione.
        public static Task<SedeUniversitaria?> SedePiuVicinaAsync(UrbanAdvisorDbContext db, double lat, double lon)
        {
            var p = GeoUtils.Punto(lat, lon);
            return db.SediUniversitarie
                .Where(s => s.Geog != null && s.Tipo != "museo")
                .OrderBy(s => s.Geog!.Distance(p))
                .FirstOrDefaultAsync();
        }

        // Migliore corsa diretta di trasporto pubblico tra due posizioni in una fascia oraria (null se assente).
        public static async Task<ConnessioneTpl?> CercaConnessioneAsync(
            UrbanAdvisorDbContext db, double oLat, double oLon, double dLat, double dLon, int ora, int giorno)
        {
            var parametri = new object[]
            {
                new NpgsqlParameter("olat", NpgsqlDbType.Double) { Value = oLat },
                new NpgsqlParameter("olon", NpgsqlDbType.Double) { Value = oLon },
                new NpgsqlParameter("dlat", NpgsqlDbType.Double) { Value = dLat },
                new NpgsqlParameter("dlon", NpgsqlDbType.Double) { Value = dLon },
                new NpgsqlParameter("raggio", NpgsqlDbType.Double) { Value = RaggioPedonaleMetri },
                new NpgsqlParameter("ora", NpgsqlDbType.Integer) { Value = Math.Clamp(ora, 0, 23) },
                new NpgsqlParameter("tipo", NpgsqlDbType.Text) { Value = ScoringService.TipoGiorno(Math.Clamp(giorno, 0, 6)) },
                new NpgsqlParameter("dev", NpgsqlDbType.Double) { Value = FattoreDeviazione },
                new NpgsqlParameter("vpiedi", NpgsqlDbType.Double) { Value = VelocitaPiediMetriMin }
            };
            var lista = await db.Database.SqlQueryRaw<ConnessioneTpl>(SqlConnessione, parametri).ToListAsync();
            return lista.FirstOrDefault();
        }

        // Tempo (minuti) per una modalita' tra due posizioni; per il TPL e' il minimo tra bus diretto e piedi.
        public static async Task<(double minuti, string mezzo)> TempoAsync(
            UrbanAdvisorDbContext db, double oLat, double oLon, double dLat, double dLon, int ora, int giorno, string modalita)
        {
            double distanza = GeoUtils.HaversineMetri(oLat, oLon, dLat, dLon);
            double piedi = MinutiPiedi(distanza);
            if (modalita == "piedi") return (piedi, "piedi");
            if (modalita == "bici") return (MinutiBici(distanza), "bici");

            var conn = await CercaConnessioneAsync(db, oLat, oLon, dLat, dLon, ora, giorno);
            if (conn != null && conn.TotaleMin < piedi) return (conn.TotaleMin, "bus " + conn.Linee);
            return (piedi, "piedi");
        }

        // Dettaglio multimodale (piedi, bici, TPL) tra due posizioni, nel formato usato dall'API.
        public static async Task<object> DettaglioAsync(
            UrbanAdvisorDbContext db, double oLat, double oLon, double dLat, double dLon, int ora, int giorno)
        {
            double distanza = GeoUtils.HaversineMetri(oLat, oLon, dLat, dLon);
            double piedi = MinutiPiedi(distanza);
            var conn = await CercaConnessioneAsync(db, oLat, oLon, dLat, dLon, ora, giorno);

            object trasportoPubblico = conn == null
                ? (object)new
                {
                    disponibile = false,
                    motivo = $"Nessuna corsa diretta tra fermate entro {(int)RaggioPedonaleMetri} m da partenza e arrivo " +
                             $"nella fascia {ora}:00-{ora + 1}:00 ({ScoringService.TipoGiorno(giorno)})"
                }
                : (object)new
                {
                    disponibile = true,
                    tempo_totale_minuti = Math.Round(conn.TotaleMin, 1),
                    piu_veloce_a_piedi = piedi < conn.TotaleMin,
                    dettaglio = new
                    {
                        a_piedi_fino_fermata_min = Math.Round(MinutiPiedi(conn.DistDa), 1),
                        attesa_media_min = Math.Round(30.0 / conn.Corse, 1),
                        a_bordo_min = Math.Round(conn.BordoMin, 1),
                        a_piedi_da_fermata_min = Math.Round(MinutiPiedi(conn.DistA), 1),
                        fermata_partenza = conn.NomeDa,
                        fermata_arrivo = conn.NomeA,
                        linee = conn.Linee,
                        corse_ora = conn.Corse,
                        headway_minuti = Math.Round(60.0 / conn.Corse, 1)
                    }
                };

            return new
            {
                distanza_diretta_metri = Math.Round(distanza, 0),
                piedi = new { tempo_minuti = Math.Round(piedi, 1) },
                bici = new { tempo_minuti = Math.Round(MinutiBici(distanza), 1) },
                trasporto_pubblico = trasportoPubblico
            };
        }
    }
}
