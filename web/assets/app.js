/* FIIs · ranking — SPA sem dependências.
 *
 * O servidor só entrega a tabela normalizada; seleção, ranking, filtros e
 * ordenação acontecem aqui. As estratégias são porte direto de Program.cs. */

'use strict';

const API = 'api/fiis.php';

/* ---------------------------------------------------------------- critérios */

const SRANK = {
  minDividendYield: 0.04,
  minPvp: 0.04,
  maxPvp: 1.2,
  minValorDeMercado: 500e6,
  maxVacanciaMedia: 0.3,
  minLiquidez: 1e6,
};

const BAL = {
  minLiquidezDiaria: 2e6,
  minValorDeMercado: 500e6,
  minDy: 0.07,
  maxDy: 0.20,
  maxDyOverFfo: 1.5,
  minPvpTijolo: 0.55,
  minPvpPapel: 0.80,
  maxPvp: 1.05,
  maxVacanciaTijolo: 0.15,
  minImoveis: 3,
};

const NORD_TICKERS = ['BRCR11', 'HGLG11', 'JSRE11', 'HGRE11', 'RECT11', 'KNRI11', 'HSML11',
  'MGFF11', 'GTWR11', 'HGBS11', 'BRCO11', 'GGRC11', 'RBRF11'];

const MY_TICKERS = ['VRTA11', 'RBRY11', 'RBRF11', 'VILG11', 'HGPO11', 'HSML11', 'HGRU11',
  'HGRE11', 'HSLG11', 'DEVA11', 'KNSC11', 'JSRE11', 'VGIR11', 'BTLG11', 'RVBI11'];

/* --------------------------------------------------------------- formatação */

const nf = (min, max = min) => new Intl.NumberFormat('pt-BR', {
  minimumFractionDigits: min,
  maximumFractionDigits: max,
});

const num2 = nf(2);
const num0 = nf(0);
const pct2 = new Intl.NumberFormat('pt-BR', { style: 'percent', minimumFractionDigits: 2 });

const fmt = {
  money: (v) => num2.format(v),
  int: (v) => num0.format(v),
  pct: (v) => pct2.format(v),
  short: (v) => {
    const abs = Math.abs(v);
    if (abs >= 1e9) return nf(2).format(v / 1e9) + ' bi';
    if (abs >= 1e6) return nf(1).format(v / 1e6) + ' mi';
    if (abs >= 1e3) return nf(0).format(v / 1e3) + ' mil';
    return num0.format(v);
  },
};

/* -------------------------------------------------------------- estratégias */

const isPaper = (f) => f.papel;

/** Posição de cada fundo (1 = melhor) segundo keyFn, crescente. */
function rankBy(list, keyFn) {
  const ranks = new Map();
  [...list].sort((a, b) => keyFn(a) - keyFn(b)).forEach((f, i) => ranks.set(f, i + 1));
  return ranks;
}

const BALANCED_FILTERS = [
  { label: 'Liquidez > R$ 2 mi', test: (f) => f.liquidez > BAL.minLiquidezDiaria },
  { label: 'Valor de mercado > R$ 500 mi', test: (f) => f.valorDeMercado > BAL.minValorDeMercado },
  { label: 'DY entre 7% e 20%', test: (f) => f.dividendYield >= BAL.minDy && f.dividendYield <= BAL.maxDy },
  {
    label: 'P/VP 0,80–1,05 (papel) · 0,55–1,05 (tijolo)',
    test: (f) => f.pvp <= BAL.maxPvp && f.pvp >= (isPaper(f) ? BAL.minPvpPapel : BAL.minPvpTijolo),
  },
  { label: 'Vacância < 15% (tijolo)', test: (f) => isPaper(f) || f.vacanciaMedia < BAL.maxVacanciaTijolo },
  { label: '≥ 3 imóveis (tijolo)', test: (f) => isPaper(f) || f.qtdDeImoveis >= BAL.minImoveis },
  { label: 'DY coberto pelo FFO (≤ 1,5×)', test: (f) => f.ffoYield > 0 && f.dividendYield <= f.ffoYield * BAL.maxDyOverFfo },
];

