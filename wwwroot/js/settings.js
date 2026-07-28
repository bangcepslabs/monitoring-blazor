window.monitorSettings = (function () {
  function get(key, fallback) {
    try {
      const raw = localStorage.getItem(key);
      if (raw === null || raw === undefined) return fallback;
      return JSON.parse(raw);
    } catch {
      return fallback;
    }
  }

  function set(key, value) {
    try {
      localStorage.setItem(key, JSON.stringify(value));
      window.dispatchEvent(new CustomEvent('monitor-settings-changed', {
        detail: { key, value }
      }));
    } catch {
      // ignore
    }
  }

  function watch(key, dotnetRef, methodName) {
    const handler = event => {
      if (!event || !event.detail || event.detail.key !== key) {
        return;
      }

      try {
        dotnetRef.invokeMethodAsync(methodName, event.detail.value);
      } catch {
        // ignore
      }
    };

    const registryKey = `monitor-settings-watch-${key}`;
    const previous = window[registryKey];
    if (previous) {
      window.removeEventListener('monitor-settings-changed', previous);
    }

    window[registryKey] = handler;
    window.addEventListener('monitor-settings-changed', handler);
  }

  function unwatch(key) {
    const registryKey = `monitor-settings-watch-${key}`;
    const handler = window[registryKey];
    if (handler) {
      window.removeEventListener('monitor-settings-changed', handler);
      delete window[registryKey];
    }
  }

  function scrollToSection(id) {
    try {
      const el = document.getElementById(id);
      if (!el) {
        return;
      }

      el.scrollIntoView({ behavior: 'smooth', block: 'start' });
    } catch {
      // ignore
    }
  }

  return { get, set, watch, unwatch, scrollToSection };
})();
