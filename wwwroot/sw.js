// NexusM service worker.
//
// Update model - why a client can never be stuck on an old build:
//  - index.html and sw.js are served with no-store and fetched network-first below, so every
//    page load sees the CURRENT references (app.js?v=N, app.css?v=N).
//  - app.js / app.css are cached ONLY under their exact versioned URL (cache-first). A new
//    build references a new ?v=, which is a cache miss and is fetched from the network; the
//    old entries die with the old cache name on activate. RULE (CLAUDE.md): bump the ?v= in
//    index.html AND in SHELL below on every JS/CSS change, and bump CACHE with it.
//  - The page asks the browser to re-check sw.js when the tab regains focus and every 30 min
//    (App._swInitUpdateWatch); the browser also does it on its own every 24 h. A changed sw.js
//    installs, takes over immediately (skipWaiting + clients.claim) and the page shows a
//    "new version - reload" banner. Nothing reloads automatically.
//  - Images, thumbnails, language files and everything else are NOT intercepted: the browser
//    HTTP cache (ETag / Last-Modified revalidation) handles them. The previous cache-first +
//    background refetch doubled image traffic and grew Cache Storage without bound.
const CACHE = 'nexusm-v853';
const SHELL = [
    '/',
    '/css/app.css?v=414',
    '/js/app.js?v=823',
    '/favicon.svg',
    '/icon-512.png',
    '/manifest.json',
];

self.addEventListener('install', e => {
    e.waitUntil(
        caches.open(CACHE)
            .then(c => c.addAll(SHELL))
            .then(() => self.skipWaiting())
    );
});

self.addEventListener('activate', e => {
    e.waitUntil(
        caches.keys()
            .then(keys => Promise.all(
                keys.filter(k => k !== CACHE).map(k => caches.delete(k))
            ))
            .then(() => self.clients.claim())
    );
});

self.addEventListener('fetch', e => {
    if (e.request.method !== 'GET') return;
    const url = new URL(e.request.url);
    if (url.origin !== self.location.origin) return;
    const { pathname, search } = url;

    // Never intercept: streaming, API, DLNA, HLS segments, templates
    if (pathname.startsWith('/api/') ||
        pathname.startsWith('/dlna/') ||
        pathname.startsWith('/hls/') ||
        pathname.startsWith('/templates/')) {
        return;
    }

    // Navigation requests (index.html): network first, and force {cache:'no-store'} so
    // the request BYPASSES the browser HTTP cache. Without this, network-first still let
    // the browser return a stale HTTP-cached index.html (referencing an old app.js?v=N),
    // so a new build was only picked up after a manual hard-refresh. On success we refresh
    // the cached '/' shell too, so the offline fallback never goes ancient. Falls back to
    // the cached shell only when the network is unavailable.
    if (e.request.mode === 'navigate') {
        e.respondWith(
            fetch(e.request, { cache: 'no-store' }).then(res => {
                if (res && res.ok) {
                    const clone = res.clone();
                    caches.open(CACHE).then(c => c.put('/', clone));
                }
                return res;
            }).catch(() => caches.match('/'))
        );
        return;
    }

    // Core app shell (app.js / app.css) WITH a ?v= query: cache-first keyed by the exact
    // versioned URL. index.html (never cached) decides which version is asked for, so a new
    // build is a new URL, a cache miss, and a network fetch. Without ?v= (never the case in
    // the app itself) fall through to the network so nothing unversioned gets pinned.
    if ((pathname === '/js/app.js' || pathname === '/css/app.css') && /[?&]v=/.test(search)) {
        e.respondWith(
            caches.match(e.request).then(cached => cached || fetch(e.request).then(res => {
                if (res.ok) {
                    const clone = res.clone(); // clone synchronously before any async gap
                    caches.open(CACHE).then(c => c.put(e.request, clone));
                }
                return res;
            }))
        );
        return;
    }

    // Small precached shell files (icons, manifest): cache-first, network fallback.
    if (SHELL.includes(pathname)) {
        e.respondWith(caches.match(e.request).then(cached => cached || fetch(e.request)));
        return;
    }

    // Everything else (posters, thumbnails, album art, lang files, fonts...): not intercepted.
});