const SRANK_FILTERS = [
  { label: 'DY > 4%', test: (f) => f.dividendYield > SRANK.minDividendYield },
  { label: '0,04 < P/VP < 1,2', test: (f) => f.pvp > SRANK.minPvp && f.pvp < SRANK.maxPvp },
  { label: 'Valor de mercado > R$ 500 mi', test: (f) => f.valorDeMercado > SRANK.minValorDeMercado },
  { label: 'Vacância < 30%', test: (f) => f.vacanciaMedia < SRANK.maxVacanciaMedia },
  { label: 'Liquidez > R$ 1 mi', test: (f) => f.liquidez > SRANK.minLiquidez },
];

const balancedChecks = (f) => ({
  dividendYield: f.dividendYield >= BAL.minDy && f.dividendYield <= BAL.maxDy,
  pvp: f.pvp <= BAL.maxPvp && f.pvp >= (isPaper(f) ? BAL.minPvpPapel : BAL.minPvpTijolo),
  valorDeMercado: f.valorDeMercado > BAL.minValorDeMercado,
  liquidez: f.liquidez > BAL.minLiquidezDiaria,
  vacanciaMedia: isPaper(f) || f.vacanciaMedia < BAL.maxVacanciaTijolo,
});

const srankChecks = (f) => ({
  dividendYield: f.dividendYield > SRANK.minDividendYield,
  pvp: f.pvp > SRANK.minPvp && f.pvp < SRANK.maxPvp,
  valorDeMercado: f.valorDeMercado > SRANK.minValorDeMercado,
  liquidez: f.liquidez > SRANK.minLiquidez,
  vacanciaMedia: f.vacanciaMedia < SRANK.maxVacanciaMedia,
});

const STRATEGIES = {
  equilibrada: {
    label: 'Equilibrada',
    note: 'Réguas separadas para papel e tijolo, piso no P/VP (desconto extremo em FII costuma ser sintoma, '
      + 'não oportunidade) e distribuição coberta pelo resultado operacional. Ordena pela soma das posições '
      + 'em DY e P/VP — barato e rentável pesam igual —, desempate pela liquidez.',
    filters: BALANCED_FILTERS,
    checks: balancedChecks,
    criteria: BALANCED_FILTERS,
    rank(eligible) {
      const byYield = rankBy(eligible, (f) => -f.dividendYield);
      const byPrice = rankBy(eligible, (f) => f.pvp);
      return [...eligible].sort((a, b) =>
        (byYield.get(a) + byPrice.get(a)) - (byYield.get(b) + byPrice.get(b)) || b.liquidez - a.liquidez);
    },
  },

  srank: {
    label: 'S-Rank',
    note: 'Os cinco cortes do binário original, ordenados apenas por P/VP crescente — por isso o topo da lista '
      + 'é sistematicamente o que o mercado mais penalizou.',
    filters: SRANK_FILTERS,
    checks: srankChecks,
    criteria: SRANK_FILTERS,
    rank: (eligible) => [...eligible].sort((a, b) => a.pvp - b.pvp),
  },

  nord: {
    label: 'Carteira Nord',
    note: 'Lista fixa de tickers, sem filtro. Os valores fora dos critérios da Equilibrada aparecem marcados.',
    tickers: NORD_TICKERS,
    checks: balancedChecks,
    criteria: BALANCED_FILTERS,
  },

  minha: {
    label: 'Minha carteira',
    note: 'Lista fixa de tickers, sem filtro. Serve como auditoria: o que está marcado reprova na Equilibrada.',
    tickers: MY_TICKERS,
    checks: balancedChecks,
    criteria: BALANCED_FILTERS,
  },

  todos: {
    label: 'Todos',
    note: 'A tabela inteira do Fundamentus, sem nenhum corte. Use os filtros do cabeçalho para garimpar.',
    checks: balancedChecks,
    criteria: BALANCED_FILTERS,
  },
};

