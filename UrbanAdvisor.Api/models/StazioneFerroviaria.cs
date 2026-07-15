using NetTopologySuite.Geometries;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace UrbanAdvisor.Api.Models
{
    [Table("stazioni_ferroviarie")]
    public class StazioneFerroviaria
    {
        [Key] [Column("codice")] public string Codice { get; set; } = null!;
        [Column("denominazione")] public string? Denominazione { get; set; }
        [Column("geom")] public Point? Geom { get; set; }
    }
}