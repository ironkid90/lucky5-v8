/**
 * cabinet-stage-vnext.js
 * OWNER: Codex Agent 2
 * PURPOSE: Card stage choreography, hold-lamp state, button press assets, double-up viewport.
 * LOADS AFTER: game.js
 * DO NOT EDIT: game.css, game.js, index.html, any .cs backend files
 */

'use strict';

window.CabinetStage = (function () {
    const DEFAULT_MAX_TRAIL_PER_PAGE = 4;

    function _resolveConfig(overrides) {
        const cfg = (typeof GAME_CONFIG !== 'undefined') ? GAME_CONFIG : null;
        const timing = cfg && cfg.timing ? cfg.timing : {};
        const assets = cfg && cfg.assets ? cfg.assets : {};

        // Arcade-calibrated defaults (2026-06-16 classic feel).
        // Slower, more deliberate pacing that simulates old mechanical hardware.
        const next = {
            cardBack: assets.cardBack || '/assets/images/cards/bside.png',
            dealBaseMs: Number(timing.dealBaseMs) || 120,
            dealStaggerMs: Number(timing.dealStaggerMs) || 180,
            dealDurationMs: Number(timing.dealAnimDurationMs) || 300,
            drawOutMs: Number(timing.drawOutMs) || 100,
            drawInMs: Number(timing.drawInMs) || 150,
            drawStaggerMs: Number(timing.drawStaggerMs) || 100,
            drawRevealStartMs: Number(timing.drawRevealStartMs) || 120,
            shuffleFrameMs: Number(timing.shuffleFrameMs) || 130,
            lucky5ActiveMs: Number(timing.lucky5FlashDurationMs) || 1000
        };

        if (overrides && typeof overrides === 'object') {
            Object.assign(next, overrides);
        }

        return next;
    }

    let _config = _resolveConfig();
    let _shuffleInterval = null;
    let _isDoubleUpMode = false;
    let _lucky5Timer = null;
    let _duTrailCards = [];
    let _duDealerCard = null;

    function _normalizeSuit(value) {
        if (!value) return '';
        const text = String(value).trim().toUpperCase();
        if (!text) return '';
        const first = text.charAt(0);
        if ('CDHS'.includes(first)) return first;
        return '';
    }

    function _normalizeRank(value) {
        if (value == null) return '';
        const text = String(value).trim().toUpperCase();
        if (!text) return '';
        if (text === 'T' || text === '10') return '10';
        if (/^[2-9]$/.test(text)) return text;
        if ('JQKA'.includes(text)) return text;
        return '';
    }

    function _normalizeCode(inputCode, rank, suit) {
        const parsedSuit = _normalizeSuit(suit);
        const parsedRank = _normalizeRank(rank);

        if (parsedRank && parsedSuit) {
            return `${parsedRank}${parsedSuit}`;
        }

        if (!inputCode) {
            return '';
        }

        const text = String(inputCode).trim().toUpperCase();
        if (!text) {
            return '';
        }

        const codeSuit = _normalizeSuit(text.slice(-1));
        const codeRank = _normalizeRank(text.slice(0, -1));
        if (codeRank && codeSuit) {
            return `${codeRank}${codeSuit}`;
        }

        return '';
    }

    function _asCard(input) {
        if (!input) return null;

        if (typeof input === 'string') {
            const code = _normalizeCode(input, '', '');
            if (!code) return null;
            return {
                code,
                rank: code.slice(0, -1),
                suit: code.slice(-1)
            };
        }

        if (input.code || (input.rank && input.suit)) {
            const code = _normalizeCode(input.code, input.rank, input.suit);
            if (!code) return null;
            return {
                code,
                rank: _normalizeRank(input.rank) || code.slice(0, -1),
                suit: _normalizeSuit(input.suit) || code.slice(-1)
            };
        }

        return null;
    }

    function _animateRAF(duration, easingFn, onFrame, onComplete) {
        let start = null;
        function frame(time) {
            if (!start) start = time;
            let progress = (time - start) / duration;
            if (progress > 1) progress = 1;
            
            const eased = easingFn(progress);
            onFrame(eased);
            
            if (progress < 1) {
                requestAnimationFrame(frame);
            } else if (onComplete) {
                onComplete();
            }
        }
        requestAnimationFrame(frame);
    }

    function _setFaceDiagnostic(slotEl, hasError, reason) {
        if (!slotEl) return;

        slotEl.classList.toggle('v8-face-error', Boolean(hasError));

        if (hasError && reason) {
            slotEl.dataset.faceError = reason;
            return;
        }

        delete slotEl.dataset.faceError;
    }

    function resolveCardFaceSrc(cardLike) {
        const card = _asCard(cardLike);
        return card && card.code
            ? `/assets/images/cards/${card.code}.png`
            : _config.cardBack;
    }

    function _cardSrc(code) {
        return resolveCardFaceSrc(code);
    }

    function _applyCardFace(slotEl, img, cardLike, options) {
        const card = _asCard(cardLike);
        const requireFace = Boolean(options && options.requireFace);

        if (!slotEl || !img) {
            return null;
        }

        img.onerror = null;
        img.onload = null;

        if (!card || !card.code) {
            img.src = _config.cardBack;
            img.alt = requireFace ? 'Missing card face' : 'Card back';
            _setFaceDiagnostic(slotEl, requireFace, requireFace ? 'missing-card-code' : '');
            return null;
        }

        _setFaceDiagnostic(slotEl, false, '');
        img.onload = function handleFaceLoad() {
            _setFaceDiagnostic(slotEl, false, '');
        };
        img.onerror = function handleFaceError() {
            _setFaceDiagnostic(slotEl, true, `missing-face:${card.code}`);
            img.alt = `${card.code} missing face`;
        };
        img.src = resolveCardFaceSrc(card);
        img.alt = card.code;
        return card;
    }

    function _allCardCodes() {
        return Array.isArray(window.ALL_CARD_CODES) && window.ALL_CARD_CODES.length > 0
            ? window.ALL_CARD_CODES
            : [];
    }

    function _pickShuffleCode(codes, previousCode) {
        if (!Array.isArray(codes) || codes.length === 0) {
            return '';
        }

        if (codes.length === 1) {
            return codes[0];
        }

        let nextCode = previousCode;
        while (nextCode === previousCode) {
            nextCode = codes[Math.floor(Math.random() * codes.length)];
        }

        return nextCode;
    }

    function _slot(index) {
        return document.querySelector(`.card-slot[data-slot="${index}"]`);
    }

    function _cardImg(slotEl) {
        return slotEl ? slotEl.querySelector('.card-face img') : null;
    }

    function _holdBtn(index) {
        return document.querySelector(`#hold-row .cab-hold[data-index="${index}"]`);
    }

    function _duSlot(index) {
        return document.querySelector(`.du-card-slot[data-du-slot="${index}"]`);
    }

    function _duFrame(slotEl) {
        return slotEl ? slotEl.querySelector('.du-card-frame') : null;
    }

    function _duImg(slotEl) {
        return slotEl ? slotEl.querySelector('img') : null;
    }

    function _duLabel(slotEl) {
        return slotEl ? slotEl.querySelector('.du-card-label') : null;
    }

    function _asTrailEntry(input) {
        if (!input) return null;

        if (input.card || input.label) {
            const card = _asCard(input.card || input);
            if (!card) return null;
            return {
                card,
                label: String(input.label || '').trim().toUpperCase()
            };
        }

        const card = _asCard(input);
        return card ? { card, label: '' } : null;
    }

    // _setButtonBackground and _buttonAsset removed to allow pure CSS buttons

    function _stopShuffle() {
        if (_shuffleInterval) {
            clearInterval(_shuffleInterval);
            _shuffleInterval = null;
        }

        document.querySelectorAll('.du-card-slot.du-shuffling').forEach(slotEl => {
            slotEl.classList.remove('du-shuffling');
        });
    }

    function _ensureMainSlots() {
        const area = document.getElementById('card-area');
        if (!area) return false;

        if (_isDoubleUpMode || area.querySelectorAll('.card-slot').length !== 5) {
            initCardSlots();
        }

        return true;
    }

    function _resetMainSlot(slotEl) {
        if (!slotEl) return;

        slotEl.classList.remove('held', 'lucky5-active');
        _setFaceDiagnostic(slotEl, false, '');
        slotEl.style.transition = 'none';
        slotEl.style.transform = 'translateY(0)';
        slotEl.style.opacity = '1';

        const face = slotEl.querySelector('.card-face');
        if (face) {
            face.style.transition = 'none';
            face.style.opacity = '1';
        }
    }

    function _ensureDoubleUpSlots() {
        const area = document.getElementById('card-area');
        if (!area) return false;

        area.classList.add('du-mode');
        if (!_isDoubleUpMode || area.querySelectorAll('.du-card-slot').length !== 5) {
            area.innerHTML = '';

            for (let i = 0; i < 5; i++) {
                const slot = document.createElement('div');
                slot.className = 'du-card-slot';
                slot.dataset.duSlot = i;

                const label = document.createElement('div');
                label.className = 'du-card-label';

                const frame = document.createElement('div');
                frame.className = 'du-card-frame';

                const img = document.createElement('img');
                img.src = _config.cardBack;
                img.alt = 'Card back';

                frame.appendChild(img);
                slot.appendChild(label);
                slot.appendChild(frame);
                area.appendChild(slot);
            }
        }

        _isDoubleUpMode = true;
        return true;
    }

    function _clearDoubleUpSlots() {
        for (let i = 0; i < 5; i++) {
            const slotEl = _duSlot(i);
            const frame = _duFrame(slotEl);
            const label = _duLabel(slotEl);
            const img = _duImg(slotEl);

            if (!slotEl || !frame || !label || !img) continue;

            slotEl.classList.remove('du-trail-card', 'du-shuffling', 'du-chall-in', 'du-challenger-card', 'lucky5-active');
            _setFaceDiagnostic(slotEl, false, '');
            frame.classList.remove('dealer-card', 'lucky5-glow', 'du-flip-in', 'du-flip-out');
            label.textContent = '';
            img.src = _config.cardBack;
            img.alt = 'Card back';
        }
    }

    function _getVisibleDoubleUpWindow(trailCards, dealerCard) {
        const normalizedTrail = Array.isArray(trailCards)
            ? trailCards.map(_asTrailEntry).filter(Boolean)
            : [];
        const normalizedDealer = _asCard(dealerCard);

        const maxTrailPerPage = Math.max(1, Number(window.GAME_CONFIG?.doubleUp?.maxTrailPerPage) || DEFAULT_MAX_TRAIL_PER_PAGE);
        const carryStep = Math.max(1, maxTrailPerPage - 1);

        let startIndex = 0;
        if (normalizedTrail.length > maxTrailPerPage) {
            const overshoot = normalizedTrail.length - maxTrailPerPage;
            const pages = Math.ceil(overshoot / carryStep);
            startIndex = pages * carryStep;
        }

        const visibleTrail = normalizedTrail.slice(startIndex);
        const sequence = visibleTrail.slice();
        let dealerIndex = -1;

        if (normalizedDealer) {
            dealerIndex = Math.min(visibleTrail.length, 4);
            sequence.push({
                card: normalizedDealer,
                label: 'DEALER'
            });
        }

        return {
            sequence: sequence.slice(0, 5),
            dealerIndex,
            revealIndex: Math.min(sequence.length, 4)
        };
    }

    function _statusLabel(status) {
        switch (String(status || '').toLowerCase()) {
            case 'win':
                return 'WIN';
            case 'lose':
                return 'LOSE';
            case 'push':
                return 'SAFE';
            default:
                return '';
        }
    }

    function _renderDoubleUpSequence(sequence, dealerIndex, revealIndex, options) {
        if (!_ensureDoubleUpSlots()) return;

        const opts = (typeof options === 'string')
            ? { outcome: options }
            : (options && typeof options === 'object' ? options : {});
        const revealedLabel = String(opts.challengerLabel || '').trim().toUpperCase()
            || _statusLabel(opts.outcome || opts.status);

        _clearDoubleUpSlots();

        for (let i = 0; i < 5; i++) {
            const slotEl = _duSlot(i);
            const frame = _duFrame(slotEl);
            const label = _duLabel(slotEl);
            const img = _duImg(slotEl);
            const entry = sequence[i] || null;
            const card = entry && entry.card ? entry.card : null;
            const entryLabel = entry && entry.label ? String(entry.label).trim().toUpperCase() : '';

            if (!slotEl || !frame || !label || !img) continue;

            frame.classList.remove('du-flip-in', 'du-flip-out');

            if (card) {
                _applyCardFace(slotEl, img, card, { requireFace: true });
            } else {
                _applyCardFace(slotEl, img, null, { requireFace: false });
            }

            if (card && i < dealerIndex) {
                slotEl.classList.add('du-trail-card');
                label.textContent = entryLabel || 'PLAYED';
            }

            if (card && i === dealerIndex) {
                frame.classList.add('dealer-card');
                label.textContent = 'DEALER';
            }

            if (!card && revealIndex === i) {
                label.textContent = 'BIG / SMALL ?';
            }

            if (card && revealIndex == null && i === dealerIndex + 1) {
                slotEl.classList.add('du-challenger-card');
                label.textContent = revealedLabel || entryLabel || label.textContent;
            }

            if (card && card.code === '5S') {
                frame.classList.add('lucky5-glow');
            }
        }
    }

    function _beginSequentialShuffle(trailCards, dealerCard, options) {
        _stopShuffle();

        const view = _getVisibleDoubleUpWindow(trailCards, dealerCard);
        _renderDoubleUpSequence(view.sequence, view.dealerIndex, view.revealIndex, Object.assign({ pending: true }, options || {}));

        const slotEl = _duSlot(view.revealIndex);
        const img = _duImg(slotEl);
        const codes = _allCardCodes();

        if (!slotEl || !img || codes.length === 0) {
            return;
        }

        slotEl.classList.add('du-shuffling');

        const frameMs = Math.max(60, Number(_config.shuffleFrameMs) || 80);
        const frameEl = _duFrame(slotEl);
        let lastCode = '';
        _shuffleInterval = setInterval(() => {
            const code = _pickShuffleCode(codes, lastCode);
            lastCode = code;

            if (frameEl) {
                frameEl.classList.remove('du-flip-in');
                frameEl.classList.add('du-flip-out');
            }

            setTimeout(() => {
                img.src = resolveCardFaceSrc(code);
                img.alt = `${code} shuffle`;

                if (frameEl) {
                    frameEl.classList.remove('du-flip-out');
                    frameEl.classList.add('du-flip-in');
                }
            }, Math.min(60, Math.max(30, Math.round(frameMs * 0.45))));
        }, frameMs);
    }

    function configure(overrides) {
        _config = _resolveConfig(overrides);
        return getConfig();
    }

    function getConfig() {
        return {
            cardBack: _config.cardBack,
            dealBaseMs: _config.dealBaseMs,
            dealStaggerMs: _config.dealStaggerMs,
            dealDurationMs: _config.dealDurationMs,
            drawOutMs: _config.drawOutMs,
            drawInMs: _config.drawInMs,
            drawStaggerMs: _config.drawStaggerMs,
            drawRevealStartMs: _config.drawRevealStartMs,
            shuffleFrameMs: _config.shuffleFrameMs,
            lucky5ActiveMs: _config.lucky5ActiveMs
        };
    }

    function renderHand(cardArray, heldIndexes) {
        if (!_ensureMainSlots()) return;

        _stopShuffle();

        const cards = Array.isArray(cardArray) ? cardArray.map(_asCard) : [];
        const held = new Set(Array.isArray(heldIndexes) ? heldIndexes : Array.from(heldIndexes || []));

        clearAllHolds();

        for (let i = 0; i < 5; i++) {
            const slotEl = _slot(i);
            const img = _cardImg(slotEl);

            if (!slotEl || !img) continue;

            _resetMainSlot(slotEl);
            _applyCardFace(slotEl, img, cards[i], { requireFace: true });
            setHold(i, held.has(i));
        }
    }

    function initCardSlots() {
        const area = document.getElementById('card-area');
        if (!area) return;

        _stopShuffle();
        area.classList.remove('du-mode');
        area.innerHTML = '';

        for (let i = 0; i < 5; i++) {
            const slot = document.createElement('div');
            slot.className = 'card-slot';
            slot.dataset.slot = i;

            const face = document.createElement('div');
            face.className = 'card-face';

            const img = document.createElement('img');
            img.src = _config.cardBack;
            img.alt = 'Card back';

            face.appendChild(img);

            const badge = document.createElement('div');
            badge.className = 'hold-badge';
            badge.textContent = 'HOLD';

            slot.appendChild(face);
            slot.appendChild(badge);
            area.appendChild(slot);
        }

        _duTrailCards = [];
        _duDealerCard = null;
        _isDoubleUpMode = false;
    }

    function dealCards(cardArray, onComplete) {
        if (!_ensureMainSlots()) return;

        _stopShuffle();
        clearAllHolds();

        const cards = Array.isArray(cardArray) ? cardArray.map(_asCard) : [];
        const baseDelay = Math.max(0, Number(_config.dealBaseMs) || 0);
        const stagger = Math.max(40, Number(_config.dealStaggerMs) || 100);
        const duration = Math.max(80, Number(_config.dealDurationMs) || 120);

        cards.forEach((card, i) => {
            const slotEl = _slot(i);
            const img = _cardImg(slotEl);
            if (!slotEl || !img) return;

            _resetMainSlot(slotEl);
            _applyCardFace(slotEl, img, card, { requireFace: true });
            slotEl.style.transform = 'translateY(-60px)';
        });

        setTimeout(() => requestAnimationFrame(() => {
            cards.forEach((card, i) => {
                setTimeout(() => {
                    const slotEl = _slot(i);
                    if (!slotEl) return;

                    _animateRAF(duration, p => 1 - Math.pow(1 - p, 2), eased => {
                        const y = -60 * (1 - eased);
                        slotEl.style.transform = `translateY(${y}px)`;
                    }, () => {
                        slotEl.style.transform = 'translateY(0)';
                        if (i === cards.length - 1 && onComplete) {
                            setTimeout(onComplete, 40);
                        }
                    });
                }, i * stagger);
            });
        }), baseDelay);
    }

    function drawCards(newCardArray, heldIndexes, onComplete) {
        if (!_ensureMainSlots()) return;

        _stopShuffle();

        const held = new Set(Array.isArray(heldIndexes) ? heldIndexes : Array.from(heldIndexes || []));
        const cards = Array.isArray(newCardArray) ? newCardArray.map(_asCard) : [];
        let pending = 0;

        const outMs = Math.max(40, Number(_config.drawOutMs) || 60);
        const inMs = Math.max(60, Number(_config.drawInMs) || 80);
        const staggerMs = Math.max(20, Number(_config.drawStaggerMs) || 40);
        const revealStartMs = Math.max(0, Number(_config.drawRevealStartMs) || 0);

        cards.forEach((card, i) => {
            const slotEl = _slot(i);
            const img = _cardImg(slotEl);
            const face = slotEl ? slotEl.querySelector('.card-face') : null;

            if (!slotEl || !img) return;

            if (held.has(i)) {
                slotEl.classList.add('held');
                _applyCardFace(slotEl, img, card, { requireFace: true });
                if (face) {
                    face.style.transition = 'none';
                    face.style.opacity = '1';
                }
                return;
            }

            pending++;
            slotEl.classList.remove('held');

            setTimeout(() => {
                if (face) {
                    face.style.transition = `opacity ${outMs}ms ease-in`;
                    face.style.opacity = '0';
                }

                setTimeout(() => {
                    _applyCardFace(slotEl, img, card, { requireFace: true });

                    if (face) {
                        face.style.transition = `opacity ${inMs}ms ease-out`;
                        face.style.opacity = '1';
                    }

                    pending--;
                    if (pending === 0 && onComplete) {
                        onComplete();
                    }
                }, outMs);
            }, revealStartMs + (i * staggerMs));
        });

        if (pending === 0 && onComplete) {
            onComplete();
        }
    }

    function setHold(slotIndex, isHeld) {
        const slotEl = _slot(slotIndex);
        if (slotEl) slotEl.classList.toggle('held', isHeld);

        const btn = _holdBtn(slotIndex);
        if (btn) {
            btn.classList.toggle('active', isHeld);
            btn.setAttribute('aria-label', isHeld ? 'HOLD ON' : 'HOLD OFF');
            btn.title = isHeld ? 'HOLD' : '';
        }
    }

    function clearAllHolds() {
        for (let i = 0; i < 5; i++) {
            setHold(i, false);
        }
    }

    function initButtonAssets() {
        // Disabled to allow pure CSS buttons from cabinet-v8-quality.css
    }

    function enterDoubleUp(dealerCard, trailCards = []) {
        _duTrailCards = Array.isArray(trailCards)
            ? trailCards.map(_asTrailEntry).filter(Boolean)
            : [];
        _duDealerCard = _asCard(dealerCard);
        _beginSequentialShuffle(_duTrailCards, _duDealerCard, { pending: true });
    }

    function updateDoubleUpTrail(trailCards, dealerCard, challengerCard, statusOrOptions) {
        _duTrailCards = Array.isArray(trailCards)
            ? trailCards.map(_asTrailEntry).filter(Boolean)
            : [];
        _duDealerCard = _asCard(dealerCard);

        const options = (typeof statusOrOptions === 'string')
            ? { outcome: statusOrOptions }
            : (statusOrOptions && typeof statusOrOptions === 'object' ? statusOrOptions : {});

        if (!challengerCard) {
            _beginSequentialShuffle(_duTrailCards, _duDealerCard, options);
            return;
        }

        _stopShuffle();

        const challenger = _asCard(challengerCard);
        const view = _getVisibleDoubleUpWindow(_duTrailCards, _duDealerCard);
        const sequence = view.sequence.slice(0, view.revealIndex);
        sequence[view.revealIndex] = {
            card: challenger,
            label: String(options.challengerLabel || '').trim().toUpperCase()
        };

        _renderDoubleUpSequence(sequence, view.dealerIndex, null, Object.assign({}, options, { pending: false }));

        const revealSlot = _duSlot(view.revealIndex);
        if (revealSlot) {
            revealSlot.classList.add('du-chall-in');
        }

        if (challenger && challenger.code === '5S') {
            showLucky5Active();
        }
    }

    function shuffleChallenger() {
        _beginSequentialShuffle(_duTrailCards, _duDealerCard, { pending: true });
    }

    function exitDoubleUp() {
        _stopShuffle();
        _isDoubleUpMode = false;
        _duTrailCards = [];
        _duDealerCard = null;

        const area = document.getElementById('card-area');
        if (area) area.classList.remove('du-mode');

        initCardSlots();
    }

    function showLucky5Active() {
        const banner = document.getElementById('lucky5-banner');
        if (banner) {
            banner.classList.add('active');
            if (_lucky5Timer) clearTimeout(_lucky5Timer);
            _lucky5Timer = setTimeout(() => {
                banner.classList.remove('active');
                _lucky5Timer = null;
            }, Math.max(200, Number(_config.lucky5ActiveMs) || 700));
        }

        document.querySelectorAll('.card-slot, .du-card-slot').forEach(slotEl => {
            slotEl.classList.add('lucky5-active');
            setTimeout(() => slotEl.classList.remove('lucky5-active'), Math.max(200, Number(_config.lucky5ActiveMs) || 700));
        });
    }

    return {
        configure,
        getConfig,
        resolveCardFaceSrc,
        initCardSlots,
        renderHand,
        dealCards,
        drawCards,
        setHold,
        clearAllHolds,
        initButtonAssets,
        enterDoubleUp,
        updateDoubleUpTrail,
        shuffleChallenger,
        exitDoubleUp,
        showLucky5Active,
        isDoubleUpMode: function() { return _isDoubleUpMode; }
    };
}());

// Global getter for game.js - reads from CabinetStage module
function isDoubleUpMode() {
    if (window.CabinetStage && typeof window.CabinetStage.isDoubleUpMode === 'function') {
        return window.CabinetStage.isDoubleUpMode();
    }
    // Fallback: check for du-mode class on document
    return document.querySelector('.du-mode') !== null;
}
