# Plateia

Plataforma de eventos e ingressos: **qualquer produtor** (palestrante, empresa, coletivo, produtora) cria a conta, monta o evento
com o próprio local e espaços, vende ingressos com pagamento pela **Cielo**, divulga e faz o check-in. O público encontra os
eventos numa vitrine com busca por cidade, data e categoria e recebe o ingresso com QR code na hora.

Mesma tecnologia dos outros sistemas: **C# / .NET 8, ASP.NET Core MVC, Entity Framework Core 8 e MySQL 8 (Pomelo)**. CSS e JS sem bibliotecas.

![Vitrine](docs/telas/30-inicio.png)

## Ver funcionando agora (eventos de teste)

Com Docker instalado:

```bash
docker compose up --build
```

Abra **http://localhost:8080**. O banco já vem com produtores, eventos, inscrições e pagamentos **fictícios**:

| Acesso | E-mail | Senha |
|---|---|---|
| Administrador (aprova produtores, vê tudo) | `admin@plateia.local` | `admin12345` |
| Produtora "Marina Duarte Treinamentos" | `demo@plateia.local` | `demo12345` |
| Outros produtores de teste | `fermento@exemplo.com`, `hub@exemplo.com`, `movimento@exemplo.com`, `palco@exemplo.com` | `demo12345` |
| Produtor aguardando aprovação | `atelie@exemplo.com` | `demo12345` |

Sem Docker: rode como em "Rodar localmente" com `Demo__Criar=true` e um banco vazio.
Para recomeçar do zero: `docker compose down -v`.

## O que já faz

| Área | Funções |
|---|---|
| **Vitrine pública** `/` | Busca (o quê, onde, quando), categorias, eventos em destaque, "nesta semana", por cidade, online; filtros por data, categoria, cidade, online e gratuitos em `/eventos`. |
| **Produtores** | Cadastro próprio em `/criar-conta` (fica "em análise" até o administrador aprovar; antes disso só cria rascunhos). Perfil público em `/p/{produtor}` com bio, Instagram, site e eventos. |
| **Local e espaços** | Presencial, online ou híbrido; nome do local, endereço, bairro, cidade/UF e link "Como chegar"; link da transmissão (só no ingresso de quem está em dia). Espaços com lotação própria (ex.: Pista 150, Mesas 48) e cada ingresso ligado a um espaço. Capa com imagem enviada pelo produtor (ou capa gerada com a cor). Categoria e classificação etária. |
| **Meus eventos** | Cartões no formato de ingresso com capacidade, confirmados, pagos e a receber; filtros Próximos / Rascunhos / Passados; duplicar evento (para edições que se repetem) e excluir (só sem inscrições). |
| **Venda (Cielo)** | Cada inscrição paga ganha um Link de Pagamento da Cielo. A pessoa paga na página segura da Cielo (crédito, débito ou Pix, conforme a conta) e o ingresso vira "Pago" sozinho pela notificação. Nenhum dado de cartão passa pelo Plateia. |
| **Checkout** | Um pedido com até 10 ingressos do mesmo evento (vários tipos), escolhidos com + e −. Quem compra informa os próprios dados e o nome de cada participante (e-mail e WhatsApp dos outros são opcionais); cada participante ganha o próprio ingresso com QR code. Um pagamento só pelo total do pedido (um Link de Pagamento da Cielo); página do pedido em `/pedido/{código}` com todos os ingressos. |
| **Entrar com Google, Apple e Microsoft** | Produtores criam conta e entram com Google (Gmail), Apple (iCloud) ou Microsoft (Outlook/Hotmail), além de e-mail e senha. Contas novas seguem "em análise". Em "Minha conta": conectar/desconectar provedores e criar senha. Cada provedor só aparece quando configurado (veja `.env.exemplo`). |
| **Contas do público** | "Cadastre-se" no menu com Google, Apple (iCloud), Microsoft (Outlook/Hotmail) ou e-mail e senha. "Meus ingressos" com tudo o que a pessoa comprou ou recebeu (exige e-mail confirmado por código). "Quero vender ingressos" ativa a área de produtor na mesma conta. |
| **Esqueci minha senha** | Código de 6 dígitos por e-mail (vale 15 min, 5 tentativas, um por vez). Mesmo código confirma o e-mail das contas novas. SMTP em `Email__*`. |
| **Página de checkout** | Depois de escolher os ingressos, a pessoa vai para `/pedido/{código}/checkout`: cartão de crédito digitado no próprio site (Silent Order Post da Cielo: número, validade e CVV vão direto do navegador para a Cielo; o Plateia só recebe um token), com parcelamento, ou Pix com QR Code e copia e cola (a página muda sozinha quando o Pix cai). |
| **Meia-entrada e lotes** | O produtor marca quais ingressos têm meia (50%); o comprador escolhe inteira ou meia para cada participante. Virada de lote automática: um ingresso pode "abrir quando o de cima esgotar ou encerrar", além de início e fim das vendas por data. |
| **Lotes e cupons** | Vários tipos de ingresso com data limite de venda ("1º lote até…"); cupons de desconto em % ou R$, com limite de usos e validade. |
| **Divulgação** | Links rastreáveis por canal (Instagram, WhatsApp, e-mail, cartaz…); resultado de cada canal (visitas → inscrições → pagos); botões de compartilhar e mensagem pronta; artes para stories e feed geradas na hora (PNG); QR code da página para cartazes (SVG). Agenda pública em `/agenda` com todos os eventos abertos. |
| **Painel do evento** | Grade de assentos (pago, pendente, cortesia, presente, livre); anel de pagamentos com valor recebido e a receber; check-in; vendas por tipo de ingresso; inscrições dos últimos 14 dias; feed "ao vivo" que se atualiza sozinho a cada 30 s. |
| **Participantes** | Forma de pagamento (Cielo · Pix, crédito…), cupom e canal de cada pessoa; botão "Atualizar Cielo"; filtros com contagem (todos, pagos, pendentes, cortesia, presentes, cancelados); busca por nome, e-mail, telefone ou código; marcar entrada com um clique; marcar pago (PIX/dinheiro), cortesia ou pendente; cobrar ou enviar o ingresso pelo WhatsApp; cancelar; ações em lote; adicionar participante manualmente; exportar planilha (CSV para Excel). |
| **Check-in (portaria)** | Campo para leitor de QR USB ou digitação; câmera do celular (Chrome no Android); avisa se a pessoa já entrou, se o código é inválido, se a inscrição foi cancelada ou se o pagamento está pendente (dá para receber na hora e liberar). |
| **Evento** | Nome, chamada, descrição, data, local, capacidade, cor, vários tipos de ingresso (preço e limite próprios), instruções de pagamento, rascunho/publicado/encerrado/cancelado, abrir/fechar inscrições. |
| **Página pública** `/e/{evento}` | Capa gerada com a cor do evento, escolha do ingresso, aviso de "restam X lugares", inscrição. Rascunho só aparece para o organizador (prévia). |
| **Ingresso** `/ingresso/{código}` | QR code, código, situação do pagamento com instruções (PIX), adicionar à agenda (.ics), imprimir. |
| **Organizadores (administrador)** | Criar acesso com senha temporária, gerar nova senha, desativar. |

