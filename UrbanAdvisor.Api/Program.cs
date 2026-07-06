using Microsoft.EntityFrameworkCore;
using UrbanAdvisor.Api.Data;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddOpenApi();

// Configura il Database PostgreSQL + PostGIS
builder.Services.AddDbContext<UrbanAdvisorDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        o => o.UseNetTopologySuite() // Questo abilita le query spaziali!
    ));
    
var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseHttpsRedirection();

// ==========================================
// I NOSTRI ENDPOINT (API REST)
// ==========================================

// Endpoint per recuperare tutte le biblioteche
app.MapGet("/api/biblioteche", async (UrbanAdvisorDbContext db) =>
{
    // Usiamo una query LINQ per estrarre i dati e convertire la geometria PostGIS 
    // in coordinate Latitudine/Longitudine semplici per il frontend Web
    var biblioteche = await db.Biblioteche
        .Select(b => new 
        {
            id = b.Id,
            nome = b.Nome,
            indirizzo = b.Indirizzo,
            quartiere = b.Quartiere,
            postazioni = b.PostazioniLettura,
            // Estraiamo la coordinata Y (Latitudine) e X (Longitudine) dal MultiPoint
            lat = b.Geom != null ? b.Geom.Coordinate.Y : 0,
            lon = b.Geom != null ? b.Geom.Coordinate.X : 0
        })
        .ToListAsync();

    return Results.Ok(biblioteche);
})
.WithName("GetBiblioteche");

// Endpoint Fermate Bus
app.MapGet("/api/fermate", async (UrbanAdvisorDbContext db) =>
{
    return Results.Ok(await db.FermateBus
        .Select(f => new {
            id = f.CodiceFermata,
            nome = f.NomeFermata,
            linea = f.LineaBus,
            lat = f.Geom != null ? f.Geom.Coordinate.Y : 0,
            lon = f.Geom != null ? f.Geom.Coordinate.X : 0
        }).ToListAsync());
});

// Endpoint Residenze
app.MapGet("/api/residenze", async (UrbanAdvisorDbContext db) =>
{
    return Results.Ok(await db.ResidenzeUniversitarie
        .Select(r => new {
            id = r.Id,
            nome = r.Nome,
            posti = r.PostiLetto,
            lat = r.Geom != null ? r.Geom.Coordinate.Y : 0,
            lon = r.Geom != null ? r.Geom.Coordinate.X : 0
        }).ToListAsync());
});

// Endpoint Aree Verdi
app.MapGet("/api/areeverdi", async (UrbanAdvisorDbContext db) =>
{
    return Results.Ok(await db.AreeVerdi
        .Select(a => new {
            id = a.Id,
            nome = a.NomeArea,
            tipo = a.Tipologia,
            lat = a.Geom != null ? a.Geom.Coordinate.Y : 0,
            lon = a.Geom != null ? a.Geom.Coordinate.X : 0
        }).ToListAsync());
});

// ==========================================
// ALGORITMO CONTEXT-AWARE: STUDENT SCORE
// ==========================================

app.MapGet("/api/accessibility/score", async (double lat, double lon, int ora, UrbanAdvisorDbContext db) =>
{
    // 1. Creiamo il punto geometrico dell'utente (Nota: PostGIS usa Longitudine, Latitudine)
    var userLocation = new NetTopologySuite.Geometries.Point(lon, lat) { SRID = 4326 };

    // Gradi approssimativi per 1 km (1 grado = ~111 km, quindi 1km = ~0.009 gradi)
    double raggioUnKm = 0.009; 
    
    int score = 0;
    string motivazione = "";

    // 2. Calcoliamo la distanza dalla fermata bus più vicina
    var fermataPiuVicina = await db.FermateBus
        .OrderBy(f => f.Geom.Distance(userLocation))
        .FirstOrDefaultAsync();

    double distanzaFermataGradi = fermataPiuVicina?.Geom?.Distance(userLocation) ?? 999;
    double distanzaFermataMetri = distanzaFermataGradi * 111000; // Conversione grezza in metri

    // 3. Logica basata sull'ORARIO (Context-Awareness)
    bool isGiorno = ora >= 8 && ora < 20;

    if (isGiorno)
    {
        // CONTESTO DIURNO: Contiamo le biblioteche nel raggio di 1 km
        var bibliotecheVicine = await db.Biblioteche
            .CountAsync(b => b.Geom.IsWithinDistance(userLocation, raggioUnKm));

        // Assegniamo 10 punti per ogni biblioteca (max 50)
        int scoreStudio = Math.Min(bibliotecheVicine * 10, 50);
        
        // Punteggio Trasporti (max 50 punti se a meno di 100m, decresce fino a 1km)
        int scoreTrasporti = distanzaFermataMetri <= 1000 ? (int)(50 - (distanzaFermataMetri / 20)) : 0;
        scoreTrasporti = Math.Clamp(scoreTrasporti, 0, 50);

        score = scoreStudio + scoreTrasporti;
        motivazione = $"Giorno: Trovate {bibliotecheVicine} biblioteche a 15 min a piedi. Fermata bus a {(int)distanzaFermataMetri}m.";
    }
    else
    {
        // CONTESTO NOTTURNO: Biblioteche chiuse. Focus su mobilità sicura e residenze.
        var residenzeVicine = await db.ResidenzeUniversitarie
            .CountAsync(r => r.Geom.IsWithinDistance(userLocation, raggioUnKm));

        // Punteggio Trasporti raddoppiato di notte (max 80 punti)
        int scoreTrasporti = distanzaFermataMetri <= 1000 ? (int)(80 - (distanzaFermataMetri / 12.5)) : 0;
        scoreTrasporti = Math.Clamp(scoreTrasporti, 0, 80);

        // Bonus sicurezza se vicino a una residenza universitaria (20 punti)
        int scoreSicurezza = residenzeVicine > 0 ? 20 : 0;

        score = scoreTrasporti + scoreSicurezza;
        motivazione = $"Notte: Focus su trasporti. Fermata a {(int)distanzaFermataMetri}m. Residenze vicine: {residenzeVicine}.";
    }

    return Results.Ok(new 
    { 
        punteggio = score, 
        fascia = isGiorno ? "Diurna" : "Notturna",
        dettaglio = motivazione 
    });
});

app.Run();