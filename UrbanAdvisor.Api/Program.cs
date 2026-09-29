// ============================================================================
// Program.cs - Student Urban Accessibility Advisor
// Configurazione dell'applicazione .NET (Minimal API) e definizione di tutti
// gli endpoint REST: lettura PoI, ricerca spaziale, scoring context-aware,
// profilazione, analisi spaziale/temporale, mobilita', privacy e clustering.
// Distanze e buffer sono calcolati in metri sulla colonna geography 'geog'.
// Logica applicativa in Services/: ScoringService (score context-aware),
// MobilityService (tempi multimodali e GTFS), PrivacyService (metriche di
// privacy), SpatialStats (Moran), GeoUtils (Haversine e griglia).
// Il giorno della settimana segue la convenzione 0 = lunedi' ... 6 = domenica.
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

// Route assistant: tempi a piedi, in bici e con TPL (GTFS) verso una sede universitaria.
// Destinazione: la sede (esclusi i musei) più vicina a destLat/destLon se indicati, altrimenti all'origine.
app.MapGet("/api/mobility/tempo-percorrenza", async (double lat, double lon, int? ora, int? giorno,
    double? destLat, double? destLon, UrbanAdvisorDbContext db) =>
{
    int oraVal = Math.Clamp(ora ?? 14, 0, 23);
    int giornoVal = Math.Clamp(giorno ?? ScoringService.GiornoOggi(), 0, 6);
    var sede = await MobilityService.SedePiuVicinaAsync(db, destLat ?? lat, destLon ?? lon);
    if (sede?.Geom == null)
        return Results.Ok(new { disponibile = false, motivo = "Nessuna sede universitaria trovata nel dataset" });

    var percorso = await MobilityService.DettaglioAsync(db, lat, lon, sede.Geom.Y, sede.Geom.X, oraVal, giornoVal);
    return Results.Ok(new
    {
        disponibile = true,
        ora = oraVal, giorno = giornoVal, tipo_giorno = ScoringService.TipoGiorno(giornoVal),
        sede_destinazione = sede.Nome,
        destinazione = new { lat = sede.Geom.Y, lon = sede.Geom.X },
        percorso
    });
});

