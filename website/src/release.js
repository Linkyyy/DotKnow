// Update this single manifest when a new verified desktop build is published.
export const release = Object.freeze({
  version: "0.4.0-preview.5",
  commit: "a5f8e42553dd6a9afc49c30e83a60d20df7da7b7",
  windowsFile: "TypeLingo-Windows-x64.zip",
  size: "67.4 MB",
  sha256: "a54804b02411d7e6e9fd4cf0e80d625eca49b26b03c0dc9f9e9764e7eea0416c",
});
export const artifactUrl = (file) =>
  `https://github.com/Linkyyy/TypeLingo/raw/${release.commit}/downloads/${file}`;
