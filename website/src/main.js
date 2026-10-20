import { release, artifactUrl } from "./release.js";

document.querySelectorAll("[data-version]").forEach((el) => {
  el.textContent = release.version;
});
document.querySelectorAll("[data-size]").forEach((el) => {
  el.textContent = release.size;
});
document.querySelectorAll("[data-sha256]").forEach((el) => {
  el.textContent = release.sha256;
});
const files = {
  windows: release.windowsFile,
  source: "DotKnow-Source.zip",
  checksums: "SHA256SUMS.txt",
};
document.querySelectorAll("[data-download]").forEach((link) => {
  link.href = artifactUrl(files[link.dataset.download]);
});

const menu = document.querySelector(".menu-toggle");
const navigation = document.querySelector("#navigation");
function closeMenu() {
  menu.setAttribute("aria-expanded", "false");
  menu.setAttribute("aria-label", "打开导航");
  navigation.classList.remove("is-open");
}
menu.addEventListener("click", () => {
  const open = menu.getAttribute("aria-expanded") !== "true";
  menu.setAttribute("aria-expanded", String(open));
  menu.setAttribute("aria-label", open ? "关闭导航" : "打开导航");
  navigation.classList.toggle("is-open", open);
});
navigation
  .querySelectorAll("a")
  .forEach((link) => link.addEventListener("click", closeMenu));
document.addEventListener("keydown", (event) => {
  if (event.key === "Escape") {
    closeMenu();
  }
});

const scenes = {
  read: {
    name: "读到一个陌生概念", eyebrow: "CURIOSITY STARTS HERE",
    heading: "为什么会这样？", context: ["阅读到这里，停了一下。", "选中这一小段，把疑问问清楚。"],
    source: "熵（entropy）", pair: "百科 · 中文回答",
    question: "先解释这个概念，再举一个生活中的例子。",
    result: "熵描述系统可能状态的数量。就像一副扑克牌：打乱的排列方式，远多于整齐排列的方式。",
  },
  write: {
    name: "一句想读懂的日语", eyebrow: "LANGUAGES STILL MATTER",
    heading: "跨过语言这道坎。", context: ["翻译依然是重要的一部分。", "中英日互译，常用表达留在本地。"],
    source: "また明日。", pair: "翻译 · 日语 → 中文",
    question: "翻译成中文，保持自然的表达。", result: "明天见。",
  },
  learn: {
    name: "换成自己的提问方式", eyebrow: "YOUR QUESTION. YOUR WAY.",
    heading: "这次，听懂为止。", context: ["预设以外，也有你的问题。", "不必离开正在阅读的内容。"],
    source: "复利", pair: "自由提问 · 固定示例",
    question: "像给小学生讲故事一样解释这个词。",
    result: "把存下的利息也加入本金，下次一起产生利息。这就是“利滚利”，也是复利的直观含义。",
  },
};
const tabs = [...document.querySelectorAll("[data-scene]")];
function selectScene(tab) {
  tabs.forEach((item) => {
    const selected = item === tab;
    item.setAttribute("aria-selected", String(selected));
    item.tabIndex = selected ? 0 : -1;
  });
  const scene = scenes[tab.dataset.scene];
  for (const [key, id] of Object.entries({
    name: "demo-document-name",
    eyebrow: "demo-eyebrow",
    heading: "demo-heading",
    source: "demo-source",
    pair: "demo-pair",
    result: "demo-result",
    question: "demo-question",
  })) {
    document.getElementById(id).textContent = scene[key];
  }
  const context = document.getElementById("demo-context");
  context.replaceChildren(
    document.createTextNode(scene.context[0]),
    document.createElement("br"),
    document.createTextNode(scene.context[1]),
  );
  document.querySelector("#demo-panel").setAttribute("aria-labelledby", tab.id);
  document.querySelector(".stage-topline > span:last-child").textContent =
    `0${tabs.indexOf(tab) + 1} / 03`;
  document
    .querySelector("#demo-favorite")
    .setAttribute("aria-pressed", "false");
}
tabs.forEach((tab, index) => {
  tab.addEventListener("click", () => selectScene(tab));
  tab.addEventListener("keydown", (event) => {
    let next;
    if (event.key === "ArrowRight") next = (index + 1) % tabs.length;
    if (event.key === "ArrowLeft")
      next = (index + tabs.length - 1) % tabs.length;
    if (event.key === "Home") next = 0;
    if (event.key === "End") next = tabs.length - 1;
    if (next !== undefined) {
      event.preventDefault();
      selectScene(tabs[next]);
      tabs[next].focus();
    }
  });
});

