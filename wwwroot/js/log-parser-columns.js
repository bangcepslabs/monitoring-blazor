(function () {
  const minWidth = 72;
  const maxWidth = 720;

  function loadState(storageKey) {
    try {
      const raw = window.localStorage.getItem(storageKey);
      return raw ? JSON.parse(raw) : {};
    } catch {
      return {};
    }
  }

  function saveState(storageKey, state) {
    try {
      window.localStorage.setItem(storageKey, JSON.stringify(state));
    } catch {
      // ignore storage failures
    }
  }

  function getColumns(table) {
    return Array.from(table.querySelectorAll("colgroup col[data-column]"));
  }

  function applyWidths(table, state) {
    getColumns(table).forEach((col) => {
      const key = col.dataset.column;
      const width = state[key];
      if (typeof width === "number" && Number.isFinite(width)) {
        col.style.width = `${Math.min(maxWidth, Math.max(minWidth, width))}px`;
      }
    });
  }

  function bindResizer(table, resizer, storageKey, state) {
    if (resizer.dataset.bound === "true") {
      return;
    }

    const columnKey = resizer.dataset.column;
    const targetCol = table.querySelector(`col[data-column='${columnKey}']`);
    if (!targetCol) {
      return;
    }

    resizer.dataset.bound = "true";

    resizer.addEventListener("mousedown", (event) => {
      event.preventDefault();
      event.stopPropagation();

      const startX = event.clientX;
      const startWidth = targetCol.getBoundingClientRect().width;
      resizer.classList.add("is-resizing");

      const onMove = (moveEvent) => {
        const nextWidth = Math.min(maxWidth, Math.max(minWidth, Math.round(startWidth + (moveEvent.clientX - startX))));
        targetCol.style.width = `${nextWidth}px`;
        state[columnKey] = nextWidth;
      };

      const onUp = () => {
        resizer.classList.remove("is-resizing");
        saveState(storageKey, state);
        window.removeEventListener("mousemove", onMove);
        window.removeEventListener("mouseup", onUp);
      };

      window.addEventListener("mousemove", onMove);
      window.addEventListener("mouseup", onUp, { once: true });
    });
  }

  window.logParserColumns = {
    init: function (selector, storageKey) {
      const table = document.querySelector(selector);
      if (!table) {
        return;
      }

      const state = loadState(storageKey);
      applyWidths(table, state);

      table.querySelectorAll(".column-resizer[data-column]").forEach((resizer) => {
        bindResizer(table, resizer, storageKey, state);
      });
    },

    reset: function (selector, storageKey) {
      const table = document.querySelector(selector);
      if (!table) {
        return;
      }

      getColumns(table).forEach((col) => {
        col.style.removeProperty("width");
      });

      try {
        window.localStorage.removeItem(storageKey);
      } catch {
        // ignore storage failures
      }
    }
  };
})();
