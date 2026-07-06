using NetTopologySuite.Geometries;
using System.ComponentModel.DataAnnotations.Schema;

namespace UrbanAdvisor.Api.Models
{
    [Table("aree_verdi")]
    public class AreaVerde
    {
        [Column("id")] public int Id { get; set; }
        [Column("nome_area")] public string? NomeArea { get; set; }
        [Column("tipologia")] public string? Tipologia { get; set; }
        [Column("quartiere")] public string? Quartiere { get; set; }
        [Column("ubicazione")] public string? Ubicazione { get; set; }
        [Column("geom")] public Point? Geom { get; set; }
    }
}