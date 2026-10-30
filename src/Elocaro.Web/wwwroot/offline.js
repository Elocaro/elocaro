// Offline page of the installed web app: tries again by itself and returns to the app as soon as the server answers.
(() => {
    const retry = async () => {
        try {
            const response = await fetch('/health/live', { cache: 'no-store' });
            if (response.ok) {
                location.replace('/');
            }
        } catch {
            // still no connection: try again later
        }
    };

    window.addEventListener('online', retry);
    document.addEventListener('visibilitychange', () => {
        if (document.visibilityState === 'visible') {
            retry();
        }
    });
    setInterval(retry, 5000);
})();
