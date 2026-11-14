// Update this single manifest when a new verified desktop build is published.
export const release = Object.freeze({
  version: "0.6.0-preview.1",
  commit: "7a857e990a5a6ad7c261496eb8f6f444162e9d63",
  windowsFile: "DotKnow-Windows-x64.zip",
  size: "69.0 MB",
  sha256: "ab75315743e78c9044c206456b8b5af52877c98c9c25d7b38befc3159537db85",
});
export const artifactUrl = (file) =>
  `https://github.com/Linkyyy/DotKnow/raw/${release.commit}/downloads/${file}`;
