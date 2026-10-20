// Update this single manifest when a new verified desktop build is published.
export const release = Object.freeze({
  version: "0.4.0-preview.6",
  commit: "93d63fb9fbfee94bf4f302a852326bcc60407571",
  windowsFile: "TypeLingo-Windows-x64.zip",
  size: "68.5 MB",
  sha256: "9acd644e0ae57118d03cf50176804b68d2109d72fb7a44d377e098dbbfcebb4a",
});
export const artifactUrl = (file) =>
  `https://github.com/Linkyyy/TypeLingo/raw/${release.commit}/downloads/${file}`;
