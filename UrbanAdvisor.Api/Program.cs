// ============================================================================
// Program.cs - Student Urban Accessibility Advisor
// Configurazione dell'applicazione .NET (Minimal API) e definizione di tutti
// gli endpoint REST: lettura PoI, ricerca spaziale, scoring context-aware,
// profilazione, analisi spaziale/temporale, privacy e clustering.
// Il front-end statico e' servito dalla cartella Frontend (WebRootPath).
// ============================================================================

using Microsoft.EntityFrameworkCore;
using UrbanAdvisor.Api.Data;
using UrbanAdvisor.Api.Models;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    WebRootPath = "Frontend"
});

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
app.UseDefaultFiles();
app.UseStaticFiles();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

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

// Restituisce tutte le piste ciclabili.
app.MapGet("/api/piste", async (UrbanAdvisorDbContext db) =>
{
    return Results.Ok(await db.PisteCiclabili
        .Select(p => new {
            id = p.Id, codice = p.Codice,
            lunghezza = p.Lunghezza, utilizzo = p.Utilizzo,
            categoria = "piste"
        }).ToListAsync());
});

// Restituisce i PoI entro un raggio da una posizione, ordinati per distanza.
app.MapGet("/api/nearby", async (double lat, double lon, int raggio, string? categoria, UrbanAdvisorDbContext db) =>
{
    double raggioGradi = raggio / 111000.0;
    var userPoint = new NetTopologySuite.Geometries.Point(lon, lat) { SRID = 4326 };
    var risultati = new List<object>();

    if (categoria == null || categoria == "biblioteche")
    {
        var items = await db.Biblioteche
            .Where(b => b.Geom != null && b.Geom.IsWithinDistance(userPoint, raggioGradi))
            .Select(b => new {
                id = b.Id.ToString(), nome = b.Nome, categoria = "biblioteche",
                dettaglio = b.Indirizzo ?? "",
                lat = b.Geom!.Coordinate.Y, lon = b.Geom!.Coordinate.X,
                distanzaMetri = (int)(b.Geom!.Distance(userPoint) * 111000)
            }).ToListAsync();
        risultati.AddRange(items);
    }

    if (categoria == null || categoria == "fermate")
    {
        var items = await db.FermateBus
            .Where(f => f.Geom != null && f.Geom.IsWithinDistance(userPoint, raggioGradi))
            .Select(f => new {
                id = f.CodiceFermata, nome = f.NomeFermata ?? "", categoria = "fermate",
                dettaglio = f.LineaBus ?? "",
                lat = f.Geom!.Coordinate.Y, lon = f.Geom!.Coordinate.X,
                distanzaMetri = (int)(f.Geom!.Distance(userPoint) * 111000)
            }).ToListAsync();
        risultati.AddRange(items);
    }

    if (categoria == null || categoria == "areeverdi")
    {
        var items = await db.AreeVerdi
            .Where(a => a.Geom != null && a.Geom.IsWithinDistance(userPoint, raggioGradi))
            .Select(a => new {
                id = a.Id.ToString(), nome = a.NomeArea ?? "", categoria = "areeverdi",
                dettaglio = a.Tipologia ?? "",
                lat = a.Geom!.Coordinate.Y, lon = a.Geom!.Coordinate.X,
                distanzaMetri = (int)(a.Geom!.Distance(userPoint) * 111000)
            }).ToListAsync();
        risultati.AddRange(items);
    }

    if (categoria == null || categoria == "residenze")
    {
        var items = await db.ResidenzeUniversitarie
            .Where(r => r.Geom != null && r.Geom.IsWithinDistance(userPoint, raggioGradi))
            .Select(r => new {
                id = r.Id, nome = r.Nome ?? "", categoria = "residenze",
                dettaglio = $"{r.PostiLetto} posti letto",
                lat = r.Geom!.Coordinate.Y, lon = r.Geom!.Coordinate.X,
                distanzaMetri = (int)(r.Geom!.Distance(userPoint) * 111000)
            }).ToListAsync();
        risultati.AddRange(items);
    }

    if (categoria == null || categoria == "stazioni")
    {
        var items = await db.StazioniFerroviarie
            .Where(s => s.Geom != null && s.Geom.IsWithinDistance(userPoint, raggioGradi))
            .Select(s => new {
                id = s.Codice, nome = s.Denominazione ?? "", categoria = "stazioni",
                dettaglio = s.Denominazione ?? "",
                lat = s.Geom!.Coordinate.Y, lon = s.Geom!.Coordinate.X,
                distanzaMetri = (int)(s.Geom!.Distance(userPoint) * 111000)
            }).ToListAsync();
        risultati.AddRange(items);
    }
        if (categoria == null || categoria == "mense")
    {
        var items = await db.Mense
            .Where(m => m.Geom != null && m.Geom.IsWithinDistance(userPoint, raggioGradi))
            .Select(m => new {
                id = m.Id.ToString(), nome = m.Nome ?? "", categoria = "mense",
                dettaglio = m.Tipo == "mensa" ? "Mensa universitaria" : "Punto Ristoro",
                lat = m.Geom!.Coordinate.Y, lon = m.Geom!.Coordinate.X,
                distanzaMetri = (int)(m.Geom!.Distance(userPoint) * 111000)
            }).ToListAsync();
        risultati.AddRange(items);
    }

    if (categoria == null || categoria == "sedi")
    {
        var items = await db.SediUniversitarie
            .Where(s => s.Geom != null && s.Geom.IsWithinDistance(userPoint, raggioGradi))
            .Select(s => new {
                id = s.Id.ToString(), nome = s.Nome ?? "", categoria = "sedi",
                dettaglio = s.Tipo == "museo" ? "Museo" : "Dipartimento/Ufficio",
                lat = s.Geom!.Coordinate.Y, lon = s.Geom!.Coordinate.X,
                distanzaMetri = (int)(s.Geom!.Distance(userPoint) * 111000)
            }).ToListAsync();
        risultati.AddRange(items);
    }

    var ordinati = risultati.Cast<dynamic>().OrderBy(r => (int)r.distanzaMetri).ToList();
    return Results.Ok(new { totale = ordinati.Count, raggio_metri = raggio, risultati = ordinati });
});

