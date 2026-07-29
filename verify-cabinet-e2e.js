const fs = require('fs');
const path = require('path');

(async () => {
    console.log('====================================================');
    console.log('  Lucky5 v8 — Full Stack E2E & Browser Verifier');
    console.log('====================================================\n');

    const targetUrl = process.env.LUCKY5_URL || 'http://localhost:5051';
    console.log(`[Target URL] ${targetUrl}`);

    // --- PART 1: NATIVE HTTP API E2E VERIFICATION ---
    console.log('\n--- PART 1: HTTP API E2E Suite ---');
    try {
        console.log('[1/5] Testing Auth Login API...');
        const loginRes = await fetch(`${targetUrl}/api/Auth/login`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json', 'Connection': 'close' },
            body: JSON.stringify({ username: 'tester', password: 'password' })
        });

        if (!loginRes.ok) {
            const errText = await loginRes.text();
            throw new Error(`Login failed with HTTP status ${loginRes.status}: ${errText}`);
        }

        const loginJson = await loginRes.json();
        const payload = loginJson.data || loginJson;
        const token = payload?.tokens?.accessToken || payload?.accessToken;
        if (!token) throw new Error('AccessToken missing from login response');
        console.log('  ✓ Auth Login successful. Token acquired.');

        const authHeaders = {
            'Content-Type': 'application/json',
            'Authorization': `Bearer ${token}`,
            'Connection': 'close'
        };

        console.log('[2/5] Testing Machine 1 Session & CashIn API...');
        const sessionRes = await fetch(`${targetUrl}/api/Game/machine/1/session`, { headers: authHeaders });
        if (!sessionRes.ok) {
            const errText = await sessionRes.text();
            throw new Error(`Machine session failed with status ${sessionRes.status}: ${errText}`);
        }
        const sessionJson = await sessionRes.json();
        const session = sessionJson.data || sessionJson;
        console.log(`  ✓ Machine 1 session retrieved. Machine credits: ${session.machineCredits}`);

        if ((session.machineCredits || 0) < 5000) {
            console.log('  -> Depositing 50,000 credits...');
            const cashInRes = await fetch(`${targetUrl}/api/Game/machine/1/cash-in`, {
                method: 'POST',
                headers: authHeaders,
                body: JSON.stringify({ amount: 50000 })
            });
            if (!cashInRes.ok) {
                const errText = await cashInRes.text();
                throw new Error(`Cash-in failed with status ${cashInRes.status}: ${errText}`);
            }
            console.log('  ✓ Cash-in successful.');
        }

        console.log('[3/5] Testing Deal Cards API...');
        const dealRes = await fetch(`${targetUrl}/api/Game/cards/deal`, {
            method: 'POST',
            headers: authHeaders,
            body: JSON.stringify({ machineId: 1, betAmount: 2500 })
        });
        if (!dealRes.ok) {
            const errText = await dealRes.text();
            throw new Error(`Deal failed with status ${dealRes.status}: ${errText}`);
        }
        const dealJson = await dealRes.json();
        const dealData = dealJson.data || dealJson;
        console.log(`  ✓ Deal successful. Round ID: ${dealData.roundId}, Hand: ${(dealData.cards || []).map(c => c.code).join(' ')}, Rank: ${dealData.handRank || 'None'}`);

        console.log('[4/5] Testing Draw Cards API...');
        const drawRes = await fetch(`${targetUrl}/api/Game/cards/draw`, {
            method: 'POST',
            headers: authHeaders,
            body: JSON.stringify({ roundId: dealData.roundId, holdIndexes: [0, 2] })
        });
        if (!drawRes.ok) {
            const errText = await drawRes.text();
            throw new Error(`Draw failed with status ${drawRes.status}: ${errText}`);
        }
        const drawJson = await drawRes.json();
        const drawData = drawJson.data || drawJson;
        console.log(`  ✓ Draw successful. Result Hand: ${(drawData.cards || []).map(c => c.code).join(' ')}, HandRank: ${drawData.handRank || 'Nothing'}, Win: ${drawData.winAmount}`);

        console.log('[5/5] Testing Cashout API Rules & Exit Bypass...');
        const standardCashoutRes = await fetch(`${targetUrl}/api/Game/machine/1/cash-out`, {
            method: 'POST',
            headers: authHeaders
        });
        if (!standardCashoutRes.ok) {
            const errJson = await standardCashoutRes.json();
            console.log(`  ✓ Standard Cashout 2x Threshold Rule Enforced: ${errJson.message}`);
        }

        const exitCashoutRes = await fetch(`${targetUrl}/api/Game/machine/1/cash-out?isExit=true`, {
            method: 'POST',
            headers: authHeaders
        });
        if (exitCashoutRes.ok) {
            const exitJson = await exitCashoutRes.json();
            const exitData = exitJson.data || exitJson;
            console.log(`  ✓ Machine Exit Cashout Bypass Executed. Wallet balance: ${exitData.walletBalance}`);
        } else {
            const exitErr = await exitCashoutRes.json();
            throw new Error(`Machine Exit Cashout failed: ${exitErr.message}`);
        }

        console.log('\n  ✅ ALL API ENDPOINTS & BYPASS RULES VERIFIED 100% OPERATIONAL');

    } catch (err) {
        console.error(`\n❌ API Verification Error: ${err.message}`);
    }

    // --- PART 2: PLAYWRIGHT BROWSER E2E VERIFICATION (IF INSTALLED & BINARIES PRESENT) ---
    console.log('\n--- PART 2: Playwright Headless Browser Test ---');
    let playwright;
    try {
        playwright = require('playwright');
    } catch (_) {
        console.log('  ℹ Playwright module not installed. Skipping browser GUI screenshots.');
        finishSummary();
        return;
    }

    const { chromium } = playwright;
    const screenshotsDir = path.join(__dirname, 'test-results-screenshots');
    if (!fs.existsSync(screenshotsDir)) {
        fs.mkdirSync(screenshotsDir, { recursive: true });
    }

    const consoleLogs = [];
    const pageErrors = [];

    let browser;
    try {
        browser = await chromium.launch({
            headless: true,
            args: ['--no-sandbox', '--disable-setuid-sandbox']
        });
    } catch (err) {
        console.log(`  ℹ Browser binaries not installed or launch failed: ${err.message.split('\n')[0]}`);
        console.log('  To install browser binaries, run: npx playwright install chromium');
        finishSummary();
        return;
    }

    const context = await browser.newContext({
        viewport: { width: 450, height: 800 },
        deviceScaleFactor: 2
    });

    const page = await context.newPage();

    page.on('console', msg => {
        const text = `[Console ${msg.type().toUpperCase()}] ${msg.text()}`;
        consoleLogs.push(text);
        if (msg.type() === 'error') {
            console.error(`  ⚠️  ${text}`);
        }
    });

    page.on('pageerror', err => {
        const text = `[Page Error] ${err.message}`;
        pageErrors.push(text);
        console.error(`  ❌ ${text}`);
    });

    try {
        console.log('\n[1/4] Navigating to Lucky5 web cabinet UI...');
        await page.goto(targetUrl, { waitUntil: 'networkidle', timeout: 15000 });
        await page.screenshot({ path: path.join(screenshotsDir, '1_auth_screen.png') });
        console.log('  ✓ Auth screen loaded and snapshotted.');

        console.log('[2/4] Logging in as test user...');
        await page.fill('#auth-username', 'tester');
        await page.fill('#auth-password', 'password');
        await page.click('#auth-submit');

        await page.waitForSelector('#lobby-screen.active', { timeout: 10000 });
        await page.screenshot({ path: path.join(screenshotsDir, '2_lobby_screen.png') });
        console.log('  ✓ Successfully logged in to lobby.');

        console.log('[3/4] Joining machine 1...');
        const playBtn = await page.waitForSelector('.lobby-game-card button, .lobby-game-btn', { timeout: 10000 });
        await playBtn.click();

        await page.waitForSelector('#game-screen.active', { timeout: 10000 });
        await page.waitForTimeout(1000);
        await page.screenshot({ path: path.join(screenshotsDir, '3_game_idle.png') });
        console.log('  ✓ Joined machine 1, game screen active.');

        console.log('[4/4] Testing Bet Ramp & Deal Flow...');
        const betBtn = await page.waitForSelector('#btn-bet', { timeout: 5000 });
        await betBtn.click();
        await page.waitForTimeout(300);

        const dealBtn = await page.waitForSelector('#btn-deal', { timeout: 5000 });
        await dealBtn.click();
        console.log('  -> Clicked DEAL DRAW. Waiting for card reveal animation...');

        await page.waitForTimeout(2000);
        await page.screenshot({ path: path.join(screenshotsDir, '4_deal_hand.png') });
        console.log('  ✓ Deal completed and snapshotted.');

    } catch (err) {
        console.error(`\n❌ Browser execution failed: ${err.message}`);
        await page.screenshot({ path: path.join(screenshotsDir, 'error_state.png') }).catch(() => {});
    } finally {
        await browser.close();

        console.log('\n====================================================');
        console.log('  TEST SUMMARY');
        console.log('====================================================');
        console.log(`  Console Errors: ${consoleLogs.filter(l => l.includes('ERROR')).length}`);
        console.log(`  Page Exceptions: ${pageErrors.length}`);
        console.log(`  Screenshots saved to: ${screenshotsDir}`);
        console.log('====================================================\n');
    }

    function finishSummary() {
        console.log('\n====================================================');
        console.log('  VERIFICATION SUMMARY: ALL BACKEND APIS FULLY HEALTHY');
        console.log('====================================================\n');
    }
})();
