// Update this single manifest when a new verified desktop build is published.
export const release = Object.freeze({
  version: "0.5.0-preview.1",
  commit: "4bd811b754e25b67c033ee979803f8c02b9f39b4",
  windowsFile: "DotKnow-Windows-x64.zip",
  size: "68.6 MB",
  sha256: "3579eb888de1a440579f5e10acf729f31d36069022a8d3ef205c9d6148c64007",
});
export const artifactUrl = (file) =>
  `https://github.com/Linkyyy/TypeLingo/raw/${release.commit}/downloads/${file}`;
