using Plateia.Application;
using Plateia.Domain;
using Texto_ = Plateia.Web.Infra.Texto;

namespace Plateia.Web.ViewModels;

public record CartaoEvento(int Id, string Titulo, string Slug, string Local, DateTime InicioEm, int Capacidade, string Cor,
    StatusEvento Status, string Organizador, int Confirmados, int Pagos, int Pendentes, int Presentes);

public record OpcaoOrganizador(int Id, string Nome);

public class ListaParticipantes
{
    public required ResumoEvento Resumo { get; init; }
    public required List<Inscricao> Itens { get; init; }
    public required string Filtro { get; init; }
    public string? Busca { get; init; }
    public Evento Evento => Resumo.Evento;
}

public class PaginaEvento
{
    public required Evento Evento { get; init; }
    public required List<ResumoTipo> Tipos { get; init; }
    public required int Livres { get; init; }
    public required bool Previa { get; init; }
    public bool PagamentoOnline { get; init; }
    public bool TemCupons { get; init; }
    public List<ResumoEspaco> Espacos { get; init; } = [];
    public List<CartaoPublico> Relacionados { get; set; } = [];
    /// <summary>Canal de divulgação de onde a pessoa veio (vai junto na inscrição).</summary>
    public string? Canal { get; set; }
    public bool AceitaInscricoes => Evento.Status == StatusEvento.Publicado && Evento.InscricoesAbertas && Livres > 0;
}

/// <summary>Um ingresso do formulário de checkout. <see cref="Meu"/> = usa os dados de quem está comprando.</summary>
public class ParticipanteForm
{
    public int TipoId { get; set; }
    public string? Nome { get; set; }
    public string? Email { get; set; }
    public string? Telefone { get; set; }
    public bool Meu { get; set; }
    /// <summary>Meia-entrada (50%), quando o ingresso oferece.</summary>
    public bool Meia { get; set; }
}

public class PaginaMeusIngressos
{
    public required Usuario Usuario { get; init; }
    public required List<Inscricao> Itens { get; init; }
}

/// <summary>O que a pessoa já tinha preenchido, para devolver o formulário quando algo dá errado.</summary>
public record FormCheckout(string? Nome, string? Email, string? Telefone, string? Cupom, List<ParticipanteForm> Participantes);

public record OrganizadorLinha(int Id, string Nome, string Email, string? Telefone, Perfil Perfil, bool Ativo, int Eventos,
    bool Aprovado = true, string? NomePublico = null, string? Slug = null, string? Cidade = null);


public class PaginaDivulgacao
{
    public required Evento Evento { get; init; }
    public required string LinkBase { get; init; }
    public required List<LinhaFunil> Funil { get; init; }
    public required List<UsoCupom> Cupons { get; init; }
    public required int Confirmados { get; init; }
    public decimal MenorPreco { get; init; }
}

public class PaginaInicio
{
    public required List<CartaoPublico> Destaques { get; init; }
    public required List<CartaoPublico> EstaSemana { get; init; }
    public required List<CartaoPublico> Proximos { get; init; }
    public required List<CartaoPublico> Online { get; init; }
    public required Dictionary<CategoriaEvento, int> Categorias { get; init; }
    public required List<(string Cidade, int Total)> Cidades { get; init; }
    public required int Total { get; init; }
}

public class PaginaBusca
{
    public required List<CartaoPublico> Itens { get; init; }
    public string? Texto { get; init; }
    public CategoriaEvento? Categoria { get; init; }
    public string? Cidade { get; init; }
    public string? Quando { get; init; }
    public bool Online { get; init; }
    public bool Gratis { get; init; }
    public required Dictionary<CategoriaEvento, int> Categorias { get; init; }
    public required List<(string Cidade, int Total)> Cidades { get; init; }

    /// <summary>Monta o link da busca trocando um filtro e mantendo os outros.</summary>
    public string Link(string? texto = "\0", string? categoria = "\0", string? cidade = "\0", string? quando = "\0", bool? online = null, bool? gratis = null)
    {
        var p = new List<string>();
        void Add(string k, string? v) { if (!string.IsNullOrEmpty(v)) p.Add($"{k}={Uri.EscapeDataString(v)}"); }
        Add("q", texto == "\0" ? Texto : texto);
        Add("categoria", categoria == "\0" ? (Categoria is { } c ? Texto_.SlugCategoria(c) : null) : categoria);
        Add("cidade", cidade == "\0" ? Cidade : cidade);
        Add("quando", quando == "\0" ? Quando : quando);
        if (online ?? Online) p.Add("online=1");
        if (gratis ?? Gratis) p.Add("gratis=1");
        return "/eventos" + (p.Count > 0 ? "?" + string.Join("&", p) : "");
    }
}

public class PaginaProdutor
{
    public required Usuario Produtor { get; init; }
    public required List<CartaoPublico> Eventos { get; init; }
    public required int Realizados { get; init; }
}
