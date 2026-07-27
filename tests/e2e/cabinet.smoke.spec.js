// @ts-check
import { test, expect } from '@playwright/test';

test('Lucky5 landing page loads with cabinet shell and auth screen', async ({ page }) => {
    const response = await page.goto('/', { waitUntil: 'networkidle' });
    expect(response?.status()).toBe(200);

    // Title
    await expect(page).toHaveTitle(/Lucky 5/);

    // Auth screen visible
    await expect(page.locator('#auth-screen')).toBeVisible();
    await expect(page.locator('#game-message')).toBeVisible();

    // Critical CSS loaded (game.css should contain admin grid)
    const stylesheets = await page.evaluate(() =>
        Array.from(document.styleSheets).map(s => s.href)
    );
    expect(stylesheets.some(href => href && href.includes('game.css'))).toBeTruthy();

    // Core JS modules loaded
    const hasCabinetStore = await page.evaluate(() => typeof window.CabinetState !== 'undefined');
    expect(hasCabinetStore).toBeTruthy();
});

test('Login flow reaches lobby screen', async ({ page }) => {
    await page.goto('/', { waitUntil: 'networkidle' });

    // Fill login form
    await page.fill('#username', 'admin');
    await page.fill('#password', 'password');
    await page.click('#btn-login');

    // Should redirect to OTP or lobby — wait for screen change
    await page.waitForFunction(
        () => document.getElementById('lobby-screen')?.classList.contains('active'),
        { timeout: 10000 }
    );

    await expect(page.locator('#lobby-screen')).toBeVisible();
});

test('Admin panel submenu tabs are all reachable', async ({ page }) => {
    // This test verifies the admin grid fix from Phase C
    await page.goto('/?admin-debug=1', { waitUntil: 'networkidle' });

    // Inject admin role and activate
    await page.evaluate(() => {
        sessionStorage.setItem('lucky5_role', 'admin');
    });

    // Reload to pick up role
    await page.reload({ waitUntil: 'networkidle' });

    // Navigate to admin if the login screen appears
    const adminScreen = page.locator('#admin-screen');
    if (!await adminScreen.isVisible().catch(() => false)) {
        // Try to login as admin first
        await page.fill('#username', 'admin');
        await page.fill('#password', 'password');
        await page.click('#btn-login');
        await page.waitForTimeout(2000);

        // Force admin screen
        await page.evaluate(() => {
            if (typeof showAdmin === 'function') showAdmin();
        });
    }

    // Verify all 5 admin tabs are present in HTML
    const tabs = ['dashboard', 'players', 'agents', 'machines', 'audit'];
    for (const tab of tabs) {
        const btn = page.locator(`.admin-tab-btn[data-tab="${tab}"]`);
        await expect(btn).toBeAttached({ timeout: 5000 });
    }

    // Verify they are within viewport bounds
    const allVisible = await page.evaluate(() => {
        return Array.from(document.querySelectorAll('.admin-tab-btn')).every(btn => {
            const rect = btn.getBoundingClientRect();
            return rect.left >= 0 && rect.right <= window.innerWidth
                && rect.top >= 0 && rect.bottom <= window.innerHeight;
        });
    });
    expect(allVisible).toBeTruthy();
});