/* -------------------------------------------------------------------- tabela */

const COLUMNS = [
  { key: 'rank', label: '#', css: 'rank num', sortable: false, filter: { type: 'clear' } },
  {
    key: 'ticker', label: 'Ticker', css: 'ticker', sort: (f) => f.ticker, type: 'text',
    filter: { type: 'text', name: 'q', placeholder: 'ticker ou nome' },
    render: (f) => `<span>${esc(f.ticker)}</span><small title="${esc(f.nome)}">${esc(f.nome)}</small>`,
  },
  {
    key: 'segmento', label: 'Segmento', css: 'segmento', sort: (f) => f.segmento, type: 'text',
    filter: { type: 'select', name: 'segmento' },
    render: (f) => `${esc(f.segmento || '—')}<span class="tag tag-${f.papel ? 'papel' : 'tijolo'}">${f.papel ? 'papel' : 'tijolo'}</span>`,
  },
  { key: 'cotacao', label: 'Cotação', css: 'num', sort: (f) => f.cotacao, render: (f) => fmt.money(f.cotacao) },
  {
    key: 'dividendYield', label: 'DY', css: 'num', sort: (f) => f.dividendYield, flag: 'dividendYield',
    filter: { type: 'number', name: 'dyMin', placeholder: 'mín %' },
    render: (f) => fmt.pct(f.dividendYield),
  },
  { key: 'ffoYield', label: 'FFO Y.', css: 'num', sort: (f) => f.ffoYield, render: (f) => fmt.pct(f.ffoYield) },
  {
    key: 'pvp', label: 'P/VP', css: 'num', sort: (f) => f.pvp, flag: 'pvp',
    filter: { type: 'number', name: 'pvpMax', placeholder: 'máx' },
    render: (f) => num2.format(f.pvp),
  },
  {
    key: 'valorDeMercado', label: 'Valor mercado', css: 'num', sort: (f) => f.valorDeMercado, flag: 'valorDeMercado',
    filter: { type: 'number', name: 'vmMin', placeholder: 'mín mi' },
    render: (f) => fmt.short(f.valorDeMercado), title: (f) => fmt.money(f.valorDeMercado),
  },
  {
    key: 'liquidez', label: 'Liquidez', css: 'num', sort: (f) => f.liquidez, flag: 'liquidez',
    filter: { type: 'number', name: 'liqMin', placeholder: 'mín mi' },
    render: (f) => fmt.short(f.liquidez), title: (f) => fmt.money(f.liquidez),
  },
  {
    key: 'qtdDeImoveis', label: 'Imóveis', css: 'num', sort: (f) => f.qtdDeImoveis,
    filter: { type: 'classe', name: 'classe' },
    render: (f) => fmt.int(f.qtdDeImoveis),
  },
  {
    key: 'vacanciaMedia', label: 'Vacância', css: 'num', sort: (f) => f.vacanciaMedia, flag: 'vacanciaMedia',
    filter: { type: 'number', name: 'vacMax', placeholder: 'máx %' },
    render: (f) => fmt.pct(f.vacanciaMedia),
  },
  { key: 'capRate', label: 'Cap rate', css: 'num', sort: (f) => f.capRate, render: (f) => fmt.pct(f.capRate) },
];

/* --------------------------------------------------------------------- estado */

const state = {
  all: [],
  meta: null,
  strategy: 'equilibrada',
  limit: 30,
  sort: { key: 'rank', dir: 'asc' },
  filters: { q: '', segmento: '', classe: '', dyMin: null, pvpMax: null, vmMin: null, liqMin: null, vacMax: null },
  open: null,
};

const $ = (id) => document.getElementById(id);

function esc(s) {
  return String(s ?? '').replace(/[&<>"']/g, (c) =>
    ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]);
}