let toastTimer;
function notify(message) {
  const toast = document.getElementById("toast");
  clearTimeout(toastTimer);
  toast.textContent = message;
  toast.classList.add("visible");
  toastTimer = setTimeout(() => toast.classList.remove("visible"), 3200);
}
document.querySelector("#copy-checksum").addEventListener("click", async () => {
  try {
    await navigator.clipboard.writeText(release.sha256);
    notify("安装包校验值已复制");
  } catch {
    notify("浏览器未允许复制，请手动选中校验值。");
  }
});

const themeTabs = [...document.querySelectorAll("[data-theme]")];
const themeModes = [...document.querySelectorAll("[data-theme-mode]")];
const themeLabels = { paper: "浅纸", forest: "深林", ocean: "雾蓝", lavender: "暮紫" };
let previewTheme = "paper";
let previewMode = "light";
function renderTheme() {
  const label = `${themeLabels[previewTheme]} · ${previewMode === "dark" ? "深色" : "浅色"}模式`;
  const image = document.querySelector("#theme-image");
  image.src = `/assets/screens/theme-${previewTheme}-${previewMode}.png`;
  image.alt = `点知主页真实截图，${label}`;
  document.querySelector("#theme-caption").textContent = `${label} · 真实 Windows 截图`;
  document.querySelector("#theme-panel").setAttribute("aria-labelledby", `theme-${previewTheme}`);
  themeTabs.forEach((tab) => {
    const selected = tab.dataset.theme === previewTheme;
    tab.setAttribute("aria-selected", String(selected));
    tab.tabIndex = selected ? 0 : -1;
  });
  themeModes.forEach((button) => button.setAttribute("aria-pressed", String(button.dataset.themeMode === previewMode)));
}
themeTabs.forEach((tab, index) => {
  tab.addEventListener("click", () => { previewTheme = tab.dataset.theme; renderTheme(); });
  tab.addEventListener("keydown", (event) => {
    let next;
    if (event.key === "ArrowRight") next = (index + 1) % themeTabs.length;
    if (event.key === "ArrowLeft") next = (index + themeTabs.length - 1) % themeTabs.length;
    if (event.key === "Home") next = 0;
    if (event.key === "End") next = themeTabs.length - 1;
    if (next !== undefined) {
      event.preventDefault(); previewTheme = themeTabs[next].dataset.theme; renderTheme(); themeTabs[next].focus();
    }
  });
});
themeModes.forEach((button) => button.addEventListener("click", () => { previewMode = button.dataset.themeMode; renderTheme(); }));

document.querySelector("#demo-copy").addEventListener("click", async () => {
  try {
    await navigator.clipboard.writeText(
      document.querySelector("#demo-result").textContent,
    );
    notify("示例回答已复制");
  } catch {
    notify("浏览器未允许复制，请选中示例回答手动复制。");
  }
});
document.querySelector("#demo-favorite").addEventListener("click", (event) => {
  const button = event.currentTarget;
  const saved = button.getAttribute("aria-pressed") !== "true";
  button.setAttribute("aria-pressed", String(saved));
  notify(
    saved ? "已体验收藏 · 实际收藏请在 Windows 软件中使用" : "已取消示例收藏",
  );
});
const reveal = document.querySelector("#review-reveal");
const answer = document.querySelector("#review-answer");
const ratings = document.querySelector(".review-ratings");
reveal.addEventListener("click", () => {
  answer.hidden = false;
  ratings.hidden = false;
  reveal.hidden = true;
  reveal.setAttribute("aria-expanded", "true");
  ratings.querySelector("button").focus();
});
document.querySelectorAll("[data-rating]").forEach((button) =>
  button.addEventListener("click", () => {
    document.querySelector("#review-feedback").textContent =
      button.dataset.rating === "remembered"
        ? "真好，记住了这次相遇。软件会为收藏安排下次复习。"
        : "没关系，再给自己一点时间。";
    answer.hidden = true;
    ratings.hidden = true;
    reveal.hidden = false;
    reveal.setAttribute("aria-expanded", "false");
    reveal.focus();
  }),
);
