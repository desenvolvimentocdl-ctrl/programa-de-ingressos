// Plateia · interações (sem bibliotecas)
(() => {
    "use strict";
    const $ = (s, el = document) => el.querySelector(s);
    const $$ = (s, el = document) => [...el.querySelectorAll(s)];
    const calmo = matchMedia("(prefers-reduced-motion: reduce)").matches;
    const csrf = $('meta[name="csrf"]')?.content ?? "";
    const real = new Intl.NumberFormat("pt-BR", { style: "currency", currency: "BRL", maximumFractionDigits: 0 });

    async function enviar(url, dados) {
        const r = await fetch(url, {
            method: "POST",
            headers: { "X-CSRF": csrf, "Accept": "application/json" },
            body: new URLSearchParams(dados)
        });
        const j = await r.json().catch(() => ({}));
        if (!r.ok) throw new Error(j.erro || "Não foi possível salvar. Tente de novo.");
        return j;
    }

    function aviso(texto, erro = false) {
        $$(".toast").forEach(t => t.remove());
        const t = document.createElement("div");
        t.className = "toast" + (erro ? " erro" : "");
        t.setAttribute("role", erro ? "alert" : "status");
        t.textContent = texto;
        document.body.append(t);
        setTimeout(() => t.remove(), 4600);
    }

    // Números que "contam" até o valor, só quando aparecem na tela.
    function contar(el) {
        const alvo = parseFloat(el.dataset.contar);
        const dinheiro = el.dataset.formato === "dinheiro";
        const fmt = v => dinheiro ? real.format(v) : Math.round(v).toLocaleString("pt-BR");
        if (calmo || !isFinite(alvo) || alvo === 0) { el.textContent = fmt(alvo || 0); return; }
        const dur = 900, ini = performance.now();
        const passo = t => {
            const k = Math.min(1, (t - ini) / dur);
            el.textContent = fmt(alvo * (1 - Math.pow(1 - k, 4)));
            if (k < 1) requestAnimationFrame(passo);
        };
        requestAnimationFrame(passo);
    }
    const obs = new IntersectionObserver(es => es.forEach(e => {
        if (e.isIntersecting) { contar(e.target); obs.unobserve(e.target); }
    }));
    $$("[data-contar]").forEach(el => obs.observe(el));

    // Copiar link / código
    document.addEventListener("click", async e => {
        const b = e.target.closest("[data-copiar]");
        if (!b) return;
        e.preventDefault();
        try { await navigator.clipboard.writeText(b.dataset.copiar); aviso(b.dataset.aviso || "Copiado."); }
        catch { prompt("Copie:", b.dataset.copiar); }
    });

    // Diálogos
    document.addEventListener("click", e => {
        const a = e.target.closest("[data-abrir]");
        if (a) { e.preventDefault(); $(a.dataset.abrir)?.showModal(); }
        const f = e.target.closest("[data-fechar]");
        if (f) { e.preventDefault(); f.closest("dialog")?.close(); }
    });
    $$("dialog").forEach(d => d.addEventListener("click", e => { if (e.target === d) d.close(); }));

    // Confirmação antes de ações destrutivas
    document.addEventListener("submit", e => {
        const msg = e.submitter?.dataset.confirmar || e.target.dataset.confirmar;
        if (msg && !confirm(msg)) e.preventDefault();
    });

    // Fecha menus <details> ao clicar fora
    document.addEventListener("click", e => {
        $$("details.menu[open]").forEach(d => { if (!d.contains(e.target)) d.open = false; });
    });

    // ---------- Participantes: seleção e ações em lote ----------
    const barra = $(".barra-lote");
    if (barra) {
        const caixas = () => $$("input[data-sel]");
        const atualizar = () => {
            const marcadas = caixas().filter(c => c.checked);
            caixas().forEach(c => c.closest("tr").classList.toggle("sel", c.checked));
            $("b", barra).textContent = `${marcadas.length} selecionado${marcadas.length === 1 ? "" : "s"}`;
            barra.classList.toggle("on", marcadas.length > 0);
            const todos = $("#sel-todos");
            if (todos) { todos.checked = marcadas.length > 0 && marcadas.length === caixas().length; todos.indeterminate = marcadas.length > 0 && !todos.checked; }
        };
        document.addEventListener("change", e => {
            if (e.target.matches("input[data-sel]")) atualizar();
            if (e.target.id === "sel-todos") { caixas().forEach(c => c.checked = e.target.checked); atualizar(); }
        });
        $("form", barra).addEventListener("submit", e => {
            $$("input[name=ids]", e.target).forEach(i => i.remove());
            caixas().filter(c => c.checked).forEach(c => {
                const h = document.createElement("input");
                h.type = "hidden"; h.name = "ids"; h.value = c.value;
                e.target.append(h);
            });
        });
        $("[data-limpar]", barra)?.addEventListener("click", () => { caixas().forEach(c => c.checked = false); atualizar(); });
    }

    // Presença com um clique, com carimbo
    document.addEventListener("click", async e => {
        const b = e.target.closest("[data-presenca]");
        if (!b) return;
        const presente = b.dataset.presenca === "1";
        const celula = b.closest(".presenca");
        try {
            const r = await enviar(b.dataset.url, { presente });
            celula.innerHTML = presente
                ? `<button type="button" class="carimbo batendo" data-presenca="0" data-url="${b.dataset.url}" title="Desfazer presença">PRESENTE<small>${r.hora}</small></button>`
                : `<button type="button" class="btn-entrar" data-presenca="1" data-url="${b.dataset.url}">Marcar entrada</button>`;
            if (presente && navigator.vibrate) navigator.vibrate(30);
        } catch (err) { aviso(err.message, true); }
    });

    // ---------- Painel: feed ao vivo ----------
    const feed = $("[data-aovivo]");
    if (feed) {
        const vistos = new Set($$("li[data-chave]", feed).map(l => l.dataset.chave));
        const buscar = async () => {
            if (document.hidden) return;
            try {
                const r = await fetch(feed.dataset.aovivo, { headers: { "Accept": "text/html" } });
                if (!r.ok) return;
                const tmp = document.createElement("div");
                tmp.innerHTML = await r.text();
                const novo = $(".feed", tmp) ?? tmp.firstElementChild;
                if (!novo) return;
                $$("li[data-chave]", novo).forEach(li => { if (!vistos.has(li.dataset.chave)) { li.classList.add("novo"); vistos.add(li.dataset.chave); } });
                feed.replaceChildren(novo);
            } catch { /* sem rede: tenta no próximo ciclo */ }
        };
        setInterval(buscar, 30000);
        document.addEventListener("visibilitychange", () => { if (!document.hidden) buscar(); });
    }

    // ---------- Formulário do evento ----------
    const formEvento = $("#form-evento");
    if (formEvento) {
        const previa = $(".previa .cartaz");
        const tinta = hex => {
            const c = i => { const v = parseInt(hex.substr(i, 2), 16) / 255; return v <= .03928 ? v / 12.92 : Math.pow((v + .055) / 1.055, 2.4); };
            return (.2126 * c(1) + .7152 * c(3) + .0722 * c(5)) > .32 ? "#14110F" : "#FFFFFF";
        };
        const pintar = cor => {
            if (!/^#[0-9a-f]{6}$/i.test(cor)) return;
            previa.style.setProperty("--cor", cor);
            previa.style.setProperty("--tinta", tinta(cor));
            $("#cor-final").value = cor;
        };
        formEvento.addEventListener("change", e => {
            if (e.target.name === "cor-opcao") pintar(e.target.value);
            if (e.target.id === "cor-livre") { pintar(e.target.value); $$("input[name=cor-opcao]").forEach(r => r.checked = false); }
        });
        $("#cor-livre")?.addEventListener("input", e => pintar(e.target.value));
        const espelhar = (campo, alvo, padrao) => {
            const c = $(`[name=${campo}]`, formEvento), a = $(alvo);
            if (c && a) c.addEventListener("input", () => a.textContent = c.value.trim() || padrao);
        };
        espelhar("titulo", "#previa-titulo", "Nome do evento");
        espelhar("local", "#previa-local", "Local");

        const lista = $("#tipos");
        $("#add-tipo")?.addEventListener("click", () => {
            const n = $("#modelo-tipo").content.firstElementChild.cloneNode(true);
            lista.append(n);
            $("input[name=tipo_nome]", n).focus();
        });
        lista?.addEventListener("click", e => {
            const b = e.target.closest("[data-remover-tipo]");
            if (!b) return;
            const linha = b.closest(".tipo-linha");
            if ($$(".tipo-linha:not(.removido)", lista).length <= 1) { aviso("O evento precisa de pelo menos um tipo de ingresso.", true); return; }
            if (!$("input[name=tipo_id]", linha).value) { linha.remove(); return; }
            $("input[name=tipo_remover]", linha).value = "1";
            $$("[required]", linha).forEach(i => i.required = false);
            linha.classList.add("removido");
        });
    }

    // ---------- Portaria ----------
    const portaria = $("#form-checkin");
    if (portaria) {
        const campo = $("input[name=codigo]", portaria);
        const caixa = $("#resultado");
        const contador = $("#presentes");
        let ultimo = "", ocupado = false;

        const bip = ok => {
            try {
                const ctx = bip.ctx ??= new AudioContext();
                const o = ctx.createOscillator(), g = ctx.createGain();
                o.frequency.value = ok ? 880 : 220; o.type = ok ? "sine" : "square";
                g.gain.setValueAtTime(.12, ctx.currentTime); g.gain.exponentialRampToValueAtTime(.001, ctx.currentTime + (ok ? .18 : .35));
                o.connect(g).connect(ctx.destination); o.start(); o.stop(ctx.currentTime + .4);
            } catch { }
            navigator.vibrate?.(ok ? 40 : [80, 60, 80]);
        };

        const titulos = { Liberado: "Entrada liberada", JaEntrou: "Já entrou", PagamentoPendente: "Pagamento pendente", Cancelada: "Inscrição cancelada", NaoEncontrado: "Não encontrado" };
        const esc = s => String(s ?? "").replace(/[&<>"']/g, c => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));

        const mostrar = (r, codigo) => {
            const i = r.inscricao;
            caixa.className = `bilhete resultado ${r.resultado}`;
            void caixa.offsetWidth;
            caixa.classList.add("chegou");
            let corpo = "";
            if (!i) corpo = `<p class="suave">Nenhum ingresso deste evento com o código <b class="mono">${esc(codigo)}</b>. Confira as letras ou busque pelo nome na lista de participantes.</p>`;
            else {
                corpo = `<div class="pessoa" style="display:flex;gap:14px;align-items:center"><span class="av" style="width:52px;height:52px;font-size:16px">${esc(i.iniciais)}</span>
                    <div><h3>${esc(i.nome)}</h3><span class="suave">${esc(i.tipo)} · <span class="mono">${esc(i.codigo)}</span></span></div></div>`;
                if (r.resultado === "JaEntrou") corpo += `<p>Entrou às <b class="mono">${esc(i.hora)}</b>. Pode ser o mesmo ingresso sendo usado duas vezes.</p>`;
                if (r.resultado === "Cancelada") corpo += `<p>Esta inscrição foi cancelada e não dá direito à entrada.</p>`;
                if (r.resultado === "PagamentoPendente") corpo += `<p>Falta pagar <b class="mono">${esc(i.valor)}</b>. Receba agora e libere a entrada, ou oriente a pessoa.</p>
                    <button class="btn pri" type="button" data-liberar="${esc(i.codigo)}">Recebi o pagamento e liberar</button>`;
                if (r.resultado === "Liberado") corpo += `<span class="carimbo batendo" style="justify-self:start;font-size:22px">PRESENTE<small>${esc(i.hora)}</small></span>`;
            }
            caixa.innerHTML = `<div class="topo-r"><span>${titulos[r.resultado]}</span></div><div class="corpo-r">${corpo}</div>`;
            if (contador) { contador.dataset.contar = r.presentes; contar(contador); }
            bip(r.resultado === "Liberado");
            if (r.resultado === "Liberado" && i) {
                const lista = $("#recentes");
                if (lista) {
                    $(".vazio", lista)?.remove();
                    const li = document.createElement("li");
                    li.className = "novo";
                    li.innerHTML = `<span class="ic Entrou">✓</span><span>${esc(i.nome)}</span><time>${esc(i.hora)}</time>`;
                    lista.prepend(li);
                }
            }
        };

        const checar = async (codigo, liberar = false) => {
            if (ocupado || !codigo) return;
            ocupado = true;
            try {
                const r = await enviar(portaria.action, { codigo, liberar });
                mostrar(r, codigo);
            } catch (err) { aviso(err.message, true); }
            finally { ocupado = false; campo.value = ""; campo.focus(); }
        };

        portaria.addEventListener("submit", e => { e.preventDefault(); checar(campo.value.trim()); });
        caixa.addEventListener("click", e => {
            const b = e.target.closest("[data-liberar]");
            if (b) checar(b.dataset.liberar, true);
        });

        // Leitura do QR pela câmera (navegadores com BarcodeDetector, ex.: Chrome no Android).
        const btnCam = $("#btn-camera");
        if (btnCam && "BarcodeDetector" in window) {
            btnCam.hidden = false;
            let stream, timer;
            btnCam.addEventListener("click", async () => {
                const box = $(".camera");
                if (stream) { stream.getTracks().forEach(t => t.stop()); stream = null; clearInterval(timer); box.classList.remove("on"); btnCam.textContent = "Ler QR com a câmera"; return; }
                try {
                    stream = await navigator.mediaDevices.getUserMedia({ video: { facingMode: "environment" } });
                    const video = $("video", box);
                    video.srcObject = stream; await video.play();
                    box.classList.add("on"); btnCam.textContent = "Desligar câmera";
                    const det = new BarcodeDetector({ formats: ["qr_code"] });
                    timer = setInterval(async () => {
                        if (ocupado) return;
                        const [c] = await det.detect(video).catch(() => []);
                        if (c && c.rawValue !== ultimo) { ultimo = c.rawValue; checar(c.rawValue); setTimeout(() => ultimo = "", 4000); }
                    }, 350);
                } catch { aviso("Não foi possível abrir a câmera. Digite o código.", true); }
            });
        }
    }
})();
