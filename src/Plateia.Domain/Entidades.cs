namespace Plateia.Domain;

/// <summary>Organizador (palestrante ou representante) vê só os próprios eventos; Administrador vê tudo e cadastra organizadores.</summary>
public enum Perfil { Organizador = 0, Administrador = 1 }

public class Usuario
{
    public int Id { get; set; }
    public string Nome { get; set; } = "";
    public string Email { get; set; } = "";
    public string? Telefone { get; set; }
    public string SenhaHash { get; set; } = "";
    public Perfil Perfil { get; set; }
    public bool Ativo { get; set; } = true;
    /// <summary>Produtores que se cadastram sozinhos só publicam eventos depois da aprovação do administrador.</summary>
    public bool Aprovado { get; set; } = true;
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;

    // Perfil público do produtor (página /p/{slug}).
    public string? NomePublico { get; set; }
    public string? Slug { get; set; }
    public string? Bio { get; set; }
    public string? Instagram { get; set; }
    public string? Site { get; set; }
    public string? Cidade { get; set; }

    public string NomeExibicao => string.IsNullOrWhiteSpace(NomePublico) ? Nome : NomePublico!;
}

public enum CategoriaEvento
{
    Negocios = 0, Cursos = 1, Tecnologia = 2, Marketing = 3, Saude = 4, Gastronomia = 5,
    Musica = 6, ArteCultura = 7, Esportes = 8, Infantil = 9, Networking = 10, Outros = 99
}

public enum FormatoEvento { Presencial = 0, Online = 1, Hibrido = 2 }

/// <summary>Rascunho não aparece para o público; Publicado aceita inscrições (se abertas); Encerrado e Cancelado só mostram a página.</summary>
public enum StatusEvento { Rascunho = 0, Publicado = 1, Encerrado = 2, Cancelado = 3 }

public class Evento
{
    public int Id { get; set; }
    public string Titulo { get; set; } = "";
    public string Slug { get; set; } = "";
    public string? Subtitulo { get; set; }
    public string? Descricao { get; set; }
    public CategoriaEvento Categoria { get; set; } = CategoriaEvento.Negocios;
    public FormatoEvento Formato { get; set; }
    /// <summary>Nome do lugar (ex.: "Teatro Municipal") ou "Online".</summary>
    public string Local { get; set; } = "";
    public string? Endereco { get; set; }
    public string? Bairro { get; set; }
    public string? Cidade { get; set; }
    public string? Uf { get; set; }
    public string? Cep { get; set; }
    /// <summary>Link da transmissão (só aparece no ingresso de quem está confirmado e em dia).</summary>
    public string? LinkOnline { get; set; }
    /// <summary>"Livre", "12+", "16+", "18+".</summary>
    public string? Classificacao { get; set; }
    /// <summary>Caminho da imagem de capa enviada pelo produtor (vazio = capa gerada com a cor).</summary>
    public string? Imagem { get; set; }
    /// <summary>Aparece em destaque na página inicial (decisão do administrador).</summary>
    public bool Destaque { get; set; }
    /// <summary>Gravado em UTC.</summary>
    public DateTime InicioEm { get; set; }
    public DateTime? FimEm { get; set; }
    public int Capacidade { get; set; }
    /// <summary>Cor de destaque do evento (#RRGGBB), usada na página pública e no ingresso.</summary>
    public string Cor { get; set; } = "#FF6B4A";
    public StatusEvento Status { get; set; }
    public bool InscricoesAbertas { get; set; } = true;
    /// <summary>Texto livre mostrado no ingresso enquanto o pagamento está pendente (ex.: chave PIX).</summary>
    public string? InstrucoesPagamento { get; set; }
    public int OrganizadorId { get; set; }
    public Usuario? Organizador { get; set; }
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;

    public List<TipoIngresso> Tipos { get; set; } = new();
    public List<Inscricao> Inscricoes { get; set; } = new();
    public List<Cupom> Cupons { get; set; } = new();
    public List<EspacoEvento> Espacos { get; set; } = new();

    public bool Visivel => Status != StatusEvento.Rascunho;
    public bool Presencial => Formato != FormatoEvento.Online;
    public string CidadeUf => string.Join("/", new[] { Cidade, Uf }.Where(x => !string.IsNullOrWhiteSpace(x)));
}

/// <summary>
/// Espaço do evento definido pelo produtor (ex.: "Plateia", "Mezanino", "Sala de workshop", "Área VIP"),
/// cada um com a sua lotação. Os tipos de ingresso ficam ligados a um espaço.
/// </summary>
public class EspacoEvento
{
    public int Id { get; set; }
    public int EventoId { get; set; }
    public Evento? Evento { get; set; }
    public string Nome { get; set; } = "";
    public string? Descricao { get; set; }
    public int Capacidade { get; set; }
    public int Ordem { get; set; }
}

