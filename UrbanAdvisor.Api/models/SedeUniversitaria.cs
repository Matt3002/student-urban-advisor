using NetTopologySuite.Geometries;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace UrbanAdvisor.Api.Models
{
    [Table("sedi_universitarie")]
    public class SedeUniversitaria
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("tipo")]
        public string? Tipo { get; set; }

        [Column("nome")]
        public string? Nome { get; set; }

        [Column("indirizzo")]
        public string? Indirizzo { get; set; }

        [Column("url")]
        public string? Url { get; set; }

        [Column("geom")]
        public Point? Geom { get; set; }
    }
}