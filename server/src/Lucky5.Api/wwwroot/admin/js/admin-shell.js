/*
 * admin-shell.js
 * Shared behaviour for every page inside the admin dashboard shell:
 *   - session guard (redirect to login.html if not authenticated)
 *   - sidebar nav active-state highlighting
 *   - top bar username display + logout wiring
 *   - a small toast helper reused by later admin pages
 *
 * Depends on admin-api.js being loaded first (window.AdminSession, window.AdminApi).
 * Include on every admin/*.html page except login.html.
 */
(function attachLucky5AdminShell(global) {
    'use strict';

    function currentPageName() {
        var path = global.location.pathname || '';
        var segments = path.split('/').filter(Boolean);
        var last = segments.length ? segments[segments.length - 1] : '';
        return last || 'index.html';
    }

    function guardSession() {
        var session = (global.AdminSession && global.AdminSession.isValid()) ? global.AdminSession.get() : null;
        var role = session && session.profile && session.profile.role ? String(session.profile.role).toLowerCase() : '';
        if (!session || role !== 'admin') {
            if (global.AdminSession) global.AdminSession.clear();
            global.location.href = 'login.html';
            return null;
        }
        return session;
    }

    function highlightActiveNav() {
        var page = currentPageName();
        var links = document.querySelectorAll('.admin-nav-link[href]');
        links.forEach(function (link) {
            var href = link.getAttribute('href') || '';
            var normalizedHref = href.split('/').filter(Boolean).pop() || 'index.html';
            if (normalizedHref === page) {
                link.classList.add('is-active');
            } else {
                link.classList.remove('is-active');
            }
        });
    }

    function renderUserChip(session) {
        var usernameEl = document.getElementById('admin-username');
        var avatarEl = document.getElementById('admin-user-avatar');
        var username = (session && session.profile && (session.profile.username || session.profile.displayName)) || 'admin';
        if (usernameEl) usernameEl.textContent = username;
        if (avatarEl) avatarEl.textContent = username.charAt(0).toUpperCase();
    }

    function wireLogout(session) {
        var logoutBtn = document.getElementById('admin-logout-btn');
        if (!logoutBtn) return;

        logoutBtn.addEventListener('click', function onLogoutClick(event) {
            event.preventDefault();
            logoutBtn.disabled = true;

            var finish = function finishLogout() {
                if (global.AdminSession) global.AdminSession.clear();
                global.location.href = 'login.html';
            };

            if (global.AdminApi && typeof global.AdminApi.logout === 'function') {
                global.AdminApi.logout().then(finish).catch(finish);
            } else {
                finish();
            }
        });
    }

    function ensureToastContainer() {
        var container = document.getElementById('admin-toast-container');
        if (!container) {
            container = document.createElement('div');
            container.id = 'admin-toast-container';
            container.className = 'admin-toast-container';
            document.body.appendChild(container);
        }
        return container;
    }

    function showToast(message, type, durationMs) {
        var container = ensureToastContainer();
        var toast = document.createElement('div');
        var toastType = type === 'error' ? 'admin-toast-error' : (type === 'success' ? 'admin-toast-success' : '');
        toast.className = 'admin-toast ' + toastType;
        toast.textContent = message;
        container.appendChild(toast);

        global.setTimeout(function removeToast() {
            if (toast.parentNode) toast.parentNode.removeChild(toast);
        }, durationMs || 4000);
    }

    function init() {
        var session = guardSession();
        if (!session) return null;

        highlightActiveNav();
        renderUserChip(session);
        wireLogout(session);

        return session;
    }

    global.AdminShell = {
        init: init,
        guardSession: guardSession,
        highlightActiveNav: highlightActiveNav,
        showToast: showToast
    };

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})(window);