function remember() {
  try {
    localStorage.setItem('fiis.ui', JSON.stringify({ strategy: state.strategy, limit: state.limit }));
  } catch { /* modo privado, armazenamento bloqueado: a UI funciona igual */ }
}

function recall() {
  try {
    const saved = JSON.parse(localStorage.getItem('fiis.ui') || '{}');
    if (saved.strategy && STRATEGIES[saved.strategy]) state.strategy = saved.strategy;
    if (Number.isInteger(saved.limit)) state.limit = saved.limit;
  } catch { /* idem */ }
}

/* ---------------------------------------------------------------- pipeline */

/** Aplica a estratégia: devolve elegíveis já rankeados e o funil de cortes. */
function select() {
  const strategy = STRATEGIES[state.strategy];
  const universe = state.all;

  if (strategy.tickers) {
    const wanted = new Set(strategy.tickers.map((t) => t.toUpperCase()));
    const picked = universe.filter((f) => wanted.has(f.ticker.toUpperCase()))
      .sort((a, b) => a.pvp - b.pvp);
    return { eligible: picked, funnel: [], ranked: false };
  }

  if (!strategy.filters) {
    // Sem corte, P/VP crescente só traria os zerados para o topo: a liquidez é
    // o ponto de partida útil para garimpar na tabela inteira.
    return { eligible: [...universe].sort((a, b) => b.liquidez - a.liquidez), funnel: [], ranked: false };
  }

  const funnel = [];
  let current = universe;

  for (const step of strategy.filters) {
    const next = current.filter(step.test);
    funnel.push({ label: step.label, before: current.length, after: next.length });
    current = next;
  }

  const ranked = strategy.rank(current);
  ranked.forEach((f, i) => { f._rank = i + 1; });

  return { eligible: ranked, funnel, ranked: true };
}

function passesFilters(f) {
  const x = state.filters;

  if (x.q) {
    const q = x.q.toLowerCase();
    if (!f.ticker.toLowerCase().includes(q) && !f.nome.toLowerCase().includes(q)) return false;
  }

  if (x.segmento && f.segmento !== x.segmento) return false;
  if (x.classe === 'papel' && !f.papel) return false;
  if (x.classe === 'tijolo' && f.papel) return false;
  if (x.dyMin !== null && f.dividendYield < x.dyMin / 100) return false;
  if (x.pvpMax !== null && f.pvp > x.pvpMax) return false;
  if (x.vmMin !== null && f.valorDeMercado < x.vmMin * 1e6) return false;
  if (x.liqMin !== null && f.liquidez < x.liqMin * 1e6) return false;
  if (x.vacMax !== null && f.vacanciaMedia > x.vacMax / 100) return false;

  return true;
}

function sortRows(rows, hasRank) {
  const { key, dir } = state.sort;
  const column = COLUMNS.find((c) => c.key === key);
  const factor = dir === 'asc' ? 1 : -1;

  // 'rank' = a ordem que a própria estratégia produziu (score, P/VP ou liquidez).
  if (key === 'rank') return rows;

  if (!column || !column.sort) return rows;

  return [...rows].sort((a, b) => {
    const x = column.sort(a);
    const y = column.sort(b);
    const cmp = column.type === 'text' ? String(x).localeCompare(y, 'pt-BR') : x - y;
    return factor * cmp;
  });
}

/* ----------------------------------------------------------------- renderers */

function renderPills() {
  $('pills').innerHTML = Object.entries(STRATEGIES).map(([key, s]) =>
    `<button type="button" class="pill" role="tab" data-strategy="${key}" aria-selected="${key === state.strategy}">${esc(s.label)}</button>`
  ).join('');
}

