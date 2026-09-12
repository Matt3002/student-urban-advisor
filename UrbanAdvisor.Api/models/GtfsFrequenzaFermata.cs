using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace UrbanAdvisor.Api.Models
{
    [Table("gtfs_frequenze_fermata")]
    public class GtfsFrequenzaFermata
    {
        [Key] [Column("id")] public int Id { get; set; }
        [Column("stop_id")] public string StopId { get; set; } = null!;
        [Column("fascia_oraria")] public int FasciaOraria { get; set; }
        [Column("numero_corse")] public int NumeroCorse { get; set; }
    }
}