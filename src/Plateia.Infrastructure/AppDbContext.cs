using Plateia.Application;
using Plateia.Domain;
using Microsoft.EntityFrameworkCore;

namespace Plateia.Infrastructure;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IAppDbContext
{
    public DbSet<Usuario> Usuarios => Set<Usuario>();
    public DbSet<Evento> Eventos => Set<Evento>();
    public DbSet<TipoIngresso> TiposIngresso => Set<TipoIngresso>();
    public DbSet<Inscricao> Inscricoes => Set<Inscricao>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Usuario>(e =>
        {
            e.Property(x => x.Nome).HasMaxLength(120);
            e.Property(x => x.Email).HasMaxLength(180);
            e.Property(x => x.Telefone).HasMaxLength(20);
            e.Property(x => x.SenhaHash).HasMaxLength(300);
            e.HasIndex(x => x.Email).IsUnique();
        });

        b.Entity<Evento>(e =>
        {
            e.Property(x => x.Titulo).HasMaxLength(160);
            e.Property(x => x.Slug).HasMaxLength(80);
            e.Property(x => x.Subtitulo).HasMaxLength(200);
            e.Property(x => x.Descricao).HasMaxLength(8000);
            e.Property(x => x.Local).HasMaxLength(160);
            e.Property(x => x.Endereco).HasMaxLength(250);
            e.Property(x => x.Cor).HasMaxLength(7);
            e.Property(x => x.InstrucoesPagamento).HasMaxLength(2000);
            e.HasIndex(x => x.Slug).IsUnique();
            e.HasIndex(x => x.OrganizadorId);
            e.HasOne(x => x.Organizador).WithMany().HasForeignKey(x => x.OrganizadorId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Tipos).WithOne(x => x.Evento!).HasForeignKey(x => x.EventoId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Inscricoes).WithOne(x => x.Evento!).HasForeignKey(x => x.EventoId).OnDelete(DeleteBehavior.Cascade);
            e.Ignore(x => x.Visivel);
        });

        b.Entity<TipoIngresso>(e =>
        {
            e.Property(x => x.Nome).HasMaxLength(100);
            e.Property(x => x.Descricao).HasMaxLength(300);
            e.Property(x => x.Preco).HasPrecision(10, 2);
            e.Ignore(x => x.Gratuito);
        });

        b.Entity<Inscricao>(e =>
        {
            e.Property(x => x.Nome).HasMaxLength(160);
            e.Property(x => x.Email).HasMaxLength(180);
            e.Property(x => x.Telefone).HasMaxLength(15);
            e.Property(x => x.Codigo).HasMaxLength(12);
            e.Property(x => x.Valor).HasPrecision(10, 2);
            e.Property(x => x.FormaPagamento).HasMaxLength(40);
            e.Property(x => x.Observacao).HasMaxLength(500);
            e.HasIndex(x => x.Codigo).IsUnique();
            e.HasIndex(x => new { x.EventoId, x.Status });
            e.HasIndex(x => new { x.EventoId, x.Email });
            e.HasOne(x => x.TipoIngresso).WithMany().HasForeignKey(x => x.TipoIngressoId).OnDelete(DeleteBehavior.Restrict);
            e.Ignore(x => x.Ativa);
        });
    }
}
