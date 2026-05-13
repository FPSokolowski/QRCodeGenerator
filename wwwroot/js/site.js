(() => {
  const contentTypeInputs = document.querySelectorAll('input[name="ContentType"]');
  const contentPanels = document.querySelectorAll("[data-content-panel]");
  const printPreview = {
    zoom: 0.48,
    logoDataUri: ""
  };
  const defaultQrSettings = {
    designEnabled: false,
    rounded: false,
    darkMode: false,
    foreground: "#111827",
    background: "#ffffff",
    gradient: "none",
    eyes: "standard",
    frame: "none",
    template: "clean",
    logoDataUri: "",
    cta: "Scan me"
  };
  const allowedLogoTypes = new Set(["image/png", "image/jpeg", "image/webp", "image/gif", "image/bmp"]);
  const maxLogoBytes = 2 * 1024 * 1024;
  const persistedQrStateKey = "qrGenerator.generatedState.v1";
  let currentQrSettings = { ...defaultQrSettings };
  let qrRiskAlertTimer = null;
  const printCopyTemplate = document.querySelector("[data-print-copy-template]")?.cloneNode(true);
  const printSheetTemplate = document.querySelector("[data-print-page-template]")?.cloneNode(true);
  const printSheetSizes = {
    a4: { width: 210, height: 297 },
    a5: { width: 148, height: 210 },
    letter: { width: 216, height: 279 },
    dl: { width: 99, height: 210 },
    square: { width: 210, height: 210 }
  };

  function setActiveContentPanel() {
    const activeValue = document.querySelector('input[name="ContentType"]:checked')?.value || "general";

    contentPanels.forEach((panel) => {
      const isActive = panel.dataset.contentPanel === activeValue;
      panel.hidden = !isActive;
      panel.querySelectorAll("input, select, textarea").forEach((field) => {
        field.disabled = !isActive;
      });
    });
  }

  contentTypeInputs.forEach((input) => input.addEventListener("change", setActiveContentPanel));
  setActiveContentPanel();

  function getSettingsFromControls() {
    const settings = { ...defaultQrSettings };

    document.querySelectorAll("[data-qr-setting]").forEach((field) => {
      const key = field.dataset.qrSetting;
      settings[key] = field.type === "checkbox" ? field.checked : field.value;
    });
    settings.logoDataUri = currentQrSettings.logoDataUri || "";

    return settings;
  }

  function writeSettingsToControls(settings) {
    document.querySelectorAll("[data-qr-setting]").forEach((field) => {
      const key = field.dataset.qrSetting;
      if (field.type === "checkbox") {
        field.checked = Boolean(settings[key]);
      } else {
        field.value = settings[key] ?? "";
      }
    });
  }

  function applyQrSettings(target, settings) {
    if (!target) {
      return;
    }

    target.classList.toggle("qr-design-enabled", Boolean(settings.designEnabled));
    target.classList.toggle("qr-rounded", Boolean(settings.rounded));
    target.classList.toggle("qr-dark", Boolean(settings.darkMode));
    target.dataset.gradient = settings.gradient || "none";
    target.dataset.frame = settings.frame || "none";
    target.dataset.template = settings.template || "clean";
    target.dataset.eyes = settings.eyes || "standard";
    target.style.setProperty("--qr-fg", settings.foreground || defaultQrSettings.foreground);
    target.style.setProperty("--qr-bg", settings.background || defaultQrSettings.background);
    ensureQrEyes(target);

    const cta = target.querySelector(".qr-cta-preview");
    if (cta) {
      cta.textContent = settings.designEnabled ? (settings.cta || "") : "";
      cta.hidden = !settings.designEnabled || !settings.cta;
    }

    let logo = target.querySelector(".qr-logo-preview");
    if (settings.logoDataUri) {
      if (!logo) {
        logo = document.createElement("img");
        logo.className = "qr-logo-preview";
        logo.alt = "";
        target.appendChild(logo);
      }
      logo.src = settings.logoDataUri;
      logo.hidden = false;
    } else if (logo) {
      logo.removeAttribute("src");
      logo.hidden = true;
    }
  }

  function ensureQrEyes(target) {
    if (!target || target.querySelector(".qr-eye")) {
      return;
    }

    ["tl", "tr", "bl"].forEach((position) => {
      const eye = document.createElement("span");
      eye.className = `qr-eye qr-eye-${position}`;
      eye.setAttribute("aria-hidden", "true");
      target.appendChild(eye);
    });
  }

  function applySettingsEverywhere(settings) {
    document.querySelectorAll(".modifiable-qr").forEach((target) => applyQrSettings(target, settings));
  }

  function getGeneratedPayload() {
    return document.querySelector("[data-generated-payload]")?.value || "";
  }

  function setGeneratedPayload(payload) {
    const payloadInput = document.querySelector("[data-generated-payload]");
    if (payloadInput) {
      payloadInput.value = payload || "";
    }

    document.querySelectorAll('.download-row input[name="text"]').forEach((input) => {
      input.value = payload || "";
    });
  }

  function setQrImageSource(dataUri) {
    if (!dataUri) {
      return;
    }

    document.querySelectorAll(".modifiable-qr > img, [data-print-qr] > img").forEach((image) => {
      image.src = dataUri;
      image.classList.remove("placeholder-preview-image");
    });
    [printCopyTemplate, printSheetTemplate].forEach((template) => {
      template?.querySelectorAll(".modifiable-qr > img, [data-print-qr] > img").forEach((image) => {
        image.src = dataUri;
        image.classList.remove("placeholder-preview-image");
      });
    });

    document.querySelectorAll(".modifiable-qr").forEach((target) => {
      target.dataset.originalSrc = dataUri;
    });
  }

  function showGeneratedControls() {
    document.querySelectorAll("[data-result-only]").forEach((element) => {
      element.hidden = false;
    });
  }

  function updatePreviewStatus(payload) {
    const status = document.querySelector("[data-preview-status]");
    if (!status) {
      return;
    }

    status.querySelector(".status-dot")?.classList.add("active");
    const title = status.querySelector("strong");
    const copy = status.querySelector("p");
    if (title) {
      title.textContent = status.dataset.generatedLabel || title.textContent;
    }
    if (copy) {
      copy.textContent = payload || "";
    }
  }

  function collectGeneratedQrState() {
    const payload = getGeneratedPayload();
    const image = document.querySelector("#mainQrPreview img");
    if (!payload || !image?.src || image.classList.contains("placeholder-preview-image")) {
      return null;
    }

    return {
      payload,
      pngDataUri: image.src,
      settings: currentQrSettings,
      errorCorrectionLevel: document.querySelector('input[name="ErrorCorrectionLevel"]:checked')?.value || "Q"
    };
  }

  function persistGeneratedQrState() {
    const state = collectGeneratedQrState();
    if (!state) {
      return;
    }

    sessionStorage.setItem(persistedQrStateKey, JSON.stringify(state));
  }

  function restoreGeneratedQrState() {
    if (getGeneratedPayload()) {
      return false;
    }

    const rawState = sessionStorage.getItem(persistedQrStateKey);
    if (!rawState) {
      return false;
    }

    try {
      const state = JSON.parse(rawState);
      if (!state?.payload || !state?.pngDataUri) {
        return false;
      }

      const mainPreview = document.getElementById("mainQrPreview");
      mainPreview?.classList.remove("placeholder-card");
      mainPreview?.classList.add("modifiable-qr");
      setGeneratedPayload(state.payload);
      setQrImageSource(state.pngDataUri);
      currentQrSettings = { ...defaultQrSettings, ...(state.settings || {}) };
      writeSettingsToControls(currentQrSettings);
      setErrorCorrectionLevel(state.errorCorrectionLevel || "Q");
      applySettingsEverywhere(currentQrSettings);
      updatePreviewStatus(state.payload);
      showGeneratedControls();
      return true;
    } catch {
      return false;
    }
  }

  function syncModifyPreview() {
    currentQrSettings = getSettingsFromControls();
    applyQrSettings(document.getElementById("modifyQrPreview"), currentQrSettings);
  }

  function showLogoError(visible, scope = "modify") {
    const error = document.querySelector(`[data-logo-error="${scope}"]`);
    if (error) {
      error.hidden = !visible;
    }
  }

  function setLogoDropzoneText(fileName, scope = "modify") {
    const label = document.querySelector(`[data-logo-dropzone-text="${scope}"]`);
    if (label) {
      label.textContent = fileName || label.dataset.defaultText || "";
    }
  }

  function setErrorCorrectionLevel(level) {
    document.querySelectorAll(`input[name="ErrorCorrectionLevel"][value="${level}"]`).forEach((input) => {
      input.checked = true;
    });
    document.querySelectorAll('input[name="errorCorrectionLevel"]').forEach((input) => {
      input.value = level;
    });
  }

  async function sanitizeLogoFile(file) {
    if (!file || file.size > maxLogoBytes || !allowedLogoTypes.has(file.type)) {
      throw new Error("Unsupported logo file");
    }

    const dataUri = await new Promise((resolve, reject) => {
      const reader = new FileReader();
      reader.onload = () => resolve(String(reader.result || ""));
      reader.onerror = reject;
      reader.readAsDataURL(file);
    });
    const image = await loadImage(dataUri);
    const size = 512;
    const scale = Math.min(1, size / Math.max(image.naturalWidth || image.width, image.naturalHeight || image.height));
    const width = Math.max(1, Math.round((image.naturalWidth || image.width) * scale));
    const height = Math.max(1, Math.round((image.naturalHeight || image.height) * scale));
    const canvas = document.createElement("canvas");
    canvas.width = width;
    canvas.height = height;
    const ctx = canvas.getContext("2d", { alpha: true });
    ctx.clearRect(0, 0, width, height);
    ctx.drawImage(image, 0, 0, width, height);
    return canvas.toDataURL("image/png");
  }

  async function regenerateBaseQrForCenteredLogo() {
    const payload = document.querySelector("[data-generated-payload]")?.value;
    const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
    if (!payload || !token) {
      return false;
    }

    const body = new FormData();
    body.append("__RequestVerificationToken", token);
    body.append("text", payload);
    body.append("format", "png");
    body.append("errorCorrectionLevel", "H");

    const response = await fetch("/Home/Download", {
      method: "POST",
      body,
      credentials: "same-origin"
    });
    if (!response.ok) {
      throw new Error("QR regeneration failed");
    }

    const blob = await response.blob();
    const dataUri = await new Promise((resolve, reject) => {
      const reader = new FileReader();
      reader.onload = () => resolve(String(reader.result || ""));
      reader.onerror = reject;
      reader.readAsDataURL(blob);
    });

    document.querySelectorAll(".modifiable-qr > img, [data-print-qr] > img").forEach((image) => {
      image.src = dataUri;
    });
    [printCopyTemplate, printSheetTemplate].forEach((template) => {
      template?.querySelectorAll(".modifiable-qr > img, [data-print-qr] > img").forEach((image) => {
        image.src = dataUri;
      });
    });
    document.querySelectorAll('input[name="errorCorrectionLevel"]').forEach((input) => {
      input.value = "H";
    });
    setErrorCorrectionLevel("H");
    return true;
  }

  async function handleLogoFile(file) {
    try {
      const sanitized = await sanitizeLogoFile(file);
      await regenerateBaseQrForCenteredLogo().catch(() => false);
      currentQrSettings.logoDataUri = sanitized;
      showLogoError(false);
      setLogoDropzoneText(file.name);
      applySettingsEverywhere(currentQrSettings);
      applyQrSettings(document.getElementById("modifyQrPreview"), currentQrSettings);
      persistGeneratedQrState();
    } catch {
      currentQrSettings.logoDataUri = "";
      showLogoError(true);
      applySettingsEverywhere(currentQrSettings);
      applyQrSettings(document.getElementById("modifyQrPreview"), currentQrSettings);
      persistGeneratedQrState();
    }
  }

  async function handlePrintLogoFile(file) {
    try {
      const sanitized = await sanitizeLogoFile(file);
      printPreview.logoDataUri = sanitized;
      showLogoError(false, "print");
      setLogoDropzoneText(file.name, "print");
      updatePrintPreview();
    } catch {
      printPreview.logoDataUri = "";
      showLogoError(true, "print");
      setLogoDropzoneText("", "print");
      updatePrintPreview();
    }
  }

  document.querySelectorAll('input[name="ErrorCorrectionLevel"]').forEach((input) => {
    input.addEventListener("change", () => {
      if (input.checked) {
        setErrorCorrectionLevel(input.value);
      }
    });
  });

  document.querySelectorAll("[data-qr-setting]").forEach((field) => {
    field.addEventListener("input", syncModifyPreview);
    field.addEventListener("change", syncModifyPreview);
  });

  document.querySelectorAll('[data-qr-setting="foreground"], [data-qr-setting="background"]').forEach((field) => {
    field.addEventListener("input", () => {
      const gradient = document.querySelector('[data-qr-setting="gradient"]');
      if (gradient) {
        gradient.value = "none";
      }
      syncModifyPreview();
    });
  });

  document.querySelector("[data-reset-qr-settings]")?.addEventListener("click", () => {
    currentQrSettings = { ...defaultQrSettings };
    writeSettingsToControls(currentQrSettings);
    const logoInput = document.querySelector("[data-logo-input]");
    if (logoInput) {
      logoInput.value = "";
    }
    setLogoDropzoneText("", "modify");
    showLogoError(false);
    applyQrSettings(document.getElementById("modifyQrPreview"), currentQrSettings);
  });

  document.querySelector("[data-apply-qr-settings]")?.addEventListener("click", () => {
    currentQrSettings = getSettingsFromControls();
    applySettingsEverywhere(currentQrSettings);
    persistGeneratedQrState();
  });

  document.getElementById("qrModifyModal")?.addEventListener("show.bs.modal", () => {
    writeSettingsToControls(currentQrSettings);
    applyQrSettings(document.getElementById("modifyQrPreview"), currentQrSettings);
    showQrRiskAlert();
  });

  document.getElementById("qrModifyModal")?.addEventListener("hidden.bs.modal", () => {
    hideQrRiskAlert();
  });

  document.querySelector("[data-qr-risk-dot]")?.addEventListener("click", showQrRiskAlert);

  document.querySelector("[data-logo-input]")?.addEventListener("change", (event) => {
    const file = event.currentTarget.files?.[0];
    if (file) {
      handleLogoFile(file);
    }
  });

  document.querySelectorAll("[data-logo-dropzone]").forEach((dropzone) => {
    const scope = dropzone.dataset.logoDropzone || "modify";
    const clearDrag = () => dropzone.classList.remove("drag-over");
    dropzone.addEventListener("dragenter", (event) => {
      event.preventDefault();
      dropzone.classList.add("drag-over");
    });
    dropzone.addEventListener("dragover", (event) => {
      event.preventDefault();
      dropzone.classList.add("drag-over");
    });
    dropzone.addEventListener("dragleave", clearDrag);
    dropzone.addEventListener("drop", (event) => {
      event.preventDefault();
      clearDrag();
      const file = event.dataTransfer?.files?.[0];
      if (file) {
        if (scope === "print") {
          handlePrintLogoFile(file);
        } else {
          handleLogoFile(file);
        }
      }
    });
  });

  function showQrRiskAlert() {
    const alert = document.querySelector("[data-qr-risk-alert]");
    const dot = document.querySelector("[data-qr-risk-dot]");
    if (!alert || !dot) {
      return;
    }

    alert.hidden = false;
    dot.hidden = true;
    clearTimeout(qrRiskAlertTimer);
    qrRiskAlertTimer = setTimeout(hideQrRiskAlert, 10000);
  }

  function hideQrRiskAlert() {
    const alert = document.querySelector("[data-qr-risk-alert]");
    const dot = document.querySelector("[data-qr-risk-dot]");
    if (alert) {
      alert.hidden = true;
    }
    if (dot) {
      dot.hidden = false;
    }
    clearTimeout(qrRiskAlertTimer);
  }

  function hexToRgb(hex) {
    const normalized = (hex || "#000000").replace("#", "");
    return {
      r: parseInt(normalized.slice(0, 2), 16),
      g: parseInt(normalized.slice(2, 4), 16),
      b: parseInt(normalized.slice(4, 6), 16)
    };
  }

  function hexToRgba(hex, alpha = 1) {
    const color = hexToRgb(hex);
    return `rgba(${color.r}, ${color.g}, ${color.b}, ${alpha})`;
  }

  function getGradientStops(settings) {
    switch (settings.gradient) {
      case "ocean":
        return ["#dff7ff", "#d6fff8", "#eff6ff"];
      case "sunrise":
        return ["#fff1d6", "#ffd0c7", "#f8d8ff"];
      case "violet":
        return ["#ede9fe", "#f5d0fe", "#dbeafe"];
      case "forest":
        return ["#dcfce7", "#ccfbf1", "#f7fee7"];
      case "mono":
        return ["#f8fafc", "#cbd5e1", "#f8fafc"];
      default:
        return null;
    }
  }

  function loadImage(src) {
    return new Promise((resolve, reject) => {
      const img = new Image();
      img.onload = () => resolve(img);
      img.onerror = reject;
      img.src = src;
    });
  }

  function drawRoundedRect(ctx, x, y, width, height, radius) {
    const safeRadius = Math.min(radius, width / 2, height / 2);
    ctx.beginPath();
    ctx.moveTo(x + safeRadius, y);
    ctx.arcTo(x + width, y, x + width, y + height, safeRadius);
    ctx.arcTo(x + width, y + height, x, y + height, safeRadius);
    ctx.arcTo(x, y + height, x, y, safeRadius);
    ctx.arcTo(x, y, x + width, y, safeRadius);
    ctx.closePath();
  }

  function fillQrBackground(ctx, settings, x, y, size) {
    const stops = getGradientStops(settings);
    if (settings.designEnabled && stops) {
      const gradient = ctx.createLinearGradient(x, y, x + size, y + size);
      gradient.addColorStop(0, stops[0]);
      gradient.addColorStop(0.52, stops[1]);
      gradient.addColorStop(1, stops[2]);
      ctx.fillStyle = gradient;
    } else {
      ctx.fillStyle = settings.darkMode ? "#10131c" : (settings.background || defaultQrSettings.background);
    }
    ctx.fill();
  }

  async function renderCurrentQrPng() {
    const source = document.querySelector("#mainQrPreview img") || document.querySelector("#modifyQrPreview img");
    if (!source) {
      return null;
    }

    const settings = currentQrSettings;
    const size = 1200;
    const extraBottom = settings.designEnabled && settings.cta ? 72 : 0;
    const canvas = document.createElement("canvas");
    canvas.width = size;
    canvas.height = size + extraBottom;
    const ctx = canvas.getContext("2d");
    const sourceImage = await loadImage(source.src);

    ctx.clearRect(0, 0, canvas.width, canvas.height);

    if (!settings.designEnabled) {
      ctx.fillStyle = "#ffffff";
      ctx.fillRect(0, 0, size, size);
      ctx.drawImage(sourceImage, 0, 0, size, size);
      return canvas.toDataURL("image/png");
    }

    const margin = 66;
    const cardSize = size - (margin * 2);
    const borderWidth = settings.frame === "poster" ? 54 : 32;
    const padding = 82;
    const cardRadius = settings.frame === "badge" ? cardSize / 2 : settings.rounded ? 88 : 64;
    const innerRadius = settings.rounded ? 34 : 10;
    const qrX = margin + borderWidth + padding;
    const qrY = margin + borderWidth + padding;
    const qrSize = cardSize - ((borderWidth + padding) * 2);

    ctx.save();
    ctx.shadowColor = "rgba(0, 0, 0, 0.28)";
    ctx.shadowBlur = settings.template === "premium" ? 46 : 30;
    ctx.shadowOffsetY = settings.template === "premium" ? 26 : 18;

    drawRoundedRect(ctx, margin, margin, cardSize, cardSize, cardRadius);
    fillQrBackground(ctx, settings, margin, margin, cardSize);
    ctx.restore();

    ctx.lineWidth = borderWidth;
    if (settings.frame === "soft") {
      ctx.strokeStyle = "rgba(255, 255, 255, 0.82)";
    } else if (settings.frame === "none") {
      ctx.strokeStyle = hexToRgba(settings.foreground || defaultQrSettings.foreground, 0.24);
    } else {
      ctx.strokeStyle = settings.foreground || defaultQrSettings.foreground;
    }

    drawRoundedRect(ctx, margin + borderWidth / 2, margin + borderWidth / 2, cardSize - borderWidth, cardSize - borderWidth, Math.max(0, cardRadius - borderWidth / 2));
    ctx.stroke();

    if (settings.template === "premium") {
      ctx.lineWidth = 10;
      ctx.strokeStyle = "rgba(185, 201, 255, 0.62)";
      drawRoundedRect(ctx, margin - 12, margin - 12, cardSize + 24, cardSize + 24, cardRadius + 12);
      ctx.stroke();
    }

    ctx.fillStyle = "#ffffff";
    drawRoundedRect(ctx, qrX, qrY, qrSize, qrSize, innerRadius);
    ctx.fill();
    ctx.save();
    drawRoundedRect(ctx, qrX, qrY, qrSize, qrSize, innerRadius);
    ctx.clip();
    if (settings.darkMode) {
      ctx.filter = "invert(1)";
    }
    ctx.drawImage(sourceImage, qrX, qrY, qrSize, qrSize);
    ctx.restore();

    if (settings.logoDataUri) {
      const logoImage = await loadImage(settings.logoDataUri);
      const logoBox = qrSize * 0.28;
      const logoPadding = logoBox * 0.14;
      const logoX = qrX + (qrSize - logoBox) / 2;
      const logoY = qrY + (qrSize - logoBox) / 2;
      drawRoundedRect(ctx, logoX, logoY, logoBox, logoBox, logoBox * 0.22);
      ctx.fillStyle = "#ffffff";
      ctx.fill();
      ctx.save();
      drawRoundedRect(ctx, logoX + logoPadding, logoY + logoPadding, logoBox - (logoPadding * 2), logoBox - (logoPadding * 2), logoBox * 0.14);
      ctx.clip();
      ctx.drawImage(logoImage, logoX + logoPadding, logoY + logoPadding, logoBox - (logoPadding * 2), logoBox - (logoPadding * 2));
      ctx.restore();
    }

    if (settings.cta) {
      const pillWidth = cardSize * 0.76;
      const pillHeight = 58;
      const pillX = margin + (cardSize - pillWidth) / 2;
      const pillY = margin + cardSize - (pillHeight * 0.5);
      ctx.fillStyle = settings.foreground || defaultQrSettings.foreground;
      drawRoundedRect(ctx, pillX, pillY, pillWidth, pillHeight, pillHeight / 2);
      ctx.fill();
      ctx.fillStyle = "#ffffff";
      ctx.font = "bold 30px Arial, sans-serif";
      ctx.textAlign = "center";
      ctx.textBaseline = "middle";
      ctx.fillText(settings.cta, size / 2, pillY + pillHeight / 2);
    }

    return canvas.toDataURL("image/png");
  }

  document.querySelectorAll('form input[name="format"][value="png"]').forEach((formatInput) => {
    const form = formatInput.closest("form");
    form?.addEventListener("submit", async (event) => {
      event.preventDefault();
      const dataUri = await renderCurrentQrPng();
      if (!dataUri) {
        return;
      }
      const link = document.createElement("a");
      link.href = dataUri;
      link.download = `qr-code-${new Date().toISOString().replace(/[-:.TZ]/g, "").slice(0, 14)}.png`;
      document.body.appendChild(link);
      link.click();
      link.remove();
    });
  });

  function updatePrintPreview() {
    const printArea = document.getElementById("printArea");
    if (!printArea || !printSheetTemplate || !printCopyTemplate) {
      return;
    }

    const value = (name) => document.querySelector(`[data-print-setting="${name}"]`)?.value || "";
    const format = value("format") || "a4";
    const gradient = value("gradient") || "none";
    const title = value("title");
    const qrTitle = value("qrTitle");
    const label = value("label");
    const labelBackground = value("labelBackground") || "#111827";
    const labelText = value("labelText") || "#ffffff";
    const description = value("description");
    const contentAlign = value("contentAlign") || "center";
    const copies = Math.max(1, Math.min(48, Number(value("copies") || 1)));
    const copyGap = Math.max(0, Math.min(30, Number(value("copyGap") || 6)));
    const logoPosition = value("logoPosition") || "right";
    const logoScale = Math.max(50, Math.min(100, Number(value("logoScale") || 50)));
    const cutLines = Boolean(document.querySelector('[data-print-setting="cutLines"]')?.checked);
    const qrSize = Math.max(20, Math.min(180, Number(value("qrSize") || 55)));
    const margin = Math.max(0, Math.min(40, Number(value("margin") || 12)));
    const bleed = Math.max(0, Math.min(8, Number(value("bleed") || 3)));
    const pageSize = printSheetSizes[format] || printSheetSizes.a4;
    const pagePadding = margin + bleed;
    const contentWidth = Math.max(20, pageSize.width - (pagePadding * 2));
    const contentHeight = Math.max(20, pageSize.height - (pagePadding * 2));
    const headerHeight = (title ? 9 : 0) + (description ? 6 : 0) + (title || description ? 5 : 0);
    const copyTitleHeight = qrTitle ? 6 : 0;
    const labelHeight = label ? 8 : 0;
    const logoSize = printPreview.logoDataUri ? qrSize * (logoScale / 100) : 0;
    const logoGap = printPreview.logoDataUri ? 5 : 0;
    const horizontalLogo = logoPosition === "left" || logoPosition === "right";
    const lockupWidth = horizontalLogo ? qrSize + logoSize + logoGap : Math.max(qrSize, logoSize);
    const lockupHeight = horizontalLogo ? Math.max(qrSize, logoSize) : qrSize + logoSize + logoGap;
    const copyWidth = lockupWidth;
    const copyHeight = copyTitleHeight + lockupHeight + labelHeight + (qrTitle ? 3 : 0) + (label ? 3 : 0);
    const columns = Math.max(1, Math.floor((contentWidth + copyGap) / (copyWidth + copyGap)));
    const rows = Math.max(1, Math.floor((contentHeight - headerHeight + copyGap) / (copyHeight + copyGap)));
    const copiesPerPage = Math.max(1, columns * rows);

    const buildSheet = (copyCount) => {
      const sheet = printSheetTemplate.cloneNode(true);
      sheet.hidden = false;
      sheet.removeAttribute("data-print-page-template");
      sheet.className = `print-sheet sheet-${format} gradient-${gradient}`;
      sheet.style.setProperty("--sheet-height", `${pageSize.height}mm`);
      sheet.style.setProperty("--qr-size", `${qrSize}mm`);
      sheet.style.setProperty("--print-margin", `${margin}mm`);
      sheet.style.setProperty("--print-bleed", `${bleed}mm`);
      sheet.style.setProperty("--copy-gap", `${copyGap}mm`);
      sheet.style.setProperty("--copy-columns", String(columns));
      sheet.style.setProperty("--print-logo-size", `${logoSize}mm`);
      sheet.style.setProperty("--label-bg", labelBackground);
      sheet.style.setProperty("--label-text", labelText);
      sheet.classList.toggle("has-cut-lines", cutLines);

      const titleNode = sheet.querySelector("[data-print-title]");
      if (titleNode) {
        titleNode.textContent = title;
        titleNode.hidden = !title;
      }

      const descriptionNode = sheet.querySelector("[data-print-description]");
      if (descriptionNode) {
        descriptionNode.textContent = description;
        descriptionNode.hidden = !description;
      }

      const copiesRoot = sheet.querySelector("[data-print-copies]");
      copiesRoot?.replaceChildren();
      const contentRoot = sheet.querySelector(".print-content");
      contentRoot?.classList.toggle("align-top", contentAlign === "top");
      contentRoot?.classList.toggle("align-center", contentAlign !== "top");

      for (let index = 0; index < copyCount; index += 1) {
        const copy = printCopyTemplate.cloneNode(true);
        copy.hidden = false;
        copy.removeAttribute("data-print-copy-template");
        const lockup = copy.querySelector("[data-print-lockup]");
        lockup?.classList.remove("logo-right", "logo-left", "logo-top", "logo-bottom");
        lockup?.classList.add(`logo-${logoPosition}`);
        const qrTitleNode = copy.querySelector("[data-print-qr-title]");
        if (qrTitleNode) {
          qrTitleNode.textContent = qrTitle;
          qrTitleNode.hidden = !qrTitle;
        }
        const labelNode = copy.querySelector("[data-print-label]");
        if (labelNode) {
          labelNode.textContent = label;
          labelNode.hidden = !label;
        }
        const logoNode = copy.querySelector("[data-print-logo]");
        if (logoNode) {
          logoNode.src = printPreview.logoDataUri;
          logoNode.hidden = !printPreview.logoDataUri;
        }
        applyQrSettings(copy.querySelector("[data-print-qr]"), currentQrSettings);
        copiesRoot?.appendChild(copy);
      }

      return sheet;
    };

    printArea.replaceChildren();
    let remaining = copies;
    while (remaining > 0) {
      const count = Math.min(copiesPerPage, remaining);
      printArea.appendChild(buildSheet(count));
      remaining -= count;
    }
  }

  document.querySelectorAll("[data-print-setting]").forEach((field) => {
    field.addEventListener("input", updatePrintPreview);
    field.addEventListener("change", updatePrintPreview);
  });

  document.querySelector("[data-print-logo-input]")?.addEventListener("change", (event) => {
    const file = event.currentTarget.files?.[0];
    if (!file) {
      printPreview.logoDataUri = "";
      showLogoError(false, "print");
      setLogoDropzoneText("", "print");
      updatePrintPreview();
      return;
    }

    handlePrintLogoFile(file);
  });

  function setPrintZoom(value) {
    printPreview.zoom = Math.max(0.28, Math.min(0.78, value));
    document.getElementById("printArea")?.style.setProperty("--print-preview-zoom", String(printPreview.zoom));
  }

  document.querySelectorAll("[data-print-zoom]").forEach((button) => {
    button.addEventListener("click", () => {
      const direction = button.dataset.printZoom === "in" ? 1 : -1;
      setPrintZoom(printPreview.zoom + (direction * 0.06));
    });
  });

  document.getElementById("qrPrintModal")?.addEventListener("show.bs.modal", () => {
    setPrintZoom(printPreview.zoom);
    updatePrintPreview();
  });
  document.querySelectorAll("[data-preferences-form]").forEach((form) => {
    form.addEventListener("submit", persistGeneratedQrState);
  });
  restoreGeneratedQrState();
  const activeErrorCorrection = document.querySelector('input[name="ErrorCorrectionLevel"]:checked')?.value;
  if (activeErrorCorrection) {
    setErrorCorrectionLevel(activeErrorCorrection);
  }
  syncModifyPreview();
  applySettingsEverywhere(currentQrSettings);
})();
