using NetTopologySuite.Geometries;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace UrbanAdvisor.Api.Models
{
    [Table("mense")]
    public class Mensa
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("nome")]
        public string? Nome { get; set; }

        [Column("indirizzo")]
        public string? Indirizzo { get; set; }

        [Column("tipo")]
        public string? Tipo { get; set; }

        [Column("gestore")]
        public string? Gestore { get; set; }

        [Column("geom")]
        public Point? Geom { get; set; }
    }
}