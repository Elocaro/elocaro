// Dialog for a lost Blazor connection (markup in Components/App.razor). The framework sets the state on the element
// (class components-reconnect-show|failed|rejected|paused|resume-failed and the event components-reconnect-state-changed);
// this script opens and closes the dialog and offers "retry" and "reload".
(() => {
    const modal = document.getElementById('components-reconnect-modal');
    if (!modal) {
        return;
    }

    const open = () => {
        if (!modal.open) {
            modal.showModal();
        }
    };

    const close = () => {
        if (modal.open) {
            modal.close();
        }
    };

    modal.addEventListener('cancel', (event) => event.preventDefault());

    modal.addEventListener('components-reconnect-state-changed', (event) => {
        const state = event.detail && event.detail.state;
        if (state === 'show' || state === 'failed' || state === 'paused' || state === 'resume-failed') {
            open();
        } else if (state === 'hide') {
            close();
        } else if (state === 'rejected') {
            location.reload();
        }
    });

    const retry = async () => {
        try {
            if (!(await Blazor.reconnect())) {
                if (!(await Blazor.resumeCircuit())) {
                    location.reload();
                    return;
                }
            }

            close();
        } catch {
            location.reload();
        }
    };

    document.getElementById('components-reconnect-button')?.addEventListener('click', retry);
    document.getElementById('components-reload-button')?.addEventListener('click', () => location.reload());
    const failed = () => modal.classList.contains('components-reconnect-failed');
    document.addEventListener('visibilitychange', () => {
        if (document.visibilityState === 'visible' && failed()) {
            retry();
        }
    });
    // Back online (e.g. after a tunnel or a night with the phone asleep): reconnect without waiting for a tap.
    window.addEventListener('online', () => {
        if (failed()) {
            retry();
        }
    });
    // While the dialog says "no connection" and the page is visible, keep trying quietly; the first success closes it or reloads the page.
    setInterval(() => {
        if (failed() && document.visibilityState === 'visible' && navigator.onLine !== false) {
            retry();
        }
    }, 10000);
})();
