using Plateia.Domain;
using Microsoft.EntityFrameworkCore;

namespace Plateia.Application;

public record DadosTipo(int? Id, string Nome, string? Descricao, decimal Preco, int? Quantidade, bool Remover,
    DateTime? VendasAteUtc = null, string? EspacoChave = null);

/// <summary>Chave liga o espaço aos tipos no formulário: "e12" para um espaço já salvo, "n0", "n1"… para novos.</summary>
public record DadosEspaco(string Chave, int? Id, string Nome, string? Descricao, int Capacidade, bool Remover);

public record LocalEvento(FormatoEvento Formato, string? Local, string? Endereco, string? Bairro, string? Cidade, string? Uf, string? Cep, string? LinkOnline);

public record DadosEvento(
    string Titulo, string? Subtitulo, string? Descricao, CategoriaEvento Categoria, LocalEvento Onde, string? Classificacao,
    DateTime InicioUtc, DateTime? FimUtc, int Capacidade, string Cor, StatusEvento Status,
    bool InscricoesAbertas, string? InstrucoesPagamento, int? OrganizadorId, List<DadosEspaco> Espacos, List<DadosTipo> Tipos,
    bool? Destaque = null);

public class EventoService(IAppDbContext db)
{
    public static readonly string[] Classificacoes = ["Livre", "10+", "12+", "14+", "16+", "18+"];

    public IQueryable<Evento> Visiveis(UsuarioAtual eu) =>
        eu.Admin ? db.Eventos : db.Eventos.Where(e => e.OrganizadorId == eu.Id);

    public Task<Evento?> ObterAsync(int id, UsuarioAtual eu) =>
        Visiveis(eu).Include(e => e.Tipos.OrderBy(t => t.Ordem)).Include(e => e.Espacos.OrderBy(x => x.Ordem)).Include(e => e.Organizador)
            .FirstOrDefaultAsync(e => e.Id == id);

    public async Task<Evento> SalvarAsync(int? id, DadosEvento d, UsuarioAtual eu)
    {
        var titulo = Utilidades.Limpo(d.Titulo, 160) ?? throw new RegraNegocioException("Informe o nome do evento.");
        if (d.FimUtc is { } fim && fim <= d.InicioUtc) throw new RegraNegocioException("O término precisa ser depois do início.");
        if (!Utilidades.CorValida(d.Cor)) throw new RegraNegocioException("Escolha uma cor válida.");
        var o = d.Onde;
        var link = Utilidades.Limpo(o.LinkOnline, 300);
        if (link is not null && !(Uri.TryCreate(link, UriKind.Absolute, out var u) && u.Scheme is "http" or "https"))
            throw new RegraNegocioException("O link da transmissão precisa começar com https://");
        var local = Utilidades.Limpo(o.Local, 160) ?? (o.Formato == FormatoEvento.Online ? "Online" : throw new RegraNegocioException("Informe o nome do local."));
        var cidade = Utilidades.Limpo(o.Cidade, 80);
        var uf = Utilidades.Limpo(o.Uf, 2)?.ToUpperInvariant();
        if (o.Formato != FormatoEvento.Online && (cidade is null || uf is not { Length: 2 }))
            throw new RegraNegocioException("Informe a cidade e a UF do local (ex.: Uberlândia / MG).");

        Evento e;
        if (id is null)
        {
            e = new Evento { Slug = await SlugLivreAsync(titulo) };
            db.Eventos.Add(e);
        }
        else
            e = await ObterAsync(id.Value, eu) ?? throw new RegraNegocioException("Evento não encontrado.");

        var org = eu.Admin && d.OrganizadorId is { } oid
            ? (await db.Usuarios.AnyAsync(x => x.Id == oid && x.Ativo) ? oid : throw new RegraNegocioException("Organizador inválido."))
            : (id is null ? eu.Id : e.OrganizadorId);
        if (d.Status == StatusEvento.Publicado && e.Status != StatusEvento.Publicado && !eu.Admin
            && !await db.Usuarios.AnyAsync(x => x.Id == org && x.Aprovado))
            throw new RegraNegocioException("Sua conta de produtor ainda está em análise. Salve como rascunho; assim que for aprovada, você publica.");
        e.Organizador = null;
        e.OrganizadorId = org;

        e.Titulo = titulo;
        e.Subtitulo = Utilidades.Limpo(d.Subtitulo, 200);
        e.Descricao = Utilidades.Limpo(d.Descricao, 8000);
        e.Categoria = d.Categoria;
        e.Formato = o.Formato;
        e.Local = local;
        e.Endereco = o.Formato == FormatoEvento.Online ? null : Utilidades.Limpo(o.Endereco, 250);
        e.Bairro = o.Formato == FormatoEvento.Online ? null : Utilidades.Limpo(o.Bairro, 80);
        e.Cidade = o.Formato == FormatoEvento.Online ? null : cidade;
        e.Uf = o.Formato == FormatoEvento.Online ? null : uf;
        e.Cep = o.Formato == FormatoEvento.Online ? null : Utilidades.Limpo(o.Cep, 9);
        e.LinkOnline = o.Formato == FormatoEvento.Presencial ? null : link;
        e.Classificacao = Classificacoes.Contains(d.Classificacao) ? d.Classificacao : null;
        e.InicioEm = d.InicioUtc;
        e.FimEm = d.FimUtc;
        e.Cor = d.Cor.ToUpperInvariant();
        e.Status = d.Status;
        e.InscricoesAbertas = d.InscricoesAbertas;
        e.InstrucoesPagamento = Utilidades.Limpo(d.InstrucoesPagamento, 2000);
        if (eu.Admin && d.Destaque is { } dest) e.Destaque = dest;

        var espacos = await AplicarEspacosAsync(e, d.Espacos);
        // Com espaços, a lotação do evento é a soma deles; sem espaços, vale a capacidade informada.
        e.Capacidade = e.Espacos.Count > 0 ? e.Espacos.Sum(x => x.Capacidade) : d.Capacidade;
        if (e.Capacidade < 1 || e.Capacidade > 100_000) throw new RegraNegocioException("A capacidade precisa ser entre 1 e 100.000 lugares.");
        if (e.Id != 0)
        {
            var ativos = await db.Inscricoes.CountAsync(i => i.EventoId == e.Id && i.Status == StatusInscricao.Confirmada);
            if (e.Capacidade < ativos)
                throw new RegraNegocioException($"Já existem {ativos} inscrições confirmadas. A capacidade não pode ficar abaixo disso.");
        }

        await AplicarTiposAsync(e, d.Tipos, espacos);
        if (!e.Tipos.Any(t => t.Ativo)) throw new RegraNegocioException("Cadastre pelo menos um tipo de ingresso.");

        await db.SaveChangesAsync();
        return e;
    }

