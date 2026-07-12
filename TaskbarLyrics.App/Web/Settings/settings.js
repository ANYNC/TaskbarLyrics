const sourceNames = {
  QQMusic: "QQ音乐",
  Netease: "网易云音乐",
  Kugou: "酷狗音乐",
  Spotify: "Spotify"
};

const sourceIcons = {
  QQMusic: "../../Assets/PlayerIcons/QQ音乐.png",
  Netease: "../../Assets/PlayerIcons/网易云音乐.png",
  Kugou: "../../Assets/PlayerIcons/酷狗音乐.png",
  Spotify: "../../Assets/PlayerIcons/spotify.png"
};

const presetForegroundColors = {
  Dark: "#FF111827",
  Light: "#FFFFFFFF"
};

let state = null;
let fonts = [];
let draggedSource = null;
let updateReleaseUrl = "";
let activeSelectTrigger = null;
let activeSelectIndex = -1;
let colorDraft = { h: 0, s: 0, v: 1, hex: "#FFFFFFFF" };
let colorPointerActive = false;

const selectOptions = {
  spectrumDisplayMode: [
    { value: "PureMusicOnly", label: "仅纯音乐时" },
    { value: "PureMusicOrNoLyrics", label: "纯音乐或无歌词时" },
    { value: "Always", label: "始终显示" }
  ],
  fontFamily: [],
  fontWeight: [
    { value: "Light", label: "细体" }, { value: "Normal", label: "常规" },
    { value: "Medium", label: "中等" }, { value: "SemiBold", label: "半粗体" }, { value: "Bold", label: "粗体" }
  ],
  foregroundColorMode: [{ value: "Dark", label: "深色" }, { value: "Light", label: "浅色" }, { value: "Custom", label: "自定义" }],
  horizontalAnchor: [{ value: "Left", label: "左侧" }, { value: "Center", label: "居中" }, { value: "Right", label: "右侧" }]
};
const presetColors = ["#FFFFFFFF", "#FFA1A1AA", "#FF18181B", "#FFEF4444", "#FFF97316", "#FFEAB308", "#FF22C55E", "#FF06B6D4", "#FF3B82F6", "#FFA855F7"];

const bridge = {
  post(message) {
    window.chrome?.webview?.postMessage(JSON.stringify(message));
  }
};

function updateSetting(key, value) {
  if (!state) return;
  value = normalizeSettingValue(key, value);
  state[key] = value;
  if (key === "foregroundColorMode") updateForegroundMode(value);
  if (key === "foregroundColor") {
    state.foregroundColorMode = "Custom";
    updateSwatch(value);
    updateColorModeControl();
  }
  if (key === "enableSpectrum") updateSpectrumChildren();
  syncSelectTriggers();
  updateDimensionSteppers(key);
  updateRangeControls();
  animateSettingFeedback(key);
  bridge.post({ type: "update", key, value });
}

function normalizeSettingValue(key, value) {
  if (!state) return value;
  const stepper = document.querySelector(`.stepper[data-key="${key}"]`);
  const rangeControl = document.querySelector(`.range-control [data-key="${key}"]`);
  if (!stepper && !rangeControl) return value;
  const range = stepper ? getStepperRange(stepper) : getRangeControlRange(rangeControl);
  return clamp(Number(value), range.min, range.max);
}

function animateSettingFeedback(key) {
  const control = document.querySelector(`[data-key="${key}"]`);
  const row = control?.closest(".player, .row, .form-row");
  if (!row) return;

  row.classList.remove("setting-updated");
  row.offsetWidth;
  row.classList.add("setting-updated");
}

function setState(nextState, fontList = fonts) {
  state = nextState;
  fonts = fontList;
  renderFonts();
  renderControls();
  renderOrder();
  renderAbout();
}

function renderFonts() {
  selectOptions.fontFamily = fonts.map((font) => ({
    value: typeof font === "string" ? font : font.value,
    label: typeof font === "string" ? font : font.label
  })).filter((font) => font.value);
}

