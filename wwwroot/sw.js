const CACHE = 'nexusm-v560';
const SHELL = [
    '/',
    '/css/app.css?v=279',
    '/js/app.js?v=558',
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
    const { pathname } = new URL(e.request.url);

    // Never intercept: streaming, API, DLNA, HLS segments, templates
    if (pathname.startsWith('/api/') ||
        pathname.startsWith('/dlna/') ||
        pathname.startsWith('/hls/') ||
        pathname.startsWith('/templates/')) {
        return;
    }

    // Navigation requests: network first, fall back to cached shell
    if (e.request.mode === 'navigate') {
        e.respondWith(
            fetch(e.request).catch(() => caches.match('/'))
        );
        return;
    }

    // Core app shell (app.js / app.css): network-first so a new build is picked
    // up immediately even if the cache version wasn't bumped. Falls back to cache
    // only when offline. Prevents stale-JS-after-update issues like the missing
    // Cast button (a cache-first strategy can keep serving an old script).
    if (pathname === '/js/app.js' || pathname === '/css/app.css') {
        e.respondWith(
            fetch(e.request).then(res => {
                if (res.ok) {
                    const clone = res.clone();
                    caches.open(CACHE).then(c => c.put(e.request, clone));
                }
                return res;
            }).catch(() => caches.match(e.request))
        );
        return;
    }

    // Static assets: cache first, update cache in background
    e.respondWith(
        caches.match(e.request).then(cached => {
            const fresh = fetch(e.request).then(res => {
                if (res.ok) {
                    const clone = res.clone(); // clone synchronously before any async gap
                    caches.open(CACHE).then(c => c.put(e.request, clone));
                }
                return res;
            });
            return cached || fresh;
        })
    );
});
