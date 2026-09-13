using NetTopologySuite.Geometries;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace UrbanAdvisor.Api.Models
{
    [Table("gtfs_fermate")]
    public class GtfsFermata
    {
        [Key] [Column("stop_id")] public string StopId { get; set; } = null!;
        [Column("nome")] public string? Nome { get; set; }
        [Column("geom")] public Point? Geom { get; set; }
    }
}