function renderControls() {
  if (!state) return;

  for (const input of document.querySelectorAll("[data-key]")) {
    const key = input.dataset.key;
    if (!(key in state)) continue;

    if (input.type === "checkbox") {
      input.checked = Boolean(state[key]);
    } else if (input.tagName === "TEXTAREA" && Array.isArray(state[key])) {
      input.value = state[key].join("\n");
    } else {
      input.value = state[key] ?? "";
    }
  }

  syncSelectTriggers();
  updateSwatch(state.foregroundColor);
  updateColorModeControl();
  updateSpectrumChildren();
  updateDimensionSteppers();
  updateRangeControls();
}

function updateSpectrumChildren() {
  const enabled = Boolean(state?.enableSpectrum);
  document.querySelectorAll(".spectrum-child").forEach((row) => {
    row.classList.toggle("disabled", !enabled);
    row.querySelectorAll("input, select, textarea, button").forEach((control) => {
      control.disabled = !enabled;
    });
  });
}

function getStepperRange(stepper) {
  const safeKey = stepper.dataset.safeKey;
  const useSafeRange = safeKey ? Boolean(state?.[safeKey]) : false;
  const min = Number(useSafeRange ? stepper.dataset.safeMin : stepper.dataset.extendedMin ?? stepper.dataset.min);
  const max = Number(useSafeRange ? stepper.dataset.safeMax : stepper.dataset.extendedMax ?? stepper.dataset.max);
  return {
    min: Number.isFinite(min) ? min : Number(stepper.dataset.min),
    max: Number.isFinite(max) ? max : Number(stepper.dataset.max)
  };
}

function getRangeControlRange(input) {
  return {
    min: Number(input.min),
    max: Number(input.max)
  };
}

function updateRangeValue(input) {
  const output = document.querySelector(`[data-value-for="${input.dataset.key}"]`);
  if (!output) return;
  output.textContent = `${input.value} px`;
}

function getCoverCornerRadiusMax() {
  const coverSize = Number(state?.coverSize);
  return Math.max(0, Math.floor((Number.isFinite(coverSize) ? coverSize : 40) / 2));
}

function updateRangeControls(changedKey = "") {
  document.querySelectorAll('.range-control input[type="range"][data-key]').forEach((input) => {
    const key = input.dataset.key;
    if (changedKey && key !== changedKey && !(key === "coverCornerRadius" && changedKey === "coverSize")) return;

    if (key === "coverCornerRadius") {
      input.max = String(getCoverCornerRadiusMax());
    }

    const min = Number(input.min);
    const max = Number(input.max);
    const current = Number(input.value);
    const next = clamp(Number.isFinite(current) ? current : Number(state?.[key] ?? min), min, max);
    input.value = String(next);
    if (state && key in state && Number(state[key]) !== next) {
      state[key] = next;
      bridge.post({ type: "update", key, value: next });
    }

    updateRangeValue(input);
  });
}

function updateDimensionSteppers(changedKey = "") {
  if (!state) return;
  document.querySelectorAll(".stepper[data-safe-key]").forEach((stepper) => {
    const key = stepper.dataset.key;
    const safeKey = stepper.dataset.safeKey;
    if (changedKey && changedKey !== key && changedKey !== safeKey) return;

    const range = getStepperRange(stepper);
    stepper.dataset.min = String(range.min);
    stepper.dataset.max = String(range.max);

    if (!(key in state)) return;
    const decimals = Number(stepper.dataset.decimals);
    const next = clamp(Number(state[key]), range.min, range.max);
    if (next !== Number(state[key])) {
      state[key] = next;
      bridge.post({ type: "update", key, value: next });
    }

    const input = stepper.querySelector("input");
    if (input) input.value = formatNumber(Number(state[key]), decimals);
  });
}

function escapeHtml(value) {
  const node = document.createElement("span");
  node.textContent = value;
  return node.innerHTML;
}

function syncSelectTriggers() {
  document.querySelectorAll(".select-trigger[data-key]").forEach((trigger) => {
    const options = selectOptions[trigger.dataset.key] ?? [];
    const selected = options.find((option) => String(option.value) === String(state?.[trigger.dataset.key]));
    trigger.querySelector(".select-trigger-value").textContent = selected?.label ?? "请选择";
  });
}

