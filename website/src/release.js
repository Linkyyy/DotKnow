// Update this single manifest when a new verified desktop build is published.
export const release = Object.freeze({
  version: "0.5.0-preview.3",
  commit: "7c7b5ee9a4733fcee13668e85e81b5bec03a61fc",
  windowsFile: "DotKnow-Windows-x64.zip",
  size: "68.9 MB",
  sha256: "2be20220c378f898db523a0f6a2b567d12ca7972accaf5d8c005e43d1936f721",
});
export const artifactUrl = (file) =>
  `https://github.com/Linkyyy/DotKnow/raw/${release.commit}/downloads/${file}`;
