// ============================================================================
// Program.cs - Student Urban Accessibility Advisor
// Configurazione dell'applicazione .NET (Minimal API) e definizione di tutti
// gli endpoint REST: lettura PoI, ricerca spaziale, scoring context-aware,
// profilazione, analisi spaziale/temporale, mobilita', privacy e clustering.
// Distanze e buffer sono calcolati in metri sulla colonna geography 'geog';
// la formula dello score e' centralizzata in Services/ScoringService.cs.
// Il front-end statico e' servito da un container nginx separato che inoltra
// le chiamate /api/ a questo servizio. Documento OpenAPI: /openapi/v1.json.
// ============================================================================

using Microsoft.EntityFrameworkCore;
using UrbanAdvisor.Api.Data;
using UrbanAdvisor.Api.Models;
using UrbanAdvisor.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

builder.Services.AddOpenApi();

builder.Services.AddDbContext<UrbanAdvisorDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        o => o.UseNetTopologySuite()
    ));

var app = builder.Build();

app.UseCors("AllowAll");
app.MapOpenApi();

// Restituisce tutte le biblioteche con coordinate.
app.MapGet("/api/biblioteche", async (UrbanAdvisorDbContext db) =>
{
    var data = await db.Biblioteche
        .Select(b => new {
            id = b.Id, nome = b.Nome, indirizzo = b.Indirizzo,
            quartiere = b.Quartiere, postazioni = b.PostazioniLettura,
            categoria = "biblioteche",
            lat = b.Geom != null ? b.Geom.Coordinate.Y : 0,
            lon = b.Geom != null ? b.Geom.Coordinate.X : 0
        }).ToListAsync();
    return Results.Ok(data);
}).WithName("GetBiblioteche");

// Restituisce tutte le sale studio.
app.MapGet("/api/salestudio", async (UrbanAdvisorDbContext db) =>
{
    return Results.Ok(await db.SaleStudio
        .Select(s => new {
            id = s.Id, nome = s.Nome, indirizzo = s.Indirizzo,
            posti = s.Posti, fonte = s.Fonte,
            categoria = "salestudio",
            lat = s.Geom != null ? s.Geom.Coordinate.Y : 0,
            lon = s.Geom != null ? s.Geom.Coordinate.X : 0
        }).ToListAsync());
});

// Restituisce tutte le fermate bus TPER.
app.MapGet("/api/fermate", async (UrbanAdvisorDbContext db) =>
{
    return Results.Ok(await db.FermateBus
        .Select(f => new {
            id = f.CodiceFermata, nome = f.NomeFermata, linea = f.LineaBus,
            categoria = "fermate",
            lat = f.Geom != null ? f.Geom.Coordinate.Y : 0,
            lon = f.Geom != null ? f.Geom.Coordinate.X : 0
        }).ToListAsync());
});

// Restituisce tutte le residenze universitarie.
app.MapGet("/api/residenze", async (UrbanAdvisorDbContext db) =>
{
    return Results.Ok(await db.ResidenzeUniversitarie
        .Select(r => new {
            id = r.Id, nome = r.Nome, posti = r.PostiLetto,
            categoria = "residenze",
            lat = r.Geom != null ? r.Geom.Coordinate.Y : 0,
            lon = r.Geom != null ? r.Geom.Coordinate.X : 0
        }).ToListAsync());
});

// Restituisce tutte le aree verdi.
app.MapGet("/api/areeverdi", async (UrbanAdvisorDbContext db) =>
{
    return Results.Ok(await db.AreeVerdi
        .Select(a => new {
            id = a.Id, nome = a.NomeArea, tipo = a.Tipologia,
            categoria = "areeverdi",
            lat = a.Geom != null ? a.Geom.Coordinate.Y : 0,
            lon = a.Geom != null ? a.Geom.Coordinate.X : 0
        }).ToListAsync());
});

// Restituisce tutte le stazioni ferroviarie.
app.MapGet("/api/stazioni", async (UrbanAdvisorDbContext db) =>
{
    return Results.Ok(await db.StazioniFerroviarie
        .Select(s => new {
            id = s.Codice, nome = s.Denominazione,
            categoria = "stazioni",
            lat = s.Geom != null ? s.Geom.Coordinate.Y : 0,
            lon = s.Geom != null ? s.Geom.Coordinate.X : 0
        }).ToListAsync());
});

// Restituisce tutte le mense e i punti ristoro universitari.
app.MapGet("/api/mense", async (UrbanAdvisorDbContext db) =>
{
    return Results.Ok(await db.Mense
        .Select(m => new {
            id = m.Id, nome = m.Nome,
            tipo = m.Tipo == "mensa" ? "Mensa universitaria" : "Punto Ristoro",
            indirizzo = m.Indirizzo, gestore = m.Gestore,
            categoria = "mense",
            lat = m.Geom != null ? m.Geom.Coordinate.Y : 0,
            lon = m.Geom != null ? m.Geom.Coordinate.X : 0
        }).ToListAsync());
});

// Restituisce tutte le sedi universitarie (dipartimenti, uffici, musei) - fonte: dati.unibo.it "Punti di interesse".
app.MapGet("/api/sedi", async (UrbanAdvisorDbContext db) =>
{
    return Results.Ok(await db.SediUniversitarie
        .Select(s => new {
            id = s.Id, nome = s.Nome,
            tipo = s.Tipo == "museo" ? "Museo" : "Dipartimento/Ufficio",
            indirizzo = s.Indirizzo, url = s.Url,
            categoria = "sedi",
            lat = s.Geom != null ? s.Geom.Coordinate.Y : 0,
            lon = s.Geom != null ? s.Geom.Coordinate.X : 0
        }).ToListAsync());
});

