import { existsSync } from "node:fs";

// Use system Chromium in the managed workspace; use Playwright's installed
// browser on other machines and in CI. The override supports custom installs.
export const launchOptions = {
  executablePath:
    process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE_PATH ||
    (existsSync("/usr/bin/chromium") ? "/usr/bin/chromium" : undefined),
  args:
    process.env.CI || existsSync("/usr/bin/chromium") ? ["--no-sandbox"] : [],
};
