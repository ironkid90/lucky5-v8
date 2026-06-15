'use strict';

window.CabinetAudio = (function () {
    const clips = new Map();
    const queues = {
        high: [],
        normal: [],
        low: []
    };

    let playing = false;

    const DEFAULT_EVENTS = Object.freeze({
        press:          { src: '/assets/sounds/press.mp3',          volume: 0.3,  priority: 'normal' },
        invalid:        { src: '/assets/sounds/press.mp3',          volume: 0.18, priority: 'high'   },
        deal:           { src: '/assets/sounds/deal.mp3',           volume: 0.25, priority: 'normal' },
        draw:           { src: '/assets/sounds/draw.mp3',           volume: 0.25, priority: 'normal' },
        collect:        { src: '/assets/sounds/collect.mp3',        volume: 0.22, priority: 'low'    },
        lucky5:         { src: '/assets/sounds/lucky5.mp3',         volume: 0.4,  priority: 'high'   },
        machineClose:   { src: '/assets/sounds/machine-close.mp3',  volume: 0.4,  priority: 'high'   },
        win:            { src: '/assets/sounds/win.mp3',            volume: 0.35, priority: 'high'   },
        bonusClaim:     { src: '/assets/sounds/bonus-claim.mp3',    volume: 0.4,  priority: 'high'   },
        doubleUpWin:    { src: '/assets/sounds/doubleup-win.mp3',   volume: 0.35, priority: 'high'   },
        doubleUpLose:   { src: '/assets/sounds/doubleup-lose.mp3',  volume: 0.25, priority: 'normal' },
        cashIn:         { src: '/assets/sounds/cash-in.mp3',        volume: 0.2,  priority: 'low'    },
        cashOut:        { src: '/assets/sounds/cash-out.mp3',       volume: 0.3,  priority: 'normal' },
        hold:           { src: '/assets/sounds/hold.mp3',           volume: 0.15, priority: 'low'    },
        jackpot:        { src: '/assets/sounds/jackpot.mp3',        volume: 0.5,  priority: 'high'   }
    });

    function _getEventMap() {
        const cfg = window.GAME_CONFIG && window.GAME_CONFIG.audio ? window.GAME_CONFIG.audio : null;
        return cfg && cfg.events ? { ...DEFAULT_EVENTS, ...cfg.events } : DEFAULT_EVENTS;
    }

    function _ensureClip(name) {
        if (clips.has(name)) return clips.get(name);
        const eventDef = _getEventMap()[name];
        if (!eventDef || !eventDef.src) return null;
        const audio = new Audio(eventDef.src);
        audio.preload = 'auto';
        audio.volume = typeof eventDef.volume === 'number' ? eventDef.volume : 0.3;
        clips.set(name, audio);
        return audio;
    }

    function preload() {
        Object.keys(_getEventMap()).forEach(_ensureClip);
    }

    function _nextQueueItem() {
        return queues.high.shift() || queues.normal.shift() || queues.low.shift() || null;
    }

    function _pump() {
        if (playing) return;
        const item = _nextQueueItem();
        if (!item) return;

        const clip = _ensureClip(item.name);
        if (!clip) return;

        playing = true;
        clip.pause();
        clip.currentTime = 0;
        clip.volume = typeof item.volume === 'number' ? item.volume : clip.volume;

        const finish = () => {
            clip.removeEventListener('ended', finish);
            clip.removeEventListener('error', finish);
            playing = false;
            _pump();
        };

        clip.addEventListener('ended', finish, { once: true });
        clip.addEventListener('error', finish, { once: true });
        const maybePromise = clip.play();
        if (maybePromise && typeof maybePromise.catch === 'function') {
            maybePromise.catch(() => {
                playing = false;
                _pump();
            });
        }
    }

    function queue(eventName, options) {
        const eventDef = _getEventMap()[eventName];
        if (!eventDef) return;
        const priority = (options && options.priority) || eventDef.priority || 'normal';
        queues[priority] = queues[priority] || queues.normal;
        queues[priority].push({
            name: eventName,
            volume: options && typeof options.volume === 'number' ? options.volume : eventDef.volume
        });
        _pump();
    }

    function playNow(eventName, options) {
        const clip = _ensureClip(eventName);
        if (!clip) return;
        clip.pause();
        clip.currentTime = 0;
        clip.volume = typeof options?.volume === 'number' ? options.volume : clip.volume;
        clip.play().catch(() => {});
    }

    return {
        preload,
        queue,
        playNow
    };
})();