    async Task<Dictionary<string, EspacoEvento>> AplicarEspacosAsync(Evento e, List<DadosEspaco> lista)
    {
        var mapa = new Dictionary<string, EspacoEvento>();
        var ocupados = e.Id == 0
            ? new Dictionary<int, int>()
            : await db.Inscricoes.Where(i => i.EventoId == e.Id && i.Status == StatusInscricao.Confirmada && i.TipoIngresso!.EspacoId != null)
                .GroupBy(i => i.TipoIngresso!.EspacoId!.Value).Select(g => new { g.Key, N = g.Count() })
                .ToDictionaryAsync(x => x.Key, x => x.N);
        var ordem = 0;
        foreach (var x in lista)
        {
            var existente = x.Id is { } eid ? e.Espacos.FirstOrDefault(s => s.Id == eid) : null;
            var nome = Utilidades.Limpo(x.Nome, 80);
            if (x.Remover || nome is null)
            {
                if (existente is null) continue;
                if (ocupados.GetValueOrDefault(existente.Id) > 0)
                    throw new RegraNegocioException($"O espaço \"{existente.Nome}\" já tem inscrições e não pode ser removido.");
                foreach (var t in e.Tipos.Where(t => t.EspacoId == existente.Id)) { t.EspacoId = null; t.Espaco = null; }
                e.Espacos.Remove(existente);
                db.Espacos.Remove(existente);
                continue;
            }
            if (x.Capacidade < 1) throw new RegraNegocioException($"Informe quantos lugares tem \"{nome}\".");
            if (existente is not null && ocupados.TryGetValue(existente.Id, out var n) && x.Capacidade < n)
                throw new RegraNegocioException($"\"{nome}\" já tem {n} inscrições; a lotação não pode ficar abaixo disso.");
            var esp = existente ?? new EspacoEvento();
            esp.Nome = nome;
            esp.Descricao = Utilidades.Limpo(x.Descricao, 200);
            esp.Capacidade = x.Capacidade;
            esp.Ordem = ordem++;
            if (existente is null) e.Espacos.Add(esp);
            mapa[x.Chave] = esp;
        }
        return mapa;
    }

    async Task AplicarTiposAsync(Evento e, List<DadosTipo> tipos, Dictionary<string, EspacoEvento> espacos)
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
            // Com espaços cadastrados, todo tipo pertence a um (se não escolheu, vai para o primeiro).
            var esp = t.EspacoChave is { } ch && espacos.TryGetValue(ch, out var achado) ? achado : espacos.Values.FirstOrDefault();
            tipo.Espaco = esp;
            if (esp is null) tipo.EspacoId = null;
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
            Local = o.Local, Endereco = o.Endereco, Bairro = o.Bairro, Cidade = o.Cidade, Uf = o.Uf, Cep = o.Cep, LinkOnline = o.LinkOnline,
            Categoria = o.Categoria, Formato = o.Formato, Classificacao = o.Classificacao, Imagem = o.Imagem,
            InicioEm = o.InicioEm, FimEm = o.FimEm, Capacidade = o.Capacidade, Cor = o.Cor,
            Status = StatusEvento.Rascunho, InscricoesAbertas = true, InstrucoesPagamento = o.InstrucoesPagamento, OrganizadorId = o.OrganizadorId,
        };
        var copias = o.Espacos.ToDictionary(x => x.Id, x => new EspacoEvento { Nome = x.Nome, Descricao = x.Descricao, Capacidade = x.Capacidade, Ordem = x.Ordem });
        e.Espacos = copias.Values.ToList();
        e.Tipos = o.Tipos.Where(t => t.Ativo).OrderBy(t => t.Ordem)
            .Select(t => new TipoIngresso { Nome = t.Nome, Descricao = t.Descricao, Preco = t.Preco, Quantidade = t.Quantidade, Ordem = t.Ordem,
                Espaco = t.EspacoId is { } eid && copias.TryGetValue(eid, out var c) ? c : null }).ToList();
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