// Restituisce le piste ciclabili con la geometria (lista di linee [lat, lon]) per il disegno su mappa.
app.MapGet("/api/piste", async (UrbanAdvisorDbContext db) =>
{
    var piste = await db.PisteCiclabili.Where(p => p.Geom != null).ToListAsync();
    return Results.Ok(piste.Select(p => new
    {
        id = p.Id, codice = p.Codice,
        lunghezza = p.Lunghezza, utilizzo = p.Utilizzo,
        categoria = "piste",
        linee = p.Geom!.Geometries
            .Select(g => g.Coordinates.Select(c => new[] { Math.Round(c.Y, 6), Math.Round(c.X, 6) }).ToArray())
            .ToArray()
    }));
});

// Route assistant: tempo di percorrenza multimodale verso la sede universitaria più vicina.
app.MapGet("/api/mobility/tempo-percorrenza", async (double lat, double lon, int? ora, UrbanAdvisorDbContext db) =>
{
    int oraVal = ora ?? 14;
    var risultato = await CalcolaTempoMultimodale(lat, lon, oraVal, db);
    return Results.Ok(risultato);
});

// Isocrone su griglia + top-5 aree più raggiungibili, per una modalità e fascia oraria date.
app.MapGet("/api/mobility/isocrona", async (int? ora, string? modalita, UrbanAdvisorDbContext db) =>
{
    int oraVal = ora ?? 14;
    string modalitaVal = (modalita ?? "trasporto_pubblico").ToLower();
    if (modalitaVal != "piedi" && modalitaVal != "bici" && modalitaVal != "trasporto_pubblico")
        modalitaVal = "trasporto_pubblico";

    int n = 10;
    var celle = new List<(double lat, double lon, bool disponibile, double? tempo, string? motivo)>();
    foreach (var (cLat, cLon) in GeoUtils.CentriGriglia(n))
    {
        var (disp, tempo, motivo) = await CalcolaTempoModalita(cLat, cLon, oraVal, modalitaVal, db);
        celle.Add((cLat, cLon, disp, tempo, motivo));
    }

    var griglia = celle.Select(c => new
    {
        lat = c.lat, lon = c.lon, disponibile = c.disponibile,
        tempo_minuti = c.disponibile ? Math.Round(c.tempo!.Value, 1) : (double?)null,
        motivo = c.motivo
    });

    var areeRaggiungibili = celle
        .Where(c => c.disponibile)
        .OrderBy(c => c.tempo)
        .Take(5)
        .Select((c, idx) => new
        {
            posizione = idx + 1, lat = c.lat, lon = c.lon,
            tempo_minuti = Math.Round(c.tempo!.Value, 1)
        });

    return Results.Ok(new
    {
        ora = oraVal, modalita = modalitaVal, celle = n,
        griglia,
        aree_piu_raggiungibili = areeRaggiungibili
    });
});

// Restituisce i PoI entro un raggio (metri) da una posizione, ordinati per distanza reale.
app.MapGet("/api/nearby", async (double lat, double lon, int raggio, string? categoria, UrbanAdvisorDbContext db) =>
{
    var g = GeoUtils.Punto(lat, lon);
    var risultati = new List<PoiVicino>();

    if (categoria == null || categoria == "biblioteche")
        risultati.AddRange(await db.Biblioteche
            .Where(b => b.Geog != null && b.Geog.IsWithinDistance(g, raggio))
            .Select(b => new PoiVicino(b.Id.ToString(), b.Nome ?? "", "biblioteche", b.Indirizzo ?? "",
                b.Geom!.Coordinate.Y, b.Geom!.Coordinate.X, (int)b.Geog!.Distance(g)))
            .ToListAsync());

    if (categoria == null || categoria == "salestudio")
        risultati.AddRange(await db.SaleStudio
            .Where(s => s.Geog != null && s.Geog.IsWithinDistance(g, raggio))
            .Select(s => new PoiVicino(s.Id.ToString(), s.Nome ?? "", "salestudio", s.Indirizzo ?? "",
                s.Geom!.Y, s.Geom!.X, (int)s.Geog!.Distance(g)))
            .ToListAsync());

    if (categoria == null || categoria == "fermate")
        risultati.AddRange(await db.FermateBus
            .Where(f => f.Geog != null && f.Geog.IsWithinDistance(g, raggio))
            .Select(f => new PoiVicino(f.CodiceFermata, f.NomeFermata ?? "", "fermate", f.LineaBus ?? "",
                f.Geom!.Y, f.Geom!.X, (int)f.Geog!.Distance(g)))
            .ToListAsync());

    if (categoria == null || categoria == "areeverdi")
        risultati.AddRange(await db.AreeVerdi
            .Where(a => a.Geog != null && a.Geog.IsWithinDistance(g, raggio))
            .Select(a => new PoiVicino(a.Id.ToString(), a.NomeArea ?? "", "areeverdi", a.Tipologia ?? "",
                a.Geom!.Y, a.Geom!.X, (int)a.Geog!.Distance(g)))
            .ToListAsync());

    if (categoria == null || categoria == "residenze")
        risultati.AddRange(await db.ResidenzeUniversitarie
            .Where(r => r.Geog != null && r.Geog.IsWithinDistance(g, raggio))
            .Select(r => new PoiVicino(r.Id, r.Nome ?? "", "residenze", r.PostiLetto + " posti letto",
                r.Geom!.Y, r.Geom!.X, (int)r.Geog!.Distance(g)))
            .ToListAsync());

    if (categoria == null || categoria == "stazioni")
        risultati.AddRange(await db.StazioniFerroviarie
            .Where(s => s.Geog != null && s.Geog.IsWithinDistance(g, raggio))
            .Select(s => new PoiVicino(s.Codice, s.Denominazione ?? "", "stazioni", s.Denominazione ?? "",
                s.Geom!.Y, s.Geom!.X, (int)s.Geog!.Distance(g)))
            .ToListAsync());

    if (categoria == null || categoria == "mense")
        risultati.AddRange(await db.Mense
            .Where(m => m.Geog != null && m.Geog.IsWithinDistance(g, raggio))
            .Select(m => new PoiVicino(m.Id.ToString(), m.Nome ?? "", "mense",
                m.Tipo == "mensa" ? "Mensa universitaria" : "Punto Ristoro",
                m.Geom!.Y, m.Geom!.X, (int)m.Geog!.Distance(g)))
            .ToListAsync());

    if (categoria == null || categoria == "sedi")
        risultati.AddRange(await db.SediUniversitarie
            .Where(s => s.Geog != null && s.Geog.IsWithinDistance(g, raggio))
            .Select(s => new PoiVicino(s.Id.ToString(), s.Nome ?? "", "sedi",
                s.Tipo == "museo" ? "Museo" : "Dipartimento/Ufficio",
                s.Geom!.Y, s.Geom!.X, (int)s.Geog!.Distance(g)))
            .ToListAsync());

    var ordinati = risultati.OrderBy(r => r.DistanzaMetri).ToList();
    return Results.Ok(new { totale = ordinati.Count, raggio_metri = raggio, risultati = ordinati });
});

