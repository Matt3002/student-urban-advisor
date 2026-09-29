// ============================================================================
// SalaStudio.cs - Modello dati per le sale studio (import da data/sale-studio.csv)
// ============================================================================
using NetTopologySuite.Geometries;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace UrbanAdvisor.Api.Models
{
    [Table("sale_studio")]
    public class SalaStudio
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("nome")]
        public string? Nome { get; set; }

        [Column("indirizzo")]
        public string? Indirizzo { get; set; }

        [Column("posti")]
        public int? Posti { get; set; }

        [Column("fonte")]
        public string? Fonte { get; set; }

        [Column("geom")]
        public Point? Geom { get; set; }

        [Column("geog", TypeName = "geography")]
        [DatabaseGenerated(DatabaseGeneratedOption.Computed)]
        public Point? Geog { get; set; }
    }
}
