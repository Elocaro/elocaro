// Service worker of the installed web app: an offline notice instead of the browser error page, and hints from the server.
// The push message is empty; title and text are fetched from the server (so they never pass through the push service).
const CACHE = 'miethahn-offline-v1';
const OFFLINE = '/offline.html';

self.addEventListener('install', event => {
    event.waitUntil(caches.open(CACHE).then(cache => cache.add(OFFLINE)));
    self.skipWaiting();
});

self.addEventListener('activate', event => {
    event.waitUntil(caches.keys().then(keys => Promise.all(keys.filter(k => k !== CACHE).map(k => caches.delete(k)))).then(() => self.clients.claim()));
});

self.addEventListener('fetch', event => {
    if (event.request.mode === 'navigate') {
        event.respondWith(fetch(event.request).catch(() => caches.match(OFFLINE)));
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
        hints = [{ title: 'MietHahn', body: 'Es gibt Neuigkeiten.', url: '/' }];
    }
    await Promise.all(hints.map(h => self.registration.showNotification(h.title, {
        body: h.body,
        icon: '/icon.svg',
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
