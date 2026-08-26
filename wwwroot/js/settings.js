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

  function downloadText(fileName, content, contentType) {
    try {
      const blob = new Blob([content ?? ''], { type: contentType || 'text/plain;charset=utf-8' });
      const url = URL.createObjectURL(blob);
      const anchor = document.createElement('a');
      anchor.href = url;
      anchor.download = fileName || 'download.txt';
      document.body.appendChild(anchor);
      anchor.click();
      document.body.removeChild(anchor);
      URL.revokeObjectURL(url);
    } catch {
      // ignore
    }
  }

  async function copyText(content) {
    try {
      await navigator.clipboard.writeText(content ?? '');
      return true;
    } catch {
      return false;
    }
  }

  function showToast(message, kind) {
    if (!message) return;

    const tone = kind === 'danger' ? 'danger' : kind === 'warning' ? 'warning' : 'success';
    let container = document.getElementById('ops-toast-region');
    if (!container) {
      container = document.createElement('div');
      container.id = 'ops-toast-region';
      container.className = 'ops-toast-region';
      container.setAttribute('aria-live', 'polite');
      container.setAttribute('aria-atomic', 'true');
      document.body.appendChild(container);
    }

    const toast = document.createElement('div');
    toast.className = `ops-toast ops-toast--${tone}`;
    toast.setAttribute('role', 'status');
    toast.innerHTML = `<span class="ops-toast__icon" aria-hidden="true">${tone === 'danger' ? '!' : tone === 'warning' ? '!' : '✓'}</span><span class="ops-toast__message"></span><button type="button" class="ops-toast__close" aria-label="Close">×</button>`;
    toast.querySelector('.ops-toast__message').textContent = message;
    toast.querySelector('.ops-toast__close').addEventListener('click', () => toast.remove());
    container.appendChild(toast);

    window.setTimeout(() => {
      toast.classList.add('ops-toast--leaving');
      window.setTimeout(() => toast.remove(), 180);
    }, 4200);
  }

  return { get, set, watch, unwatch, scrollToSection, downloadText, copyText, showToast };
})();
