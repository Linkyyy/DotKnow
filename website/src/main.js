import { release, artifactUrl } from "./release.js";

document.querySelectorAll("[data-version]").forEach((el) => {
  el.textContent = release.version;
});
document.querySelectorAll("[data-size]").forEach((el) => {
  el.textContent = release.size;
});
const files = {
  windows: release.windowsFile,
  source: "TypeLingo-Source.zip",
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
    name: "一封来自远方的信",
    eyebrow: "A NOTE FOR TODAY",
    heading: "Good things take time.",
    context: [
      "There is a whole world out there.",
      "Take a breath, stay curious, and",
    ],
    source: "enjoy the little things.",
    pair: "英语 → 中文",
    result: "享受生活中的小美好。",
  },
  write: {
    name: "写给合作伙伴的邮件",
    eyebrow: "MAKE YOURSELF UNDERSTOOD",
    heading: "把想法，准确传达。",
    context: ["关于下周的项目安排，", "我想和你确认一下时间。"],
    source: "期待与你进一步交流。",
    pair: "中文 → 英语",
    result: "I look forward to discussing this further with you.",
  },
  learn: {
    name: "今天想记住的一句话",
    eyebrow: "A LITTLE MORE EVERY DAY",
    heading: "出会いを、大切に。",
    context: ["每一种语言里，", "都有一些值得留下的温柔。"],
    source: "また明日。",
    pair: "日本語 → 中文",
    result: "明天见。",
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
document.querySelector("#demo-copy").addEventListener("click", async () => {
  try {
    await navigator.clipboard.writeText(
      document.querySelector("#demo-result").textContent,
    );
    notify("示例译文已复制");
  } catch {
    notify("浏览器未允许复制，请选中示例译文手动复制。");
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
