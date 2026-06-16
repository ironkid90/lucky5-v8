/**
 * cabinet-firebase.js — Firebase integration stub
 * -------------------------------------------------
 * This module is a soft dependency. The cabinet gracefully degrades
 * when Firebase is not configured. game.js checks `window.CabinetFirebase`
 * before calling any methods.
 *
 * To enable: provide a firebase config via GET /api/config/firebase
 * and the enterLobbyAfterLogin handler will call CabinetFirebase.init().
 */
(function () {
    'use strict';

    window.CabinetFirebase = {
        initialized: false,

        init: function () {
            if (this.initialized) return;
            var cfg = window.LUCKY5_FIREBASE_CONFIG;
            if (!cfg || !cfg.configured) return;
            // Firebase initialization would go here if needed.
            // The current Lucky5 cabinet does not require Firebase for gameplay.
            this.initialized = true;
            if (typeof console !== 'undefined') {
                console.log('[Lucky5] Firebase stub initialized (no-op).');
            }
        },
    };
})();
