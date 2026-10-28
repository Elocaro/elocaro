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
    document.addEventListener('visibilitychange', () => {
        if (document.visibilityState === 'visible' && modal.classList.contains('components-reconnect-failed')) {
            retry();
        }
    });
})();
