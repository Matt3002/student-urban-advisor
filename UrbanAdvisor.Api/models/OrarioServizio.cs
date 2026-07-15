using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace UrbanAdvisor.Api.Models
{
    [Table("orari_servizi")]
    public class OrarioServizio
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("categoria")]
        public string Categoria { get; set; } = null!;

        [Column("nome_servizio")]
        public string? NomeServizio { get; set; }

        /// <summary>0=Lunedì, 1=Martedì, ..., 5=Sabato, 6=Domenica</summary>
        [Column("giorno_settimana")]
        public int GiornoSettimana { get; set; }

        [Column("ora_apertura")]
        public TimeOnly OraApertura { get; set; }

        [Column("ora_chiusura")]
        public TimeOnly OraChiusura { get; set; }
    }
}