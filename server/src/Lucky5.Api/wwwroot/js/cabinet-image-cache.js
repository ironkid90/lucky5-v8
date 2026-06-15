/**
 * cabinet-image-cache.js   Phase 6 — Image Caching Layer
 * Uses the browser Cache API to avoid refetching game assets on repeat visits.
 * Falls back gracefully if Cache API is unavailable (e.g., HTTP in some browsers).
 */
window.CabinetImageCache = (function () {
    const CACHE_NAME = 'lucky5-images-v1';
    const supported = typeof caches !== 'undefined';

    async function _openCache() {
        return supported ? await caches.open(CACHE_NAME) : null;
    }

    /**
     * Fetch an image URL — returns from cache if available, else fetches and caches.
     * @param {string} url
     * @returns {Promise<string>} object URL suitable for img.src
     */
    async function fetchCached(url) {
        if (!supported) return url;
        try {
            const cache = await _openCache();
            const cached = await cache.match(url);
            if (cached) {
                const blob = await cached.blob();
                return URL.createObjectURL(blob);
            }
            const response = await fetch(url);
            if (response.ok) {
                await cache.put(url, response.clone());
                const blob = await response.blob();
                return URL.createObjectURL(blob);
            }
        } catch (_) { /* fallback to original URL */ }
        return url;
    }

    /**
     * Preload a list of image URLs into the cache.
     * @param {string[]} urls
     */
    async function preload(urls) {
        if (!supported || !urls || urls.length === 0) return;
        const cache = await _openCache();
        await Promise.allSettled(
            urls.map(async url => {
                const existing = await cache.match(url);
                if (!existing) {
                    const res = await fetch(url);
                    if (res.ok) await cache.put(url, res);
                }
            })
        );
    }

    /** Purge all cached images (call on logout or version bump). */
    async function clear() {
        if (!supported) return;
        await caches.delete(CACHE_NAME);
    }

    return { fetchCached, preload, clear };
})();
