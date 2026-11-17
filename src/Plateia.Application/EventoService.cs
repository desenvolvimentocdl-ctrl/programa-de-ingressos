using Plateia.Domain;
using Microsoft.EntityFrameworkCore;

namespace Plateia.Application;

public record DadosTipo(int? Id, string Nome, string? Descricao, decimal Preco, int? Quantidade, bool Remover, DateTime? VendasAteUtc = null);

public record DadosEvento(
    string Titulo, string? Subtitulo, string? Descricao, string Local, string? Endereco,
    DateTime InicioUtc, DateTime? FimUtc, int Capacidade, string Cor, StatusEvento Status,
    bool InscricoesAbertas, string? InstrucoesPagamento, int? OrganizadorId, List<DadosTipo> Tipos);

public class EventoService(IAppDbContext db)
{
    public IQueryable<Evento> Visiveis(UsuarioAtual eu) =>
        eu.Admin ? db.Eventos : db.Eventos.Where(e => e.OrganizadorId == eu.Id);

    public Task<Evento?> ObterAsync(int id, UsuarioAtual eu) =>
        Visiveis(eu).Include(e => e.Tipos.OrderBy(t => t.Ordem)).Include(e => e.Organizador)
            .FirstOrDefaultAsync(e => e.Id == id);

    public async Task<Evento> SalvarAsync(int? id, DadosEvento d, UsuarioAtual eu)
    {
        var titulo = Utilidades.Limpo(d.Titulo, 160) ?? throw new RegraNegocioException("Informe o nome do evento.");
        var local = Utilidades.Limpo(d.Local, 160) ?? throw new RegraNegocioException("Informe o local do evento.");
        if (d.Capacidade < 1 || d.Capacidade > 100_000) throw new RegraNegocioException("A capacidade precisa ser entre 1 e 100.000 lugares.");
        if (d.FimUtc is { } fim && fim <= d.InicioUtc) throw new RegraNegocioException("O término precisa ser depois do início.");
        if (!Utilidades.CorValida(d.Cor)) throw new RegraNegocioException("Escolha uma cor válida.");

        Evento e;
        if (id is null)
        {
            e = new Evento { Slug = await SlugLivreAsync(titulo) };
            db.Eventos.Add(e);
        }
        else
        {
            e = await ObterAsync(id.Value, eu) ?? throw new RegraNegocioException("Evento não encontrado.");
            var ativos = await db.Inscricoes.CountAsync(i => i.EventoId == e.Id && i.Status == StatusInscricao.Confirmada);
            if (d.Capacidade < ativos)
                throw new RegraNegocioException($"Já existem {ativos} inscrições confirmadas. A capacidade não pode ficar abaixo disso.");
        }

        e.Organizador = null;
        e.OrganizadorId = eu.Admin && d.OrganizadorId is { } org
            ? (await db.Usuarios.AnyAsync(u => u.Id == org && u.Ativo) ? org : throw new RegraNegocioException("Organizador inválido."))
            : (id is null ? eu.Id : e.OrganizadorId);

        e.Titulo = titulo;
        e.Subtitulo = Utilidades.Limpo(d.Subtitulo, 200);
        e.Descricao = Utilidades.Limpo(d.Descricao, 8000);
        e.Local = local;
        e.Endereco = Utilidades.Limpo(d.Endereco, 250);
        e.InicioEm = d.InicioUtc;
        e.FimEm = d.FimUtc;
        e.Capacidade = d.Capacidade;
        e.Cor = d.Cor.ToUpperInvariant();
        e.Status = d.Status;
        e.InscricoesAbertas = d.InscricoesAbertas;
        e.InstrucoesPagamento = Utilidades.Limpo(d.InstrucoesPagamento, 2000);

        await AplicarTiposAsync(e, d.Tipos);
        if (!e.Tipos.Any(t => t.Ativo)) throw new RegraNegocioException("Cadastre pelo menos um tipo de ingresso.");

        await db.SaveChangesAsync();
        return e;
    }