// Buffer analysis: conteggi, km di piste e densita' dei PoI in un cerchio di raggio dato (metri).
app.MapGet("/api/area/indicatori", async (double lat, double lon, int raggio, UrbanAdvisorDbContext db) =>
{
    var c = await ScoringService.ContaServiziAsync(db, lat, lon, raggio);
    double areaKm2 = Math.PI * Math.Pow(raggio / 1000.0, 2);

    return Results.Ok(new
    {
        raggio_metri = raggio,
        area_km2 = Math.Round(areaKm2, 3),
        totale_poi = c.TotalePoi,
        dettaglio = new
        {
            biblioteche = c.Biblioteche,
            sale_studio = c.SaleStudio,
            fermate_bus = c.Fermate,
            aree_verdi = c.AreeVerdi,
            residenze = c.Residenze,
            stazioni = c.Stazioni,
            mense = c.Mense,
            sedi = c.Sedi,
            piste_km = Math.Round(c.KmPiste, 2)
        },
        densita = new
        {
            servizi_per_km2 = Math.Round(c.TotalePoi / areaKm2, 1),
            km_piste_per_km2 = Math.Round(c.KmPiste / areaKm2, 2)
        },
        distanza_fermata_metri = (int)c.DistanzaFermataMetri
    });
});

// Restituisce la lista dei profili utente.
app.MapGet("/api/profili", async (UrbanAdvisorDbContext db) =>
{
    return Results.Ok(await db.ProfiliUtente.OrderBy(p => p.Id).ToListAsync());
});

// Restituisce un singolo profilo utente per id.
app.MapGet("/api/profili/{id}", async (int id, UrbanAdvisorDbContext db) =>
{
    var profilo = await db.ProfiliUtente.FindAsync(id);
    return profilo is not null ? Results.Ok(profilo) : Results.NotFound();
});

// Crea un nuovo profilo utente.
app.MapPost("/api/profili", async (ProfiloUtente profilo, UrbanAdvisorDbContext db) =>
{
    profilo.Id = 0;
    NormalizzaPesi(profilo);
    db.ProfiliUtente.Add(profilo);
    await db.SaveChangesAsync();
    return Results.Created($"/api/profili/{profilo.Id}", profilo);
});

// Aggiorna un profilo utente esistente (tutti i pesi, comprese mense e sedi).
app.MapPut("/api/profili/{id}", async (int id, ProfiloUtente input, UrbanAdvisorDbContext db) =>
{
    var profilo = await db.ProfiliUtente.FindAsync(id);
    if (profilo is null) return Results.NotFound();

    profilo.Nome = input.Nome;
    profilo.PesoTrasporti = input.PesoTrasporti;
    profilo.PesoBiblioteche = input.PesoBiblioteche;
    profilo.PesoAreeVerdi = input.PesoAreeVerdi;
    profilo.PesoMobilitaSostenibile = input.PesoMobilitaSostenibile;
    profilo.PesoResidenze = input.PesoResidenze;
    profilo.PesoMense = input.PesoMense;
    profilo.PesoSedi = input.PesoSedi;
    NormalizzaPesi(profilo);
    await db.SaveChangesAsync();
    return Results.Ok(profilo);
});

// Elimina un profilo utente.
app.MapDelete("/api/profili/{id}", async (int id, UrbanAdvisorDbContext db) =>
{
    var profilo = await db.ProfiliUtente.FindAsync(id);
    if (profilo is null) return Results.NotFound();
    db.ProfiliUtente.Remove(profilo);
    await db.SaveChangesAsync();
    return Results.NoContent();
});

// Calcola lo Student Accessibility Score context-aware (posizione, ora, profilo) e lo salva nello storico.
app.MapGet("/api/ranking", async (double lat, double lon, int ora, int? profiloId, UrbanAdvisorDbContext db) =>
{
    ProfiloUtente? profilo = profiloId.HasValue ? await db.ProfiliUtente.FindAsync(profiloId.Value) : null;
    var conteggi = await ScoringService.ContaServiziAsync(db, lat, lon);
    var r = ScoringService.CalcolaScore(conteggi, ora, PesiProfilo.Da(profilo));

    var storico = new SuggerimentoStorico
    {
        ProfiloId = profilo?.Id,
        Lat = lat, Lon = lon, Ora = ora,
        Punteggio = r.Punteggio,
        Fascia = r.Fascia,
        Motivazione = r.Dettaglio,
        CreatedAt = DateTime.UtcNow
    };
    db.SuggerimentiStorico.Add(storico);
    await db.SaveChangesAsync();

    return Results.Ok(new
    {
        punteggio = r.Punteggio,
        fascia = r.Fascia,
        profilo = profilo?.Nome ?? "Default (bilanciato)",
        dettaglio = r.Dettaglio,
        motivi = r.Motivi,
        subscores = r.Subscores.PerApi(),
        storico_id = storico.Id
    });
});

