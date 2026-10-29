// Registers the service worker (offline notice, hints).
if ('serviceWorker' in navigator && window.isSecureContext) {
    window.addEventListener('load', () => {
        navigator.serviceWorker.register('/sw.js', { scope: '/' }).catch(() => { /* no service worker, the app works without */ });
    });
}
