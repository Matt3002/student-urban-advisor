using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace UrbanAdvisor.Api.Models
{
    [Table("profili_utente")]
    public class ProfiloUtente
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("nome")]
        public string Nome { get; set; } = null!;

        [Column("peso_trasporti")]
        public int PesoTrasporti { get; set; } = 50;

        [Column("peso_biblioteche")]
        public int PesoBiblioteche { get; set; } = 50;

        [Column("peso_aree_verdi")]
        public int PesoAreeVerdi { get; set; } = 50;

        [Column("peso_mobilita_sostenibile")]
        public int PesoMobilitaSostenibile { get; set; } = 50;

        [Column("peso_residenze")]
        public int PesoResidenze { get; set; } = 50;

        [Column("peso_mense")]
        public int PesoMense { get; set; } = 50;

        [Column("peso_sedi")]
        public int PesoSedi { get; set; } = 50;
    }
}