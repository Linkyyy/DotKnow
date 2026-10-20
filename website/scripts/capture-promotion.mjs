// These are browser screenshots of the fixed website examples, not generated app screenshots.
import { chromium } from "@playwright/test";
import { copyFile } from "node:fs/promises";
import { launchOptions } from "./browser.mjs";

const browser = await chromium.launch(launchOptions);
try {
  const page = await browser.newPage({ viewport: { width: 900, height: 1000 }, deviceScaleFactor: 1 });
  await page.goto("http://127.0.0.1:4173");
  await page.evaluate(() => document.fonts.ready);
  await page.addStyleTag({ content: ".hero-stage{width:640px;max-width:none;height:640px;padding:24px}.demo-workspace{height:430px;margin-inline:0}.translation-result{font-size:17px}.document-content h2{font-size:22px}" });
  await page.locator(".hero-stage").screenshot({ path: "../docs/promotion/assistant-square.png" });
  await copyFile("public/assets/social.png", "../docs/promotion/hero.png");
  await copyFile("../docs/ui-preview/assistant-encyclopedia.png", "../docs/promotion/app-assistant.png");
  await copyFile("../docs/ui-preview/settings-prompts.png", "../docs/promotion/app-prompts.png");
  console.log("Saved DotKnow website illustrations and separately attributed real WPF previews.");
} finally {
  await browser.close();
}
