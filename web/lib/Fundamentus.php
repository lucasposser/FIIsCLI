<?php

declare(strict_types=1);

/**
 * Lê e normaliza a tabela de FIIs do Fundamentus.
 *
 * Porte direto de FundamentusScraper.cs: mesmas colunas, mesmo parsing
 * numérico pt-BR e a mesma regra de pular linha malformada em vez de abortar.
 */
final class Fundamentus
{
    public const SOURCE_URL = 'https://fundamentus.com.br/fii_resultado.php?&interface=classic';

    /** Ordem das colunas da tabela, como publicada pelo Fundamentus. */
    private const COL_PAPEL = 0;   // contém o <a> (ticker) e o <span title=...> (nome)
    private const COL_SEGMENTO = 1;
    private const COL_COTACAO = 2;
    private const COL_FFO_YIELD = 3;
    private const COL_DIVIDEND_YIELD = 4;
    private const COL_PVP = 5;
    private const COL_VALOR_DE_MERCADO = 6;
    private const COL_LIQUIDEZ = 7;
    private const COL_QTD_DE_IMOVEIS = 8;
    private const COL_PRECO_M2 = 9;
    private const COL_ALUGUEL_M2 = 10;
    private const COL_CAP_RATE = 11;
    private const COL_VACANCIA_MEDIA = 12;
    private const COL_ENDERECO = 13;
    private const COL_COUNT = 14;

    private const TIMEOUT_SECONDS = 25;
    private const USER_AGENT = 'FIIsWeb/1.0 (+https://github.com/; leitor de tabela pública)';

    /**
     * Baixa a página e devolve a lista de fundos já normalizada.
     *
     * @return array{items: list<array<string, mixed>>, skipped: int}
     */
    public static function load(): array
    {
        return self::parse(self::download());
    }

    /**
     * @return array{items: list<array<string, mixed>>, skipped: int}
     */
    public static function parse(string $html): array
    {
        // A página é servida em ISO-8859-1; sem isso os nomes vêm quebrados no JSON.
        if (!mb_check_encoding($html, 'UTF-8')) {
            $html = mb_convert_encoding($html, 'UTF-8', 'ISO-8859-1');
        }

        $previous = libxml_use_internal_errors(true);

        $dom = new DOMDocument();
        $dom->loadHTML(
            '<meta http-equiv="Content-Type" content="text/html; charset=utf-8">' . $html,
            LIBXML_NOERROR | LIBXML_NOWARNING
        );

        libxml_clear_errors();
        libxml_use_internal_errors($previous);

        $xpath = new DOMXPath($dom);
        $rows = $xpath->query('//tbody/tr');

        if ($rows === false || $rows->length === 0) {
            throw new RuntimeException(
                'Não encontrei as linhas da tabela — o layout do Fundamentus deve ter mudado.'
            );
        }

        $items = [];
        $skipped = 0;

        foreach ($rows as $row) {
            $fii = self::parseRow($xpath, $row);

            if ($fii === null) {
                $skipped++;
                continue;
            }

            $items[] = $fii;
        }

        if ($items === []) {
            throw new RuntimeException('A tabela veio sem nenhuma linha válida.');
        }

        return ['items' => $items, 'skipped' => $skipped];
    }

    /**
     * @return array<string, mixed>|null
     */
    private static function parseRow(DOMXPath $xpath, DOMNode $row): ?array
    {
        $cells = $xpath->query('./td', $row);

        if ($cells === false || $cells->length < self::COL_COUNT) {
            return null;
        }

        $papel = $cells->item(self::COL_PAPEL);

        // O ticker é a chave da linha: sem ele a linha não serve para nada.
        $ticker = self::text($xpath->query('.//a', $papel)->item(0));

        if ($ticker === '') {
            return null;
        }

        // O nome vem do atributo title do <span>, buscado pelo nome e não por posição.
        $span = $xpath->query('.//span', $papel)->item(0);
        $nome = $span instanceof DOMElement ? trim($span->getAttribute('title')) : '';

        $qtdDeImoveis = self::parseInt(self::text($cells->item(self::COL_QTD_DE_IMOVEIS)));

        return [
            'ticker' => $ticker,
            'nome' => $nome !== '' ? $nome : $ticker,
            'segmento' => self::text($cells->item(self::COL_SEGMENTO)),
            'cotacao' => self::parseDecimal(self::text($cells->item(self::COL_COTACAO))),
            'ffoYield' => self::parseDecimal(self::text($cells->item(self::COL_FFO_YIELD))),
            'dividendYield' => self::parseDecimal(self::text($cells->item(self::COL_DIVIDEND_YIELD))),
            'pvp' => self::parseDecimal(self::text($cells->item(self::COL_PVP))),
            'valorDeMercado' => self::parseDecimal(self::text($cells->item(self::COL_VALOR_DE_MERCADO))),
            'liquidez' => self::parseDecimal(self::text($cells->item(self::COL_LIQUIDEZ))),
            'qtdDeImoveis' => $qtdDeImoveis,
            'precoM2' => self::parseDecimal(self::text($cells->item(self::COL_PRECO_M2))),
            'aluguelM2' => self::parseDecimal(self::text($cells->item(self::COL_ALUGUEL_M2))),
            'capRate' => self::parseDecimal(self::text($cells->item(self::COL_CAP_RATE))),
            'vacanciaMedia' => self::parseDecimal(self::text($cells->item(self::COL_VACANCIA_MEDIA))),
            'endereco' => self::text($cells->item(self::COL_ENDERECO)),

            // Sem carteira física reportada = fundo de papel. A coluna Segmento
            // não serve para isso: "Multicategoria" e "Outros" concentram 57% da
            // tabela e há fundo de papel rotulado como "Logística".
            'papel' => $qtdDeImoveis === 0,
        ];
    }