// Restituisce lo storico dei suggerimenti generati.
app.MapGet("/api/suggerimenti", async (int? profiloId, int? limit, UrbanAdvisorDbContext db) =>
{
    var query = db.SuggerimentiStorico.AsQueryable();
    if (profiloId.HasValue)
        query = query.Where(s => s.ProfiloId == profiloId.Value);

    var risultati = await query
        .OrderByDescending(s => s.CreatedAt)
        .Take(limit ?? 50)
        .ToListAsync();

    return Results.Ok(risultati);
});

// Salva il feedback dell'utente su un suggerimento.
app.MapPut("/api/suggerimenti/{id}/feedback", async (int id, FeedbackRequest req, UrbanAdvisorDbContext db) =>
{
    var sug = await db.SuggerimentiStorico.FindAsync(id);
    if (sug is null) return Results.NotFound();

    sug.Feedback = req.Feedback;
    await db.SaveChangesAsync();
    return Results.Ok(sug);
});

// Restituisce statistiche aggregate di utilizzo.
app.MapGet("/api/statistiche", async (UrbanAdvisorDbContext db) =>
{
    var totSuggerimenti = await db.SuggerimentiStorico.CountAsync();
    var mediaScore = totSuggerimenti > 0
        ? await db.SuggerimentiStorico.AverageAsync(s => s.Punteggio)
        : 0;
    var perFascia = await db.SuggerimentiStorico
        .GroupBy(s => s.Fascia)
        .Select(g => new { fascia = g.Key, conteggio = g.Count(), media = (int)g.Average(s => s.Punteggio) })
        .ToListAsync();
    var feedbackStats = await db.SuggerimentiStorico
        .Where(s => s.Feedback != null)
        .GroupBy(s => s.Feedback)
        .Select(g => new { tipo = g.Key, conteggio = g.Count() })
        .ToListAsync();

    return Results.Ok(new
    {
        totale_suggerimenti = totSuggerimenti,
        punteggio_medio = (int)mediaScore,
        per_fascia = perFascia,
        feedback = feedbackStats
    });
});

// Restituisce i punti PoI pesati per la heatmap (filtrabile per categoria).
app.MapGet("/api/heatmap", async (string? categoria, UrbanAdvisorDbContext db) =>
{
    var punti = new List<object>();

    if (categoria == null || categoria == "biblioteche")
        punti.AddRange(await db.Biblioteche.Where(b => b.Geom != null)
            .Select(b => new { lat = b.Geom!.Coordinate.Y, lon = b.Geom!.Coordinate.X, peso = 1.0, cat = "biblioteche" })
            .ToListAsync());
    if (categoria == null || categoria == "salestudio")
        punti.AddRange(await db.SaleStudio.Where(s => s.Geom != null)
            .Select(s => new { lat = s.Geom!.Y, lon = s.Geom!.X, peso = 1.0, cat = "salestudio" })
            .ToListAsync());
    if (categoria == null || categoria == "fermate")
        punti.AddRange(await db.FermateBus.Where(f => f.Geom != null)
            .Select(f => new { lat = f.Geom!.Y, lon = f.Geom!.X, peso = 0.3, cat = "fermate" })
            .ToListAsync());
    if (categoria == null || categoria == "areeverdi")
        punti.AddRange(await db.AreeVerdi.Where(a => a.Geom != null)
            .Select(a => new { lat = a.Geom!.Y, lon = a.Geom!.X, peso = 0.6, cat = "areeverdi" })
            .ToListAsync());
    if (categoria == null || categoria == "residenze")
        punti.AddRange(await db.ResidenzeUniversitarie.Where(r => r.Geom != null)
            .Select(r => new { lat = r.Geom!.Y, lon = r.Geom!.X, peso = 0.8, cat = "residenze" })
            .ToListAsync());
    if (categoria == null || categoria == "stazioni")
        punti.AddRange(await db.StazioniFerroviarie.Where(s => s.Geom != null)
            .Select(s => new { lat = s.Geom!.Y, lon = s.Geom!.X, peso = 0.7, cat = "stazioni" })
            .ToListAsync());
    if (categoria == null || categoria == "mense")
        punti.AddRange(await db.Mense.Where(m => m.Geom != null)
            .Select(m => new { lat = m.Geom!.Y, lon = m.Geom!.X, peso = 0.9, cat = "mense" })
            .ToListAsync());
    if (categoria == null || categoria == "sedi")
        punti.AddRange(await db.SediUniversitarie.Where(s => s.Geom != null)
            .Select(s => new { lat = s.Geom!.Y, lon = s.Geom!.X, peso = 0.5, cat = "sedi" })
            .ToListAsync());

    return Results.Ok(new { totale = punti.Count, punti });
});

// Density analysis: griglia NxN con conteggi e score per cella (una sola query spaziale).
app.MapGet("/api/density/grid", async (int? celle, int? profiloId, int? ora, UrbanAdvisorDbContext db) =>
{
    int n = Math.Clamp(celle ?? 8, 2, 20);
    int oraVal = ora ?? 14;
    ProfiloUtente? profilo = profiloId.HasValue ? await db.ProfiliUtente.FindAsync(profiloId.Value) : null;
    var pesi = PesiProfilo.Da(profilo);

    var conteggi = await ScoringService.ContaServiziAsync(db, GeoUtils.CentriGriglia(n));
    var griglia = conteggi.Select(c =>
    {
        var r = ScoringService.CalcolaScore(c, oraVal, pesi);
        return new
        {
            lat = c.Lat, lon = c.Lon, score = r.Punteggio,
            totale_poi = c.TotalePoi,
            dettaglio = DettaglioConteggi(c)
        };
    }).ToList();

    return Results.Ok(new { celle = n, ora = oraVal, fascia = ScoringService.IsDiurna(oraVal) ? "Diurna" : "Notturna",
        raggio_metri = ScoringService.RaggioMetri, profilo = profilo?.Nome ?? "Default", griglia });
});

