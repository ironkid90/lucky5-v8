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

        // AI9-calibrated defaults — VSYNC-locked frame counts at 60Hz.
        // One global staggerFrames drives deal and draw.
        const next = {
            cardBack: assets.cardBack || '/assets/images/cards/bside.png',
            staggerFrames:        Number(timing.staggerFrames)        || 12,
            dealBaseFrames:       Number(timing.dealBaseFrames)       || 5,
            dealDurationFrames:   Number(timing.dealDurationFrames)   || 11,
            drawStaggerFrames:    Number(timing.drawStaggerFrames || timing.staggerFrames) || 12,
            drawOutFrames:        Number(timing.drawOutFrames)        || 1,
            drawDurationFrames:   Number(timing.drawDurationFrames || timing.dealDurationFrames) || 11,
            drawRevealStartFrames:Number(timing.drawRevealStartFrames !== undefined ? timing.drawRevealStartFrames : (timing.dealBaseFrames || 5)) || 5,
            shuffleFrameMs: Number(timing.shuffleFrameMs) || 130,
            lucky5ActiveMs: Number(timing.lucky5FlashDurationMs) || 1000
        };

        if (overrides && typeof overrides === 'object') {
            Object.assign(next, overrides);
        }

        return next;
    }

    let _config = _resolveConfig();
    let _isDoubleUpMode = false;
    let _duTrailCards = [];
    let _duDealerCard = null;
    let _activeDealToken = null;
    let _activeDrawToken = null;
    let _activeShuffleToken = null;
    let _lucky5Token = null;

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
        const totalTicks = window.CabinetClock.msToTicks(duration);
        let elapsedTicks = 0;

        const tickHandler = function(tickCount) {
            elapsedTicks++;
            const progress = Math.min(elapsedTicks / totalTicks, 1);
            const eased = easingFn(progress);
            onFrame(eased);

            if (progress >= 1) {
                window.CabinetClock.unregisterHandler(tickHandler);
                if (onComplete) onComplete();
            }
        };

        window.CabinetClock.registerHandler(tickHandler);
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

    const SUIT_SYMBOLS = {
        'H': '♥',
        'D': '♦',
        'C': '♣',
        'S': '♠'
    };

    const _cardTemplateCache = {};

    function _getCardTemplate(inputCard) {
            const card = _asCard(inputCard);
            const code = card && card.code ? card.code.toUpperCase() : 'BACK';

            if (_cardTemplateCache[code]) {
                return _cardTemplateCache[code].cloneNode(true);
            }

            const template = document.createElement('div');
            template.style.width = '100%';
            template.style.height = '100%';
            template.style.display = 'block';

            if (code === 'BACK') {
                // Use the bside.png asset
                template.innerHTML = `<img src="/assets/images/cards/bside.png" class="card-back-pattern" style="width:100%; height:100%; object-fit:contain; display:block;" />`;
            } else {
                template.innerHTML = `<img src="/assets/images/cards/${code}.png" class="card-front" style="width:100%; height:100%; object-fit:contain; display:block;" />`;
            }

            _cardTemplateCache[code] = template.firstElementChild;
            return _cardTemplateCache[code].cloneNode(true);
        }

    function _precacheAllCards() {
        const codes = _allCardCodes();
        codes.forEach(c => _getCardTemplate(c));
        _getCardTemplate(null); // Precache back
    }

    function _renderDomCard(inputCard) {
        // Fallback for string-based callers (e.g. initCardSlots)
        const node = _getCardTemplate(inputCard);
        return node.outerHTML;
    }

    function _applyCardFace(slotEl, faceContainer, cardLike, options) {
        const card = _asCard(cardLike);
        const requireFace = Boolean(options && options.requireFace);

        if (!slotEl || !faceContainer) {
            return null;
        }

        if (!card || !card.code) {
            faceContainer.replaceChildren(_getCardTemplate(null));
            _setFaceDiagnostic(slotEl, requireFace, requireFace ? 'missing-card-code' : '');
            return null;
        }

        _setFaceDiagnostic(slotEl, false, '');
        faceContainer.replaceChildren(_getCardTemplate(card));
        return card;
    }

    function _allCardCodes() {
        if (Array.isArray(window.ALL_CARD_CODES) && window.ALL_CARD_CODES.length > 0) {
            return window.ALL_CARD_CODES;
        }
        const suits = ['H','D','C','S'];
        const ranks = ['2','3','4','5','6','7','8','9','10','J','Q','K','A'];
        const fallback = [];
        for (const r of ranks) {
            for (const s of suits) fallback.push(r + s);
        }
        return fallback;
    }

    function _createPresentationRandom(noise) {
        const values = [
            Number(noise?.suspenseMs),
            Number(noise?.revealMs),
            Number(noise?.flipFrames),
            Number(noise?.pulseFrames)
        ];
        if (!values.some(Number.isFinite)) return Math.random;

        let state = 2166136261;
        values.forEach(value => {
            state ^= (Number.isFinite(value) ? Math.trunc(value) : 0) >>> 0;
            state = Math.imul(state, 16777619) >>> 0;
        });

        return () => {
            state = (Math.imul(state, 1664525) + 1013904223) >>> 0;
            return state / 4294967296;
        };
    }

    function _pickShuffleCode(codes, previousCode, nextRandom = Math.random) {
        if (!Array.isArray(codes) || codes.length === 0) {
            return '';
        }

        if (codes.length === 1) {
            return codes[0];
        }

        for (let attempt = 0; attempt < codes.length * 2; attempt++) {
            const nextCode = codes[Math.floor(nextRandom() * codes.length)];
            if (nextCode !== previousCode) {
                return nextCode;
            }
        }

        const previousIndex = codes.indexOf(previousCode);
        return codes[(previousIndex + 1) % codes.length];
    }

    function _slot(index) {
        return document.querySelector(`.card-slot[data-slot="${index}"]`);
    }

    function _cardImg(slotEl) {
        return slotEl ? slotEl.querySelector('.card-face') : null;
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
        return slotEl ? slotEl.querySelector('.du-card-frame') : null;
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
        _activeShuffleToken = null;
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

        slotEl.classList.remove('held', 'lucky5-active', 'card-pre-deal', 'card-pre-draw', 'card-deal-thump', 'card-draw-thump');
        _setFaceDiagnostic(slotEl, false, '');
        // Clear any leftover inline animation styles — CSS classes own the state now
        slotEl.style.transition = '';
        slotEl.style.transform = '';
        slotEl.style.opacity = '';

        const face = slotEl.querySelector('.card-face');
        if (face) {
            face.style.transition = '';
            face.style.opacity = '';
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
                frame.innerHTML = _renderDomCard(null);

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
            img.innerHTML = _renderDomCard(null);
        }
    }

    function _getVisibleDoubleUpWindow(trailCards, dealerCard, isPending) {
        const normalizedTrail = Array.isArray(trailCards)
            ? trailCards.map(_asTrailEntry).filter(Boolean)
            : [];
        const normalizedDealer = _asCard(dealerCard);

        const sequence = Array(5).fill(null);
        let dealerIndex = -1;
        let revealIndex = -1;

        const L = normalizedTrail.length;

        if (L > 0) {
            const start = Math.max(0, Math.floor((L - 1) / 4) * 4);
            
            for (let i = 0; i < 5; i++) {
                if (start + i < L) {
                    sequence[i] = normalizedTrail[start + i];
                }
            }
            
            dealerIndex = L - 1 - start;
            if (dealerIndex >= 0 && dealerIndex < 5 && sequence[dealerIndex]) {
                sequence[dealerIndex].label = 'DEALER';
            }
            
            for (let i = 0; i < dealerIndex; i++) {
                if (sequence[i]) {
                    sequence[i].label = 'PLAYED';
                }
            }
            
            const nextIndex = L - start;
            if (nextIndex < 5) {
                revealIndex = nextIndex;
                if (isPending === false && normalizedDealer) {
                    sequence[revealIndex] = {
                        card: normalizedDealer,
                        label: ''
                    };
                }
            }
        } else if (normalizedDealer) {
            sequence[0] = {
                card: normalizedDealer,
                label: 'DEALER'
            };
            dealerIndex = 0;
            revealIndex = 1;
        }

        return {
            sequence,
            dealerIndex,
            revealIndex
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

    function syncDoubleUpTrailFromServer(trailCards, dealerCard, isPending, outcome, options) {
        const key = JSON.stringify({ trailCards, dealerCard, isPending, outcome, options });
        if (_lastSyncKey === key) return;
        _lastSyncKey = key;

        const view = _getVisibleDoubleUpWindow(trailCards, dealerCard, isPending);
        _renderDoubleUpSequence(view.sequence, view.dealerIndex, view.revealIndex, Object.assign({ outcome }, options || {}));
    }

    function _beginSequentialShuffle(trailCards, dealerCard, options) {
        _stopShuffle();

        const view = _getVisibleDoubleUpWindow(trailCards, dealerCard, true);
        _renderDoubleUpSequence(view.sequence, view.dealerIndex, view.revealIndex, Object.assign({ pending: true }, options || {}));

        const slotEl = _duSlot(view.revealIndex);
        const img = _duImg(slotEl);
        const codes = _allCardCodes();

        if (!slotEl || !img || codes.length === 0) {
            return;
        }

        slotEl.classList.add('du-shuffling');

        const frameMs = Number(_config.shuffleFrameMs) || 130;
        const frameTicks = window.CabinetClock.msToTicks(frameMs);
        const frameEl = _duFrame(slotEl);
        const nextRandom = _createPresentationRandom(options?.noise);
        let lastCode = '';

        const currentShuffleToken = {};
        _activeShuffleToken = currentShuffleToken;

        function runShuffleStep() {
            if (_activeShuffleToken !== currentShuffleToken) return;

            const code = _pickShuffleCode(codes, lastCode, nextRandom);
            lastCode = code;

            if (frameEl) {
                frameEl.classList.remove('du-flip-in');
                frameEl.classList.add('du-flip-out');
            }

            const halfFrames = Math.max(2, Math.round(frameTicks * 0.45));
            window.CabinetClock.delayTicks(halfFrames, () => {
                if (_activeShuffleToken !== currentShuffleToken) return;

                img.innerHTML = _renderDomCard(_asCard(code));

                if (frameEl) {
                    frameEl.classList.remove('du-flip-out');
                    frameEl.classList.add('du-flip-in');
                }
            });

            window.CabinetClock.delayTicks(frameTicks, runShuffleStep);
        }

        runShuffleStep();
    }

    function configure(overrides) {
        _config = _resolveConfig(overrides);
        return getConfig();
    }

    function getConfig() {
        return {
            cardBack: _config.cardBack,
            staggerFrames: _config.staggerFrames,
            dealBaseFrames: _config.dealBaseFrames,
            dealDurationFrames: _config.dealDurationFrames,
            drawStaggerFrames: _config.drawStaggerFrames,
            drawOutFrames: _config.drawOutFrames,
            drawDurationFrames: _config.drawDurationFrames,
            drawRevealStartFrames: _config.drawRevealStartFrames,
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
            face.innerHTML = _renderDomCard(null);

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
            const baseFrames = Math.max(0, Number(_config.dealBaseFrames) || 5);
            const staggerFrames = Math.max(1, Number(_config.staggerFrames) || 12);

            const dealToken = {};
            _activeDealToken = dealToken;

            // Phase 1: render all cards in DOM, hidden in-place (no off-screen slide).
            // Cards start with .card-pre-deal (hidden state) — the CSS thump animation
            // will pop them into view when .card-deal-thump replaces it.
            cards.forEach((card, i) => {
                const slotEl = _slot(i);
                const img = _cardImg(slotEl);
                if (!slotEl || !img) return;

                _resetMainSlot(slotEl);
                _applyCardFace(slotEl, img, card, { requireFace: true });
                // Use CSS class for hidden state — avoids inline-style vs animation conflicts
                slotEl.classList.add('card-pre-deal');
            });

            let completedCount = 0;

            cards.forEach((card, i) => {
                if (_activeDealToken !== dealToken) return;

                const frameDelay = baseFrames + (i * staggerFrames);
                window.CabinetClock.delayTicks(frameDelay, () => {
                    if (_activeDealToken !== dealToken) return;

                    const slotEl = _slot(i);
                    if (!slotEl) {
                        completedCount++;
                        if (completedCount === cards.length && onComplete) {
                            window.CabinetClock.delayTicks(1, onComplete);
                        }
                        return;
                    }

                    // First card: trigger deck-wide shadow burst for physical thump feel
                    if (i === 0) {
                        const area = document.getElementById('card-area');
                        if (area) {
                            area.classList.remove('card-area-thump');
                            void area.offsetWidth;
                            area.classList.add('card-area-thump');
                        }
                    }

                    // Thump the card into view — CSS animation handles scale+opacity.
                    // Remove pre-deal hidden state first so the animation starts clean.
                    slotEl.classList.remove('card-pre-deal');
                    slotEl.classList.remove('card-draw-thump');
                    void slotEl.offsetWidth; // force reflow for fresh animation
                    slotEl.classList.add('card-deal-thump');

                    completedCount++;
                    if (completedCount === cards.length && onComplete) {
                        window.CabinetClock.delayTicks(
                            Math.round((Number(_config.dealDurationFrames) || 11)),
                            onComplete
                        );
                    }
                });
            });
        }

    function drawCards(newCardArray, heldIndexes, onComplete) {
        if (!_ensureMainSlots()) return;

        _stopShuffle();

        const held = new Set(Array.isArray(heldIndexes) ? heldIndexes : Array.from(heldIndexes || []));
        const cards = Array.isArray(newCardArray) ? newCardArray.map(_asCard) : [];

        const drawToken = {};
        _activeDrawToken = drawToken;

        let pending = 0;

        const baseFrames = Math.max(0, Number(_config.drawRevealStartFrames !== undefined ? _config.drawRevealStartFrames : (_config.dealBaseFrames || 5)));
        const staggerFrames = Math.max(1, Number(_config.drawStaggerFrames || _config.staggerFrames || 12));

        cards.forEach((card, i) => {
            if (!held.has(i)) {
                pending++;
            }
        });

        // Phase 1: Update held cards (face + held state). Held cards stay visible
        // and static — no animation. Non-held cards keep their old face visible
        // until their replacement thumps in.
        cards.forEach((card, i) => {
            const slotEl = _slot(i);
            const img = _cardImg(slotEl);

            if (!slotEl || !img) return;

            if (held.has(i)) {
                slotEl.classList.add('held');
                _applyCardFace(slotEl, img, card, { requireFace: true });
                return;
            }

            slotEl.classList.remove('held');
            // AIPoker parity: unheld card disappears immediately on draw request
            slotEl.classList.add('card-pre-draw');
            slotEl.classList.remove('card-deal-thump', 'card-draw-thump');
        });

        if (pending === 0 && onComplete) {
            window.CabinetClock.delayTicks(1, onComplete);
            return;
        }

        let completedCount = 0;
        let unheldSeqIndex = 0;
        cards.forEach((card, i) => {
            if (_activeDrawToken !== drawToken) return;
            if (held.has(i)) return; // Already handled — held cards stay put
            const currentSeq = unheldSeqIndex++;
            window.CabinetClock.delayTicks(baseFrames + (currentSeq * staggerFrames), () => {
                if (_activeDrawToken !== drawToken) return;
                const slotEl = _slot(i);
                if (!slotEl) {
                    completedCount++;
                    if (completedCount === pending && onComplete) {
                        window.CabinetClock.delayTicks(1, onComplete);
                    }
                    return;
                }

                const img = _cardImg(slotEl);
                if (img) {
                    // Replace the face with the new card in hidden state, then thump it into view.
                    slotEl.classList.add('card-pre-draw');
                    _applyCardFace(slotEl, img, card, { requireFace: true });

                    // Force reflow so the hidden state is applied before animation
                    void slotEl.offsetWidth;
                }

                // Thump the replacement card into place
                slotEl.classList.remove('card-pre-draw');
                slotEl.classList.remove('card-deal-thump');
                void slotEl.offsetWidth;
                slotEl.classList.add('card-draw-thump');

                completedCount++;
                if (completedCount === pending && onComplete) {
                    window.CabinetClock.delayTicks(
                        Math.round((Number(_config.drawDurationFrames) || 11)),
                        onComplete
                    );
                }
            });
        });
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
        const flash = document.getElementById('lucky5-flash');
        const currentToken = {};
        _lucky5Token = currentToken;

        const durationFrames = Math.max(12, Math.round((Number(_config.lucky5ActiveMs) || 1000) / (1000/60)));
        
        if (banner) {
            banner.classList.add('active');
        }

        if (flash) {
            void flash.offsetWidth;
            flash.classList.add('active');
        }

        document.querySelectorAll('.card-slot, .du-card-slot').forEach(slotEl => {
            slotEl.classList.add('lucky5-active');
        });

        window.CabinetClock.delayTicks(durationFrames, () => {
            if (_lucky5Token !== currentToken) return;

            if (banner) {
                banner.classList.remove('active');
            }

            if (flash) {
                flash.classList.remove('active');
            }

            document.querySelectorAll('.card-slot, .du-card-slot').forEach(slotEl => {
                slotEl.classList.remove('lucky5-active');
            });
            _lucky5Token = null;
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
        renderDomCard: _renderDomCard,
        showLucky5Active,
        isDoubleUpMode: function() { return _isDoubleUpMode; },
        precacheAllCards: _precacheAllCards
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
