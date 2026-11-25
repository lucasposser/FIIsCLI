# FIIs · ranking (web)

Saída web do `FIIsCLI`: mesma leitura do Fundamentus, mesmas estratégias, numa
SPA com tabela ordenável e filtros no cabeçalho.

Sem banco, sem Composer, sem build. São 5 arquivos — dá para subir por FTP.

## Arquitetura

```
index.html            a página
assets/styles.css     tema claro/escuro, sem framework
assets/app.js         estratégias, ranking, filtros, ordenação e render
api/fiis.php          único endpoint: devolve a tabela normalizada em JSON
lib/Fundamentus.php   scraping + parsing pt-BR (porte de FundamentusScraper.cs)
cache/                cache de arquivo (precisa ser gravável)
```

**Por que PHP no meio?** O navegador não consegue buscar o Fundamentus direto —
a origem não manda cabeçalho CORS. O PHP faz esse papel e mais nada: busca,
normaliza, guarda em cache e devolve JSON.

**Por que as estratégias rodam no cliente?** Trocar de estratégia não precisa de
round-trip, e o painel de funil (quanto cada corte elimina) sai de graça. O
servidor entrega os 576 fundos uma vez (~350 KB) e o resto é instantâneo.

**Cache**: TTL de 20 min, escrita atômica (nenhum GET concorrente lê JSON pela
metade) e *stale-on-error* — se o Fundamentus cair, o site serve o cache vencido
com um aviso em vez de quebrar. `?refresh=1` força a busca, respeitando um piso
de 60s para não martelar a origem.

## Deploy na Hostinger

**Suba numa subpasta, nunca na raiz** — ex.: `public_html/fii/`.

Na raiz haveria dois estragos: o `.htaccess` daqui sobrescreveria o da raiz
(que carrega o Basic Auth, as exceções dos endpoints públicos e os `SetEnv` com
credenciais), e o `index.html` disputaria o `DirectoryIndex` com o `index.php`,
trocando a home do domínio por esta página.

1. Copie o conteúdo desta pasta para `public_html/fii/`.
2. Garanta que `fii/cache/` seja gravável (755 basta; o PHP cria se faltar).
3. Acesse `https://seu-dominio/fii/`.

Todos os caminhos são relativos (`assets/...`, `api/fiis.php`), então a pasta
roda em qualquer ponto de montagem, sem configurar base path. Se a raiz exigir
Basic Auth, a subpasta herda: o navegador pede a senha uma vez e o `fetch` da
API reusa as credenciais, por ser mesma origem.

O bloqueio de `lib/` e `cache/` é feito por um `.htaccess` dentro de cada pasta
(`Require all denied`), e não por regra na raiz — assim funciona em qualquer
subpasta.

Requisitos: PHP 8.0+ com `dom`, `mbstring` e `curl` (ou `allow_url_fopen`) — tudo
padrão na Hostinger. O `.htaccess` desta pasta só desliga listagem de diretório e
liga gzip/expires; o site funciona sem ele.

**Se der erro de certificado** (`unable to get local issuer certificate`), o PHP
do servidor está sem CA bundle: aponte `curl.cainfo` no `php.ini`. Nunca
desligue a verificação SSL para contornar.

## Uso

- **Pills de estratégia**: Equilibrada (padrão), S-Rank, Carteira Nord, Minha
  carteira, Todos.
- **Cabeçalho da tabela**: clique no rótulo para ordenar; a linha de baixo tem os
  filtros (texto, segmento, classe tijolo/papel, DY mínimo, P/VP máximo, valor e
  liquidez mínimos em milhões, vacância máxima). O botão `limpar` zera tudo.
- **Clique numa linha** para abrir detalhes: preço/m², aluguel/m², endereço e o
  checklist de critérios daquele fundo.
- **`[!]` vermelho** marca o valor fora do critério da estratégia ativa. Nas
  estratégias com corte nada aparece marcado (por construção, todos passam) — a
  marca é útil nos modos de carteira, onde vira auditoria do que você já tem.

A ordem do `#` é a posição na estratégia e não muda quando você filtra: filtrar
estreita a lista, não reembaralha o ranking.

## Relação com o CLI

`assets/app.js` é porte direto de `Program.cs` e `lib/Fundamentus.php` de
`FundamentusScraper.cs`. As três implementações foram conferidas lado a lado
sobre os mesmos dados: mesmo funil (576 → 71 → 63 → 55 → 44 → 39 → 34 → 32),
mesmo top 10 nas duas estratégias e as mesmas marcas `[!]` na carteira.

Mexeu num critério? Mexa nos dois lugares — `Program.cs` e `assets/app.js`.