// Recommendation engine: le top-N zone urbane con motivazione.
app.MapGet("/api/raccomandazioni", async (int? profiloId, int? ora, int? top, UrbanAdvisorDbContext db) =>
{
    int oraVal = ora ?? 14;
    int topN = Math.Clamp(top ?? 5, 1, 20);
    ProfiloUtente? profilo = profiloId.HasValue ? await db.ProfiliUtente.FindAsync(profiloId.Value) : null;
    var pesi = PesiProfilo.Da(profilo);

    var conteggi = await ScoringService.ContaServiziAsync(db, GeoUtils.CentriGriglia(10));
    var topZone = conteggi
        .Select(c => (c, r: ScoringService.CalcolaScore(c, oraVal, pesi)))
        .OrderByDescending(z => z.r.Punteggio)
        .Take(topN)
        .Select((z, idx) => new
        {
            posizione = idx + 1,
            lat = z.c.Lat, lon = z.c.Lon,
            student_accessibility_score = z.r.Punteggio,
            motivazione = string.Join(", ", z.r.Motivi),
            dettaglio = DettaglioConteggi(z.c)
        });

    return Results.Ok(new
    {
        profilo = profilo?.Nome ?? "Default",
        ora = oraVal, fascia = ScoringService.IsDiurna(oraVal) ? "Diurna" : "Notturna",
        raccomandazioni = topZone
    });
});

// Temporal analytics: servizi aperti/chiusi per giorno e ora (gestisce anche gli orari oltre la mezzanotte).
app.MapGet("/api/temporale/disponibilita", async (int? giorno, int? ora, UrbanAdvisorDbContext db) =>
{
    int giornoVal = giorno ?? (int)DateTime.Now.DayOfWeek;
    giornoVal = giornoVal == 0 ? 6 : giornoVal - 1;
    int oraVal = Math.Clamp(ora ?? DateTime.Now.Hour, 0, 23);
    var oraTime = new TimeOnly(oraVal, 0);

    string[] giorniNomi = { "Lunedì", "Martedì", "Mercoledì", "Giovedì", "Venerdì", "Sabato", "Domenica" };

    var tuttiOrari = await db.OrariServizi
        .Where(o => o.GiornoSettimana == giornoVal)
        .ToListAsync();

    var perCategoria = tuttiOrari
        .GroupBy(o => o.Categoria)
        .Select(g => new
        {
            categoria = g.Key,
            totale_servizi = g.Count(),
            aperti_ora = g.Count(o => Aperto(oraTime, o.OraApertura, o.OraChiusura)),
            chiusi_ora = g.Count(o => !Aperto(oraTime, o.OraApertura, o.OraChiusura)),
            orario_tipico = g.First().OraApertura.ToString("HH:mm") + " - " + g.First().OraChiusura.ToString("HH:mm")
        })
        .ToList();

    var distribuzioneOraria = Enumerable.Range(0, 24).Select(h =>
    {
        var t = new TimeOnly(h, 0);
        return new
        {
            ora = h,
            aperti = tuttiOrari.Count(o => Aperto(t, o.OraApertura, o.OraChiusura))
        };
    });

    return Results.Ok(new
    {
        giorno = giornoVal,
        giorno_nome = giorniNomi[giornoVal],
        ora = oraVal,
        servizi = perCategoria,
        distribuzione_oraria = distribuzioneOraria
    });
});

// Temporal analytics: distribuzione dei suggerimenti per ora.
app.MapGet("/api/temporale/statistiche", async (UrbanAdvisorDbContext db) =>
{
    var perOra = await db.SuggerimentiStorico
        .GroupBy(s => s.Ora)
        .Select(g => new { ora = g.Key, conteggio = g.Count(), media = (int)g.Average(s => s.Punteggio) })
        .OrderBy(x => x.ora)
        .ToListAsync();

    var perFascia = await db.SuggerimentiStorico
        .GroupBy(s => s.Fascia)
        .Select(g => new { fascia = g.Key, conteggio = g.Count(), media = (int)g.Average(s => s.Punteggio) })
        .ToListAsync();

    return Results.Ok(new { per_ora = perOra, per_fascia = perFascia });
});

// Privacy: confronto tra posizione reale e posizione perturbata.
app.MapGet("/api/privacy/confronto", async (double lat, double lon, int ora, double sigma, int? profiloId, UrbanAdvisorDbContext db) =>
{
    var rng = new Random();
    double sigmaLat = sigma / 111320.0;
    double sigmaLon = sigma / (111320.0 * Math.Cos(lat * Math.PI / 180));

    double noiseLat = rng.NextDouble() * 2 - 1 + rng.NextDouble() * 2 - 1;
    double noiseLon = rng.NextDouble() * 2 - 1 + rng.NextDouble() * 2 - 1;
    double pertLat = lat + noiseLat * sigmaLat;
    double pertLon = lon + noiseLon * sigmaLon;

    ProfiloUtente? profilo = profiloId.HasValue ? await db.ProfiliUtente.FindAsync(profiloId.Value) : null;
    var pesi = PesiProfilo.Da(profilo);
    var conteggi = await ScoringService.ContaServiziAsync(db, new List<(double, double)> { (lat, lon), (pertLat, pertLon) });
    var realScore = ScoringService.CalcolaScore(conteggi[0], ora, pesi);
    var pertScore = ScoringService.CalcolaScore(conteggi[1], ora, pesi);

    double distanzaPerturbazione = GeoUtils.HaversineMetri(lat, lon, pertLat, pertLon);
    int qualityLoss = Math.Abs(realScore.Punteggio - pertScore.Punteggio);

    return Results.Ok(new
    {
        sigma_metri = sigma,
        posizione_reale = new { lat, lon },
        posizione_perturbata = new { lat = pertLat, lon = pertLon },
        privacy_perturbation_metri = Math.Round(distanzaPerturbazione, 1),
        score_reale = realScore.Punteggio,
        score_perturbato = pertScore.Punteggio,
        quality_loss = qualityLoss,
        subscores_reale = realScore.Subscores.PerApi(),
        subscores_perturbato = pertScore.Subscores.PerApi()
    });
});