// Buffer analysis: conteggi e densita' dei PoI in un'area.
app.MapGet("/api/area/indicatori", async (double lat, double lon, int raggio, UrbanAdvisorDbContext db) =>
{
    double raggioGradi = raggio / 111000.0;
    var userPoint = new NetTopologySuite.Geometries.Point(lon, lat) { SRID = 4326 };

    var nBiblioteche = await db.Biblioteche.CountAsync(b => b.Geom != null && b.Geom.IsWithinDistance(userPoint, raggioGradi));
    var nFermate = await db.FermateBus.CountAsync(f => f.Geom != null && f.Geom.IsWithinDistance(userPoint, raggioGradi));
    var nAreeVerdi = await db.AreeVerdi.CountAsync(a => a.Geom != null && a.Geom.IsWithinDistance(userPoint, raggioGradi));
    var nResidenze = await db.ResidenzeUniversitarie.CountAsync(r => r.Geom != null && r.Geom.IsWithinDistance(userPoint, raggioGradi));
    var nStazioni = await db.StazioniFerroviarie.CountAsync(s => s.Geom != null && s.Geom.IsWithinDistance(userPoint, raggioGradi));
    var nPiste = await db.PisteCiclabili.CountAsync(p => p.Geom != null && p.Geom.IsWithinDistance(userPoint, raggioGradi));
    var nMense = await db.Mense.CountAsync(m => m.Geom != null && m.Geom.IsWithinDistance(userPoint, raggioGradi));
    var nSedi = await db.SediUniversitarie.CountAsync(s => s.Geom != null && s.Geom.IsWithinDistance(userPoint, raggioGradi));

    int totale = nBiblioteche + nFermate + nAreeVerdi + nResidenze + nStazioni + nPiste + nMense + nSedi;

    return Results.Ok(new
    {
        raggio_metri = raggio,
        totale_poi = totale,
        dettaglio = new
        {
            biblioteche = nBiblioteche,
            fermate_bus = nFermate,
            aree_verdi = nAreeVerdi,
            residenze = nResidenze,
            stazioni = nStazioni,
            piste_ciclabili = nPiste,
            mense = nMense,
            sedi = nSedi
        },
        densita = new
        {
            servizi_per_km2 = Math.Round(totale / (Math.PI * Math.Pow(raggio / 1000.0, 2)), 1)
        }
    });
});

// Restituisce la lista dei profili utente.
app.MapGet("/api/profili", async (UrbanAdvisorDbContext db) =>
{
    return Results.Ok(await db.ProfiliUtente.ToListAsync());
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
    db.ProfiliUtente.Add(profilo);
    await db.SaveChangesAsync();
    return Results.Created($"/api/profili/{profilo.Id}", profilo);
});

