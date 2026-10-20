// Update this single manifest when a new verified desktop build is published.
export const release = Object.freeze({
  version: "0.5.0-preview.1",
  commit: "72c6154a18265e5291f898dfc425bcc39cc30889",
  windowsFile: "DotKnow-Windows-x64.zip",
  size: "68.8 MB",
  sha256: "5d62d88792ef83fd1762bf1ad00a70c4ff3f9ade032c676ecf7128725f737238",
});
export const artifactUrl = (file) =>
  `https://github.com/Linkyyy/TypeLingo/raw/${release.commit}/downloads/${file}`;
