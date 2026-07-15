using NetTopologySuite.Geometries;
using System.ComponentModel.DataAnnotations.Schema;

namespace UrbanAdvisor.Api.Models
{
    [Table("piste_ciclabili")]
    public class PistaCiclabile
    {
        [Column("id")] public int Id { get; set; }
        [Column("codice")] public string? Codice { get; set; }
        [Column("lunghezza")] public decimal? Lunghezza { get; set; }
        [Column("utilizzo")] public string? Utilizzo { get; set; }

        // La geometria delle piste è una MultiLineString (percorsi lineari)
        [Column("geom")] public MultiLineString? Geom { get; set; }
    }
}