// Aggiorna un profilo utente esistente.
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
    var userPoint = new NetTopologySuite.Geometries.Point(lon, lat) { SRID = 4326 };
    double raggioGradi = 1000.0 / 111000.0;

    ProfiloUtente? profilo = null;
    if (profiloId.HasValue)
        profilo = await db.ProfiliUtente.FindAsync(profiloId.Value);

    int wTrasporti = profilo?.PesoTrasporti ?? 50;
    int wBiblioteche = profilo?.PesoBiblioteche ?? 50;
    int wAreeVerdi = profilo?.PesoAreeVerdi ?? 50;
    int wMobilita = profilo?.PesoMobilitaSostenibile ?? 50;
    int wResidenze = profilo?.PesoResidenze ?? 50;
    int wMense = profilo?.PesoMense ?? 50;
    int wSedi = profilo?.PesoSedi ?? 50;

    double somma = wTrasporti + wBiblioteche + wAreeVerdi + wMobilita + wResidenze + wMense + wSedi;
    if (somma == 0) somma = 1;
    double nT = wTrasporti / somma;
    double nB = wBiblioteche / somma;
    double nV = wAreeVerdi / somma;
    double nM = wMobilita / somma;
    double nR = wResidenze / somma;
    double nMe = wMense / somma;
    double nS = wSedi / somma;

    var fermataPiuVicina = await db.FermateBus
        .Where(f => f.Geom != null)
        .OrderBy(f => f.Geom!.Distance(userPoint))
        .FirstOrDefaultAsync();
    double distFermata = (fermataPiuVicina?.Geom?.Distance(userPoint) ?? 999) * 111000;
    int scoreTrasporti = distFermata <= 1000 ? (int)(100 - distFermata / 10) : 0;
    scoreTrasporti = Math.Clamp(scoreTrasporti, 0, 100);

    int nBib = await db.Biblioteche.CountAsync(b => b.Geom != null && b.Geom.IsWithinDistance(userPoint, raggioGradi));
    int scoreBiblioteche = Math.Min(nBib * 25, 100);

    int nVerde = await db.AreeVerdi.CountAsync(a => a.Geom != null && a.Geom.IsWithinDistance(userPoint, raggioGradi));
    int scoreVerdi = Math.Min(nVerde * 10, 100);

    int nPiste = await db.PisteCiclabili.CountAsync(p => p.Geom != null && p.Geom.IsWithinDistance(userPoint, raggioGradi));
    int scoreMobilita = Math.Min(nPiste * 15, 100);

    int nRes = await db.ResidenzeUniversitarie.CountAsync(r => r.Geom != null && r.Geom.IsWithinDistance(userPoint, raggioGradi));
    int scoreResidenze = Math.Min(nRes * 30, 100);

    int nMense = await db.Mense.CountAsync(m => m.Geom != null && m.Geom.IsWithinDistance(userPoint, raggioGradi));
    int scoreMense = Math.Min(nMense * 40, 100);

    int nSedi = await db.SediUniversitarie.CountAsync(s => s.Geom != null && s.Geom.IsWithinDistance(userPoint, raggioGradi));
    int scoreSedi = Math.Min(nSedi * 3, 100);

    bool isGiorno = ora >= 8 && ora < 20;
    if (!isGiorno)
    {
        scoreBiblioteche = (int)(scoreBiblioteche * 0.3);
        scoreTrasporti = (int)(scoreTrasporti * 1.3);
        scoreResidenze = (int)(scoreResidenze * 1.5);
        scoreMense = (int)(scoreMense * 0.3);
        scoreSedi = (int)(scoreSedi * 0.2);
    }

    scoreTrasporti = Math.Clamp(scoreTrasporti, 0, 100);
    scoreResidenze = Math.Clamp(scoreResidenze, 0, 100);

    double punteggioFinale = (scoreTrasporti * nT + scoreBiblioteche * nB +
                              scoreVerdi * nV + scoreMobilita * nM + scoreResidenze * nR +
                              scoreMense * nMe + scoreSedi * nS);
    int punteggio = Math.Clamp((int)Math.Round(punteggioFinale), 0, 100);

    string fascia = isGiorno ? "Diurna" : "Notturna";
    var motivazioni = new List<string>();

    var fattori = new List<(string nome, int score, double peso)>
    {
        ("Trasporti pubblici", scoreTrasporti, nT),
        ("Biblioteche/Sale studio", scoreBiblioteche, nB),
        ("Aree verdi", scoreVerdi, nV),
        ("Mobilità sostenibile", scoreMobilita, nM),
        ("Residenze universitarie", scoreResidenze, nR),
        ("Mense/Punti ristoro", scoreMense, nMe),
        ("Sedi universitarie", scoreSedi, nS)
    };
    fattori = fattori.OrderByDescending(f => f.score * f.peso).ToList();

    foreach (var f in fattori)
    {
        string livello = f.score >= 70 ? "alto" : f.score >= 40 ? "medio" : "basso";
        motivazioni.Add($"{f.nome}: {livello} ({f.score}/100, peso {(int)(f.peso * 100)}%)");
    }

    string motivazione = $"{(isGiorno ? "🌞" : "🌙")} Fascia {fascia}. " +
                          $"Fermata bus più vicina: {(int)distFermata}m. " +
                          $"Nel raggio di 1km: {nBib} biblioteche, {nVerde} aree verdi, {nPiste} piste ciclabili, {nRes} residenze, {nMense} mense/ristori, {nSedi} sedi universitarie. " +
                          string.Join(" | ", motivazioni);

    var storico = new SuggerimentoStorico
    {
        ProfiloId = profiloId,
        Lat = lat, Lon = lon, Ora = ora,
        Punteggio = punteggio,
        Fascia = fascia,
        Motivazione = motivazione,
        CreatedAt = DateTime.UtcNow
    };
    db.SuggerimentiStorico.Add(storico);
    await db.SaveChangesAsync();

    return Results.Ok(new
    {
        punteggio,
        fascia,
        profilo = profilo?.Nome ?? "Default (bilanciato)",
        dettaglio = motivazione,
        subscores = new { trasporti = scoreTrasporti, biblioteche = scoreBiblioteche,
                          aree_verdi = scoreVerdi, mobilita = scoreMobilita, residenze = scoreResidenze,
                          mense = scoreMense, sedi = scoreSedi },
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

// Restituisce i punti PoI pesati per la heatmap.
app.MapGet("/api/heatmap", async (string? categoria, UrbanAdvisorDbContext db) =>
{
    var punti = new List<object>();

    if (categoria == null || categoria == "biblioteche")
    {
        var items = await db.Biblioteche.Where(b => b.Geom != null)
            .Select(b => new { lat = b.Geom!.Coordinate.Y, lon = b.Geom!.Coordinate.X, peso = 1.0, cat = "biblioteche" })
            .ToListAsync();
        punti.AddRange(items);
    }
    if (categoria == null || categoria == "fermate")
    {
        var items = await db.FermateBus.Where(f => f.Geom != null)
            .Select(f => new { lat = f.Geom!.Coordinate.Y, lon = f.Geom!.Coordinate.X, peso = 0.3, cat = "fermate" })
            .ToListAsync();
        punti.AddRange(items);
    }
    if (categoria == null || categoria == "areeverdi")
    {
        var items = await db.AreeVerdi.Where(a => a.Geom != null)
            .Select(a => new { lat = a.Geom!.Coordinate.Y, lon = a.Geom!.Coordinate.X, peso = 0.6, cat = "areeverdi" })
            .ToListAsync();
        punti.AddRange(items);
    }
    if (categoria == null || categoria == "residenze")
    {
        var items = await db.ResidenzeUniversitarie.Where(r => r.Geom != null)
            .Select(r => new { lat = r.Geom!.Coordinate.Y, lon = r.Geom!.Coordinate.X, peso = 0.8, cat = "residenze" })
            .ToListAsync();
        punti.AddRange(items);
    }
    if (categoria == null || categoria == "stazioni")
    {
        var items = await db.StazioniFerroviarie.Where(s => s.Geom != null)
            .Select(s => new { lat = s.Geom!.Coordinate.Y, lon = s.Geom!.Coordinate.X, peso = 0.7, cat = "stazioni" })
            .ToListAsync();
        punti.AddRange(items);
    }
    if (categoria == null || categoria == "mense")
    {
        var items = await db.Mense.Where(m => m.Geom != null)
            .Select(m => new { lat = m.Geom!.Coordinate.Y, lon = m.Geom!.Coordinate.X, peso = 0.9, cat = "mense" })
            .ToListAsync();
        punti.AddRange(items);
    }
    if (categoria == null || categoria == "sedi")
    {
        var items = await db.SediUniversitarie.Where(s => s.Geom != null)
            .Select(s => new { lat = s.Geom!.Coordinate.Y, lon = s.Geom!.Coordinate.X, peso = 0.5, cat = "sedi" })
            .ToListAsync();
        punti.AddRange(items);
    }

    return Results.Ok(new { totale = punti.Count, punti });
});

// Density analysis: griglia NxN con score per cella.
app.MapGet("/api/density/grid", async (int? celle, int? profiloId, int? ora, UrbanAdvisorDbContext db) =>
{
    int n = celle ?? 8;
    double minLat = 44.47, maxLat = 44.52;
    double minLon = 11.30, maxLon = 11.38;
    double stepLat = (maxLat - minLat) / n;
    double stepLon = (maxLon - minLon) / n;
    double raggioGradi = 500.0 / 111000.0;

    ProfiloUtente? profilo = null;
    if (profiloId.HasValue)
        profilo = await db.ProfiliUtente.FindAsync(profiloId.Value);
    int wT = profilo?.PesoTrasporti ?? 50;
    int wB = profilo?.PesoBiblioteche ?? 50;
    int wV = profilo?.PesoAreeVerdi ?? 50;
    int wM = profilo?.PesoMobilitaSostenibile ?? 50;
    int wR = profilo?.PesoResidenze ?? 50;
    int wMe = profilo?.PesoMense ?? 50;
    int wS = profilo?.PesoSedi ?? 50;
    double somma = wT + wB + wV + wM + wR + wMe + wS;
    if (somma == 0) somma = 1;

    int oraVal = ora ?? 14;
    bool isGiorno = oraVal >= 8 && oraVal < 20;

    var griglia = new List<object>();

    for (int i = 0; i < n; i++)
    {
        for (int j = 0; j < n; j++)
        {
            double cellLat = minLat + (i + 0.5) * stepLat;
            double cellLon = minLon + (j + 0.5) * stepLon;
            var pt = new NetTopologySuite.Geometries.Point(cellLon, cellLat) { SRID = 4326 };

            int nBib = await db.Biblioteche.CountAsync(b => b.Geom != null && b.Geom.IsWithinDistance(pt, raggioGradi));
            int nFer = await db.FermateBus.CountAsync(f => f.Geom != null && f.Geom.IsWithinDistance(pt, raggioGradi));
            int nVer = await db.AreeVerdi.CountAsync(a => a.Geom != null && a.Geom.IsWithinDistance(pt, raggioGradi));
            int nPis = await db.PisteCiclabili.CountAsync(p => p.Geom != null && p.Geom.IsWithinDistance(pt, raggioGradi));
            int nRes = await db.ResidenzeUniversitarie.CountAsync(r => r.Geom != null && r.Geom.IsWithinDistance(pt, raggioGradi));
            int nMense = await db.Mense.CountAsync(m => m.Geom != null && m.Geom.IsWithinDistance(pt, raggioGradi));
            int nSedi = await db.SediUniversitarie.CountAsync(s => s.Geom != null && s.Geom.IsWithinDistance(pt, raggioGradi));

            int sBib = Math.Min(nBib * 25, 100);
            int sFer = Math.Min(nFer * 2, 100);
            int sVer = Math.Min(nVer * 10, 100);
            int sPis = Math.Min(nPis * 15, 100);
            int sRes = Math.Min(nRes * 30, 100);
            int sMense = Math.Min(nMense * 40, 100);
            int sSedi = Math.Min(nSedi * 3, 100);

            if (!isGiorno)
            {
                sBib = (int)(sBib * 0.3); sFer = (int)(sFer * 1.3); sRes = (int)(sRes * 1.5);
                sMense = (int)(sMense * 0.3); sSedi = (int)(sSedi * 0.2);
            }
            sFer = Math.Clamp(sFer, 0, 100); sRes = Math.Clamp(sRes, 0, 100);

            double score = (sFer * wT + sBib * wB + sVer * wV + sPis * wM + sRes * wR + sMense * wMe + sSedi * wS) / somma;
            int totPoi = nBib + nFer + nVer + nPis + nRes + nMense + nSedi;

            griglia.Add(new
            {
                lat = cellLat, lon = cellLon, score = (int)Math.Round(score),
                totale_poi = totPoi,
                dettaglio = new { biblioteche = nBib, fermate = nFer, aree_verdi = nVer, piste = nPis, residenze = nRes, mense = nMense, sedi = nSedi }
            });
        }
    }

    return Results.Ok(new { celle = n, ora = oraVal, fascia = isGiorno ? "Diurna" : "Notturna",
        profilo = profilo?.Nome ?? "Default", griglia });
});

// Recommendation engine: le top-N zone urbane con motivazione.
app.MapGet("/api/raccomandazioni", async (int? profiloId, int? ora, int? top, UrbanAdvisorDbContext db) =>
{
    int n = 10;
    double minLat = 44.47, maxLat = 44.52;
    double minLon = 11.30, maxLon = 11.38;
    double stepLat = (maxLat - minLat) / n;
    double stepLon = (maxLon - minLon) / n;
    double raggioGradi = 500.0 / 111000.0;

    ProfiloUtente? profilo = null;
    if (profiloId.HasValue)
        profilo = await db.ProfiliUtente.FindAsync(profiloId.Value);
    int wT = profilo?.PesoTrasporti ?? 50;
    int wB = profilo?.PesoBiblioteche ?? 50;
    int wV = profilo?.PesoAreeVerdi ?? 50;
    int wM = profilo?.PesoMobilitaSostenibile ?? 50;
    int wR = profilo?.PesoResidenze ?? 50;
    int wMe = profilo?.PesoMense ?? 50;
    int wS = profilo?.PesoSedi ?? 50;
    double somma = wT + wB + wV + wM + wR + wMe + wS;
    if (somma == 0) somma = 1;

    int oraVal = ora ?? 14;
    bool isGiorno = oraVal >= 8 && oraVal < 20;
    int topN = top ?? 5;

    var zone = new List<(double lat, double lon, int score, string motivo, int nBib, int nFer, int nVer, int nPis, int nRes, int nMense, int nSedi)>();

    for (int i = 0; i < n; i++)
    {
        for (int j = 0; j < n; j++)
        {
            double cellLat = minLat + (i + 0.5) * stepLat;
            double cellLon = minLon + (j + 0.5) * stepLon;
            var pt = new NetTopologySuite.Geometries.Point(cellLon, cellLat) { SRID = 4326 };

            int nBib = await db.Biblioteche.CountAsync(b => b.Geom != null && b.Geom.IsWithinDistance(pt, raggioGradi));
            int nFer = await db.FermateBus.CountAsync(f => f.Geom != null && f.Geom.IsWithinDistance(pt, raggioGradi));
            int nVer = await db.AreeVerdi.CountAsync(a => a.Geom != null && a.Geom.IsWithinDistance(pt, raggioGradi));
            int nPis = await db.PisteCiclabili.CountAsync(p => p.Geom != null && p.Geom.IsWithinDistance(pt, raggioGradi));
            int nRes = await db.ResidenzeUniversitarie.CountAsync(r => r.Geom != null && r.Geom.IsWithinDistance(pt, raggioGradi));
            int nMense = await db.Mense.CountAsync(m => m.Geom != null && m.Geom.IsWithinDistance(pt, raggioGradi));
            int nSedi = await db.SediUniversitarie.CountAsync(s => s.Geom != null && s.Geom.IsWithinDistance(pt, raggioGradi));

            int sBib = Math.Min(nBib * 25, 100);
            int sFer = Math.Min(nFer * 2, 100);
            int sVer = Math.Min(nVer * 10, 100);
            int sPis = Math.Min(nPis * 15, 100);
            int sRes = Math.Min(nRes * 30, 100);
            int sMense = Math.Min(nMense * 40, 100);
            int sSedi = Math.Min(nSedi * 3, 100);

            if (!isGiorno)
            {
                sBib = (int)(sBib * 0.3); sFer = (int)(sFer * 1.3); sRes = (int)(sRes * 1.5);
                sMense = (int)(sMense * 0.3); sSedi = (int)(sSedi * 0.2);
            }
            sFer = Math.Clamp(sFer, 0, 100); sRes = Math.Clamp(sRes, 0, 100);

            double score = (sFer * wT + sBib * wB + sVer * wV + sPis * wM + sRes * wR + sMense * wMe + sSedi * wS) / somma;

            var motivi = new List<string>();
            if (sBib >= 50) motivi.Add($"alta densità di biblioteche ({nBib})");
            if (sFer >= 50) motivi.Add($"ben collegata ({nFer} fermate)");
            if (sVer >= 50) motivi.Add($"ricca di aree verdi ({nVer})");
            if (sPis >= 50) motivi.Add($"presenza piste ciclabili ({nPis})");
            if (sRes >= 50) motivi.Add($"vicina a residenze ({nRes})");
            if (sMense >= 50) motivi.Add($"buona offerta di mense/ristori ({nMense})");
            if (sSedi >= 50) motivi.Add($"alta concentrazione di sedi universitarie ({nSedi})");
            if (motivi.Count == 0) motivi.Add("area con servizi limitati");

            zone.Add((cellLat, cellLon, (int)Math.Round(score), string.Join(", ", motivi), nBib, nFer, nVer, nPis, nRes, nMense, nSedi));
        }
    }

    var topZone = zone.OrderByDescending(z => z.score).Take(topN).Select((z, idx) => new
    {
        posizione = idx + 1,
        lat = z.lat, lon = z.lon,
        student_accessibility_score = z.score,
        motivazione = z.motivo,
        dettaglio = new { biblioteche = z.nBib, fermate = z.nFer, aree_verdi = z.nVer, piste = z.nPis, residenze = z.nRes, mense = z.nMense, sedi = z.nSedi }
    });

    return Results.Ok(new
    {
        profilo = profilo?.Nome ?? "Default",
        ora = oraVal, fascia = isGiorno ? "Diurna" : "Notturna",
        raccomandazioni = topZone
    });
});

// Temporal analytics: servizi aperti/chiusi per giorno e ora.
app.MapGet("/api/temporale/disponibilita", async (int? giorno, int? ora, UrbanAdvisorDbContext db) =>
{
    int giornoVal = giorno ?? (int)DateTime.Now.DayOfWeek;
    giornoVal = giornoVal == 0 ? 6 : giornoVal - 1;
    int oraVal = ora ?? DateTime.Now.Hour;
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
            aperti_ora = g.Count(o => oraTime >= o.OraApertura && oraTime < o.OraChiusura),
            chiusi_ora = g.Count(o => !(oraTime >= o.OraApertura && oraTime < o.OraChiusura)),
            orario_tipico = g.First().OraApertura.ToString("HH:mm") + " - " + g.First().OraChiusura.ToString("HH:mm")
        })
        .ToList();

    var distribuzioneOraria = Enumerable.Range(0, 24).Select(h =>
    {
        var t = new TimeOnly(h, 0);
        return new
        {
            ora = h,
            aperti = tuttiOrari.Count(o => t >= o.OraApertura && t < o.OraChiusura)
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
    double sigmaGradi = sigma / 111000.0;

    double noiseLat = rng.NextDouble() * 2 - 1 + rng.NextDouble() * 2 - 1;
    double noiseLon = rng.NextDouble() * 2 - 1 + rng.NextDouble() * 2 - 1;
    double pertLat = lat + noiseLat * sigmaGradi;
    double pertLon = lon + noiseLon * sigmaGradi;

    var realScore = await CalcolaScoreInterno(lat, lon, ora, profiloId, db);
    var pertScore = await CalcolaScoreInterno(pertLat, pertLon, ora, profiloId, db);

    double distanzaPerturbazione = Math.Sqrt(Math.Pow((pertLat - lat) * 111000, 2) + Math.Pow((pertLon - lon) * 111000 * Math.Cos(lat * Math.PI / 180), 2));
    int qualityLoss = Math.Abs(realScore.punteggio - pertScore.punteggio);

    return Results.Ok(new
    {
        sigma_metri = sigma,
        posizione_reale = new { lat, lon },
        posizione_perturbata = new { lat = pertLat, lon = pertLon },
        privacy_perturbation_metri = Math.Round(distanzaPerturbazione, 1),
        score_reale = realScore.punteggio,
        score_perturbato = pertScore.punteggio,
        quality_loss = qualityLoss,
        subscores_reale = realScore.subscores,
        subscores_perturbato = pertScore.subscores
    });
});

// Privacy: analisi del trade-off tra privacy e qualita' del servizio.
app.MapGet("/api/privacy/tradeoff", async (double lat, double lon, int ora, int? profiloId, UrbanAdvisorDbContext db) =>
{
    var rng = new Random(42);
    var livelli = new[] { 0, 50, 100, 200, 500, 1000, 2000 };
    int campioni = 5;

    var risultati = new List<object>();

    foreach (var sigma in livelli)
    {
        double sigmaGradi = sigma / 111000.0;
        var scores = new List<int>();
        var distanze = new List<double>();

        for (int i = 0; i < campioni; i++)
        {
            double n1 = rng.NextDouble() * 2 - 1 + rng.NextDouble() * 2 - 1;
            double n2 = rng.NextDouble() * 2 - 1 + rng.NextDouble() * 2 - 1;
            double pLat = lat + n1 * sigmaGradi;
            double pLon = lon + n2 * sigmaGradi;

            var sc = await CalcolaScoreInterno(pLat, pLon, ora, profiloId, db);
            scores.Add(sc.punteggio);
            distanze.Add(Math.Sqrt(Math.Pow((pLat - lat) * 111000, 2) + Math.Pow((pLon - lon) * 111000 * Math.Cos(lat * Math.PI / 180), 2)));
        }

        var scoreReale = await CalcolaScoreInterno(lat, lon, ora, profiloId, db);
        risultati.Add(new
        {
            sigma_metri = sigma,
            privacy_perturbation_media = Math.Round(distanze.Average(), 1),
            score_medio_perturbato = (int)scores.Average(),
            score_reale = scoreReale.punteggio,
            quality_loss_medio = (int)Math.Round(scores.Select(s => Math.Abs(s - scoreReale.punteggio)).Average())
        });
    }

    return Results.Ok(new { lat, lon, ora, risultati });
});

// Funzione helper: calcola score e sub-score per una posizione (riusata da privacy e clustering).
static async Task<(int punteggio, object subscores)> CalcolaScoreInterno(
    double lat, double lon, int ora, int? profiloId, UrbanAdvisorDbContext db)
{
    var pt = new NetTopologySuite.Geometries.Point(lon, lat) { SRID = 4326 };
    double rGradi = 1000.0 / 111000.0;

    ProfiloUtente? profilo = null;
    if (profiloId.HasValue) profilo = await db.ProfiliUtente.FindAsync(profiloId.Value);
    int wT = profilo?.PesoTrasporti ?? 50, wB = profilo?.PesoBiblioteche ?? 50;
    int wV = profilo?.PesoAreeVerdi ?? 50, wM = profilo?.PesoMobilitaSostenibile ?? 50, wR = profilo?.PesoResidenze ?? 50;
    int wMe = profilo?.PesoMense ?? 50, wS = profilo?.PesoSedi ?? 50;
    double somma = wT + wB + wV + wM + wR + wMe + wS; if (somma == 0) somma = 1;

    var fermata = await db.FermateBus.Where(f => f.Geom != null).OrderBy(f => f.Geom!.Distance(pt)).FirstOrDefaultAsync();
    double dFerm = (fermata?.Geom?.Distance(pt) ?? 999) * 111000;
    int sT = Math.Clamp(dFerm <= 1000 ? (int)(100 - dFerm / 10) : 0, 0, 100);
    int sB = Math.Min(await db.Biblioteche.CountAsync(b => b.Geom != null && b.Geom.IsWithinDistance(pt, rGradi)) * 25, 100);
    int sV = Math.Min(await db.AreeVerdi.CountAsync(a => a.Geom != null && a.Geom.IsWithinDistance(pt, rGradi)) * 10, 100);
    int sM = Math.Min(await db.PisteCiclabili.CountAsync(p => p.Geom != null && p.Geom.IsWithinDistance(pt, rGradi)) * 15, 100);
    int sR = Math.Min(await db.ResidenzeUniversitarie.CountAsync(r => r.Geom != null && r.Geom.IsWithinDistance(pt, rGradi)) * 30, 100);
    int sMe = Math.Min(await db.Mense.CountAsync(m => m.Geom != null && m.Geom.IsWithinDistance(pt, rGradi)) * 40, 100);
    int sS = Math.Min(await db.SediUniversitarie.CountAsync(s => s.Geom != null && s.Geom.IsWithinDistance(pt, rGradi)) * 3, 100);

    bool isGiorno = ora >= 8 && ora < 20;
    if (!isGiorno)
    {
        sB = (int)(sB * 0.3); sT = Math.Clamp((int)(sT * 1.3), 0, 100); sR = Math.Clamp((int)(sR * 1.5), 0, 100);
        sMe = (int)(sMe * 0.3); sS = (int)(sS * 0.2);
    }

    double score = (sT * wT + sB * wB + sV * wV + sM * wM + sR * wR + sMe * wMe + sS * wS) / somma;
    return (Math.Clamp((int)Math.Round(score), 0, 100),
        new { trasporti = sT, biblioteche = sB, aree_verdi = sV, mobilita = sM, residenze = sR, mense = sMe, sedi = sS });
}

// Analytics avanzata: clustering K-Means delle zone e indice di Moran.
app.MapGet("/api/clustering", async (int? k, int? profiloId, int? ora, UrbanAdvisorDbContext db) =>
{
    int numClusters = k ?? 4;
    int n = 10;
    double minLat = 44.47, maxLat = 44.52, minLon = 11.30, maxLon = 11.38;
    double stepLat = (maxLat - minLat) / n, stepLon = (maxLon - minLon) / n;
    double rGradi = 500.0 / 111000.0;
    int oraVal = ora ?? 14;

    var celle = new List<(double lat, double lon, double[] features, int score)>();
    for (int i = 0; i < n; i++)
    {
        for (int j = 0; j < n; j++)
        {
            double cLat = minLat + (i + 0.5) * stepLat;
            double cLon = minLon + (j + 0.5) * stepLon;
            var pt = new NetTopologySuite.Geometries.Point(cLon, cLat) { SRID = 4326 };

            int nB = await db.Biblioteche.CountAsync(b => b.Geom != null && b.Geom.IsWithinDistance(pt, rGradi));
            int nF = await db.FermateBus.CountAsync(f => f.Geom != null && f.Geom.IsWithinDistance(pt, rGradi));
            int nV = await db.AreeVerdi.CountAsync(a => a.Geom != null && a.Geom.IsWithinDistance(pt, rGradi));
            int nP = await db.PisteCiclabili.CountAsync(p => p.Geom != null && p.Geom.IsWithinDistance(pt, rGradi));
            int nR = await db.ResidenzeUniversitarie.CountAsync(r => r.Geom != null && r.Geom.IsWithinDistance(pt, rGradi));
            int nMe = await db.Mense.CountAsync(m => m.Geom != null && m.Geom.IsWithinDistance(pt, rGradi));
            int nS = await db.SediUniversitarie.CountAsync(s => s.Geom != null && s.Geom.IsWithinDistance(pt, rGradi));

            var sc = await CalcolaScoreInterno(cLat, cLon, oraVal, profiloId, db);
            celle.Add((cLat, cLon, new double[] { nB, nF, nV, nP, nR, nMe, nS }, sc.punteggio));
        }
    }

    int dim = 7;

    // Standardizzazione z-score: senza questo passaggio, "sedi" (range 0-50+) dominerebbe
    // la distanza euclidea rispetto a feature con range molto più piccolo (0-5), distorcendo
    // il clustering. K-Means è sensibile alla scala delle feature — va sempre normalizzato prima.
    var featMean = new double[dim];
    var featStd = new double[dim];
    for (int d = 0; d < dim; d++)
    {
        featMean[d] = celle.Average(c => c.features[d]);
        double variance = celle.Average(c => Math.Pow(c.features[d] - featMean[d], 2));
        featStd[d] = Math.Sqrt(variance);
        if (featStd[d] == 0) featStd[d] = 1; // evita divisione per zero se una feature è costante
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
            double dist = Math.Sqrt(Math.Pow(celle[i2].lat - celle[j2].lat, 2) + Math.Pow(celle[i2].lon - celle[j2].lon, 2));
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

// Record per deserializzare il corpo della richiesta di feedback.
record FeedbackRequest(string Feedback);