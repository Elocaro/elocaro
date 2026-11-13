// Service worker of the installed web app: an offline notice instead of the browser error page, and hints from the server.
// The push message is empty; title and text are fetched from the server (so they never pass through the push service).
const CACHE = 'elocaro-offline-v3';
const OFFLINE = '/offline.html';
// What the offline page needs besides itself (script, font)
const OFFLINE_ASSETS = ['/offline.js', '/fonts/roboto-flex-latin.woff2'];

self.addEventListener('install', event => {
    // The page is required; script and font are best effort (a missing font must not stop the installation)
    event.waitUntil(caches.open(CACHE).then(async cache => {
        await cache.add(OFFLINE);
        await Promise.all(OFFLINE_ASSETS.map(asset => cache.add(asset).catch(() => undefined)));
    }));
    self.skipWaiting();
});

self.addEventListener('activate', event => {
    event.waitUntil(caches.keys().then(keys => Promise.all(keys.filter(k => k !== CACHE).map(k => caches.delete(k)))).then(() => self.clients.claim()));
});

self.addEventListener('fetch', event => {
    if (event.request.mode === 'navigate') {
        event.respondWith(fetch(event.request).catch(() => caches.match(OFFLINE)));
    } else {
        // the offline page itself needs its script and font while there is no connection
        const path = new URL(event.request.url).pathname;
        if (OFFLINE_ASSETS.includes(path)) {
            event.respondWith(fetch(event.request).catch(() => caches.match(path)));
        }
    }
});

self.addEventListener('push', event => {
    event.waitUntil(showHints());
});

async function showHints() {
    let hints = [];
    try {
        const response = await fetch('/push/ausstehend', { credentials: 'same-origin', headers: { Accept: 'application/json' } });
        if (response.ok) {
            hints = await response.json();
        }
    } catch (e) {
        // offline or signed out: the generic hint below
    }
    if (!hints.length) {
        hints = [{ title: 'Elocaro', body: 'Es gibt Neuigkeiten.', url: '/' }];
    }
    await Promise.all(hints.map(h => self.registration.showNotification(h.title, {
        body: h.body,
        icon: '/icon-192.png',
        tag: h.url,
        data: { url: h.url },
    })));
}

self.addEventListener('notificationclick', event => {
    event.notification.close();
    const url = (event.notification.data && event.notification.data.url) || '/';
    event.waitUntil(self.clients.matchAll({ type: 'window', includeUncontrolled: true }).then(windows => {
        for (const client of windows) {
            if ('focus' in client) {
                client.navigate(url);
                return client.focus();
            }
        }
        return self.clients.openWindow(url);
    }));
});
