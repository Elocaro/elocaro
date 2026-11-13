// Offline page of the installed web app: tries again by itself and returns to the app as soon as the server answers.
(() => {
    const INTERVAL = 5;
    const status = document.getElementById('status');
    const button = document.getElementById('retry');
    let remaining = INTERVAL;
    let checking = false;

    const show = text => {
        if (status) {
            status.textContent = text;
        }
    };

    const retry = async () => {
        if (checking) {
            return;
        }

        checking = true;
        show('Verbindung wird geprüft …');
        try {
            const response = await fetch('/health/live', { cache: 'no-store' });
            if (response.ok) {
                show('Der Server antwortet. Elocaro wird geöffnet …');
                location.replace('/');
                return;
            }
        } catch {
            // still no connection: try again later
        }

        checking = false;
        remaining = INTERVAL;
        show(`Noch keine Verbindung. Nächster Versuch in ${remaining} s.`);
    };

    // The link goes to "/" (which the service worker answers with this page while offline): check at once instead
    button?.addEventListener('click', event => {
        event.preventDefault();
        retry();
    });
    window.addEventListener('online', retry);
    document.addEventListener('visibilitychange', () => {
        if (document.visibilityState === 'visible') {
            retry();
        }
    });
    setInterval(() => {
        if (checking) {
            return;
        }

        remaining -= 1;
        if (remaining <= 0) {
            retry();
        } else {
            show(`Noch keine Verbindung. Nächster Versuch in ${remaining} s.`);
        }
    }, 1000);
})();
