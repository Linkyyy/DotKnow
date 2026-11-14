// Update this single manifest when a new verified desktop build is published.
export const release = Object.freeze({
  version: "0.6.0-preview.2",
  commit: "d58d2f0c43a6c5a23a864e13ab1dfcc9efef2c45",
  windowsFile: "DotKnow-Windows-x64.zip",
  size: "69.0 MB",
  sha256: "7eb82baad702a27cc077ba90e80c4b76cd3da4cb0ef3e3e621a52fb072a0c717",
});
export const artifactUrl = (file) =>
  `https://github.com/Linkyyy/DotKnow/raw/${release.commit}/downloads/${file}`;