**Perfis:** Produtor vê só os próprios eventos · Administrador vê todos, aprova produtores e escolhe os destaques da vitrine.

**Pagamento:** pela Cielo quando as credenciais estão configuradas; sem elas, o ingresso mostra as instruções manuais (PIX etc.) e o organizador marca como pago. As duas formas convivem: o organizador sempre pode marcar pagamentos feitos em dinheiro ou na secretaria.

## Cielo (conta nova)

O Plateia usa o **Link de Pagamento (Checkout Cielo)**: a página de pagamento é da Cielo, então não é preciso certificado PCI nem guardar cartão.

1. No Backoffice da Cielo: **Configurações → Dados cadastrais → Credenciais de API** e copie o `ClientId` e o `ClientSecret` do Link de Pagamento.
2. Em **Configurações da loja → Notificações**, escolha o formato **POST** e cadastre:
   - URL de notificação: `https://SEU-DOMINIO/cielo/notificacao?chave=SUA_CHAVE`
   - URL de mudança de status: `https://SEU-DOMINIO/cielo/status?chave=SUA_CHAVE`
3. Deixe a **captura automática** ligada (cartão aprovado já conta como pago).
4. Defina as variáveis `Cielo__ClientId`, `Cielo__ClientSecret`, `Cielo__ChaveNotificacao` (a mesma `SUA_CHAVE`), e opcionalmente `Cielo__SoftDescriptor` (nome na fatura) e `Cielo__MaxParcelas`.

