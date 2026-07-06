using NetTopologySuite.Geometries;
using System.ComponentModel.DataAnnotations.Schema;

namespace UrbanAdvisor.Api.Models
{
    [Table("biblioteche")]
    public class Biblioteca
    {
        [Column("id")]
        public int Id { get; set; }

        [Column("nome")]
        public string? Nome { get; set; }

        [Column("indirizzo")]
        public string? Indirizzo { get; set; }

        [Column("quartiere")]
        public string? Quartiere { get; set; }

        [Column("postazioni_lettura")]
        public int? PostazioniLettura { get; set; }

        // Ecco la magia di NetTopologySuite: mappa la geometria PostGIS!
        [Column("geom")]
        public MultiPoint? Geom { get; set; }
    }
}