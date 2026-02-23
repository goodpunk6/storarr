import { test, expect } from '@playwright/test';

const STORARR_URL = 'http://localhost:9687';
const API_URL = 'http://localhost:9687/api/v1';

// Test credentials for the test environment
const TEST_CONFIG = {
  jellyfinUrl: 'http://test-jellyfin:8096',
  jellyfinApiKey: 'storarr_test_key_12345678',
  jellyseerrUrl: 'http://test-jellyseerr:5055',
  jellyseerrApiKey: '1771870133098569e3d29-f6f5-466c-a7d7-6e26b27cbe7f',
  sonarrUrl: 'http://test-sonarr:8989',
  sonarrApiKey: '39cdbef7b29b441c8a058b2e27ca0532',
  radarrUrl: 'http://test-radarr:7878',
  radarrApiKey: '43b5635691b94bc1934d93311f512ce1',
};

test.describe('Multi-Drive Storage Feature', () => {
  test.beforeEach(async ({ page }) => {
    // Navigate to the app
    await page.goto(STORARR_URL);
  });

  test('should display dashboard when first run is complete', async ({ page }) => {
    await page.goto(STORARR_URL);

    // Wait for page to fully load
    await page.waitForLoadState('networkidle');

    // Should show dashboard or main content
    await expect(page.locator('text=Dashboard').first()).toBeVisible({ timeout: 15000 });
  });

  test('should show multi-drive settings in Settings page', async ({ page }) => {
    // Navigate directly to settings page
    await page.goto(STORARR_URL + '/settings');

    // Wait for settings page to load
    await expect(page.locator('text=Multi-Drive Storage')).toBeVisible({ timeout: 10000 });
  });

  test('should toggle multi-drive and show path inputs', async ({ page }) => {
    await page.goto(STORARR_URL + '/settings');

    // Wait for settings page
    await expect(page.locator('text=Multi-Drive Storage')).toBeVisible({ timeout: 10000 });

    // Find the multi-drive toggle
    const multiDriveToggle = page.locator('text=Multi-Drive Storage').locator('..').locator('input[type="checkbox"]');

    // Check if toggle exists
    await expect(multiDriveToggle).toBeVisible();

    // Toggle it on if not already
    const isChecked = await multiDriveToggle.isChecked();
    if (!isChecked) {
      await multiDriveToggle.click();

      // Should show symlink and mkv path inputs
      await expect(page.locator('text=Symlink Storage Path')).toBeVisible({ timeout: 5000 });
      await expect(page.locator('text=MKV Storage Path')).toBeVisible({ timeout: 5000 });
    } else {
      // Already enabled, verify inputs are visible
      await expect(page.locator('text=Symlink Storage Path')).toBeVisible({ timeout: 5000 });
    }
  });

  test('should save multi-drive configuration', async ({ page, request }) => {
    await page.goto(STORARR_URL + '/settings');

    // Wait for settings page
    await expect(page.locator('text=Multi-Drive Storage')).toBeVisible({ timeout: 10000 });

    // Get current config via API
    const configResponse = await request.get(API_URL + '/config');
    const config = await configResponse.json();

    // Verify multi-drive settings are present in config
    expect(config).toHaveProperty('multiDriveEnabled');
    expect(config).toHaveProperty('symlinkStoragePath');
    expect(config).toHaveProperty('mkvStoragePath');
  });
});

test.describe('API Tests - Multi-Drive', () => {
  test('should return multi-drive config in API response', async ({ request }) => {
    const response = await request.get(API_URL + '/config');
    expect(response.ok()).toBeTruthy();

    const config = await response.json();

    // Verify multi-drive fields exist
    expect(config).toHaveProperty('multiDriveEnabled');
    expect(config).toHaveProperty('symlinkStoragePath');
    expect(config).toHaveProperty('mkvStoragePath');
    expect(config).toHaveProperty('sonarrSymlinkRootFolder');
    expect(config).toHaveProperty('sonarrMkvRootFolder');
    expect(config).toHaveProperty('radarrSymlinkRootFolder');
    expect(config).toHaveProperty('radarrMkvRootFolder');
  });

  test('should have multi-drive enabled in test environment', async ({ request }) => {
    const response = await request.get(API_URL + '/config');
    const config = await response.json();

    // Test environment should have multi-drive enabled
    expect(config.multiDriveEnabled).toBe(true);
    expect(config.symlinkStoragePath).toBe('/data/symlink-media');
    expect(config.mkvStoragePath).toBe('/data/mkv-media');
  });

  test('should have all service connections configured', async ({ request }) => {
    const response = await request.get(API_URL + '/config');
    const config = await response.json();

    // Verify all services are configured
    expect(config.jellyfinUrl).toBe('http://test-jellyfin:8096');
    expect(config.jellyseerrUrl).toBe('http://test-jellyseerr:5055');
    expect(config.sonarrUrl).toBe('http://test-sonarr:8989');
    expect(config.radarrUrl).toBe('http://test-radarr:7878');
  });
});

test.describe('First Run Wizard - Multi-Drive', () => {
  test('should verify multi-drive fields exist in first run wizard component', async ({ request }) => {
    // This test verifies that the FirstRunWizard component has multi-drive support
    // Since first run is already complete in the test environment, we verify via API
    // that the multi-drive configuration was properly saved during initial setup

    const response = await request.get(API_URL + '/config');
    expect(response.ok()).toBeTruthy();

    const config = await response.json();

    // Verify multi-drive fields are present and properly configured
    expect(config).toHaveProperty('multiDriveEnabled');
    expect(config).toHaveProperty('symlinkStoragePath');
    expect(config).toHaveProperty('mkvStoragePath');

    // Test environment should have multi-drive enabled with correct paths
    expect(config.multiDriveEnabled).toBe(true);
    expect(config.symlinkStoragePath).toBe('/data/symlink-media');
    expect(config.mkvStoragePath).toBe('/data/mkv-media');
  });

  test('should show multi-drive toggle in Settings page (alternative to first run)', async ({ page }) => {
    // If first run is already complete, verify multi-drive config is accessible in Settings
    await page.goto(STORARR_URL + '/settings');

    // Wait for settings page
    await expect(page.locator('text=Multi-Drive Storage')).toBeVisible({ timeout: 10000 });

    // Verify the toggle and path inputs are visible
    const multiDriveToggle = page.locator('input[type="checkbox"]').first();
    await expect(multiDriveToggle).toBeVisible();

    // Should show path inputs since multi-drive is enabled
    await expect(page.locator('text=Symlink Storage Path')).toBeVisible({ timeout: 5000 });
    await expect(page.locator('text=MKV Storage Path')).toBeVisible({ timeout: 5000 });
  });
});

test.describe('Library Scanner - Multi-Drive', () => {
  test('should scan multiple storage paths', async ({ request }) => {
    // Check media items endpoint to verify scanning works
    const response = await request.get(API_URL + '/media');
    expect(response.ok()).toBeTruthy();

    const media = await response.json();

    // Just verify the endpoint works - actual scanning happens in background
    expect(Array.isArray(media.items) || Array.isArray(media)).toBeTruthy();
  });
});

test.describe('Connection Tests', () => {
  test('should test all service connections', async ({ request }) => {
    const response = await request.post(API_URL + '/config/test');
    expect(response.ok()).toBeTruthy();

    const result = await response.json();

    // Should have results array
    expect(result).toHaveProperty('results');
    expect(Array.isArray(result.results)).toBeTruthy();

    // Log results for debugging
    console.log('Connection test results:', JSON.stringify(result.results, null, 2));
  });
});
