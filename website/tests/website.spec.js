import { test, expect } from "@playwright/test";
import { release, artifactUrl } from "../src/release.js";
import AxeBuilder from "@axe-core/playwright";
import { readFileSync } from "node:fs";

test("production CSP permits interactions and copying without remote requests", async ({
  page,
  context,
}) => {
  const config = JSON.parse(
    readFileSync(new URL("../vercel.json", import.meta.url), "utf8"),
  );
  const headers = Object.fromEntries(
    config.headers[0].headers.map(({ key, value }) => [key, value]),
  );
  const violations = [];
  await page.addInitScript(() => {
    window.cspErrors = [];
    document.addEventListener("securitypolicyviolation", (event) =>
      window.cspErrors.push(event.violatedDirective),
    );
  });
  page.on("request", (request) => {
    if (!request.url().startsWith("http://127.0.0.1:4173"))
      violations.push(request.url());
  });
  await page.route("http://127.0.0.1:4173/", async (route) => {
    const response = await route.fetch();
    await route.fulfill({
      response,
      headers: { ...response.headers(), ...headers },
    });
  });
  await context.grantPermissions(["clipboard-read", "clipboard-write"]);
  await page.goto("/");
  await page.getByRole("tab", { name: "自在写" }).click();
  await expect(page.locator("#demo-result")).toContainText("I look forward");
  await page.getByRole("button", { name: "复制示例译文" }).click();
  await expect(page.getByRole("status")).toHaveText("示例译文已复制");
  expect(await page.evaluate(() => navigator.clipboard.readText())).toBe(
    "I look forward to discussing this further with you.",
  );
  expect(await page.evaluate(() => window.cspErrors)).toEqual([]);
  expect(violations).toEqual([]);
});

for (const width of [390, 1440]) {
  test(`accessible structure and contrast at ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 });
    await page.goto("/");
    const results = await new AxeBuilder({ page })
      .withTags(["wcag2a", "wcag2aa", "wcag21aa"])
      .analyze();
    expect(results.violations).toEqual([]);
    await page.getByRole("button", { name: "想好了吗？查看译文" }).click();
    const revealed = await new AxeBuilder({ page })
      .include(".review-demo")
      .withTags(["wcag2a", "wcag2aa", "wcag21aa"])
      .analyze();
    expect(revealed.violations).toEqual([]);
  });
}

test("download buttons work without JavaScript and use the verified artifact", async ({
  browser,
}) => {
  const context = await browser.newContext({ javaScriptEnabled: false });
  const page = await context.newPage();
  await page.goto("http://127.0.0.1:4173");
  const downloads = page.locator('[data-download="windows"]');
  await expect(downloads).toHaveCount(2);
  for (const link of await downloads.all())
    await expect(link).toHaveAttribute(
      "href",
      artifactUrl(release.windowsFile),
    );
  await expect(page.locator('[data-download="checksums"]')).toHaveAttribute(
    "href",
    artifactUrl("SHA256SUMS.txt"),
  );
  await context.close();
});

test("scenario switching stays still and supports arrow keys", async ({
  page,
}) => {
  const errors = [];
  page.on("pageerror", (e) => errors.push(e.message));
  await page.goto("/");
  const before = await page.locator(".hero-stage").boundingBox();
  await page.getByRole("tab", { name: "自在写" }).click();
  await expect(page.locator("#demo-result")).toContainText("I look forward");
  await expect(page.locator("#demo-panel")).toHaveAttribute(
    "aria-labelledby",
    "tab-write",
  );
  const after = await page.locator(".hero-stage").boundingBox();
  expect(after).toEqual(before);
  await page.keyboard.press("ArrowRight");
  await expect(page.getByRole("tab", { name: "慢慢记" })).toBeFocused();
  await expect(page.locator("#demo-result")).toHaveText("明天见。");
  await page.getByRole("button", { name: "收藏示例译文" }).click();
  await expect(
    page.getByRole("button", { name: "收藏示例译文" }),
  ).toHaveAttribute("aria-pressed", "true");
  await expect(page.getByRole("status")).toContainText(
    "实际收藏请在 Windows 软件中使用",
  );
  expect(errors).toEqual([]);
});

test("review demo reveals and resets the answer", async ({ page }) => {
  await page.goto("/");
  await expect(page.locator("#review-answer")).toBeHidden();
  await page.getByRole("button", { name: "想好了吗？查看译文" }).click();
  await expect(page.locator("#review-answer")).toBeVisible();
  await page.getByRole("button", { name: "记住了 ✓" }).click();
  await expect(page.locator("#review-answer")).toBeHidden();
  await expect(page.locator("#review-feedback")).toContainText("安排下次复习");
});

for (const width of [320, 390, 768, 1440]) {
  test(`layout fits ${width}px and assets load`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 });
    const failedAssets = [];
    page.on("response", (response) => {
      if (
        response.url().startsWith("http://127.0.0.1:4173") &&
        response.status() >= 400
      )
        failedAssets.push(response.url());
    });
    await page.goto("/");
    await page.evaluate(() => document.fonts.ready);
    expect(
      await page.evaluate(() => document.documentElement.scrollWidth),
    ).toBeLessThanOrEqual(width);
    for (const image of await page.locator("img").all())
      expect(
        await image.evaluate((img) => img.complete && img.naturalWidth > 0),
      ).toBe(true);
    await page.locator("#faq details").first().locator("summary").click();
    await expect(page.locator("#faq details").first()).toHaveAttribute(
      "open",
      "",
    );
    if (width <= 600) {
      await page.getByRole("button", { name: "打开导航" }).click();
      await expect(page.getByRole("navigation")).toBeVisible();
      await page
        .getByRole("navigation")
        .getByRole("link", { name: "离线词库" })
        .click();
      await expect(
        page.getByRole("button", { name: "打开导航" }),
      ).toHaveAttribute("aria-expanded", "false");
    }
    expect(failedAssets).toEqual([]);
  });
}
