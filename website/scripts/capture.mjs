// Capture the actual rendered website for documentation and sharing.
// Run npm run preview -- --port 4173 in a separate terminal first.
import { chromium } from "@playwright/test";
import { mkdir } from "node:fs/promises";
import { launchOptions } from "./browser.mjs";

const browser = await chromium.launch(launchOptions);
try {
  const page = await browser.newPage({
    viewport: { width: 1440, height: 1000 },
    deviceScaleFactor: 1,
  });
  await page.goto("http://127.0.0.1:4173");
  await page.evaluate(() => document.fonts.ready);
  await mkdir("../docs/website", { recursive: true });
  await page.screenshot({
    path: "../docs/website/desktop.png",
    fullPage: true,
  });
  await page.setViewportSize({ width: 390, height: 844 });
  await page.screenshot({ path: "../docs/website/mobile.png", fullPage: true });
  await page.setViewportSize({ width: 1200, height: 630 });
  await page.addStyleTag({
    content:
      ".header{height:80px}.hero{min-height:550px;padding-block:30px;gap:30px}.hero-stage{height:510px}.demo-workspace{height:360px;margin-top:18px}.translation-window{top:165px;padding:12px}.translation-result{font-size:14px;line-height:1.5}.document-content{padding:18px 20px}.document-content h2{margin:6px 0;font-size:20px}.document-window{height:250px}.hero-caption,.illustration-note,.stage-bottom{display:none}.demo-tabs{margin-top:20px}.hero-copy h1{font-size:58px}.container{width:calc(100% - 80px)}.hero-description{font-size:13px}.hero-actions{margin-top:24px}.hero-copy .release-pill{font-size:10px}",
  });
  await page.screenshot({ path: "public/assets/social.png" });
  console.log(
    "Saved desktop, mobile and social sharing screenshots. Rebuild to copy the sharing image to dist.",
  );
} finally {
  await browser.close();
}