function renderStrategyCard(result, shown) {
  const strategy = STRATEGIES[state.strategy];

  $('strategy-name').textContent = strategy.label;
  $('strategy-note').textContent = strategy.note;

  const papel = shown.filter(isPaper).length;

  $('kpis').innerHTML = [
    ['Universo', fmt.int(state.all.length)],
    [strategy.filters ? 'Elegíveis' : 'Na lista', fmt.int(result.eligible.length)],
    ['Exibidos', fmt.int(shown.length)],
    ['Tijolo / papel', `${shown.length - papel} / ${papel}`],
  ].map(([label, value]) => `<div class="kpi"><b>${value}</b><span>${label}</span></div>`).join('');

  const funnel = $('funnel');

  if (result.funnel.length === 0) {
    funnel.innerHTML = `<li class="funnel-empty">Sem cortes nesta estratégia — a seleção é a lista de tickers ou a tabela inteira.</li>`;
    funnel.classList.add('is-empty');
    return;
  }

  funnel.classList.remove('is-empty');
  funnel.innerHTML = result.funnel.map((step) => {
    const cut = step.before - step.after;
    const width = Math.max(2, Math.round((step.after / state.all.length) * 100));
    return `<li>
      <div class="f-label">${esc(step.label)}</div>
      <div class="f-count">${fmt.int(step.after)} ${cut > 0 ? `<span class="f-cut">−${fmt.int(cut)}</span>` : ''}</div>
      <div class="f-bar"><i style="width:${width}%"></i></div>
    </li>`;
  }).join('');
}

function renderHead() {
  const { key, dir } = state.sort;

  $('row-labels').innerHTML = COLUMNS.map((c) => {
    const active = c.key === key;
    const sorted = active ? ` aria-sort="${dir === 'asc' ? 'ascending' : 'descending'}"` : '';
    const arrow = active ? (dir === 'asc' ? '▲' : '▼') : '';
    const inner = c.sortable === false
      ? esc(c.label)
      : `<button type="button" data-sort="${c.key}">${esc(c.label)}<span class="arrow">${arrow}</span></button>`;
    return `<th class="${c.css || ''}"${sorted}>${inner}</th>`;
  }).join('');

  const segmentos = [...new Set(state.all.map((f) => f.segmento).filter(Boolean))].sort((a, b) => a.localeCompare(b, 'pt-BR'));

  $('row-filters').innerHTML = COLUMNS.map((c) => {
    const f = c.filter;
    if (!f) return '<th></th>';

    if (f.type === 'clear') {
      return `<th><button type="button" class="btn" id="clear" title="Limpar filtros" style="padding:3px 7px;font-size:11px">limpar</button></th>`;
    }

    if (f.type === 'text') {
      return `<th><input type="search" data-filter="${f.name}" placeholder="${f.placeholder}" value="${esc(state.filters[f.name])}" aria-label="Filtrar por ${f.placeholder}"></th>`;
    }

    if (f.type === 'select') {
      const options = ['<option value="">todos</option>'].concat(
        segmentos.map((s) => `<option value="${esc(s)}"${state.filters.segmento === s ? ' selected' : ''}>${esc(s)}</option>`)
      ).join('');
      return `<th><select data-filter="segmento" aria-label="Filtrar por segmento">${options}</select></th>`;
    }

    if (f.type === 'classe') {
      const value = state.filters.classe;
      return `<th><select data-filter="classe" aria-label="Filtrar por classe">
        <option value="">ambos</option>
        <option value="tijolo"${value === 'tijolo' ? ' selected' : ''}>tijolo</option>
        <option value="papel"${value === 'papel' ? ' selected' : ''}>papel</option>
      </select></th>`;
    }

    const value = state.filters[f.name];
    return `<th><input type="number" step="any" inputmode="decimal" data-filter="${f.name}" placeholder="${f.placeholder}" value="${value ?? ''}" aria-label="${c.label} ${f.placeholder}"></th>`;
  }).join('');
}

