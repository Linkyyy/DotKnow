import { build } from "vite";
import { readFile, writeFile } from "node:fs/promises";
import { release, artifactUrl } from "../src/release.js";

await build();
// Keep download links functional with JavaScript disabled, and derive Vercel
// redirects from the same manifest so website and binary cannot drift apart.
const html = await readFile("dist/index.html", "utf8");
const files = {
  windows: release.windowsFile,
  source: "DotKnow-Source.zip",
  checksums: "SHA256SUMS.txt",
};
let hydrated = html.replace(
  /(data-download="(windows|source|checksums)"\s+href=")[^"]*(")/g,
  (_, prefix, type, suffix) => `${prefix}${artifactUrl(files[type])}${suffix}`,
);
// Visible release metadata must also stay current when JavaScript is disabled.
for (const [attribute, value] of Object.entries({ version: release.version, size: release.size, sha256: release.sha256 })) {
  const pattern = new RegExp(`(<(?:span|code)\\b[^>]*\\bdata-${attribute}[^>]*>)[^<]*(<\\/(?:span|code)\\s*>)`, "g");
  hydrated = hydrated.replace(pattern, (_, prefix, suffix) => `${prefix}${value}${suffix}`);
}
const siteUrl =
  process.env.SITE_URL ||
  (process.env.VERCEL_PROJECT_PRODUCTION_URL
    ? `https://${process.env.VERCEL_PROJECT_PRODUCTION_URL}`
    : process.env.VERCEL_URL
      ? `https://${process.env.VERCEL_URL}`
      : undefined);
if (siteUrl) {
  const origin = new URL(siteUrl).origin;
  if (!origin.startsWith("https://") || /[<>"']/.test(origin))
    throw new Error("SITE_URL must be a valid HTTPS origin");
  hydrated = hydrated
    .replace(
      'content="/assets/social.png"',
      `content="${origin}/assets/social.png"`,
    )
    .replace(
      "</head>",
      `<link rel="canonical" href="${origin}/"><meta property="og:url" content="${origin}/"></head>`,
    );
  await writeFile(
    "dist/sitemap.xml",
    `<?xml version="1.0" encoding="UTF-8"?><urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9"><url><loc>${origin}/</loc></url></urlset>`,
  );
  await writeFile(
    "dist/robots.txt",
    `User-agent: *\nAllow: /\nSitemap: ${origin}/sitemap.xml\n`,
  );
}
await writeFile("dist/index.html", hydrated);
// Static fallback for hosts that do not support redirects.
await writeFile(
  "dist/download.html",
  `<!doctype html><html lang="zh-CN"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><meta http-equiv="refresh" content="0;url=${artifactUrl(release.windowsFile)}"><title>下载点知 DotKnow</title><p>正在前往 Windows 下载… <a href="${artifactUrl(release.windowsFile)}">点击下载</a></p></html>`,
);