    async Task AplicarTiposAsync(Evento e, List<DadosTipo> tipos)
    {
        var vendidos = e.Id == 0
            ? new Dictionary<int, int>()
            : await db.Inscricoes.Where(i => i.EventoId == e.Id && i.Status == StatusInscricao.Confirmada)
                .GroupBy(i => i.TipoIngressoId).Select(g => new { g.Key, N = g.Count() })
                .ToDictionaryAsync(x => x.Key, x => x.N);
        var usados = e.Id == 0
            ? new HashSet<int>()
            : (await db.Inscricoes.Where(i => i.EventoId == e.Id).Select(i => i.TipoIngressoId).Distinct().ToListAsync()).ToHashSet();

        var ordem = 0;
        foreach (var t in tipos)
        {
            var existente = t.Id is { } tid ? e.Tipos.FirstOrDefault(x => x.Id == tid) : null;
            if (t.Remover)
            {
                if (existente is null) continue;
                if (usados.Contains(existente.Id)) existente.Ativo = false; // mantém o histórico de quem já se inscreveu
                else { e.Tipos.Remove(existente); db.TiposIngresso.Remove(existente); }
                continue;
            }
            var nome = Utilidades.Limpo(t.Nome, 100);
            if (nome is null) continue;
            if (t.Preco < 0) throw new RegraNegocioException($"O preço de \"{nome}\" não pode ser negativo.");
            if (t.Quantidade is < 1) throw new RegraNegocioException($"A quantidade de \"{nome}\" precisa ser maior que zero (ou deixe em branco).");

            var tipo = existente ?? new TipoIngresso();
            if (existente is not null && t.Quantidade is { } q && vendidos.TryGetValue(existente.Id, out var v) && q < v)
                throw new RegraNegocioException($"\"{nome}\" já tem {v} inscrições; a quantidade não pode ficar abaixo disso.");
            tipo.Nome = nome;
            tipo.Descricao = Utilidades.Limpo(t.Descricao, 300);
            tipo.Preco = Math.Round(t.Preco, 2);
            tipo.Quantidade = t.Quantidade;
            tipo.VendasAte = t.VendasAteUtc;
            tipo.Ordem = ordem++;
            tipo.Ativo = true;
            if (existente is null) e.Tipos.Add(tipo);
        }
    }

    /// <summary>Cria uma cópia em rascunho (mesmos dados e ingressos, sem inscrições), para eventos que se repetem.</summary>
    public async Task<Evento> DuplicarAsync(int id, UsuarioAtual eu)
    {
        var o = await ObterAsync(id, eu) ?? throw new RegraNegocioException("Evento não encontrado.");
        var titulo = o.Titulo.Length > 150 ? o.Titulo[..150] : o.Titulo;
        var e = new Evento
        {
            Titulo = titulo + " (cópia)", Slug = await SlugLivreAsync(titulo), Subtitulo = o.Subtitulo, Descricao = o.Descricao,
            Local = o.Local, Endereco = o.Endereco, InicioEm = o.InicioEm, FimEm = o.FimEm, Capacidade = o.Capacidade, Cor = o.Cor,
            Status = StatusEvento.Rascunho, InscricoesAbertas = true, InstrucoesPagamento = o.InstrucoesPagamento, OrganizadorId = o.OrganizadorId,
            Tipos = o.Tipos.Where(t => t.Ativo).OrderBy(t => t.Ordem)
                .Select(t => new TipoIngresso { Nome = t.Nome, Descricao = t.Descricao, Preco = t.Preco, Quantidade = t.Quantidade, Ordem = t.Ordem }).ToList()
        };
        db.Eventos.Add(e);
        await db.SaveChangesAsync();
        return e;
    }

    /// <summary>Só exclui eventos sem nenhuma inscrição; com inscrições, o caminho é marcar como Cancelado.</summary>
    public async Task ExcluirAsync(int id, UsuarioAtual eu)
    {
        var e = await ObterAsync(id, eu) ?? throw new RegraNegocioException("Evento não encontrado.");
        if (await db.Inscricoes.AnyAsync(i => i.EventoId == id))
            throw new RegraNegocioException("Este evento já tem inscrições. Em vez de excluir, marque como Cancelado.");
        db.Eventos.Remove(e);
        await db.SaveChangesAsync();
    }

    async Task<string> SlugLivreAsync(string titulo)
    {
        var baseSlug = Utilidades.Slug(titulo);
        var slug = baseSlug;
        for (var n = 2; await db.Eventos.AnyAsync(x => x.Slug == slug); n++) slug = $"{baseSlug}-{n}";
        return slug;
    }
}
