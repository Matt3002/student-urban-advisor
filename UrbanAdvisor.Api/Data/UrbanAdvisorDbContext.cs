using Microsoft.EntityFrameworkCore;
using UrbanAdvisor.Api.Models;

namespace UrbanAdvisor.Api.Data
{
    public class UrbanAdvisorDbContext : DbContext
    {
        public UrbanAdvisorDbContext(DbContextOptions<UrbanAdvisorDbContext> options)
            : base(options) { }

        // Dataset geografici
        public DbSet<Biblioteca> Biblioteche { get; set; }
        public DbSet<FermataBus> FermateBus { get; set; }
        public DbSet<AreaVerde> AreeVerdi { get; set; }
        public DbSet<ResidenzaUniversitaria> ResidenzeUniversitarie { get; set; }
        public DbSet<StazioneFerroviaria> StazioniFerroviarie { get; set; }
        public DbSet<SedeUniversitaria> SediUniversitarie { get; set; }
        public DbSet<Mensa> Mense { get; set; }
        public DbSet<PistaCiclabile> PisteCiclabili { get; set; }

        public DbSet<ProfiloUtente> ProfiliUtente { get; set; }
        public DbSet<SuggerimentoStorico> SuggerimentiStorico { get; set; }

        public DbSet<OrarioServizio> OrariServizi { get; set; }

        public DbSet<GtfsFermata> GtfsFermate { get; set; }
        public DbSet<GtfsFrequenzaFermata> GtfsFrequenzeFermata { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.HasPostgresExtension("postgis");
            base.OnModelCreating(modelBuilder);
        }
    }
}