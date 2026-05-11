(() => {
  const contentTypeInputs = document.querySelectorAll('input[name="ContentType"]');
  const contentPanels = document.querySelectorAll("[data-content-panel]");

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

  if (window.qrResultShouldOpen && window.bootstrap) {
    const modalElement = document.getElementById("qrResultModal");
    if (modalElement) {
      new bootstrap.Modal(modalElement).show();
    }
  }
})();
