// PhasmaStrap VPS service. No npm packages needed - Node 18 or newer.
//
// Update API: PhasmaStrap asks for the latest release (auto-update) and the release list (News
// page). Asking GitHub directly is limited to 60 requests an hour per IP address, which a few
// friends on one network run out of - then updates and the News page silently fail. This fetches
// from GitHub every few minutes and answers everyone from that copy, in GitHub's own format, so
// PhasmaStrap reads it with the same code. Downloads still come straight from GitHub.
//
//   GET /v1/releases/latest   same JSON as api.github.com/repos/<repo>/releases/latest
//   GET /v1/releases          same JSON as .../releases?per_page=30
//   GET /health               status of the cache
//
// Settings (environment variables; Pterodactyl sets SERVER_PORT from the allocation):
//   SERVER_PORT / PORT   port to listen on (default 8080)
//   REPO                 GitHub repository (default lacedsc3ne/Phasma)
//   GITHUB_TOKEN         optional; not needed at one refresh every 5 minutes
//   REFRESH_MINUTES      how often to ask GitHub (default 5)

const http = require("http");

const PORT = Number(process.env.SERVER_PORT || process.env.PORT || 8080);
const REPO = process.env.REPO || "lacedsc3ne/Phasma";
const TOKEN = process.env.GITHUB_TOKEN || "";
const REFRESH_MS = Math.max(1, Number(process.env.REFRESH_MINUTES || 5)) * 60 * 1000;

const cache = { latest: null, list: null, fetchedAt: 0, lastError: null, requests: 0 };

function log(message) {
    console.log(`${new Date().toISOString()} ${message}`);
}

async function fromGitHub(path) {
    const headers = { "User-Agent": "PhasmaStrap-VPS", Accept: "application/vnd.github+json" };
    if (TOKEN)
        headers.Authorization = `Bearer ${TOKEN}`;

    const response = await fetch(`https://api.github.com/repos/${REPO}${path}`, { headers, signal: AbortSignal.timeout(15000) });
    if (!response.ok)
        throw new Error(`GitHub ${path} answered ${response.status}`);

    const text = await response.text();
    JSON.parse(text); // only ever serve valid JSON
    return text;
}

async function refresh() {
    try {
        const [latest, list] = await Promise.all([fromGitHub("/releases/latest"), fromGitHub("/releases?per_page=30")]);
        const changed = cache.latest !== null && JSON.parse(latest).tag_name !== JSON.parse(cache.latest).tag_name;

        cache.latest = latest;
        cache.list = list;
        cache.fetchedAt = Date.now();
        cache.lastError = null;

        if (changed || cache.requests === 0)
            log(`Latest release: ${JSON.parse(latest).tag_name}`);
    } catch (error) {
        // keep serving the last good copy
        cache.lastError = error.message;
        log(`Refresh failed: ${error.message}`);
    }
}

function send(res, status, body, maxAgeSeconds = 60) {
    res.writeHead(status, {
        "Content-Type": "application/json; charset=utf-8",
        "Cache-Control": `public, max-age=${maxAgeSeconds}`,
    });
    res.end(body);
}

const server = http.createServer((req, res) => {
    const path = (req.url || "/").split("?")[0].replace(/\/+$/, "") || "/";

    if (req.method !== "GET" && req.method !== "HEAD")
        return send(res, 405, JSON.stringify({ error: "method not allowed" }), 0);

    cache.requests++;

    switch (path) {
        case "/v1/releases/latest":
            return cache.latest ? send(res, 200, cache.latest) : send(res, 503, JSON.stringify({ error: "not loaded yet" }), 0);

        case "/v1/releases":
            return cache.list ? send(res, 200, cache.list) : send(res, 503, JSON.stringify({ error: "not loaded yet" }), 0);

        case "/health":
            return send(res, 200, JSON.stringify({
                ok: cache.latest !== null,
                repo: REPO,
                latest: cache.latest ? JSON.parse(cache.latest).tag_name : null,
                fetchedAt: cache.fetchedAt ? new Date(cache.fetchedAt).toISOString() : null,
                lastError: cache.lastError,
                requestsServed: cache.requests,
            }), 0);

        default:
            return send(res, 404, JSON.stringify({ error: "not found" }), 0);
    }
});

refresh().then(() => {
    setInterval(refresh, REFRESH_MS);
    server.listen(PORT, "0.0.0.0", () => log(`PhasmaStrap VPS service listening on port ${PORT} (repo ${REPO}, refresh every ${REFRESH_MS / 60000} min)`));
});

process.on("SIGTERM", () => server.close(() => process.exit(0)));
process.on("SIGINT", () => server.close(() => process.exit(0)));
