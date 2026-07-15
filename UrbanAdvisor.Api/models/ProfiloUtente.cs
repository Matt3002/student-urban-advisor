using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace UrbanAdvisor.Api.Models
{
    /// <summary>
    /// Profilo utente con preferenze configurabili.
    /// I pesi (0-100) influenzano il ranking personalizzato dei PoI.
    /// Pesi diversi producono ranking diversi per la stessa posizione.
    /// </summary>
    [Table("profili_utente")]
    public class ProfiloUtente
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("nome")]
        public string Nome { get; set; } = null!;

        /// <summary>Peso dato alla vicinanza dei trasporti pubblici (fermate bus/treno)</summary>
        [Column("peso_trasporti")]
        public int PesoTrasporti { get; set; } = 50;

        /// <summary>Peso dato alla vicinanza di biblioteche e sale studio</summary>
        [Column("peso_biblioteche")]
        public int PesoBiblioteche { get; set; } = 50;

        /// <summary>Peso dato alla vicinanza di aree verdi e parchi</summary>
        [Column("peso_aree_verdi")]
        public int PesoAreeVerdi { get; set; } = 50;

        /// <summary>Peso dato alla mobilità sostenibile (piste ciclabili)</summary>
        [Column("peso_mobilita_sostenibile")]
        public int PesoMobilitaSostenibile { get; set; } = 50;

        /// <summary>Peso dato alla vicinanza di residenze universitarie</summary>
        [Column("peso_residenze")]
        public int PesoResidenze { get; set; } = 50;
    }
}