    private static function text(?DOMNode $node): string
    {
        if ($node === null) {
            return '';
        }

        return trim(preg_replace('/\s+/u', ' ', $node->textContent) ?? '');
    }

    /**
     * Converte o formato brasileiro ("1.234,56", "8,50%", "-0,30%") para float.
     * Percentuais viram fração: 8,50% => 0.085.
     */
    public static function parseDecimal(string $value): float
    {
        $signal = str_contains($value, '-') ? -1 : 1;
        $scale = str_contains($value, '%') ? 0.01 : 1.0;

        $normalized = trim(str_replace(
            ['-', '%', 'R$', ' ', '.', ','],
            ['', '', '', '', '', '.'],
            $value
        ));

        return is_numeric($normalized) ? (float) $normalized * $signal * $scale : 0.0;
    }

    /** Converte inteiros com separador de milhar; 0 quando a célula vier ilegível. */
    public static function parseInt(string $value): int
    {
        $normalized = trim(str_replace(['.', ',', ' '], '', $value));

        return ctype_digit($normalized) ? (int) $normalized : 0;
    }

    private static function download(): string
    {
        if (function_exists('curl_init')) {
            return self::downloadWithCurl();
        }

        if (ini_get('allow_url_fopen')) {
            return self::downloadWithStream();
        }

        throw new RuntimeException('Nem cURL nem allow_url_fopen estão disponíveis neste servidor.');
    }

    private static function downloadWithCurl(): string
    {
        $handle = curl_init(self::SOURCE_URL);

        curl_setopt_array($handle, [
            CURLOPT_RETURNTRANSFER => true,
            CURLOPT_FOLLOWLOCATION => true,
            CURLOPT_MAXREDIRS => 3,
            CURLOPT_TIMEOUT => self::TIMEOUT_SECONDS,
            CURLOPT_CONNECTTIMEOUT => 10,
            CURLOPT_USERAGENT => self::USER_AGENT,
            CURLOPT_ENCODING => '',
            CURLOPT_HTTPHEADER => ['Accept: text/html'],
        ]);

        $body = curl_exec($handle);
        $status = (int) curl_getinfo($handle, CURLINFO_RESPONSE_CODE);
        $error = curl_error($handle);

        curl_close($handle);

        if ($body === false || $body === '') {
            $hint = '';

            // Causa comum em ambiente local/instalação manual: sem CA bundle o
            // cURL não valida o certificado. A correção é apontar curl.cainfo
            // no php.ini — nunca desligar a verificação.
            if (stripos($error, 'certificate') !== false || stripos($error, 'SSL') !== false) {
                $hint = ' (configure curl.cainfo no php.ini apontando para um CA bundle)';
            }

            throw new RuntimeException(
                'Falha de rede ao acessar o Fundamentus: ' . ($error ?: 'resposta vazia') . $hint
            );
        }

        if ($status !== 200) {
            throw new RuntimeException("O Fundamentus respondeu HTTP {$status}.");
        }

        return (string) $body;
    }

    private static function downloadWithStream(): string
    {
        $context = stream_context_create([
            'http' => [
                'method' => 'GET',
                'timeout' => self::TIMEOUT_SECONDS,
                'header' => "User-Agent: " . self::USER_AGENT . "\r\nAccept: text/html\r\n",
            ],
        ]);

        $body = @file_get_contents(self::SOURCE_URL, false, $context);

        if ($body === false || $body === '') {
            throw new RuntimeException('Falha de rede ao acessar o Fundamentus.');
        }

        return $body;
    }
}
