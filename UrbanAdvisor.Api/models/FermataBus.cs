using NetTopologySuite.Geometries;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace UrbanAdvisor.Api.Models
{
    [Table("fermate_bus")]
    public class FermataBus
    {
        // Nella tabella PostGIS la chiave primaria è "codice_fermata" (VARCHAR),
        // NON esiste una colonna "id": mappare un "id" fittizio faceva fallire EF.
        [Key]
        [Column("codice_fermata")]
        public string CodiceFermata { get; set; } = null!;

        [Column("linea_bus")]
        public string? LineaBus { get; set; }

        [Column("nome_fermata")]
        public string? NomeFermata { get; set; }

        [Column("geom")]
        public Point? Geom { get; set; }
    }
}