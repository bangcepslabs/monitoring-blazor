window.themeManager = (function () {
  const storageKey = "monitoring-theme";

  function apply(theme) {
    const value = theme === "light" ? "light" : "dark";
    document.documentElement.setAttribute("data-theme", value);
    localStorage.setItem(storageKey, value);
    window.dispatchEvent(new CustomEvent("monitor-theme-changed", { detail: { theme: value } }));
    return value;
  }

  function init() {
    const saved = localStorage.getItem(storageKey);
    apply(saved || "dark");
  }

  function toggle() {
    const current = document.documentElement.getAttribute("data-theme") || "dark";
    return apply(current === "dark" ? "light" : "dark");
  }

  init();
  return { toggle, apply, init };
})();
