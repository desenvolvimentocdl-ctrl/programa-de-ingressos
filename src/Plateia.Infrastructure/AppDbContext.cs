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
    public DbSet<Cupom> Cupons => Set<Cupom>();
    public DbSet<EspacoEvento> Espacos => Set<EspacoEvento>();
    public DbSet<VisitaEvento> Visitas => Set<VisitaEvento>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Usuario>(e =>
        {
            e.Property(x => x.Nome).HasMaxLength(120);
            e.Property(x => x.Email).HasMaxLength(180);
            e.Property(x => x.Telefone).HasMaxLength(20);
            e.Property(x => x.SenhaHash).HasMaxLength(300);
            e.Property(x => x.NomePublico).HasMaxLength(120);
            e.Property(x => x.Slug).HasMaxLength(80);
            e.Property(x => x.Bio).HasMaxLength(1000);
            e.Property(x => x.Instagram).HasMaxLength(60);
            e.Property(x => x.Site).HasMaxLength(200);
            e.Property(x => x.Cidade).HasMaxLength(80);
            e.HasIndex(x => x.Email).IsUnique();
            e.HasIndex(x => x.Slug).IsUnique();
            e.Ignore(x => x.NomeExibicao);
        });

        b.Entity<Evento>(e =>
        {
            e.Property(x => x.Titulo).HasMaxLength(160);
            e.Property(x => x.Slug).HasMaxLength(80);
            e.Property(x => x.Subtitulo).HasMaxLength(200);
            e.Property(x => x.Descricao).HasMaxLength(8000);
            e.Property(x => x.Local).HasMaxLength(160);
            e.Property(x => x.Endereco).HasMaxLength(250);
            e.Property(x => x.Bairro).HasMaxLength(80);
            e.Property(x => x.Cidade).HasMaxLength(80);
            e.Property(x => x.Uf).HasMaxLength(2);
            e.Property(x => x.Cep).HasMaxLength(9);
            e.Property(x => x.LinkOnline).HasMaxLength(300);
            e.Property(x => x.Classificacao).HasMaxLength(10);
            e.Property(x => x.Imagem).HasMaxLength(200);
            e.HasIndex(x => new { x.Status, x.InicioEm });
            e.HasIndex(x => x.Cidade);
            e.HasMany(x => x.Espacos).WithOne(x => x.Evento!).HasForeignKey(x => x.EventoId).OnDelete(DeleteBehavior.Cascade);
            e.Ignore(x => x.Presencial);
            e.Ignore(x => x.CidadeUf);
            e.Property(x => x.Cor).HasMaxLength(7);
            e.Property(x => x.InstrucoesPagamento).HasMaxLength(2000);
            e.HasIndex(x => x.Slug).IsUnique();
            e.HasIndex(x => x.OrganizadorId);
            e.HasOne(x => x.Organizador).WithMany().HasForeignKey(x => x.OrganizadorId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Tipos).WithOne(x => x.Evento!).HasForeignKey(x => x.EventoId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Inscricoes).WithOne(x => x.Evento!).HasForeignKey(x => x.EventoId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Cupons).WithOne(x => x.Evento!).HasForeignKey(x => x.EventoId).OnDelete(DeleteBehavior.Cascade);
            e.Ignore(x => x.Visivel);
        });

        b.Entity<TipoIngresso>(e =>
        {
            e.Property(x => x.Nome).HasMaxLength(100);
            e.Property(x => x.Descricao).HasMaxLength(300);
            e.Property(x => x.Preco).HasPrecision(10, 2);
            e.Ignore(x => x.Gratuito);
            e.HasOne(x => x.Espaco).WithMany().HasForeignKey(x => x.EspacoId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<Inscricao>(e =>
        {
            e.Property(x => x.Nome).HasMaxLength(160);
            e.Property(x => x.Email).HasMaxLength(180);
            e.Property(x => x.Telefone).HasMaxLength(15);
            e.Property(x => x.Codigo).HasMaxLength(12);
            e.Property(x => x.Valor).HasPrecision(10, 2);
            e.Property(x => x.Desconto).HasPrecision(10, 2);
            e.Property(x => x.FormaPagamento).HasMaxLength(40);
            e.Property(x => x.Canal).HasMaxLength(40);
            e.Property(x => x.CieloLinkId).HasMaxLength(64);
            e.Property(x => x.CieloLinkUrl).HasMaxLength(300);
            e.Property(x => x.CieloPedido).HasMaxLength(64);
            e.HasIndex(x => x.CieloLinkId);
            e.HasIndex(x => x.CieloPedido);
            e.HasOne(x => x.Cupom).WithMany().HasForeignKey(x => x.CupomId).OnDelete(DeleteBehavior.SetNull);
            e.Property(x => x.Observacao).HasMaxLength(500);
            e.HasIndex(x => x.Codigo).IsUnique();
            e.HasIndex(x => new { x.EventoId, x.Status });
            e.HasIndex(x => new { x.EventoId, x.Email });
            e.HasOne(x => x.TipoIngresso).WithMany().HasForeignKey(x => x.TipoIngressoId).OnDelete(DeleteBehavior.Restrict);
            e.Ignore(x => x.Ativa);
        });

        b.Entity<EspacoEvento>(e =>
        {
            e.ToTable("Espacos");
            e.Property(x => x.Nome).HasMaxLength(80);
            e.Property(x => x.Descricao).HasMaxLength(200);
        });

        b.Entity<Cupom>(e =>
        {
            e.Property(x => x.Codigo).HasMaxLength(30);
            e.Property(x => x.Valor).HasPrecision(10, 2);
            e.HasIndex(x => new { x.EventoId, x.Codigo }).IsUnique();
        });

        b.Entity<VisitaEvento>(e =>
        {
            e.ToTable("Visitas");
            e.Property(x => x.Canal).HasMaxLength(40);
            e.Property(x => x.Dia).HasColumnType("date");
            e.HasIndex(x => new { x.EventoId, x.Canal, x.Dia }).IsUnique();
            e.HasOne<Evento>().WithMany().HasForeignKey(x => x.EventoId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