Como funciona:
- O link é criado quando a pessoa clica em **Pagar com a Cielo** no ingresso (valor já com desconto do cupom, válido até o dia do evento).
- O aviso da Cielo **nunca é aceito sozinho**: o Plateia consulta o link na API da Cielo e só marca como pago se lá constar pago, com o valor certo. Um mesmo pedido não vale para dois ingressos.
- Se um aviso se perder, a situação também é consultada quando a pessoa volta ao ingresso e pelo botão **Atualizar Cielo** na lista de participantes.
- Estorno ou chargeback na Cielo volta a inscrição para "Pendente" com uma observação.
- Para testar antes de vender de verdade, use o **modo de teste** do Backoffice da Cielo.

## Estrutura

```
src/
  Plateia.Domain          entidades (Usuario, Evento, TipoIngresso, Inscricao)
  Plateia.Application     regras (EventoService, InscricaoService, PagamentoService, DivulgacaoService, ResumoEvento)
  Plateia.Infrastructure  banco (AppDbContext, migrações) e Cielo (Cielo/CieloLinkPagamento.cs)
  Plateia.Web             telas (MVC + Razor), CSS e JS
banco/criar-banco.sql     cria o banco Plateia e o usuário próprio
```

## Banco de dados

Usa **o mesmo servidor MySQL dos outros sistemas, mas um banco separado (`Plateia`) com usuário próprio**, que só tem permissão nesse banco.

1. Abra `banco/criar-banco.sql`, troque `TROQUE_ESTA_SENHA` por uma senha forte e rode no MySQL com um usuário administrador.
2. As tabelas são criadas sozinhas na primeira vez que o sistema sobe (migrações do Entity Framework).

## Configuração

Nada de senha no código: tudo vem de variáveis de ambiente (veja `.env.exemplo`).

| Variável | Para quê |
|---|---|
| `ConnectionStrings__Plateia` | Conexão com o banco `Plateia` (usuário `plateia`). |
| `Admin__Email`, `Admin__Senha`, `Admin__Nome` | Cria o primeiro administrador quando o banco está vazio. Depois pode remover. |
| `Cielo__ClientId`, `Cielo__ClientSecret` | Credenciais do Link de Pagamento da Cielo. Sem elas, o pagamento fica manual. |
| `Cielo__ChaveNotificacao` | Senha que vai no fim das URLs de notificação (protege o endereço de avisos). |
| `Cielo__SoftDescriptor`, `Cielo__MaxParcelas` | Nome na fatura do cartão (até 13 letras) e parcelas máximas (padrão 1). |
| `Uploads__Pasta` | Onde ficam as capas enviadas (padrão `dados/uploads` ao lado do sistema). Faça backup dessa pasta junto com o banco. |
| `Demo__Criar` | `true` cria produtores, eventos e participantes fictícios (só se não houver eventos). Use apenas para demonstração. |

## Rodar localmente

```bash
cd src/Plateia.Web
export ConnectionStrings__Plateia="Server=127.0.0.1;Database=Plateia;User=plateia;Password=..."
export Admin__Email="seu@email" Admin__Senha="umaSenhaForte"
dotnet run
```

Com `Demo__Criar=true`, entre também como organizador de exemplo: `demo@plateia.local` / `demo12345`.

## Segurança

- Código do ingresso aleatório de 8 caracteres (sem letras ambíguas), único.
- Inscrição trava a linha do evento durante a contagem: dois pedidos simultâneos não ocupam o mesmo último lugar.
- Mesmo e-mail não se inscreve duas vezes no mesmo evento pelo site.
- Limite de 10 envios por minuto por IP no login e na inscrição; antiforgery em todos os formulários.
- Produtor só acessa os próprios eventos (os outros respondem 404); cadastro novo só publica depois de aprovado.
- Capas enviadas: só JPG/PNG/WEBP conferidos pelos primeiros bytes, até 4 MB, com nome aleatório.
- Planilha exportada protegida contra fórmulas do Excel.
- Endereço de avisos da Cielo exige a chave secreta e confere tudo na API da Cielo antes de dar baixa.

## Nova migração (quando mudar as entidades)

```bash
dotnet tool install --global dotnet-ef --version 8.0.10
cd src
ConnectionStrings__Plateia="Server=localhost;Database=Plateia;User=x;Password=x" \
  dotnet ef migrations add NomeDaMudanca -p Plateia.Infrastructure -s Plateia.Web -o Migrations
```

## Telas

| Página do evento (espaços) | Ingresso com pagamento Cielo | Divulgação |
|---|---|---|
| ![](docs/telas/32-evento-espacos.png) | ![](docs/telas/08-ingresso.png) | ![](docs/telas/09-divulgacao.png) |