// Privacy: analisi del trade-off tra privacy e qualita' del servizio (tutti i campioni in una sola query).
app.MapGet("/api/privacy/tradeoff", async (double lat, double lon, int ora, int? profiloId, UrbanAdvisorDbContext db) =>
{
    var rng = new Random(42);
    var livelli = new[] { 0, 50, 100, 200, 500, 1000, 2000 };
    int campioni = 5;

    var punti = new List<(double Lat, double Lon)> { (lat, lon) };
    var livelloPunto = new List<int> { -1 };
    foreach (var sigma in livelli)
    {
        double sigmaLat = sigma / 111320.0;
        double sigmaLon = sigma / (111320.0 * Math.Cos(lat * Math.PI / 180));
        for (int i = 0; i < campioni; i++)
        {
            double n1 = rng.NextDouble() * 2 - 1 + rng.NextDouble() * 2 - 1;
            double n2 = rng.NextDouble() * 2 - 1 + rng.NextDouble() * 2 - 1;
            punti.Add((lat + n1 * sigmaLat, lon + n2 * sigmaLon));
            livelloPunto.Add(sigma);
        }
    }

    ProfiloUtente? profilo = profiloId.HasValue ? await db.ProfiliUtente.FindAsync(profiloId.Value) : null;
    var pesi = PesiProfilo.Da(profilo);
    var conteggi = await ScoringService.ContaServiziAsync(db, punti);
    int scoreReale = ScoringService.CalcolaScore(conteggi[0], ora, pesi).Punteggio;

    var risultati = livelli.Select(sigma =>
    {
        var indici = Enumerable.Range(1, punti.Count - 1).Where(i => livelloPunto[i] == sigma).ToList();
        var scores = indici.Select(i => ScoringService.CalcolaScore(conteggi[i], ora, pesi).Punteggio).ToList();
        var distanze = indici.Select(i => GeoUtils.HaversineMetri(lat, lon, punti[i].Lat, punti[i].Lon)).ToList();
        return new
        {
            sigma_metri = sigma,
            privacy_perturbation_media = Math.Round(distanze.Average(), 1),
            score_medio_perturbato = (int)scores.Average(),
            score_reale = scoreReale,
            quality_loss_medio = (int)Math.Round(scores.Select(s => Math.Abs(s - scoreReale)).Average())
        };
    }).ToList();

    return Results.Ok(new { lat, lon, ora, risultati });
});

// Analytics avanzata: clustering K-Means delle zone e indice di Moran.
app.MapGet("/api/clustering", async (int? k, int? profiloId, int? ora, UrbanAdvisorDbContext db) =>
{
    int numClusters = Math.Clamp(k ?? 4, 2, 8);
    int oraVal = ora ?? 14;
    ProfiloUtente? profilo = profiloId.HasValue ? await db.ProfiliUtente.FindAsync(profiloId.Value) : null;
    var pesi = PesiProfilo.Da(profilo);

    var conteggi = await ScoringService.ContaServiziAsync(db, GeoUtils.CentriGriglia(10));
    var celle = conteggi.Select(c => (
        lat: c.Lat, lon: c.Lon,
        features: new double[] { c.Biblioteche + c.SaleStudio, c.Fermate, c.AreeVerdi, c.KmPiste, c.Residenze, c.Mense, c.Sedi },
        score: ScoringService.CalcolaScore(c, oraVal, pesi).Punteggio
    )).ToList();

    int dim = 7;

    var featMean = new double[dim];
    var featStd = new double[dim];
    for (int d = 0; d < dim; d++)
    {
        featMean[d] = celle.Average(c => c.features[d]);
        double variance = celle.Average(c => Math.Pow(c.features[d] - featMean[d], 2));
        featStd[d] = Math.Sqrt(variance);
        if (featStd[d] == 0) featStd[d] = 1;
    }
    var standardized = celle.Select(c => c.features.Select((v, d) => (v - featMean[d]) / featStd[d]).ToArray()).ToList();

    var centroids = celle
        .Select((c, idx) => (c, idx))
        .OrderByDescending(x => x.c.score)
        .Take(numClusters)
        .Select(x => (double[])standardized[x.idx].Clone())
        .ToList();
    var assignments = new int[celle.Count];

    for (int iter = 0; iter < 20; iter++)
    {
        for (int ci = 0; ci < celle.Count; ci++)
        {
            double minDist = double.MaxValue;
            for (int ki = 0; ki < numClusters; ki++)
            {
                double dist = 0;
                for (int d = 0; d < dim; d++) dist += Math.Pow(standardized[ci][d] - centroids[ki][d], 2);
                if (dist < minDist) { minDist = dist; assignments[ci] = ki; }
            }
        }

        for (int ki = 0; ki < numClusters; ki++)
        {
            var members = Enumerable.Range(0, celle.Count).Where(ci => assignments[ci] == ki).ToList();
            if (members.Count == 0) continue;
            for (int d = 0; d < dim; d++)
                centroids[ki][d] = members.Average(ci => standardized[ci][d]);
        }
    }

    string[] clusterLabels = { "Alta accessibilità", "Media accessibilità", "Bassa accessibilità", "Periferico" };
    var clusterGroups = Enumerable.Range(0, celle.Count)
        .GroupBy(ci => assignments[ci])
        .OrderByDescending(g => g.Average(ci => celle[ci].score))
        .Select((g, idx) => new
        {
            cluster_id = idx,
            label = idx < clusterLabels.Length ? clusterLabels[idx] : $"Cluster {idx}",
            score_medio = (int)g.Average(ci => celle[ci].score),
            num_celle = g.Count(),
            celle = g.Select(ci => new { lat = celle[ci].lat, lon = celle[ci].lon, score = celle[ci].score, cluster = idx }).ToList()
        });

    int N = celle.Count;
    double mean = celle.Average(c => c.score);
    double denominator = celle.Sum(c => Math.Pow(c.score - mean, 2));
    double W = 0, numerator = 0;

    for (int i2 = 0; i2 < N; i2++)
    {
        for (int j2 = 0; j2 < N; j2++)
        {
            if (i2 == j2) continue;
            double dist = GeoUtils.HaversineMetri(celle[i2].lat, celle[i2].lon, celle[j2].lat, celle[j2].lon);
            double wij = dist > 0 ? 1.0 / dist : 0;
            W += wij;
            numerator += wij * (celle[i2].score - mean) * (celle[j2].score - mean);
        }
    }
    double moranI = denominator > 0 ? (N / W) * (numerator / denominator) : 0;

    string moranInterpretazione = moranI > 0.3 ? "Forte clustering spaziale: zone simili tendono a essere vicine"
        : moranI > 0.1 ? "Clustering moderato: alcune aree simili sono raggruppate"
        : moranI > -0.1 ? "Distribuzione quasi casuale"
        : "Dispersione: zone diverse tendono a essere vicine";

    return Results.Ok(new
    {
        k = numClusters, ora = oraVal,
        clusters = clusterGroups,
        moran = new
        {
            indicatore = "Student Accessibility Score",
            moran_i = Math.Round(moranI, 4),
            interpretazione = moranInterpretazione,
            nota = "Moran's I: +1 = perfetto clustering, 0 = random, -1 = perfetta dispersione"
        }
    });
});