function renderBody(rows, hasRank) {
  const checks = STRATEGIES[state.strategy].checks;
  const tbody = $('tbody');

  if (rows.length === 0) {
    tbody.innerHTML = `<tr><td class="empty" colspan="${COLUMNS.length}">Nenhum fundo atende a esses filtros.</td></tr>`;
    return;
  }

  tbody.innerHTML = rows.map((f, i) => {
    const marks = checks(f);
    const cells = COLUMNS.map((c) => {
      if (c.key === 'rank') return `<td class="rank num">${hasRank ? fmt.int(f._rank) : fmt.int(i + 1)}</td>`;
      const flagged = c.flag && marks[c.flag] === false ? ' flag' : '';
      const title = c.title ? ` title="${esc(c.title(f))}"` : '';
      return `<td class="${c.css || ''}${flagged}"${title}>${c.render(f)}</td>`;
    }).join('');

    const open = state.open === f.ticker;
    return `<tr data-ticker="${esc(f.ticker)}" class="${open ? 'is-open' : ''}">${cells}</tr>`
      + (open ? detailRow(f) : '');
  }).join('');
}

function detailRow(f) {
  const criteria = STRATEGIES[state.strategy].criteria;

  const checklist = criteria.map((c) => {
    const ok = c.test(f);
    return `<li class="${ok ? 'ok' : 'no'}">${esc(c.label)}</li>`;
  }).join('');

  return `<tr class="detail"><td colspan="${COLUMNS.length}">
    <div class="detail-box">
      <div>
        <h3>${esc(f.ticker)} · ${f.papel ? 'papel' : 'tijolo'}</h3>
        <dl>
          <dt>Cotação</dt><dd>${fmt.money(f.cotacao)}</dd>
          <dt>Valor de mercado</dt><dd>${fmt.money(f.valorDeMercado)}</dd>
          <dt>Liquidez diária</dt><dd>${fmt.money(f.liquidez)}</dd>
          <dt>Preço do m²</dt><dd>${fmt.money(f.precoM2)}</dd>
          <dt>Aluguel por m²</dt><dd>${fmt.money(f.aluguelM2)}</dd>
          <dt>Qtd de imóveis</dt><dd>${fmt.int(f.qtdDeImoveis)}</dd>
        </dl>
      </div>
      <div>
        <h3>Critérios · ${esc(STRATEGIES[state.strategy].label === 'S-Rank' ? 'S-Rank' : 'Equilibrada')}</h3>
        <ul class="checklist">${checklist}</ul>
      </div>
      <div>
        <h3>Nome e endereço</h3>
        <p class="addr">${esc(f.nome)}</p>
        <p class="addr">${esc(f.endereco || 'Sem endereço informado.')}</p>
      </div>
    </div>
  </td></tr>`;
}

function renderFreshness() {
  const meta = state.meta;
  if (!meta) return;

  const age = meta.cache?.ageSeconds ?? 0;
  const when = age < 60 ? 'agora há pouco'
    : age < 3600 ? `há ${Math.round(age / 60)} min`
      : `há ${Math.round(age / 3600)} h`;

  const label = meta.cache?.state === 'stale' ? 'cache vencido (origem fora do ar)' : 'atualizado';
  const skipped = meta.skipped > 0 ? ` · ${meta.skipped} linha(s) ignorada(s)` : '';

  $('freshness').textContent = `${label} ${when} · ${fmt.int(state.all.length)} fundos${skipped}`;
}

function render() {
  const result = select();
  const filtered = result.eligible.filter(passesFilters);
  const sorted = sortRows(filtered, result.ranked);
  const shown = state.limit > 0 ? sorted.slice(0, state.limit) : sorted;

  renderPills();
  renderStrategyCard(result, shown);
  renderHead();
  renderBody(shown, result.ranked);

  const hidden = sorted.length - shown.length;
  $('table-foot').textContent = `${fmt.int(shown.length)} linha(s)`
    + (hidden > 0 ? ` · ${fmt.int(hidden)} além do limite` : '')
    + ' · clique em uma linha para ver detalhes e critérios';

  renderFreshness();
}

