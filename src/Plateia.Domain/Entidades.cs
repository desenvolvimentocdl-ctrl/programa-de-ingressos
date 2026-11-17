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
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
}

/// <summary>Rascunho não aparece para o público; Publicado aceita inscrições (se abertas); Encerrado e Cancelado só mostram a página.</summary>
public enum StatusEvento { Rascunho = 0, Publicado = 1, Encerrado = 2, Cancelado = 3 }

public class Evento
{
    public int Id { get; set; }
    public string Titulo { get; set; } = "";
    public string Slug { get; set; } = "";
    public string? Subtitulo { get; set; }
    public string? Descricao { get; set; }
    public string Local { get; set; } = "";
    public string? Endereco { get; set; }
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

    public bool Visivel => Status != StatusEvento.Rascunho;
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

    public bool Gratuito => Preco <= 0;
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
    public string? FormaPagamento { get; set; }
    public DateTime? PagoEm { get; set; }
    public DateTime? CheckinEm { get; set; }
    public DateTime? CanceladaEm { get; set; }
    public OrigemInscricao Origem { get; set; }
    public string? Observacao { get; set; }
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;

    public bool Ativa => Status == StatusInscricao.Confirmada;
}
