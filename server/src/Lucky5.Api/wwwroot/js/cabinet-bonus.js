/**
 * cabinet-bonus.js
 * Daily reward / bonus banner logic for the lobby.
 * Calls GET /api/Reward/status on lobby entry and POST /api/Reward/claim on click.
 */
window.CabinetBonus = (function () {
    const BONUS_STATUS_PATH = '/api/Reward/status';
    const BONUS_CLAIM_PATH  = '/api/Reward/claim';

    async function checkAndShowBanner() {
        const banner = document.getElementById('bonus-banner');
        const resultEl = document.getElementById('bonus-result');
        if (!banner) return;

        banner.style.display = 'none';
        if (resultEl) resultEl.style.display = 'none';

        try {
            const data = await apiCall('GET', BONUS_STATUS_PATH);
            if (data && data.isEligible) {
                banner.style.display = '';
            }
        } catch (_) {
            // Silently hide banner if endpoint unavailable
        }
    }

    async function claimBonus() {
        const claimBtn = document.getElementById('bonus-claim-btn');
        const resultEl = document.getElementById('bonus-result');
        const banner = document.getElementById('bonus-banner');
        if (!claimBtn || !resultEl) return;

        claimBtn.disabled = true;
        claimBtn.textContent = '...';

        try {
            const data = await apiCall('POST', BONUS_CLAIM_PATH);
            const fmt = new Intl.NumberFormat('en-US', { maximumFractionDigits: 0 });
            resultEl.textContent = `+${fmt.format(data.amountAwarded)} CREDIT ADDED!`;
            resultEl.style.display = '';
            claimBtn.style.display = 'none';

            if (typeof walletBalance !== 'undefined' && typeof updateLobbyBalance === 'function') {
                walletBalance = (walletBalance || 0);
                updateLobbyBalance();
            }

            setTimeout(() => {
                if (banner) banner.style.display = 'none';
            }, 4000);
        } catch (e) {
            resultEl.textContent = e.message || 'Claim failed';
            resultEl.style.display = '';
            claimBtn.disabled = false;
            claimBtn.textContent = 'CLAIM';
        }
    }

    function init() {
        const claimBtn = document.getElementById('bonus-claim-btn');
        if (claimBtn) {
            claimBtn.addEventListener('click', claimBonus);
        }
    }

    return { checkAndShowBanner, init };
})();
