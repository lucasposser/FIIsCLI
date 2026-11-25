<?php

declare(strict_types=1);

/**
 * Único endpoint da aplicação: devolve a tabela inteira de FIIs em JSON.
 *
 * As estratégias são aplicadas no cliente — o servidor só busca, normaliza e
 * guarda em cache de arquivo. Sem banco, sem dependências.
 *
 *   GET api/fiis.php            cache quando fresco, busca quando vencido
 *   GET api/fiis.php?refresh=1  força a busca (respeitando um piso de 60s)
 */

require __DIR__ . '/../lib/Fundamentus.php';

const CACHE_TTL_SECONDS = 1200;      // 20 min — a tabela é diária, não intradiária
const REFRESH_FLOOR_SECONDS = 60;    // piso entre buscas forçadas, para não martelar a origem
const CACHE_DIR = __DIR__ . '/../cache';
const CACHE_FILE = CACHE_DIR . '/fiis.json';

header('Content-Type: application/json; charset=utf-8');
header('Cache-Control: no-store');
header('X-Content-Type-Options: nosniff');

$cached = readCache();
$age = $cached === null ? null : time() - (int) $cached['fetchedAt'];
$forced = isset($_GET['refresh']) && $_GET['refresh'] !== '0';

$needsFetch = $cached === null
    || $age > CACHE_TTL_SECONDS
    || ($forced && $age > REFRESH_FLOOR_SECONDS);

if (!$needsFetch) {
    respond($cached, $forced ? 'floor' : 'hit', $age);
}

try {
    $result = Fundamentus::load();

    $payload = [
        'fetchedAt' => time(),
        'source' => Fundamentus::SOURCE_URL,
        'skipped' => $result['skipped'],
        'items' => $result['items'],
    ];

    writeCache($payload);
    respond($payload, 'miss', 0);
} catch (Throwable $e) {
    // Origem fora do ar não derruba o site: serve o cache vencido e avisa.
    if ($cached !== null) {
        $cached['warning'] = 'Dados do cache: ' . $e->getMessage();
        respond($cached, 'stale', $age);
    }

    http_response_code(502);
    echo json_encode([
        'error' => $e->getMessage(),
        'source' => Fundamentus::SOURCE_URL,
    ], JSON_UNESCAPED_UNICODE);
}

/**
 * @param array<string, mixed> $payload
 */
function respond(array $payload, string $cacheState, ?int $age): never
{
    $payload['cache'] = [
        'state' => $cacheState,
        'ageSeconds' => $age,
        'ttlSeconds' => CACHE_TTL_SECONDS,
    ];

    header('X-Cache: ' . $cacheState);

    echo json_encode($payload, JSON_UNESCAPED_UNICODE | JSON_PRESERVE_ZERO_FRACTION);
    exit;
}

/**
 * @return array<string, mixed>|null
 */
function readCache(): ?array
{
    if (!is_readable(CACHE_FILE)) {
        return null;
    }

    $raw = file_get_contents(CACHE_FILE);

    if ($raw === false || $raw === '') {
        return null;
    }

    $decoded = json_decode($raw, true);

    if (!is_array($decoded) || !isset($decoded['fetchedAt'], $decoded['items']) || !is_array($decoded['items'])) {
        return null;
    }

    return $decoded;
}

/**
 * @param array<string, mixed> $payload
 */
function writeCache(array $payload): void
{
    if (!is_dir(CACHE_DIR) && !@mkdir(CACHE_DIR, 0775, true) && !is_dir(CACHE_DIR)) {
        return; // sem cache gravável o site segue funcionando, só mais lento
    }

    $json = json_encode($payload, JSON_UNESCAPED_UNICODE | JSON_PRESERVE_ZERO_FRACTION);

    if ($json === false) {
        return;
    }

    // Escrita atômica: um GET concorrente nunca lê um JSON pela metade.
    $temp = CACHE_FILE . '.' . bin2hex(random_bytes(4)) . '.tmp';

    if (@file_put_contents($temp, $json, LOCK_EX) !== false) {
        if (!@rename($temp, CACHE_FILE)) {
            @unlink($temp);
        }
    }
}