function positionPopover(popover, trigger, minimumWidth) {
  const rect = trigger.getBoundingClientRect();
  const margin = 8;
  const width = Math.min(window.innerWidth - margin * 2, Math.max(minimumWidth, rect.width));
  popover.style.width = `${width}px`;
  const height = popover.offsetHeight;
  const below = window.innerHeight - rect.bottom - margin;
  const top = below >= height || below >= rect.top ? rect.bottom + 5 : rect.top - height - 5;
  popover.style.left = `${Math.min(window.innerWidth - width - margin, Math.max(margin, rect.left))}px`;
  popover.style.top = `${Math.max(margin, Math.min(window.innerHeight - height - margin, top))}px`;
}

function renderSelectOptions() {
  if (!activeSelectTrigger) return;
  const key = activeSelectTrigger.dataset.key;
  const options = selectOptions[key] ?? [];
  const listbox = document.getElementById("selectListbox");
  listbox.innerHTML = options.map((option, index) => {
    const selected = String(option.value) === String(state?.[key]);
    return `<div id="selectOption-${index}" class="select-option${index === activeSelectIndex ? " is-active" : ""}" role="option" aria-selected="${selected}" data-option-index="${index}"><span class="select-option-check">✓</span><span>${escapeHtml(option.label)}</span></div>`;
  }).join("");
  listbox.setAttribute("aria-activedescendant", `selectOption-${activeSelectIndex}`);
  listbox.querySelector(".is-active")?.scrollIntoView({ block: "nearest" });
}

function openSelect(trigger, direction = 0) {
  closeColorPopover(false);
  if (activeSelectTrigger && activeSelectTrigger !== trigger) closeSelect(false);
  activeSelectTrigger = trigger;
  const options = selectOptions[trigger.dataset.key] ?? [];
  const selectedIndex = options.findIndex((option) => String(option.value) === String(state?.[trigger.dataset.key]));
  activeSelectIndex = direction < 0 ? options.length - 1 : Math.max(0, selectedIndex);
  trigger.setAttribute("aria-expanded", "true");
  document.getElementById("selectPopover").hidden = false;
  renderSelectOptions();
  positionPopover(document.getElementById("selectPopover"), trigger, 210);
  document.getElementById("selectListbox").focus({ preventScroll: true });
}

function closeSelect(returnFocus = true) {
  if (!activeSelectTrigger) return;
  const trigger = activeSelectTrigger;
  trigger.setAttribute("aria-expanded", "false");
  document.getElementById("selectPopover").hidden = true;
  activeSelectTrigger = null;
  activeSelectIndex = -1;
  if (returnFocus) trigger.focus({ preventScroll: true });
}

function chooseSelectOption(index) {
  if (!activeSelectTrigger) return;
  const key = activeSelectTrigger.dataset.key;
  const option = (selectOptions[key] ?? [])[index];
  if (!option) return;
  updateSetting(key, option.value);
  closeSelect(true);
}

function updateSwatch(color) {
  const swatch = document.getElementById("colorSwatch");
  if (!swatch) return;
  const normalized = normalizeCssColor(color);
  swatch.style.background = normalized;
  const value = document.getElementById("colorValue");
  if (value) value.textContent = color ?? "";
  const presetSwatch = document.querySelector("#presetColorReadout .swatch");
  if (presetSwatch) presetSwatch.style.background = normalized;
  swatch.animate(
    [{ transform: "scale(1)" }, { transform: "scale(1.14)" }, { transform: "scale(1)" }],
    { duration: 160, easing: "ease-out" }
  );
}

function updateForegroundMode(mode) {
  if (mode in presetForegroundColors) {
    state.foregroundColor = presetForegroundColors[mode];
    updateSwatch(state.foregroundColor);
  }
  updateColorModeControl();
}

