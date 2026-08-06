'use strict';

window.Lucky5PresentationNoise = Object.freeze({
    createRandom(noise) {
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
});
