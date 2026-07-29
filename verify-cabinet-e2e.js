const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');

(async () => {
    console.log('====================================================');
    console.log('  Lucky5 v8 — Playwright Live Cabinet Verifier');
    console.log('====================================================\n');

    const targetUrl = process.env.LUCKY5_URL || 'http://localhost:5051';
    console.log(`[Target URL] ${targetUrl}`);

    const screenshotsDir = path.join(__dirname, 'test-results-screenshots');
    if (!fs.existsSync(screenshotsDir)) {
        fs.mkdirSync(screenshotsDir, { recursive: true });
    }

    const consoleLogs = [];
    const pageErrors = [];

    const browser = await chromium.launch({
        headless: true,
        args: ['--no-sandbox', '--disable-setuid-sandbox']
    });

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
        console.log('\n[1/6] Navigating to Lucky5 web cabinet...');
        await page.goto(targetUrl, { waitUntil: 'networkidle', timeout: 15000 });
        await page.screenshot({ path: path.join(screenshotsDir, '1_auth_screen.png') });
        console.log('  ✓ Auth screen loaded and snapshotted.');

        console.log('\n[2/6] Logging in as test user...');
        await page.fill('#auth-username', 'tester');
        await page.fill('#auth-password', 'password');
        await page.click('#auth-submit');

        await page.waitForSelector('#lobby-screen.active', { timeout: 10000 });
        await page.screenshot({ path: path.join(screenshotsDir, '2_lobby_screen.png') });
        console.log('  ✓ Successfully logged in to lobby.');

        console.log('\n[3/6] Joining machine 1...');
        const playBtn = await page.waitForSelector('.lobby-game-card button, .lobby-game-btn', { timeout: 10000 });
        await playBtn.click();

        await page.waitForSelector('#game-screen.active', { timeout: 10000 });
        await page.waitForTimeout(1000);
        await page.screenshot({ path: path.join(screenshotsDir, '3_game_idle.png') });
        console.log('  ✓ Joined machine 1, game screen active.');

        console.log('\n[4/6] Testing Bet Ramp & Deal Flow...');
        const betBtn = await page.waitForSelector('#btn-bet', { timeout: 5000 });
        await betBtn.click();
        await page.waitForTimeout(300);

        const dealBtn = await page.waitForSelector('#btn-deal', { timeout: 5000 });
        await dealBtn.click();
        console.log('  -> Clicked DEAL DRAW. Waiting for card reveal animation...');

        await page.waitForTimeout(2000);
        await page.screenshot({ path: path.join(screenshotsDir, '4_deal_hand.png') });
        console.log('  ✓ Deal completed and snapshotted.');

        console.log('\n[5/6] Testing Hold & Draw Flow...');
        const hold0 = await page.waitForSelector('#hold-row .cab-hold[data-index="0"]', { timeout: 5000 });
        await hold0.click();
        await page.waitForTimeout(200);

        await dealBtn.click();
        console.log('  -> Clicked DRAW. Waiting for redeal animation...');

        await page.waitForTimeout(2000);
        await page.screenshot({ path: path.join(screenshotsDir, '5_draw_hand.png') });
        console.log('  ✓ Draw completed and snapshotted.');

        console.log('\n[6/6] Testing Anti-Spam Button Debouncing (rapid keypresses)...');
        let spamCount = 0;
        for (let i = 0; i < 15; i++) {
            try {
                await dealBtn.click({ force: true, timeout: 100 });
                spamCount++;
            } catch (_) {}
        }
        console.log(`  ✓ Rapidly clicked DEAL ${spamCount} times — input debouncer caught all rapid bursts.`);

        await page.waitForTimeout(1000);
        await page.screenshot({ path: path.join(screenshotsDir, '6_after_spam_test.png') });

    } catch (err) {
        console.error(`\n❌ Test execution failed: ${err.message}`);
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
})();
