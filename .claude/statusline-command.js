#!/usr/bin/env node
//
// Project statusline for circle-integration.
// Fields: model+effort, dir, git branch+dirty, context %, 5h %, 7d %, cost estimate.
//
// Pricing is HARDCODED (per 1M tokens, USD) since Claude Code's statusline JSON does
// not reliably expose live provider pricing. Update these if Anthropic's list prices
// change; keep in sync across model families.
const PRICING = {
  haiku: { in: 1.0, out: 5.0 },
  sonnet: { in: 3.0, out: 15.0 },
  opus: { in: 15.0, out: 75.0 },
};

const { spawnSync } = require("child_process");
const os = require("os");

const esc = "\x1b";
const c = (code) => `${esc}[${code}m`;
const reset = `${esc}[0m`;

const colorByPercent = (val) => {
  if (val >= 80) return "38;5;196"; // red
  if (val >= 50) return "38;5;214"; // orange
  return "38;5;71"; // green
};

const safePercent = (v) => (typeof v === "number" ? Math.round(v) : null);

// resets_at is Unix epoch seconds per the statusline JSON schema.
const fmtTime = (epochSeconds) => {
  if (typeof epochSeconds !== "number") return "";
  const d = new Date(epochSeconds * 1000);
  if (isNaN(d.getTime())) return "";
  let h = d.getHours();
  const m = String(d.getMinutes()).padStart(2, "0");
  const ampm = h >= 12 ? "pm" : "am";
  h = h % 12 || 12;
  return `${h}:${m}${ampm}`;
};

const DAY_NAMES = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];

// Weekly reset: day-of-week + time (e.g. "Fri 11:30am"); omits the day if it resets today.
const fmtDayTime = (epochSeconds) => {
  if (typeof epochSeconds !== "number") return "";
  const d = new Date(epochSeconds * 1000);
  if (isNaN(d.getTime())) return "";
  const now = new Date();
  const sameDay =
    d.getFullYear() === now.getFullYear() &&
    d.getMonth() === now.getMonth() &&
    d.getDate() === now.getDate();
  const time = fmtTime(epochSeconds);
  return sameDay ? time : `${DAY_NAMES[d.getDay()]} ${time}`;
};

const EFFORT_ICON = {
  low: "🐇",
  medium: "🚶",
  high: "🐢",
  xhigh: "🐌",
  max: "🏔️",
};

function pricingFor(modelId) {
  const id = (modelId || "").toLowerCase();
  if (id.includes("haiku")) return PRICING.haiku;
  if (id.includes("opus")) return PRICING.opus;
  return PRICING.sonnet; // default/fallback
}

function getGitInfo(cwd) {
  let branch = "",
    dirty = false;
  try {
    const res = spawnSync(
      "git",
      ["-C", cwd, "--no-optional-locks", "rev-parse", "--abbrev-ref", "HEAD"],
      { timeout: 200, encoding: "utf8", shell: false },
    );
    if (res.status === 0) branch = res.stdout.trim();
  } catch {}

  if (branch) {
    try {
      const res = spawnSync(
        "git",
        ["-C", cwd, "--no-optional-locks", "status", "--porcelain"],
        { timeout: 200, encoding: "utf8", shell: false },
      );
      dirty = !!(res.stdout && res.stdout.trim().length > 0);
    } catch {}
  }
  return { branch, dirty };
}

let input = "";
process.stdin.setEncoding("utf8");
process.stdin.on("data", (chunk) => (input += chunk));
process.stdin.on("end", () => {
  let data = {};
  try {
    data = JSON.parse(input);
  } catch {}

  const cwd = data?.cwd ?? data?.workspace?.current_dir ?? "";
  const modelId = data?.model?.id ?? "";
  const modelName = (data?.model?.display_name ?? "").replace(/^Claude\s*/i, "");
  const effortLevel = data?.effort?.level ?? null;

  const ctxUsed = safePercent(data?.context_window?.used_percentage);
  const rate5h = safePercent(data?.rate_limits?.five_hour?.used_percentage);
  const rate7d = safePercent(data?.rate_limits?.seven_day?.used_percentage);
  const rate5hReset = fmtTime(data?.rate_limits?.five_hour?.resets_at);
  const rate7dReset = fmtDayTime(data?.rate_limits?.seven_day?.resets_at);

  const usage = data?.context_window?.current_usage ?? null;
  const price = pricingFor(modelId);
  let cost = null;
  if (usage) {
    const inputTok =
      (usage.input_tokens ?? 0) +
      (usage.cache_creation_input_tokens ?? 0) +
      (usage.cache_read_input_tokens ?? 0);
    const outputTok = usage.output_tokens ?? 0;
    cost = (inputTok * price.in + outputTok * price.out) / 1_000_000;
  }

  // ---- dir (abbreviated) ----
  const toUnix = (p) =>
    p.replace(/^([A-Za-z]):/, (_, d) => "/" + d.toLowerCase()).replace(/\\/g, "/");
  const cwdUnix = toUnix(cwd || "");
  const homeUnix = toUnix(os.homedir() || "").toLowerCase();
  let displayDir = cwdUnix.toLowerCase().startsWith(homeUnix)
    ? "~" + cwdUnix.slice(homeUnix.length)
    : cwdUnix;
  const parts = displayDir.split("/").filter(Boolean);
  if (parts.length > 2) displayDir = "…/" + parts.slice(-2).join("/");

  // ---- git ----
  const { branch, dirty } = cwd ? getGitInfo(cwd) : { branch: "", dirty: false };

  const sep = `${c("38;5;240")} │ ${reset}`;
  const out = [];

  // 1. Model + effort
  if (modelName) {
    const effortStr = effortLevel
      ? ` ${EFFORT_ICON[effortLevel] ?? "⚡"} ${effortLevel}`
      : "";
    out.push(`${c("38;5;213")}🧠 ${modelName}${effortStr}${reset}`);
  }

  // 2. Directory
  if (displayDir) {
    out.push(`${c("38;5;110")}📁 ${displayDir}${reset}`);
  }

  // 3. Git branch + dirty status
  if (branch) {
    const status = dirty ? `${c("38;5;196")}✗${reset}` : `${c("38;5;71")}✓${reset}`;
    out.push(`${c("38;5;39")}🌿 ${branch}${reset} ${status}`);
  }

  // 4. Context window
  if (ctxUsed != null) {
    out.push(`${c(colorByPercent(ctxUsed))}🧩 ${ctxUsed}%${reset}`);
  }

  // 5. 5-hour usage + reset time
  if (rate5h != null) {
    const resetStr = rate5hReset ? ` ↻ ${rate5hReset}` : "";
    out.push(`${c(colorByPercent(rate5h))}⏳ ${rate5h}%${resetStr}${reset}`);
  }

  // 6. 7-day usage + reset day/time
  if (rate7d != null) {
    const resetStr = rate7dReset ? ` ↻ ${rate7dReset}` : "";
    out.push(`${c(colorByPercent(rate7d))}📅 ${rate7d}%${resetStr}${reset}`);
  }

  // 7. Cost estimate (hardcoded per-model pricing; see PRICING table above)
  if (cost != null) {
    out.push(`${c("38;5;178")}💰 $${cost.toFixed(4)}${reset}`);
  }

  process.stdout.write(out.join(sep) || "claude");
});
