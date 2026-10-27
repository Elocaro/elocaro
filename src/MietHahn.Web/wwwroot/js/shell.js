// Behavior of the app frame that CSS cannot do: close menus, reset the scroll position of the content area on
// navigation, bring the active tab into view. Everything else works without JavaScript.
(function () {
    // Called from Blazor pages: bring a section into view (gently, unless the user prefers reduced motion).
    window.miethahnScrollTo = (id) => {
        const target = document.getElementById(id);
        if (target) {
            target.scrollIntoView({ block: 'start', behavior: matchMedia('(prefers-reduced-motion: reduce)').matches ? 'auto' : 'smooth' });
        }
    };

    const closeMenus = (except) => {
        document.querySelectorAll('details.mobile-navigation[open], details.user-menu[open]').forEach((menu) => {
            if (menu !== except) {
                menu.removeAttribute('open');
            }
        });
    };

    const revealActiveTab = () => {
        document.querySelectorAll('.tab-bar a.active').forEach((tab) => {
            tab.scrollIntoView({ block: 'nearest', inline: 'center' });
        });
    };

    document.addEventListener('click', (event) => {
        const target = event.target instanceof Element ? event.target : null;
        closeMenus(target ? target.closest('details.mobile-navigation, details.user-menu') : null);
        if (target && target.closest('details.mobile-navigation a')) {
            closeMenus(null);
        }
    });

    // Info dialogs (Components/Common/InfoDialog.razor): native <dialog>, so focus handling and Esc come from the browser.
    document.addEventListener('click', (event) => {
        const target = event.target instanceof Element ? event.target : null;
        if (!target) {
            return;
        }

        const opener = target.closest('[data-info-dialog]');
        if (opener) {
            const dialog = document.getElementById(opener.getAttribute('data-info-dialog'));
            if (dialog && typeof dialog.showModal === 'function' && !dialog.open) {
                dialog.showModal();
            }
            return;
        }

        if (target.closest('[data-info-close]')) {
            target.closest('dialog')?.close();
            return;
        }

        // A click on the backdrop lands on the dialog element itself.
        if (target instanceof HTMLDialogElement && target.classList.contains('info-dialog')) {
            target.close();
        }
    });

    // Insert buttons (template editor): put the text at the cursor of the target field; Blazor takes the value over via input/change.
    document.addEventListener('click', (event) => {
        const button = event.target instanceof Element ? event.target.closest('[data-insert]') : null;
        const area = button ? document.querySelector(button.getAttribute('data-insert-target') || 'textarea') : null;
        if (!button || !area) {
            return;
        }

        const text = button.getAttribute('data-insert');
        const start = area.selectionStart ?? area.value.length;
        const end = area.selectionEnd ?? start;
        area.setRangeText(text, start, end, 'end');
        area.focus();
        area.dispatchEvent(new Event('input', { bubbles: true }));
        area.dispatchEvent(new Event('change', { bubbles: true }));
    });

    document.addEventListener('keydown', (event) => {
        if (event.key === 'Escape') {
            closeMenus(null);
        }

        // Ctrl+K / Cmd+K opens the quick search
        if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'k') {
            const trigger = Array.from(document.querySelectorAll('.quick-search-trigger')).find((button) => button.offsetParent !== null);
            if (trigger) {
                event.preventDefault();
                trigger.click();
            }
        }
    });

    const afterNavigation = () => {
        closeMenus(null);
        const content = document.getElementById('content');
        if (content && !location.hash) {
            content.scrollTop = 0;
        }
        revealActiveTab();
    };

    // Slow page changes (enhanced navigation): CSS shows a progress bar at the top after a short delay.
    const navigating = (on) => document.documentElement.classList.toggle('is-navigating', on);
    const watchNavigation = () => {
        window.Blazor.addEventListener('enhancednavigationstart', () => navigating(true));
        window.Blazor.addEventListener('enhancednavigationend', () => navigating(false));
    };

    if (window.Blazor && typeof window.Blazor.addEventListener === 'function') {
        watchNavigation();
        window.Blazor.addEventListener('enhancednavigationend', afterNavigation);
    } else {
        document.addEventListener('DOMContentLoaded', () => {
            if (window.Blazor && typeof window.Blazor.addEventListener === 'function') {
                watchNavigation();
                window.Blazor.addEventListener('enhancednavigationend', afterNavigation);
            }
        });
    }

    document.addEventListener('DOMContentLoaded', revealActiveTab);

    // Tables become card lists on smartphones: every cell gets the text of its column header as a label
    // (CSS shows it below 700 px). Tables with merged cells stay tables.
    const labelTables = () => {
        document.querySelectorAll('table.data-table').forEach((table) => {
            if (table.hasAttribute('data-nostack') || table.querySelector('[rowspan], [colspan]')) {
                return;
            }

            const heads = Array.from(table.querySelectorAll('thead th')).map((th) => th.textContent.trim());
            if (heads.length === 0) {
                return;
            }

            table.classList.add('stackable');
            table.querySelectorAll('tbody tr:not([data-labelled])').forEach((row) => {
                row.dataset.labelled = '1';
                Array.from(row.children).forEach((cell, index) => {
                    if (cell.tagName === 'TD' && heads[index]) {
                        cell.dataset.label = heads[index];
                    }
                });
            });
        });
    };

    let scheduled = false;
    new MutationObserver(() => {
        if (!scheduled) {
            scheduled = true;
            requestAnimationFrame(() => {
                scheduled = false;
                labelTables();
            });
        }
    }).observe(document.documentElement, { childList: true, subtree: true });
    document.addEventListener('DOMContentLoaded', labelTables);
})();
