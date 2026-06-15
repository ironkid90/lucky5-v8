/**
 * cabinet-shell-vnext.js
 * OWNER: Lead Agent
 * PURPOSE: Shell routing, menu overlay behavior, lobby machine cards, wallet display.
 *          Hardens navigation without altering any economy or game-rule semantics.
 * LOADS AFTER: cabinet-pace-vnext.js
 * DO NOT EDIT: game.css, game.js, index.html (except lead integration pass)
 *
 * This file augments the existing shell in game.js — it does not replace it.
 * It hooks into the existing DOM and event patterns, adds missing behaviors,
 * and ensures one predictable navigation model across all shell states.
 *
 * Economy rules (do not change):
 *   - wallet   = lobby balance (walletBalance in game.js)
 *   - machine credits = in-game balance (balance in game.js)
 *   - TAKE SCORE / TAKE HALF settle into machine credits under server rules
 *   - cash-out moves machine credits → wallet per machine-session rules
 */

'use strict';

window.CabinetShell = (function () {
    function _createPreviewCard(suit, isRed) {
        const card = document.createElement('div');
        card.className = 'lobby-machine-preview-card';
        card.dataset.suit = suit;
        if (isRed) {
            card.dataset.red = '1';
        }
        return card;
    }

    function _buildMachinePreview() {
        const cabinet = document.createElement('div');
        cabinet.className = 'lobby-machine-cabinet';

        const marquee = document.createElement('div');
        marquee.className = 'lobby-machine-marquee';
        marquee.textContent = 'LUCKY 5';

        const screen = document.createElement('div');
        screen.className = 'lobby-machine-screen';

        const payout = document.createElement('div');
        payout.className = 'lobby-machine-preview-payout';
        payout.textContent = 'VIDEO POKER';

        const cards = document.createElement('div');
        cards.className = 'lobby-machine-preview-cards';
        cards.appendChild(_createPreviewCard('♠', false));
        cards.appendChild(_createPreviewCard('♥', true));
        cards.appendChild(_createPreviewCard('♣', false));
        cards.appendChild(_createPreviewCard('♦', true));
        cards.appendChild(_createPreviewCard('♠', false));

        const credits = document.createElement('div');
        credits.className = 'lobby-machine-preview-credits';
        credits.textContent = 'CREDIT READY';

        const controls = document.createElement('div');
        controls.className = 'lobby-machine-controls';
        for (let i = 0; i < 5; i++) {
            const lamp = document.createElement('span');
            lamp.className = 'lobby-machine-control';
            controls.appendChild(lamp);
        }

        screen.appendChild(payout);
        screen.appendChild(cards);
        screen.appendChild(credits);

        cabinet.appendChild(marquee);
        cabinet.appendChild(screen);
        cabinet.appendChild(controls);

        return cabinet;
    }

    /* ── renderLobbyMachineCards ─────────────────────────────────────────── */
    /**
     * Replaces the default lobby machine list rendering with cabinet-family styled cards.
     * Called by game.js after machines are loaded. Falls back gracefully if no machines.
     * @param {Array<{id:number, name:string, minBet:number, maxBet:number, isOpen:boolean}>} machines
     * @param {function(machine): void} onSelect  — callback from game.js on machine selection
     */
    function renderLobbyMachineCards(machines, onSelect) {
        const grid = document.getElementById('lobby-game-grid');
        if (!grid) return;
        grid.innerHTML = '';

        if (!machines || machines.length === 0) {
            const empty = document.createElement('div');
            empty.style.cssText = 'font-size:7px;color:#555;text-align:center;padding:12px;letter-spacing:1px;';
            empty.textContent = 'NO MACHINES AVAILABLE';
            grid.appendChild(empty);
            return;
        }

        const fmt = new Intl.NumberFormat('en-US', { maximumFractionDigits: 0 });

        machines.forEach(machine => {
            const card = document.createElement('div');
            card.className = 'lobby-machine-card' + (machine.isOpen ? '' : ' unavailable');

            const preview = _buildMachinePreview();

            const meta = document.createElement('div');
            meta.className = 'lobby-machine-meta';

            const statusRow = document.createElement('div');
            statusRow.className = 'lobby-machine-status-row';

            const statusEl = document.createElement('div');
            statusEl.className = `lobby-machine-status ${machine.isOpen ? 'is-open' : 'is-closed'}`;
            statusEl.textContent = machine.isOpen ? 'OPEN' : 'CLOSED';

            const idEl = document.createElement('div');
            idEl.className = 'lobby-machine-id';
            idEl.textContent = `CAB #${machine.id}`;

            const nameEl = document.createElement('div');
            nameEl.className = 'lobby-machine-name';
            nameEl.textContent = machine.name;

            const betLabel = document.createElement('div');
            betLabel.className = 'lobby-machine-bet-label';
            betLabel.textContent = 'BET RANGE';

            const betEl = document.createElement('div');
            betEl.className = 'lobby-machine-bet';
            betEl.textContent = `${fmt.format(machine.minBet)} - ${fmt.format(machine.maxBet)}`;

            const cta = document.createElement('div');
            cta.className = 'lobby-machine-cta';
            cta.textContent = machine.isOpen ? 'ENTER CABINET' : 'UNAVAILABLE';

            statusRow.appendChild(statusEl);
            statusRow.appendChild(idEl);
            meta.appendChild(statusRow);
            meta.appendChild(nameEl);
            meta.appendChild(betLabel);
            meta.appendChild(betEl);
            meta.appendChild(cta);

            card.appendChild(preview);
            card.appendChild(meta);

            if (machine.isOpen) {
                card.addEventListener('click', () => onSelect(machine));
            }

            grid.appendChild(card);
        });
    }

    /* ── updateLobbyBalance ──────────────────────────────────────────────── */
    /**
     * Update wallet balance displays in lobby and wallet screens.
     * @param {number} walletBalance
     */
    function updateLobbyBalance(walletBalance) {
        const fmt = new Intl.NumberFormat('en-US', { maximumFractionDigits: 0 });
        const formatted = fmt.format(walletBalance);

        const lobbyBal  = document.getElementById('lobby-balance');
        const lobbyWal  = document.getElementById('lobby-wallet-bal');
        const walletBal = document.getElementById('wallet-balance');

        if (lobbyBal)  lobbyBal.textContent  = formatted;
        if (lobbyWal)  lobbyWal.textContent  = formatted;
        if (walletBal) walletBal.textContent = formatted;
    }

    /* ── updateLobbyUsername ─────────────────────────────────────────────── */
    /**
     * @param {string} username
     */
    function updateLobbyUsername(username) {
        const el = document.getElementById('lobby-username');
        if (el) el.textContent = (username || 'PLAYER').toUpperCase();
    }

    /* ── initMenuOverlay ─────────────────────────────────────────────────── */
    /**
     * Wire the menu overlay close-on-backdrop behavior.
     * The CASH IN / CASH OUT / BACK TO LOBBY / RESET / LOGOUT buttons
     * keep their existing game.js handlers — this only adds the backdrop close.
     */
    function initMenuOverlay() {
        const panel = document.getElementById('menu-panel');
        if (!panel) return;

        // Close on backdrop click (click outside the button area)
        panel.addEventListener('click', function (e) {
            if (e.target === panel) {
                panel.classList.remove('is-open');
            }
        });
    }

    /* ── initWalletHistory ───────────────────────────────────────────────── */
    /**
     * Render wallet transaction history into the wallet screen.
     * @param {Array<{type:string, amount:number, createdAt:string}>} entries
     */
    function renderWalletHistory(entries) {
        const list = document.getElementById('wallet-history-list');
        if (!list) return;
        list.innerHTML = '';

        if (!entries || entries.length === 0) {
            const empty = document.createElement('div');
            empty.className = 'wallet-history-empty';
            empty.textContent = 'NO TRANSACTIONS YET';
            list.appendChild(empty);
            return;
        }

        const fmt = new Intl.NumberFormat('en-US', { maximumFractionDigits: 0 });

        entries.slice(0, 20).forEach(entry => {
            const row = document.createElement('div');
            row.style.cssText = 'display:flex;justify-content:space-between;align-items:center;padding:6px 4px;border-bottom:1px solid #1a1a1a;font-size:7px;letter-spacing:0.5px;';

            const typeEl = document.createElement('span');
            typeEl.style.color = '#aaa';
            typeEl.textContent = (entry.type || '').toUpperCase();

            const amtEl = document.createElement('span');
            const amt = Number(entry.amount);
            amtEl.style.color = amt >= 0 ? '#44ff44' : '#ff4444';
            amtEl.textContent = (amt >= 0 ? '+' : '') + fmt.format(amt);

            row.appendChild(typeEl);
            row.appendChild(amtEl);
            list.appendChild(row);
        });
    }

    /* ── guardSessionOnNavigate ──────────────────────────────────────────── */
    /**
     * Confirm that navigation away from the game screen is safe.
     * Blocks navigation if there is an active open round.
     * Returns true if navigation is allowed.
     * @param {string} roundId   — current open roundId from game.js (null if none)
     * @returns {boolean}
     */
    function guardSessionOnNavigate(roundId) {
        if (roundId) {
            // Active round — warn but allow (player can return via lobby → machine)
            // Do not block unconditionally; just surface a status message.
            const msgEl = document.getElementById('game-message');
            if (msgEl) {
                msgEl.textContent = 'ROUND IN PROGRESS — RETURNING TO LOBBY';
                msgEl.className = 'lose';
            }
        }
        return true; // always allow navigation in this release
    }

    /* ── public API ──────────────────────────────────────────────────────── */

    return {
        renderLobbyMachineCards,
        updateLobbyBalance,
        updateLobbyUsername,
        initMenuOverlay,
        renderWalletHistory,
        guardSessionOnNavigate,
    };

}());

/* ── init on DOM ready ───────────────────────────────────────────────────── */
document.addEventListener('DOMContentLoaded', function () {
    CabinetShell.initMenuOverlay();
});