// Isocrone su griglia verso una sede scelta: tempo per cella, fascia (0-10, 10-20, 20-30, 30-45, >45 min)
// e top-5 aree più raggiungibili, per modalità, fascia oraria e giorno.
app.MapGet("/api/mobility/isocrona", async (double destLat, double destLon, int? ora, int? giorno, string? modalita,
    int? celle, UrbanAdvisorDbContext db) =>
{
    int oraVal = Math.Clamp(ora ?? 9, 0, 23);
    int giornoVal = Math.Clamp(giorno ?? ScoringService.GiornoOggi(), 0, 6);
    int n = Math.Clamp(celle ?? 12, 4, 16);
    string modalitaVal = (modalita ?? "trasporto_pubblico").ToLower();
    if (modalitaVal != "piedi" && modalitaVal != "bici" && modalitaVal != "trasporto_pubblico")
        modalitaVal = "trasporto_pubblico";

    var sede = await MobilityService.SedePiuVicinaAsync(db, destLat, destLon);
    if (sede?.Geom == null)
        return Results.Ok(new { disponibile = false, motivo = "Nessuna sede universitaria trovata nel dataset" });
    double dLat = sede.Geom.Y, dLon = sede.Geom.X;

    var risultati = new List<(double lat, double lon, double minuti, string mezzo)>();
    foreach (var (cLat, cLon) in GeoUtils.CentriGriglia(n))
    {
        var (minuti, mezzo) = await MobilityService.TempoAsync(db, cLat, cLon, dLat, dLon, oraVal, giornoVal, modalitaVal);
        risultati.Add((cLat, cLon, minuti, mezzo));
    }

    var griglia = risultati.Select(c => new
    {
        lat = c.lat, lon = c.lon,
        tempo_minuti = Math.Round(c.minuti, 1),
        fascia = FasciaIsocrona(c.minuti),
        mezzo = c.mezzo
    });

    var areeRaggiungibili = risultati
        .OrderBy(c => c.minuti)
        .Take(5)
        .Select((c, idx) => new { posizione = idx + 1, lat = c.lat, lon = c.lon, tempo_minuti = Math.Round(c.minuti, 1), mezzo = c.mezzo });

    return Results.Ok(new
    {
        disponibile = true,
        ora = oraVal, giorno = giornoVal, tipo_giorno = ScoringService.TipoGiorno(giornoVal),
        modalita = modalitaVal, celle = n,
        destinazione = new { nome = sede.Nome, lat = dLat, lon = dLon },
        fasce = new[] { "0-10", "10-20", "20-30", "30-45", ">45" },
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
app.MapGet("/api/area/indicatori", async (double lat, double lon, int raggio, int? ora, int? giorno, UrbanAdvisorDbContext db) =>
{
    int oraVal = Math.Clamp(ora ?? 14, 0, 23);
    int giornoVal = Math.Clamp(giorno ?? ScoringService.GiornoOggi(), 0, 6);
    var c = await ScoringService.ContaServiziAsync(db, lat, lon, oraVal, giornoVal, raggio);
    double areaKm2 = Math.PI * Math.Pow(raggio / 1000.0, 2);

    return Results.Ok(new
    {
        raggio_metri = raggio,
        area_km2 = Math.Round(areaKm2, 3),
        totale_poi = c.TotalePoi,
        dettaglio = new
        {
            biblioteche = c.Biblioteche,
            biblioteche_aperte = c.BibliotecheAperte,
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
        distanza_fermata_metri = (int)c.DistanzaFermataMetri,
        corse_ora = c.CorseOra
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

// Calcola lo Student Accessibility Score context-aware (posizione, ora, giorno, profilo) e lo salva nello storico.
app.MapGet("/api/ranking", async (double lat, double lon, int ora, int? giorno, int? profiloId, UrbanAdvisorDbContext db) =>
{
    int oraVal = Math.Clamp(ora, 0, 23);
    int giornoVal = Math.Clamp(giorno ?? ScoringService.GiornoOggi(), 0, 6);
    ProfiloUtente? profilo = profiloId.HasValue ? await db.ProfiliUtente.FindAsync(profiloId.Value) : null;
    var conteggi = await ScoringService.ContaServiziAsync(db, lat, lon, oraVal, giornoVal);
    var r = ScoringService.CalcolaScore(conteggi, oraVal, giornoVal, PesiProfilo.Da(profilo));

    var storico = new SuggerimentoStorico
    {
        ProfiloId = profilo?.Id,
        Lat = lat, Lon = lon, Ora = oraVal, Giorno = giornoVal,
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
        giorno = giornoVal,
        tipo_giorno = r.TipoGiorno,
        profilo = profilo?.Nome ?? "Default (bilanciato)",
        dettaglio = r.Dettaglio,
        motivi = r.Motivi,
        subscores = r.Subscores.PerApi(),
        contesto = new { corse_ora = conteggi.CorseOra, distanza_fermata_metri = (int)conteggi.DistanzaFermataMetri,
                         biblioteche_aperte = conteggi.BibliotecheAperte, biblioteche = conteggi.Biblioteche },
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
app.MapGet("/api/density/grid", async (int? celle, int? profiloId, int? ora, int? giorno, UrbanAdvisorDbContext db) =>
{
    int n = Math.Clamp(celle ?? 8, 2, 20);
    int oraVal = Math.Clamp(ora ?? 14, 0, 23);
    int giornoVal = Math.Clamp(giorno ?? ScoringService.GiornoOggi(), 0, 6);
    ProfiloUtente? profilo = profiloId.HasValue ? await db.ProfiliUtente.FindAsync(profiloId.Value) : null;
    var pesi = PesiProfilo.Da(profilo);

    var conteggi = await ScoringService.ContaServiziAsync(db, GeoUtils.CentriGriglia(n), oraVal, giornoVal);
    var griglia = conteggi.Select(c =>
    {
        var r = ScoringService.CalcolaScore(c, oraVal, giornoVal, pesi);
        return new
        {
            lat = c.Lat, lon = c.Lon, score = r.Punteggio,
            totale_poi = c.TotalePoi,
            dettaglio = DettaglioConteggi(c)
        };
    }).ToList();

    return Results.Ok(new { celle = n, ora = oraVal, giorno = giornoVal,
        fascia = ScoringService.IsDiurna(oraVal) ? "Diurna" : "Notturna",
        raggio_metri = ScoringService.RaggioMetri, profilo = profilo?.Nome ?? "Default", griglia });
});

// Recommendation engine: le top-N zone urbane con motivazione.
app.MapGet("/api/raccomandazioni", async (int? profiloId, int? ora, int? giorno, int? top, UrbanAdvisorDbContext db) =>
{
    int oraVal = Math.Clamp(ora ?? 14, 0, 23);
    int giornoVal = Math.Clamp(giorno ?? ScoringService.GiornoOggi(), 0, 6);
    int topN = Math.Clamp(top ?? 5, 1, 20);
    ProfiloUtente? profilo = profiloId.HasValue ? await db.ProfiliUtente.FindAsync(profiloId.Value) : null;
    var pesi = PesiProfilo.Da(profilo);

    var conteggi = await ScoringService.ContaServiziAsync(db, GeoUtils.CentriGriglia(10), oraVal, giornoVal);
    var topZone = conteggi
        .Select(c => (c, r: ScoringService.CalcolaScore(c, oraVal, giornoVal, pesi)))
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
        ora = oraVal, giorno = giornoVal, giorno_nome = ScoringService.NomiGiorni[giornoVal],
        fascia = ScoringService.IsDiurna(oraVal) ? "Diurna" : "Notturna",
        raccomandazioni = topZone
    });
});

// Temporal analytics: servizi aperti/chiusi per giorno (0 = lunedì ... 6 = domenica) e ora, orari oltre la mezzanotte inclusi.
app.MapGet("/api/temporale/disponibilita", async (int? giorno, int? ora, UrbanAdvisorDbContext db) =>
{
    int giornoVal = Math.Clamp(giorno ?? ScoringService.GiornoOggi(), 0, 6);
    int oraVal = Math.Clamp(ora ?? DateTime.Now.Hour, 0, 23);
    var oraTime = new TimeOnly(oraVal, 0);

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

    string tipo = ScoringService.TipoGiorno(giornoVal);
    var corsePerOra = await db.GtfsFrequenzeFermata
        .Where(f => f.TipoGiorno == tipo)
        .GroupBy(f => f.FasciaOraria)
        .Select(g => new { ora = g.Key, corse = g.Sum(f => f.NumeroCorse) })
        .OrderBy(x => x.ora)
        .ToListAsync();

    return Results.Ok(new
    {
        giorno = giornoVal,
        giorno_nome = ScoringService.NomiGiorni[giornoVal],
        tipo_giorno = tipo,
        ora = oraVal,
        servizi = perCategoria,
        distribuzione_oraria = distribuzioneOraria,
        passaggi_bus_per_ora = corsePerOra
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

// Privacy: valutazione di campioni perturbati nel browser rispetto alla posizione reale.
// Per ogni campione: Privacy Perturbation (m), score, perdita di score e recall dei servizi vicini.
app.MapPost("/api/privacy/valutazione", async (ValutazionePrivacyRequest req, UrbanAdvisorDbContext db) =>
{
    if (req.Campioni == null || req.Campioni.Count == 0 || req.Campioni.Count > 500)
        return Results.BadRequest(new { errore = "Servono da 1 a 500 campioni" });

    int oraVal = Math.Clamp(req.Ora, 0, 23);
    int giornoVal = Math.Clamp(req.Giorno ?? ScoringService.GiornoOggi(), 0, 6);
    ProfiloUtente? profilo = req.ProfiloId.HasValue ? await db.ProfiliUtente.FindAsync(req.ProfiloId.Value) : null;
    var pesi = PesiProfilo.Da(profilo);

    var punti = new List<(double Lat, double Lon)> { (req.Lat, req.Lon) };
    punti.AddRange(req.Campioni.Select(c => (c.Lat, c.Lon)));
    var conteggi = await ScoringService.ContaServiziAsync(db, punti, oraVal, giornoVal);
    var recall = await PrivacyService.RecallAsync(db, req.Lat, req.Lon, req.Campioni.Select(c => (c.Lat, c.Lon)).ToList());

    var reale = ScoringService.CalcolaScore(conteggi[0], oraVal, giornoVal, pesi);
    var risultati = req.Campioni.Select((c, i) =>
    {
        var r = ScoringService.CalcolaScore(conteggi[i + 1], oraVal, giornoVal, pesi);
        return new
        {
            livello = c.Livello,
            lat = c.Lat, lon = c.Lon,
            privacy_perturbation_metri = Math.Round(GeoUtils.HaversineMetri(req.Lat, req.Lon, c.Lat, c.Lon), 1),
            score = r.Punteggio,
            quality_loss = Math.Abs(r.Punteggio - reale.Punteggio),
            recall_servizi = Math.Round(recall[i].Recall, 3),
            subscores = r.Subscores.PerApi()
        };
    }).ToList();

    return Results.Ok(new
    {
        ora = oraVal, giorno = giornoVal,
        score_reale = reale.Punteggio,
        subscores_reale = reale.Subscores.PerApi(),
        servizi_vicini_reali = recall.Count > 0 ? recall[0].Reali : 0,
        risultati
    });
});

// Indice di Moran (pesi queen, test a permutazioni) di un indicatore sulla griglia di analisi:
// densità per cella di una categoria di PoI (celle non sovrapposte) oppure Student Accessibility Score.
app.MapGet("/api/moran", async (string? indicatore, int? celle, int? profiloId, int? ora, int? giorno, UrbanAdvisorDbContext db) =>
{
    int n = Math.Clamp(celle ?? 10, 4, 20);
    string ind = (indicatore ?? "biblioteche").ToLower();
    double[] valori;
    string descrizione;

    if (ind == "score")
    {
        int oraVal = Math.Clamp(ora ?? 14, 0, 23);
        int giornoVal = Math.Clamp(giorno ?? ScoringService.GiornoOggi(), 0, 6);
        ProfiloUtente? profilo = profiloId.HasValue ? await db.ProfiliUtente.FindAsync(profiloId.Value) : null;
        var pesi = PesiProfilo.Da(profilo);
        var conteggi = await ScoringService.ContaServiziAsync(db, GeoUtils.CentriGriglia(n), oraVal, giornoVal);
        valori = conteggi.Select(c => (double)ScoringService.CalcolaScore(c, oraVal, giornoVal, pesi).Punteggio).ToArray();
        descrizione = "Student Accessibility Score al centro della cella (buffer di 500 m)";
    }
    else
    {
        if (!SpatialStats.Indicatori.TryGetValue(ind, out var categorie))
            return Results.BadRequest(new { errore = "Indicatore non valido", ammessi = SpatialStats.Indicatori.Keys.Append("score") });
        valori = await SpatialStats.ConteggiPerCellaAsync(db, categorie, n);
        descrizione = $"Numero di PoI ({string.Join(" + ", categorie)}) per cella, celle non sovrapposte";
    }

    var m = SpatialStats.MoranGriglia(valori, n);
    var centri = GeoUtils.CentriGriglia(n);
    return Results.Ok(new
    {
        indicatore = ind, descrizione, celle = n,
        moran_i = m.MoranI, atteso = m.Atteso, p_value = m.PValue, z_permutazioni = m.ZPermutazioni,
        permutazioni = m.Permutazioni, interpretazione = m.Interpretazione,
        pesi = "contiguità queen standardizzata per riga",
        valori = centri.Select((c, i) => new { lat = c.Lat, lon = c.Lon, valore = valori[i] })
    });
});

// Analytics avanzata: clustering K-Means delle zone (feature standardizzate) e indice di Moran dello score.
app.MapGet("/api/clustering", async (int? k, int? profiloId, int? ora, int? giorno, UrbanAdvisorDbContext db) =>
{
    int numClusters = Math.Clamp(k ?? 4, 2, 8);
    int oraVal = Math.Clamp(ora ?? 14, 0, 23);
    int giornoVal = Math.Clamp(giorno ?? ScoringService.GiornoOggi(), 0, 6);
    ProfiloUtente? profilo = profiloId.HasValue ? await db.ProfiliUtente.FindAsync(profiloId.Value) : null;
    var pesi = PesiProfilo.Da(profilo);

    var conteggi = await ScoringService.ContaServiziAsync(db, GeoUtils.CentriGriglia(10), oraVal, giornoVal);
    var celle = conteggi.Select(c => (
        lat: c.Lat, lon: c.Lon,
        features: new double[] { c.Biblioteche + c.SaleStudio, c.Fermate, c.AreeVerdi, c.KmPiste, c.Residenze, c.Mense, c.Sedi },
        score: ScoringService.CalcolaScore(c, oraVal, giornoVal, pesi).Punteggio
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

    var moran = SpatialStats.MoranGriglia(celle.Select(c => (double)c.score).ToArray(), 10);

    return Results.Ok(new
    {
        k = numClusters, ora = oraVal,
        clusters = clusterGroups,
        moran = new
        {
            indicatore = "Student Accessibility Score",
            moran_i = moran.MoranI,
            atteso = moran.Atteso,
            p_value = moran.PValue,
            interpretazione = moran.Interpretazione,
            nota = "Pesi queen standardizzati per riga; p-value con 999 permutazioni"
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

// Fascia di tempo di percorrenza usata per colorare le isocrone.
static string FasciaIsocrona(double minuti) =>
    minuti < 10 ? "0-10" : minuti < 20 ? "10-20" : minuti < 30 ? "20-30" : minuti < 45 ? "30-45" : ">45";

// Record per deserializzare il corpo della richiesta di feedback.
record FeedbackRequest(string Feedback);

// PoI restituito dalla ricerca dei servizi vicini.
record PoiVicino(string Id, string Nome, string Categoria, string Dettaglio, double Lat, double Lon, int DistanzaMetri);

// Campione di posizione perturbata generato nel browser (livello = parametro del meccanismo, in metri).
record CampionePrivacy(double Lat, double Lon, double Livello);

// Richiesta di valutazione privacy: posizione reale, contesto e campioni perturbati.
record ValutazionePrivacyRequest(double Lat, double Lon, int Ora, int? Giorno, int? ProfiloId, List<CampionePrivacy> Campioni);