function updateColorModeControl() {
  const picker = document.getElementById("colorPicker");
  if (!picker || !state) return;
  const custom = state.foregroundColorMode === "Custom";
  document.getElementById("colorModeControl")?.classList.toggle("is-custom", custom);
  document.getElementById("customColorControl").hidden = !custom;
  document.getElementById("presetColorReadout").hidden = custom;
  document.getElementById("colorHexInput").value = state.foregroundColor ?? "";
  picker.title = "选择自定义颜色";
}

function normalizeCssColor(color) {
  if (typeof color !== "string") return "#fff";
  if (/^#[0-9a-fA-F]{8}$/.test(color)) {
    return `#${color.slice(3)}`;
  }
  return color;
}

function hexToRgb(hex) {
  const value = hex.replace("#", "").slice(-6);
  return { r: parseInt(value.slice(0, 2), 16), g: parseInt(value.slice(2, 4), 16), b: parseInt(value.slice(4, 6), 16) };
}

function rgbToArgb({ r, g, b }) {
  return `#FF${[r, g, b].map((value) => Math.round(value).toString(16).padStart(2, "0")).join("")}`.toUpperCase();
}

function rgbToHsv({ r, g, b }) {
  r /= 255; g /= 255; b /= 255;
  const max = Math.max(r, g, b), min = Math.min(r, g, b), delta = max - min;
  let h = 0;
  if (delta) h = max === r ? 60 * (((g - b) / delta) % 6) : max === g ? 60 * ((b - r) / delta + 2) : 60 * ((r - g) / delta + 4);
  return { h: (h + 360) % 360, s: max ? delta / max : 0, v: max };
}

function hsvToRgb({ h, s, v }) {
  const c = v * s, x = c * (1 - Math.abs((h / 60) % 2 - 1)), m = v - c;
  const [r, g, b] = h < 60 ? [c, x, 0] : h < 120 ? [x, c, 0] : h < 180 ? [0, c, x] : h < 240 ? [0, x, c] : h < 300 ? [x, 0, c] : [c, 0, x];
  return { r: (r + m) * 255, g: (g + m) * 255, b: (b + m) * 255 };
}

function updateColorDraft(keepInput = false) {
  colorDraft.hex = rgbToArgb(hsvToRgb(colorDraft));
  document.getElementById("colorArea").style.setProperty("--picker-hue", colorDraft.h);
  document.getElementById("hueSlider").value = Math.round(colorDraft.h);
  document.getElementById("colorCursor").style.left = `${colorDraft.s * 100}%`;
  document.getElementById("colorCursor").style.top = `${(1 - colorDraft.v) * 100}%`;
  document.getElementById("colorDraftPreview").style.background = normalizeCssColor(colorDraft.hex);
  if (!keepInput) document.getElementById("colorDraftInput").value = colorDraft.hex;
}