app.Run();

// Dettaglio dei conteggi di una cella nel formato JSON usato dal front-end.
static object DettaglioConteggi(ConteggiArea c) => new
{
    biblioteche = c.Biblioteche, sale_studio = c.SaleStudio, fermate = c.Fermate,
    aree_verdi = c.AreeVerdi, piste_km = Math.Round(c.KmPiste, 2), residenze = c.Residenze,
    mense = c.Mense, sedi = c.Sedi
};

// Riporta i pesi del profilo nell'intervallo 0-100 ammesso dal database.
static void NormalizzaPesi(ProfiloUtente p)
{
    p.Nome = string.IsNullOrWhiteSpace(p.Nome) ? "Nuovo Profilo" : p.Nome.Trim();
    p.PesoTrasporti = Math.Clamp(p.PesoTrasporti, 0, 100);
    p.PesoBiblioteche = Math.Clamp(p.PesoBiblioteche, 0, 100);
    p.PesoAreeVerdi = Math.Clamp(p.PesoAreeVerdi, 0, 100);
    p.PesoMobilitaSostenibile = Math.Clamp(p.PesoMobilitaSostenibile, 0, 100);
    p.PesoResidenze = Math.Clamp(p.PesoResidenze, 0, 100);
    p.PesoMense = Math.Clamp(p.PesoMense, 0, 100);
    p.PesoSedi = Math.Clamp(p.PesoSedi, 0, 100);
}

// Verifica se un servizio e' aperto all'ora t; se la chiusura precede l'apertura l'orario prosegue oltre la mezzanotte.
static bool Aperto(TimeOnly t, TimeOnly apertura, TimeOnly chiusura) =>
    apertura <= chiusura ? t >= apertura && t < chiusura : t >= apertura || t < chiusura;