public class TipoIngresso
{
    public int Id { get; set; }
    public int EventoId { get; set; }
    public Evento? Evento { get; set; }
    public string Nome { get; set; } = "";
    public string? Descricao { get; set; }
    public decimal Preco { get; set; }
    /// <summary>Limite próprio deste tipo; vazio = só vale a capacidade do evento.</summary>
    public int? Quantidade { get; set; }
    public int Ordem { get; set; }
    public bool Ativo { get; set; } = true;
    public int? EspacoId { get; set; }
    public EspacoEvento? Espaco { get; set; }
    /// <summary>Fim das vendas deste tipo (ex.: "1º lote até 10/12"). Gravado em UTC; vazio = até o evento.</summary>
    public DateTime? VendasAte { get; set; }

    public bool Gratuito => Preco <= 0;
    public bool VendaEncerrada(DateTime agoraUtc) => VendasAte is { } v && agoraUtc > v;
}

public enum StatusInscricao { Confirmada = 0, Cancelada = 1 }

/// <summary>Cortesia = ingresso gratuito ou liberado sem cobrança.</summary>
public enum StatusPagamento { Pendente = 0, Pago = 1, Cortesia = 2 }

public enum OrigemInscricao { Site = 0, Manual = 1 }

public class Inscricao
{
    public int Id { get; set; }
    public int EventoId { get; set; }
    public Evento? Evento { get; set; }
    public int TipoIngressoId { get; set; }
    public TipoIngresso? TipoIngresso { get; set; }
    public string Nome { get; set; } = "";
    public string Email { get; set; } = "";
    public string? Telefone { get; set; }
    /// <summary>Código do ingresso (vai no QR). Aleatório e difícil de adivinhar.</summary>
    public string Codigo { get; set; } = "";
    public StatusInscricao Status { get; set; }
    public StatusPagamento Pagamento { get; set; }
    /// <summary>Preço do tipo de ingresso no momento da inscrição.</summary>
    public decimal Valor { get; set; }
    /// <summary>Desconto aplicado por cupom (Valor já é o preço final).</summary>
    public decimal Desconto { get; set; }
    public int? CupomId { get; set; }
    public Cupom? Cupom { get; set; }
    public string? FormaPagamento { get; set; }
    public DateTime? PagoEm { get; set; }
    public DateTime? CheckinEm { get; set; }
    public DateTime? CanceladaEm { get; set; }
    public OrigemInscricao Origem { get; set; }
    /// <summary>Canal de divulgação de onde a pessoa veio (instagram, whatsapp…), pelo link rastreável.</summary>
    public string? Canal { get; set; }

    // Pagamento online pela Cielo (Link de Pagamento).
    public string? CieloLinkId { get; set; }
    public string? CieloLinkUrl { get; set; }
    /// <summary>Número do pedido na Cielo (checkout_cielo_order_number), quando a pessoa conclui o pagamento.</summary>
    public string? CieloPedido { get; set; }
    public DateTime? CieloConsultadoEm { get; set; }
    public string? Observacao { get; set; }
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;

    public bool Ativa => Status == StatusInscricao.Confirmada;
}

public enum TipoDesconto { Percentual = 0, Valor = 1 }

/// <summary>Cupom de desconto de um evento (ex.: parceiros, divulgação, equipe).</summary>
public class Cupom
{
    public int Id { get; set; }
    public int EventoId { get; set; }
    public Evento? Evento { get; set; }
    public string Codigo { get; set; } = "";
    public TipoDesconto Tipo { get; set; }
    /// <summary>Percentual (0–100) ou valor em reais.</summary>
    public decimal Valor { get; set; }
    /// <summary>Quantas inscrições podem usar; vazio = sem limite.</summary>
    public int? Limite { get; set; }
    public DateTime? ValidoAte { get; set; }
    public bool Ativo { get; set; } = true;
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;

    public decimal DescontoSobre(decimal preco) =>
        Math.Min(preco, Math.Round(Tipo == TipoDesconto.Percentual ? preco * Valor / 100m : Valor, 2));
}

/// <summary>Visitas à página pública por canal e por dia (para medir a divulgação).</summary>
public class VisitaEvento
{
    public int Id { get; set; }
    public int EventoId { get; set; }
    public string Canal { get; set; } = "";
    public DateTime Dia { get; set; }
    public int Visitas { get; set; }
}
