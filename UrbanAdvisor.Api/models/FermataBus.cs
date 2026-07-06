using NetTopologySuite.Geometries;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace UrbanAdvisor.Api.Models
{
    [Table("fermate_bus")]
    public class FermataBus
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("codice_fermata")]
        public string? CodiceFermata { get; set; }

        [Column("linea_bus")]
        public string? LineaBus { get; set; }

        [Column("nome_fermata")]
        public string? NomeFermata { get; set; }

        [Column("geom")]
        public Point? Geom { get; set; }
    }
}