// Calcola i tempi di percorrenza multimodali (piedi, bici, TPL) dalla posizione data verso la sede
// universitaria (esclusi i musei) più vicina. TPL usa la frequenza reale GTFS per stimare l'attesa.
static async Task<object> CalcolaTempoMultimodale(double lat, double lon, int ora, UrbanAdvisorDbContext db)
{
    var origine = GeoUtils.Punto(lat, lon);

    var sede = await db.SediUniversitarie
        .Where(s => s.Geog != null && s.Tipo != "museo")
        .OrderBy(s => s.Geog!.Distance(origine))
        .FirstOrDefaultAsync();

    if (sede == null || sede.Geom == null)
        return new { disponibile = false, motivo = "Nessuna sede universitaria trovata nel dataset" };

    double distanzaDirettaMetri = GeoUtils.HaversineMetri(origine, sede.Geom);
    const double fattoreDeviazione = 1.3;
    double distanzaStradaleStimata = distanzaDirettaMetri * fattoreDeviazione;

    double velocitaPiedi = 5000.0 / 60.0;
    double tempoPiedi = distanzaStradaleStimata / velocitaPiedi;

    double velocitaBici = 15000.0 / 60.0;
    double tempoBici = distanzaStradaleStimata / velocitaBici;

    var fermataPartenza = await db.GtfsFermate
        .Where(f => f.Geog != null)
        .OrderBy(f => f.Geog!.Distance(origine))
        .FirstOrDefaultAsync();
    var fermataArrivo = await db.GtfsFermate
        .Where(f => f.Geog != null)
        .OrderBy(f => f.Geog!.Distance(sede.Geom))
        .FirstOrDefaultAsync();

    object trasportoPubblico;
    if (fermataPartenza?.Geom == null || fermataArrivo?.Geom == null)
    {
        trasportoPubblico = new { disponibile = false, motivo = "Nessuna fermata GTFS trovata" };
    }
    else
    {
        double distPartenzaFermata = GeoUtils.HaversineMetri(origine, fermataPartenza.Geom);
        double distArrivoFermata = GeoUtils.HaversineMetri(sede.Geom, fermataArrivo.Geom);
        double tempoPiediFermataPartenza = (distPartenzaFermata * fattoreDeviazione) / velocitaPiedi;
        double tempoPiediFermataArrivo = (distArrivoFermata * fattoreDeviazione) / velocitaPiedi;

        var freq = await db.GtfsFrequenzeFermata
            .FirstOrDefaultAsync(g => g.StopId == fermataPartenza.StopId && g.FasciaOraria == ora);
        int numeroCorse = freq?.NumeroCorse ?? 0;

        if (numeroCorse == 0)
        {
            trasportoPubblico = new
            {
                disponibile = false,
                motivo = $"Nessuna corsa rilevata alla fermata '{fermataPartenza.Nome}' nella fascia {ora}:00-{ora + 1}:00"
            };
        }
        else
        {
            double headwayMinuti = 60.0 / numeroCorse;
            double attesaMedia = headwayMinuti / 2.0;
            double velocitaBus = 18000.0 / 60.0;
            double distanzaTraFermate = GeoUtils.HaversineMetri(fermataPartenza.Geom, fermataArrivo.Geom);
            double tempoABordo = (distanzaTraFermate * fattoreDeviazione) / velocitaBus;
            double tempoTotale = tempoPiediFermataPartenza + attesaMedia + tempoABordo + tempoPiediFermataArrivo;

            trasportoPubblico = new
            {
                disponibile = true,
                tempo_totale_minuti = Math.Round(tempoTotale, 1),
                dettaglio = new
                {
                    a_piedi_fino_fermata_min = Math.Round(tempoPiediFermataPartenza, 1),
                    attesa_media_min = Math.Round(attesaMedia, 1),
                    a_bordo_min = Math.Round(tempoABordo, 1),
                    a_piedi_da_fermata_min = Math.Round(tempoPiediFermataArrivo, 1),
                    fermata_partenza = fermataPartenza.Nome,
                    fermata_arrivo = fermataArrivo.Nome,
                    corse_ora = numeroCorse,
                    headway_minuti = Math.Round(headwayMinuti, 1)
                }
            };
        }
    }

    return new
    {
        disponibile = true,
        sede_destinazione = sede.Nome,
        distanza_diretta_metri = Math.Round(distanzaDirettaMetri, 0),
        piedi = new { tempo_minuti = Math.Round(tempoPiedi, 1) },
        bici = new { tempo_minuti = Math.Round(tempoBici, 1) },
        trasporto_pubblico = trasportoPubblico
    };
}

// Versione snella per il calcolo su griglia: una sola modalità alla volta (piedi, bici o TPL).
static async Task<(bool disponibile, double? tempoMinuti, string? motivo)> CalcolaTempoModalita(
    double lat, double lon, int ora, string modalita, UrbanAdvisorDbContext db)
{
    var origine = GeoUtils.Punto(lat, lon);
    var sede = await db.SediUniversitarie.Where(s => s.Geog != null && s.Tipo != "museo")
        .OrderBy(s => s.Geog!.Distance(origine)).FirstOrDefaultAsync();
    if (sede == null || sede.Geom == null) return (false, null, "Nessuna sede trovata");

    const double fattoreDeviazione = 1.3;
    double distanzaStradale = GeoUtils.HaversineMetri(origine, sede.Geom) * fattoreDeviazione;

    if (modalita == "piedi")
        return (true, distanzaStradale / (5000.0 / 60.0), null);

    if (modalita == "bici")
        return (true, distanzaStradale / (15000.0 / 60.0), null);

    var fermataPartenza = await db.GtfsFermate.Where(f => f.Geog != null)
        .OrderBy(f => f.Geog!.Distance(origine)).FirstOrDefaultAsync();
    var fermataArrivo = await db.GtfsFermate.Where(f => f.Geog != null)
        .OrderBy(f => f.Geog!.Distance(sede.Geom)).FirstOrDefaultAsync();
    if (fermataPartenza?.Geom == null || fermataArrivo?.Geom == null)
        return (false, null, "Nessuna fermata GTFS trovata");

    var freq = await db.GtfsFrequenzeFermata
        .FirstOrDefaultAsync(g => g.StopId == fermataPartenza.StopId && g.FasciaOraria == ora);
    int numeroCorse = freq?.NumeroCorse ?? 0;
    if (numeroCorse == 0)
        return (false, null, $"Nessuna corsa in fascia {ora}:00");

    double velocitaPiedi = 5000.0 / 60.0;
    double velocitaBus = 18000.0 / 60.0;
    double tPiediPartenza = GeoUtils.HaversineMetri(origine, fermataPartenza.Geom) * fattoreDeviazione / velocitaPiedi;
    double tPiediArrivo = GeoUtils.HaversineMetri(sede.Geom, fermataArrivo.Geom) * fattoreDeviazione / velocitaPiedi;
    double attesa = (60.0 / numeroCorse) / 2.0;
    double aBordo = GeoUtils.HaversineMetri(fermataPartenza.Geom, fermataArrivo.Geom) * fattoreDeviazione / velocitaBus;

    return (true, tPiediPartenza + attesa + aBordo + tPiediArrivo, null);
}

// Record per deserializzare il corpo della richiesta di feedback.
record FeedbackRequest(string Feedback);

// PoI restituito dalla ricerca dei servizi vicini.
record PoiVicino(string Id, string Nome, string Categoria, string Dettaglio, double Lat, double Lon, int DistanzaMetri);
