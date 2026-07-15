using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace UrbanAdvisor.Api.Models
{
    /// <summary>
    /// Storico dei suggerimenti generati dal sistema.
    /// Ogni record memorizza il contesto in cui il suggerimento è stato prodotto
    /// (posizione, ora, profilo) e il feedback opzionale dell'utente.
    /// </summary>
    [Table("suggerimenti_storico")]
    public class SuggerimentoStorico
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("profilo_id")]
        public int? ProfiloId { get; set; }

        [Column("lat")]
        public double Lat { get; set; }

        [Column("lon")]
        public double Lon { get; set; }

        [Column("ora")]
        public int Ora { get; set; }

        [Column("punteggio")]
        public int Punteggio { get; set; }

        [Column("fascia")]
        public string Fascia { get; set; } = null!;

        [Column("motivazione")]
        public string Motivazione { get; set; } = null!;

        /// <summary>Timestamp UTC di generazione del suggerimento</summary>
        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>Feedback utente: "utile", "non_utile", "salvato" oppure null se non dato</summary>
        [Column("feedback")]
        public string? Feedback { get; set; }
    }
}