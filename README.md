# FIIsCLI

Ranking de fundos imobiliários a partir da tabela do
[Fundamentus](https://fundamentus.com.br/fii_resultado.php?&interface=classic).

O `FIIsCLI.exe` publicado estava **à frente** do fonte que havia aqui: o binário
já tinha as duas carteiras fixas (`NordTickers`/`MyTickers`), os `Evaluate*` e o
`limit` na estratégia, nada disso presente no `Program.cs` anterior. Estes fontes
foram reconstruídos a partir de `FIIsCLI.exe` + `FIIsCLI.pdb` (single-file .NET 5 /
win-x64), com os nomes recuperados do PDB, e então corrigidos e migrados para
.NET 8 — ver *O que mudou* no fim.

## Uso

```
dotnet run -- --help
dotnet run -- --limit 10 --no-pause
dotnet run -- --meus
dotnet run -- --tickers HGLG11,KNRI11
```

Publicar um executável único, como o binário original:

```
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

## Estrutura

| Arquivo                  | Conteúdo                                                   |
| ------------------------ | ---------------------------------------------------------- |
| `FII.cs`                 | O record com uma linha da tabela.                           |
| `FundamentusScraper.cs`  | Download, mapeamento das colunas e parsing numérico pt-BR.  |
| `Program.cs`             | Argumentos, estratégia de ranking e impressão.              |
| `web/`                   | Saída web: SPA + backend PHP ([web/README.md](web/README.md)). |

## Estratégias

Três modos de seleção, todos sobre a mesma tabela (~576 fundos):

| Modo | Flag | O que faz |
| ---- | ---- | --------- |
| Equilibrada | *(padrão)* / `--equilibrada` | Filtros por classe de fundo + ranking por soma de posições. |
| S-Rank | `--srank` | Os cinco cortes do binário original, ordenado só por P/VP. |
| Carteira | `--tickers` / `--nord` / `--meus` | Sem filtro: lista os tickers dados e marca com `[!]` o que reprova. |

### Equilibrada

Corrige três pontos cegos do S-Rank:

1. **P/VP ganha um piso.** O S-Rank ordena por P/VP crescente, então promove a
   primeiro lugar justamente o fundo mais penalizado pelo mercado — desconto
   extremo em FII costuma ser sintoma, não oportunidade.
2. **Papel e tijolo têm réguas diferentes.** P/VP e vacância não significam a
   mesma coisa nos dois. A classificação não usa a coluna Segmento, que é
   inservível para isso ("Multicategoria" e "Outros" concentram 57% da tabela, e
   há fundo de papel rotulado "Logística"); usa a ausência de carteira física
   (`Qtd de imóveis = 0`).
3. **A distribuição precisa estar coberta pelo resultado operacional**
   (`DY <= FFO Yield x 1,5`), o que separa yield recorrente de yield inflado por
   venda de ativo ou devolução de capital.

Cortes eliminatórios, e quanto cada um descarta na prática:

| Critério | Regra | Universo |
| -------- | ----- | -------- |
| Liquidez diária | `> R$ 2 mi` | 576 -> 71 |
| Valor de mercado | `> R$ 500 mi` | 71 -> 63 |
| Dividend Yield | `>= 7%` e `<= 20%` | 63 -> 55 |
| P/VP | papel `0,80-1,05` / tijolo `0,55-1,05` | 55 -> 44 |
| Vacância *(só tijolo)* | `< 15%` | 44 -> 39 |
| Qtd de imóveis *(só tijolo)* | `>= 3` | 39 -> 34 |
| DY coberto pelo FFO | `DY <= FFO x 1,5` | 34 -> 32 |

Ordenação por **soma das posições em DY e P/VP** (estilo *magic formula*):
barato e rentável pesam igual, e usar posição em vez do valor bruto imuniza
contra o outlier que dominaria uma média ponderada. Desempate pela liquidez.

A liquidez é de longe o corte mais restritivo — sozinha elimina 88% da tabela.


## Saída web

`web/` traz a mesma leitura e as mesmas estratégias numa SPA: tabela ordenável,
filtros no cabeçalho, painel mostrando quanto cada corte elimina e tema
claro/escuro. Sem banco, sem Composer, sem build — roda em qualquer hospedagem
PHP compartilhada.

O PHP só busca o Fundamentus, normaliza e devolve JSON com cache em arquivo (o
navegador não consegue buscar direto, a origem não manda CORS); as estratégias
rodam no cliente, então trocar de estratégia é instantâneo.

`web/assets/app.js` é porte direto de `Program.cs`, e `web/lib/Fundamentus.php`
de `FundamentusScraper.cs`. **Mexeu num critério? Mexa nos dois lugares.** As
implementações foram conferidas lado a lado sobre os mesmos dados: mesmo funil,
mesmo top 10 nas duas estratégias e as mesmas marcas `[!]`.

Deploy e detalhes em [web/README.md](web/README.md).

## O que mudou em relação ao binário original

- **Colunas materializadas uma vez por linha** (`SelectNodes("./td")` + índices
  nomeados), no lugar de catorze `Descendants("td").ElementAt(n)`.
- **Linha malformada é pulada e contada**, em vez de derrubar o processo; a
  quantidade descartada é avisada no stderr.
- **`QtdDeImoveis` usa `TryParse`** — era o único campo com `int.Parse` direto.
- **Nome vem de `GetAttributeValue("title")`**, não de `Attributes[1]` por posição.
- **Parsing numérico em `CultureInfo.InvariantCulture`**: depois da normalização o
  separador decimal é `.`, e o original dependia da cultura da máquina.
- **Entidades HTML decodificadas** (`InnerText` + `DeEntitize`, no lugar de `InnerHtml`).
- **Erro de rede/layout é tratado** e vira mensagem + código de saída (1 = falha de
  leitura, 2 = argumento inválido).
- **`args` passou a ser usado** (`--limit`, `--tickers`, `--nord`, `--meus`,
  `--no-pause`), o que também tirou do limbo as duas carteiras fixas e o
  `FilterTickers`, que eram código morto.
- **Limites da estratégia viraram constantes** compartilhadas entre o filtro e a
  marcação `[!]`, para não divergirem.
- **`Console.Clear()`/`ReadKey()` só em terminal interativo**, para a saída poder
  ser redirecionada para arquivo ou pipe.
- Percentuais e valores formatados em pt-BR (`30,23%` em vez de `0,3023`).
- **Estratégia Equilibrada** adicionada e promovida a padrão; o S-Rank original
  continua disponível em `--srank`, bit a bit como estava.
- O `[!]` passou a refletir os critérios da **estratégia ativa**, e a linha de
  segmento mostra a classe inferida (`papel`/`tijolo`).
