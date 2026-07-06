using NetTopologySuite.Geometries;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace UrbanAdvisor.Api.Models
{
    [Table("residenze_universitarie")]
    public class ResidenzaUniversitaria
    {
        [Key] [Column("id")] public string Id { get; set; } = null!;
        [Column("nome")] public string? Nome { get; set; }
        [Column("indirizzo")] public string? Indirizzo { get; set; }
        [Column("posti_letto")] public int? PostiLetto { get; set; }
        [Column("geom")] public Point? Geom { get; set; }
    }
}