function setColorDraftFromArgb(argb) {
  if (!/^#[0-9a-f]{8}$/i.test(argb)) return false;
  colorDraft = { ...rgbToHsv(hexToRgb(argb)), hex: argb.toUpperCase() };
  updateColorDraft();
  return true;
}

function openColorPopover() {
  if (!state || state.foregroundColorMode !== "Custom") return;
  closeSelect(false);
  setColorDraftFromArgb(state.foregroundColor);
  const popover = document.getElementById("colorPopover");
  popover.hidden = false;
  document.getElementById("colorPicker").setAttribute("aria-expanded", "true");
  positionPopover(popover, document.getElementById("colorPicker"), 264);
  document.getElementById("colorArea").focus({ preventScroll: true });
}

function closeColorPopover(returnFocus = true) {
  const popover = document.getElementById("colorPopover");
  if (!popover || popover.hidden) return;
  popover.hidden = true;
  document.getElementById("colorPicker").setAttribute("aria-expanded", "false");
  colorPointerActive = false;
  if (returnFocus) document.getElementById("colorPicker").focus({ preventScroll: true });
}

function updateColorFromPointer(event) {
  const rect = document.getElementById("colorArea").getBoundingClientRect();
  colorDraft.s = Math.max(0, Math.min(1, (event.clientX - rect.left) / rect.width));
  colorDraft.v = 1 - Math.max(0, Math.min(1, (event.clientY - rect.top) / rect.height));
  updateColorDraft();
}

function renderOrder() {
  const order = document.getElementById("sourceOrder");
  if (!order || !state) return;

  order.innerHTML = "";
  for (const source of state.sourceRecognitionOrder ?? []) {
    const item = document.createElement("div");
    item.className = "order-item";
    item.draggable = true;
    item.dataset.source = source;
    item.innerHTML = `<span class="handle">⋮⋮</span><img class="order-icon" src="${sourceIcons[source] ?? ""}" alt=""><strong>${sourceNames[source] ?? source}</strong>`;
    item.addEventListener("dragstart", onOrderDragStart);
    item.addEventListener("dragover", onOrderDragOver);
    item.addEventListener("drop", onOrderDrop);
    item.addEventListener("dragend", onOrderDragEnd);
    order.appendChild(item);
  }
}

function renderAbout() {
  if (!state) return;

  const version = document.getElementById("appVersion");
  if (version) version.textContent = `当前版本 ${state.appVersion || "--"}`;
}

function setUpdateStatus(payload) {
  const status = document.getElementById("updateStatus");
  const checkButton = document.getElementById("checkUpdateButton");
  const releaseButton = document.getElementById("openReleaseButton");
  const stateName = payload?.state || "";

  if (status) {
    status.textContent = payload?.message || "从 GitHub Releases 检查是否有新版本。";
    status.dataset.state = stateName;
  }

  if (checkButton) {
    checkButton.disabled = stateName === "checking";
    checkButton.textContent = stateName === "checking" ? "检查中" : "检查更新";
  }

  updateReleaseUrl = payload?.url || "";
  releaseButton?.classList.toggle("hidden", stateName !== "available");
}

function onOrderDragStart(event) {
  draggedSource = event.currentTarget.dataset.source;
  event.currentTarget.classList.add("dragging");
  event.dataTransfer.effectAllowed = "move";
}

function onOrderDragOver(event) {
  event.preventDefault();
  event.dataTransfer.dropEffect = "move";
}

function onOrderDrop(event) {
  event.preventDefault();
  const targetSource = event.currentTarget.dataset.source;
  if (!state || !draggedSource || draggedSource === targetSource) return;

  const order = [...state.sourceRecognitionOrder];
  const from = order.indexOf(draggedSource);
  const to = order.indexOf(targetSource);
  if (from < 0 || to < 0) return;

  order.splice(from, 1);
  order.splice(to, 0, draggedSource);
  state.sourceRecognitionOrder = order;
  renderOrder();
  bridge.post({ type: "reorderSources", value: order });
}

function onOrderDragEnd(event) {
  event.currentTarget.classList.remove("dragging");
  draggedSource = null;
}

function clamp(value, min, max) {
  return Math.min(max, Math.max(min, value));
}

function formatNumber(value, decimals) {
  if (decimals <= 0) return String(Math.round(value));
  return Number(value.toFixed(decimals)).toString();
}

function setupCustomControls() {
  document.getElementById("colorPresets").innerHTML = presetColors.map((color) => `<button class="color-preset" type="button" style="--preset:${color}" data-preset-color="${color}" aria-label="选择 ${color}"></button>`).join("");

  document.addEventListener("click", (event) => {
    const trigger = event.target.closest(".select-trigger");
    if (trigger) {
      if (activeSelectTrigger === trigger) closeSelect(true); else openSelect(trigger);
      return;
    }
    const option = event.target.closest("[data-option-index]");
    if (option) chooseSelectOption(Number(option.dataset.optionIndex));
  });

  document.addEventListener("keydown", (event) => {
    const trigger = event.target.closest(".select-trigger");
    if (trigger && ["ArrowDown", "ArrowUp", "Home", "End", "Enter", " "].includes(event.key)) {
      event.preventDefault();
      openSelect(trigger, event.key === "ArrowUp" || event.key === "End" ? -1 : 1);
      return;
    }
    if (event.target === document.getElementById("selectListbox") && activeSelectTrigger) {
      const options = selectOptions[activeSelectTrigger.dataset.key] ?? [];
      if (["ArrowDown", "ArrowUp", "Home", "End"].includes(event.key)) {
        event.preventDefault();
        if (event.key === "Home") activeSelectIndex = 0;
        else if (event.key === "End") activeSelectIndex = options.length - 1;
        else activeSelectIndex = (activeSelectIndex + (event.key === "ArrowDown" ? 1 : -1) + options.length) % options.length;
        renderSelectOptions();
      } else if (["Enter", " "].includes(event.key)) { event.preventDefault(); chooseSelectOption(activeSelectIndex); }
      else if (event.key === "Escape") { event.preventDefault(); closeSelect(true); }
    } else if (event.key === "Escape") closeColorPopover(true);
  });

  document.addEventListener("pointerdown", (event) => {
    if (activeSelectTrigger && !document.getElementById("selectPopover").contains(event.target) && !activeSelectTrigger.contains(event.target)) closeSelect(false);
    if (!document.getElementById("colorPopover").hidden && !document.getElementById("colorPopover").contains(event.target) && !document.getElementById("colorPicker").contains(event.target)) closeColorPopover(false);
  });
  window.addEventListener("resize", () => { closeSelect(false); closeColorPopover(false); });

  document.getElementById("colorPicker").addEventListener("click", () => document.getElementById("colorPopover").hidden ? openColorPopover() : closeColorPopover(true));
  document.getElementById("colorArea").addEventListener("pointerdown", (event) => { colorPointerActive = true; event.currentTarget.setPointerCapture(event.pointerId); updateColorFromPointer(event); });
  document.getElementById("colorArea").addEventListener("pointermove", (event) => { if (colorPointerActive) updateColorFromPointer(event); });
  document.getElementById("colorArea").addEventListener("pointerup", (event) => { colorPointerActive = false; event.currentTarget.releasePointerCapture(event.pointerId); });
  document.getElementById("hueSlider").addEventListener("input", (event) => { colorDraft.h = Number(event.target.value); updateColorDraft(); });
  document.getElementById("colorDraftInput").addEventListener("input", (event) => {
    const valid = /^#[0-9a-f]{8}$/i.test(event.target.value);
    event.target.classList.toggle("invalid", !valid);
    if (valid) setColorDraftFromArgb(event.target.value);
  });
  document.getElementById("colorPresets").addEventListener("click", (event) => {
    const preset = event.target.closest("[data-preset-color]");
    if (preset) setColorDraftFromArgb(preset.dataset.presetColor);
  });
  document.getElementById("colorCancelButton").addEventListener("click", () => closeColorPopover(true));
  document.getElementById("colorApplyButton").addEventListener("click", () => {
    if (!/^#[0-9a-f]{8}$/i.test(document.getElementById("colorDraftInput").value)) return;
    updateSetting("foregroundColor", colorDraft.hex);
    closeColorPopover(true);
  });
  document.getElementById("colorHexInput").addEventListener("change", (event) => {
    if (!/^#[0-9a-f]{8}$/i.test(event.target.value)) { event.target.classList.add("invalid"); return; }
    event.target.classList.remove("invalid");
    updateSetting("foregroundColor", event.target.value.toUpperCase());
  });
}

function setupEvents() {
  setupCustomControls();
  document.querySelectorAll("input, select, textarea").forEach((element) => {
    element.addEventListener("change", () => {
      const key = element.dataset.key;
      if (!key) return;

      const value = element.type === "checkbox"
        ? element.checked
        : parseInputValue(element);
      updateSetting(key, value);
    });
  });

  document.querySelectorAll(".stepper").forEach((stepper) => {
    stepper.addEventListener("click", (event) => {
      const button = event.target.closest("button[data-step]");
      if (!button) return;

      const key = stepper.dataset.key;
      const input = stepper.querySelector("input");
      const min = Number(stepper.dataset.min);
      const max = Number(stepper.dataset.max);
      const step = Number(stepper.dataset.step);
      const decimals = Number(stepper.dataset.decimals);
      const direction = Number(button.dataset.step);
      const current = Number.parseFloat(input.value || state?.[key] || min);
      const next = clamp(current + direction * step, min, max);
      input.value = formatNumber(next, decimals);
      updateSetting(key, Number(input.value));
      input.animate(
        [{ transform: "scale(1)" }, { transform: "scale(1.035)" }, { transform: "scale(1)" }],
        { duration: 150, easing: "ease-out" }
      );
    });
  });

  document.querySelectorAll('.range-control input[type="range"][data-key]').forEach((input) => {
    input.addEventListener("input", () => {
      updateRangeValue(input);
      updateSetting(input.dataset.key, Number(input.value));
    });
  });

  document.getElementById("resetButton")?.addEventListener("click", () => {
    bridge.post({ type: "resetDefaults" });
  });

  document.getElementById("clearCacheButton")?.addEventListener("click", () => {
    bridge.post({ type: "clearCache" });
  });

  document.getElementById("spectrumTuningButton")?.addEventListener("click", () => {
    bridge.post({ type: "openSpectrumTuning" });
  });

  document.getElementById("checkUpdateButton")?.addEventListener("click", () => {
    bridge.post({ type: "checkForUpdates" });
  });

  document.getElementById("openReleaseButton")?.addEventListener("click", () => {
    if (!updateReleaseUrl) return;
    bridge.post({ type: "openExternalLink", value: updateReleaseUrl });
  });

  document.getElementById("repositoryButton")?.addEventListener("click", () => {
    if (!state?.repositoryUrl) return;
    bridge.post({ type: "openExternalLink", value: state.repositoryUrl });
  });

  document.getElementById("sidebarToggle")?.addEventListener("click", () => {
    const windowElement = document.querySelector(".window");
    const toggle = document.getElementById("sidebarToggle");
    windowElement?.classList.toggle("collapsed");
    toggle?.animate(
      [{ transform: "scale(1)" }, { transform: "scale(.92)" }, { transform: "scale(1)" }],
      { duration: 170, easing: "ease-out" }
    );
  });

  document.querySelectorAll(".nav-item").forEach((button) => {
    button.addEventListener("click", () => {
      document.querySelectorAll(".nav-item").forEach((item) => item.classList.remove("active"));
      button.classList.add("active");
      const target = document.getElementById(button.dataset.target);
      target?.scrollIntoView({ behavior: "smooth", block: "start" });
      target?.animate(
        [
          { transform: "translateY(0)", borderColor: "rgba(120, 160, 255, .13)" },
          { transform: "translateY(-2px)", borderColor: "rgba(107, 145, 255, .42)" },
          { transform: "translateY(0)", borderColor: "rgba(120, 160, 255, .13)" }
        ],
        { duration: 280, easing: "ease-out" }
      );
    });
  });

  document.getElementById("content")?.addEventListener("scroll", updateActiveNav);
}

function parseInputValue(element) {
  const key = element.dataset.key;
  if (key === "localMusicFolders") {
    return element.value
      .split(/\r?\n/)
      .map((value) => value.trim().replace(/^"|"$/g, ""))
      .filter(Boolean);
  }

  if (["fontSize", "coverSize", "coverGap", "coverCornerRadius", "backgroundOpacity", "windowWidth", "xOffset", "yOffset"].includes(key)) {
    return Number(element.value);
  }
  return element.value;
}

function updateActiveNav() {
  const content = document.getElementById("content");
  const sections = [...document.querySelectorAll("section.card")];
  if (sections.length === 0) return;

  const active = content && content.scrollTop + content.clientHeight >= content.scrollHeight - 8
    ? sections[sections.length - 1]
    : sections
    .map((section) => ({ id: section.id, distance: Math.abs(section.getBoundingClientRect().top - 110) }))
    .sort((a, b) => a.distance - b.distance)[0];
  if (!active) return;

  document.querySelectorAll(".nav-item").forEach((item) => {
    item.classList.toggle("active", item.dataset.target === (active.id ?? active));
  });
}

window.settingsApp = { setState, setUpdateStatus };

setupEvents();
bridge.post({ type: "ready" });