function renderSkeleton() {
  renderHead();
  $('tbody').innerHTML = Array.from({ length: 8 }, () =>
    `<tr class="skeleton">${COLUMNS.map(() => '<td><span></span></td>').join('')}</tr>`).join('');
}

function showState(message, isError) {
  const box = $('state');
  box.hidden = false;
  box.className = `state${isError ? ' error' : ''}`;
  box.innerHTML = message;
}

/* --------------------------------------------------------------------- dados */

async function load(forceRefresh) {
  const button = $('refresh');
  button.disabled = true;
  button.classList.add('is-busy');
  $('state').hidden = true;

  if (state.all.length === 0) renderSkeleton();

  try {
    const response = await fetch(API + (forceRefresh ? '?refresh=1' : ''), { headers: { Accept: 'application/json' } });
    const payload = await response.json();

    if (!response.ok) throw new Error(payload.error || `HTTP ${response.status}`);

    state.all = payload.items;
    state.meta = payload;
    render();

    if (payload.warning) showState(esc(payload.warning), false);
  } catch (error) {
    if (state.all.length === 0) {
      $('tbody').innerHTML = '';
      showState(`Não consegui carregar os dados.<br><code>${esc(error.message)}</code>`, true);
    } else {
      showState(`Atualização falhou, mostrando os dados anteriores.<br><code>${esc(error.message)}</code>`, true);
    }
  } finally {
    button.disabled = false;
    button.classList.remove('is-busy');
  }
}

/* -------------------------------------------------------------------- eventos */

function bind() {
  $('pills').addEventListener('click', (e) => {
    const pill = e.target.closest('[data-strategy]');
    if (!pill) return;

    state.strategy = pill.dataset.strategy;
    state.sort = { key: 'rank', dir: 'asc' };
    state.open = null;
    remember();
    render();
  });

  $('limit').addEventListener('change', (e) => {
    state.limit = Number(e.target.value);
    remember();
    render();
  });

  $('refresh').addEventListener('click', () => load(true));

  const table = $('table');

  table.addEventListener('click', (e) => {
    const sorter = e.target.closest('[data-sort]');
    if (sorter) {
      const key = sorter.dataset.sort;
      const dir = state.sort.key === key && state.sort.dir === 'asc' ? 'desc' : 'asc';
      state.sort = { key, dir };
      render();
      return;
    }

    if (e.target.closest('#clear')) {
      for (const key of Object.keys(state.filters)) {
        state.filters[key] = typeof state.filters[key] === 'string' ? '' : null;
      }
      render();
      return;
    }

    if (e.target.closest('thead') || e.target.closest('tr.detail')) return;

    const row = e.target.closest('tr[data-ticker]');
    if (!row) return;

    state.open = state.open === row.dataset.ticker ? null : row.dataset.ticker;
    render();
  });

  // Filtros do cabeçalho: 'input' para texto/número, 'change' para os selects.
  const onFilter = (e) => {
    const field = e.target.dataset.filter;
    if (!field) return;

    // Select dispara 'input' e 'change': ignora um dos dois para não renderizar duas vezes.
    if (e.target.tagName === 'SELECT' && e.type === 'input') return;

    const raw = e.target.value;
    const isNumber = e.target.type === 'number';
    state.filters[field] = isNumber ? (raw === '' ? null : Number(raw)) : raw;

    const active = document.activeElement?.dataset?.filter;
    const caret = e.target.selectionStart;

    render();

    // O cabeçalho é reconstruído a cada render: devolve o foco ao campo.
    if (active) {
      const again = document.querySelector(`[data-filter="${active}"]`);
      if (again) {
        again.focus();
        if (again.type !== 'number' && caret !== null) {
          try { again.setSelectionRange(caret, caret); } catch { /* search input */ }
        }
      }
    }
  };

  table.addEventListener('input', onFilter);
  table.addEventListener('change', onFilter);
}

recall();
bind();
load(false);
