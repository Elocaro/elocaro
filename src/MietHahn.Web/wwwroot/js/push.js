// Switch for the notifications of this device (account page).
(async function () {
    const status = document.getElementById('push-status');
    const button = document.getElementById('push-toggle');
    if (!status || !button) {
        return;
    }

    const supported = 'serviceWorker' in navigator && 'PushManager' in window && 'Notification' in window && window.isSecureContext;
    if (!supported) {
        status.textContent = 'Dieser Browser oder diese Verbindung unterstützt keine Benachrichtigungen. Auf dem iPhone muss MietHahn zum Home-Bildschirm hinzugefügt sein.';
        return;
    }

    const json = { 'Content-Type': 'application/json' };
    const post = (url, body) => fetch(url, { method: 'POST', headers: json, credentials: 'same-origin', body: JSON.stringify(body) });
    const key = base64 => {
        const padded = (base64 + '='.repeat((4 - base64.length % 4) % 4)).replace(/-/g, '+').replace(/_/g, '/');
        return Uint8Array.from(atob(padded), c => c.charCodeAt(0));
    };

    let registration;
    try {
        registration = await navigator.serviceWorker.ready;
    } catch (e) {
        status.textContent = 'Der Hintergrunddienst konnte nicht gestartet werden.';
        return;
    }

    let subscription = await registration.pushManager.getSubscription();
    let on = false;
    if (subscription) {
        const response = await post('/push/status', { endpoint: subscription.endpoint });
        on = response.ok && (await response.json()).subscribed;
    }

    const render = message => {
        status.textContent = message || (on ? 'Benachrichtigungen sind auf diesem Gerät eingeschaltet.' : 'Benachrichtigungen sind auf diesem Gerät ausgeschaltet.');
        button.textContent = on ? 'Benachrichtigungen ausschalten' : 'Benachrichtigungen einschalten';
        button.hidden = false;
    };
    render();

    button.addEventListener('click', async () => {
        button.disabled = true;
        let message;
        try {
            if (on) {
                if (subscription) {
                    await post('/push/abmelden', { endpoint: subscription.endpoint });
                    await subscription.unsubscribe();
                    subscription = null;
                }
                on = false;
            } else if (await Notification.requestPermission() !== 'granted') {
                message = 'Die Benachrichtigungen sind im Browser nicht erlaubt. Sie können das in den Einstellungen des Browsers ändern.';
            } else {
                const publicKey = await (await fetch('/push/schluessel', { credentials: 'same-origin' })).text();
                subscription = await registration.pushManager.subscribe({ userVisibleOnly: true, applicationServerKey: key(publicKey) });
                const data = subscription.toJSON();
                const response = await post('/push/abonnieren', { endpoint: data.endpoint, p256dh: data.keys.p256dh, auth: data.keys.auth });
                if (response.ok) {
                    on = true;
                } else {
                    await subscription.unsubscribe();
                    subscription = null;
                    message = 'Der Push-Dienst dieses Browsers wird nicht unterstützt.';
                }
            }
        } catch (e) {
            message = 'Das hat nicht geklappt. Bitte versuchen Sie es später noch einmal.';
        } finally {
            button.disabled = false;
            render(message);
        }
    });
})();
