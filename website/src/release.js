// Update this single manifest when a new verified desktop build is published.
export const release = Object.freeze({
  version: "0.5.0-preview.2",
  commit: "03b921af85699b5da1da3d904061d3d2ab786b70",
  windowsFile: "DotKnow-Windows-x64.zip",
  size: "68.9 MB",
  sha256: "4e7c846d7c01f9bf03e4e6b822b9bb370d8f0b7c85664f0264a893e2b5f0a92a",
});
export const artifactUrl = (file) =>
  `https://github.com/Linkyyy/TypeLingo/raw/${release.commit}/downloads/${file}`;
