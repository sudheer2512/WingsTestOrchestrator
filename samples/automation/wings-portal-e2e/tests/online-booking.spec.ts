import { readFileSync } from 'node:fs';
import { expect, test } from '@playwright/test';

// SAMPLE tests for the sample customer portal (served by the orchestrator at /sample-portal/).
// The @feature-NNNNN tag links them to the "Online Booking" feature's creation sequence.

/** Test data reserved for this job by the orchestrator (WINGS_CONTEXT_FILE), with a fallback for local runs. */
function customer(): { name: string; mobile: string } {
  const file = process.env.WINGS_CONTEXT_FILE;
  const ctx = file ? JSON.parse(readFileSync(file, 'utf8')) : {};
  const record = ctx.data?.CUSTOMERS?.records?.[0]?.values;
  return { name: record?.name ?? 'Test Customer', mobile: record?.mobile ?? '9876543210' };
}

test.describe('Online booking @feature-00027', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('booking/new/');
  });

  test('portal booking happy path', async ({ page }) => {
    const c = customer();
    await page.getByTestId('model').selectOption('sedan');
    await page.getByTestId('customer-name').fill(c.name);
    await page.getByTestId('mobile').fill(c.mobile);
    await page.getByTestId('submit').click();
    await expect(page.getByTestId('booking-number')).toHaveText(/^BK-\d+$/);
  });

  test('rejects an invalid mobile number', async ({ page }) => {
    await page.getByTestId('mobile').fill('123');
    await page.getByTestId('submit').click();
    await expect(page.getByTestId('mobile-error')).toBeVisible();
  });

  // Fails against the sample portal on purpose (see the SAMPLE DEFECT note in the portal) to demonstrate failure evidence.
  test('festival offer takes 5% off the price', async ({ page }) => {
    await page.getByTestId('model').selectOption('sedan');
    await page.getByTestId('festival-offer').check();
    await expect(page.getByTestId('payable-amount')).toHaveText('₹11,87,500');
  });
});
