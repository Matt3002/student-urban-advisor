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

app.Run();