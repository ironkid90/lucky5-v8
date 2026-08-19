/*
 * admin-shell.js
 * Shared dashboard shell behavior reused by every admin/*.html page:
 *   - session guard (redirects to login.html when unauthenticated)
 *   - sidebar nav active-state highlighting
 *   - logout button wiring
 *   - a tiny toast helper matching the `.admin-toast` styles in admin.css
 *
 * Depends on admin-api.js being loaded first (window.AdminApi).
 */
(function attachLucky5AdminShell(global) {
    'use strict';

    const document = global.document;

    function guardSession() {
        const adminApi = global.AdminApi;
        const isLoginPage = /(^|\/)login\.html$/i.test(global.location.pathname);

        if (isLoginPage) return true;

        if (!adminApi || !adminApi.isAuthenticated()) {
            global.location.href = 'login.html';
            return false;
        }

        return true;
    }

    function applyUsername() {
        const adminApi = global.AdminApi;
        const profile = adminApi?.getCurrentProfile?.();
        const username = profile?.username || profile?.displayName || 'admin';

        document.querySelectorAll('[data-admin-username]').forEach((el) => {
            el.textContent = username;
        });
    }

    function wireLogout() {
        const adminApi = global.AdminApi;

        document.querySelectorAll('[data-admin-logout]').forEach((button) => {
            button.addEventListener('click', async (event) => {
                event.preventDefault();
                button.disabled = true;
                try {
                    await adminApi?.logout?.();
                } catch (_) {
                    // Ignore logout transport errors; we clear the local session regardless.
                } finally {
                    adminApi?.clearSession?.();
                    global.location.href = 'login.html';
                }
            });
        });
    }

    function setActiveNavLink() {
        const currentFile = (global.location.pathname.split('/').pop() || 'index.html').toLowerCase() || 'index.html';

        document.querySelectorAll('[data-admin-nav-link]').forEach((link) => {
            const href = (link.getAttribute('href') || '').toLowerCase();
            const isActive = href === currentFile;
            link.classList.toggle('is-active', isActive);
            if (isActive) {
                link.setAttribute('aria-current', 'page');
            } else {
                link.removeAttribute('aria-current');
            }
        });
    }

    function showToast(message, type = 'info', durationMs = 4000) {
        let region = document.getElementById('admin-toast-region');
        if (!region) {
            region = document.createElement('div');
            region.id = 'admin-toast-region';
            region.className = 'admin-toast-region';
            region.setAttribute('aria-live', 'polite');
            document.body.appendChild(region);
        }

        const toast = document.createElement('div');
        toast.className = `admin-toast admin-toast-${type}`;
        toast.textContent = message;
        region.appendChild(toast);

        global.setTimeout(() => {
            toast.classList.add('is-leaving');
            global.setTimeout(() => toast.remove(), 200);
        }, durationMs);

        return toast;
    }

    function initShell() {
        if (!guardSession()) return;
        applyUsername();
        wireLogout();
        setActiveNavLink();
    }

    global.Lucky5AdminShell = {
        guardSession,
        applyUsername,
        wireLogout,
        setActiveNavLink,
        showToast,
        initShell
    };

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', initShell);
    } else {
        initShell();
    }
})(window);
