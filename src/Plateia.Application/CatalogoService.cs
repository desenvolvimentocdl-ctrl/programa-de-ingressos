using Plateia.Domain;
using Microsoft.EntityFrameworkCore;

namespace Plateia.Application;

public record FiltroBusca(string? Texto = null, CategoriaEvento? Categoria = null, string? Cidade = null,
    DateTime? DeUtc = null, DateTime? AteUtc = null, bool SoOnline = false, bool SoGratis = false);

public record CartaoPublico(string Slug, string Titulo, string? Subtitulo, string Local, string? Cidade, string? Uf,
    FormatoEvento Formato, CategoriaEvento Categoria, DateTime InicioEm, string Cor, string? Imagem,
    decimal APartirDe, bool Gratuito, int Livres, bool Abertas, string Produtor, string? ProdutorSlug)
{
    public bool Lotado => Livres <= 0;
}

/// <summary>Vitrine pública: o que aparece na página inicial, na busca e na página do produtor.</summary>
public class CatalogoService(IAppDbContext db)
{
    /// <summary>Menor preço pago à venda (ignora cortesias e lotes encerrados); 0 quando o evento é todo gratuito.</summary>
    public static decimal APartirDe(IEnumerable<TipoIngresso> tipos)
    {
        var aVenda = tipos.Where(t => t.Ativo && !t.VendaEncerrada(DateTime.UtcNow) && t.Preco > 0).Select(t => t.Preco).ToList();
        return aVenda.Count == 0 ? 0 : aVenda.Min();
    }

    /// <summary>Eventos publicados, ainda não terminados, de produtores aprovados e ativos.</summary>
    IQueryable<Evento> Publicos()
    {
        var limite = DateTime.UtcNow.AddHours(-3);
        return db.Eventos.Where(e => e.Status == StatusEvento.Publicado && (e.FimEm ?? e.InicioEm) >= limite
                                     && e.Organizador!.Aprovado && e.Organizador.Ativo);
    }

    async Task<List<CartaoPublico>> CartoesAsync(IQueryable<Evento> q, int max)
    {
        var linhas = await q.OrderBy(e => e.InicioEm).Take(max)
            .Select(e => new
            {
                e.Slug, e.Titulo, e.Subtitulo, e.Local, e.Cidade, e.Uf, e.Formato, e.Categoria, e.InicioEm, e.Cor, e.Imagem,
                e.InscricoesAbertas, e.Tipos,
                Livres = e.Capacidade - e.Inscricoes.Count(i => i.Status == StatusInscricao.Confirmada),
                Produtor = e.Organizador!.NomePublico ?? e.Organizador.Nome, ProdutorSlug = e.Organizador.Slug
            }).ToListAsync();
        return linhas.Select(e => new CartaoPublico(e.Slug, e.Titulo, e.Subtitulo, e.Local, e.Cidade, e.Uf, e.Formato, e.Categoria,
            e.InicioEm, e.Cor, e.Imagem, APartirDe(e.Tipos), APartirDe(e.Tipos) == 0, e.Livres, e.InscricoesAbertas, e.Produtor, e.ProdutorSlug)).ToList();
    }

    public Task<List<CartaoPublico>> BuscarAsync(FiltroBusca f, int max = 60)
    {
        var q = Publicos();
        if (Utilidades.Limpo(f.Texto, 80) is { } t)
            q = q.Where(e => e.Titulo.Contains(t) || (e.Subtitulo != null && e.Subtitulo.Contains(t)) || e.Local.Contains(t)
                             || (e.Cidade != null && e.Cidade.Contains(t)) || (e.Organizador!.NomePublico != null && e.Organizador.NomePublico.Contains(t)));
        if (f.Categoria is { } c) q = q.Where(e => e.Categoria == c);
        if (Utilidades.Limpo(f.Cidade, 80) is { } cid) q = q.Where(e => e.Cidade == cid || e.Formato == FormatoEvento.Online && cid == "Online");
        if (f.DeUtc is { } de) q = q.Where(e => (e.FimEm ?? e.InicioEm) >= de);
        if (f.AteUtc is { } ate) q = q.Where(e => e.InicioEm < ate);
        if (f.SoOnline) q = q.Where(e => e.Formato != FormatoEvento.Presencial);
        if (f.SoGratis) q = q.Where(e => !e.Tipos.Any(x => x.Ativo && x.Preco > 0));
        return CartoesAsync(q, max);
    }

    public Task<List<CartaoPublico>> DestaquesAsync(int max = 6) => CartoesAsync(Publicos().Where(e => e.Destaque), max);

    public Task<List<CartaoPublico>> DoProdutorAsync(int organizadorId, int max = 30) =>
        CartoesAsync(Publicos().Where(e => e.OrganizadorId == organizadorId), max);

    public Task<List<CartaoPublico>> RelacionadosAsync(Evento e, int max = 3) =>
        CartoesAsync(Publicos().Where(x => x.Id != e.Id && (x.Categoria == e.Categoria || x.OrganizadorId == e.OrganizadorId)), max);

    public async Task<List<(string Cidade, int Total)>> CidadesAsync() =>
        (await Publicos().Where(e => e.Cidade != null).GroupBy(e => e.Cidade!).Select(g => new { g.Key, N = g.Count() }).ToListAsync())
        .OrderByDescending(x => x.N).ThenBy(x => x.Key).Select(x => (x.Key, x.N)).ToList();

    public async Task<Dictionary<CategoriaEvento, int>> CategoriasAsync() =>
        await Publicos().GroupBy(e => e.Categoria).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N);

    public Task<int> TotalAsync() => Publicos().CountAsync();
}
