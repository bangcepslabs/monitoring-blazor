window.monitorCharts = (function () {
  let cpuGauge, memoryGauge, diskGauge;
  let networkChart, resourceChart;
  let networkCanvas, resourceCanvas;
  let cpuGaugeCanvas = null, memGaugeCanvas = null, diskGaugeCanvas = null;

  // 전체 데이터 (pan용 슬라이딩 윈도우)
  let netFull  = { labels: [], tx: [], rx: [] };
  let resFull  = { labels: [], cpu: [], mem: [] };
  let netView  = 0;   // 현재 보이는 시작 인덱스
  let resView  = 0;
  let netWin   = 0;   // 현재 윈도우 크기 (0 = 전체)
  let resWin   = 0;

  function cssVar(name, fallback) {
    const raw = getComputedStyle(document.documentElement).getPropertyValue(name).trim();
    return raw || fallback;
  }

  function getPalette() {
    const theme = document.documentElement.getAttribute("data-theme") || "dark";
    if (theme === "light") {
      return {
        text: cssVar("--text-primary", "#1f2328"),
        subtleText: cssVar("--text-secondary", "#57606a"),
        grid: "rgba(208, 215, 222, 0.7)",
        gaugeRemainder: "rgba(208, 215, 222, 0.6)",
        tooltipBg: "rgba(255,255,255,0.96)",
        tooltipText: cssVar("--text-primary", "#1f2328"),
        tooltipBorder: "rgba(208, 215, 222, 0.9)"
      };
    }

    return {
      text: cssVar("--text-primary", "#d6e8fb"),
      subtleText: cssVar("--text-secondary", "#9eb9d3"),
      grid: "rgba(147, 178, 210, 0.22)",
      gaugeRemainder: "rgba(106, 136, 168, 0.28)",
      tooltipBg: "rgba(14,25,43,0.92)",
      tooltipText: cssVar("--text-primary", "#d6e8fb"),
      tooltipBorder: "rgba(145,178,212,0.35)"
    };
  }

  function currentPalette() {
    return getPalette();
  }

  const hoverLinePlugin = {
    id: "monitorHoverLine",
    afterDatasetsDraw(chart) {
      const active = chart.getActiveElements ? chart.getActiveElements() : [];
      if (!active || active.length === 0 || !chart.chartArea) return;

      const point = active[0].element;
      if (!point) return;

      const { ctx, chartArea } = chart;
      const palette = currentPalette();
      ctx.save();
      ctx.beginPath();
      ctx.moveTo(point.x, chartArea.top);
      ctx.lineTo(point.x, chartArea.bottom);
      ctx.lineWidth = 1;
      ctx.setLineDash([4, 4]);
      ctx.strokeStyle = palette.subtleText;
      ctx.stroke();
      ctx.restore();
    }
  };

  // ── 게이지 ───────────────────────────────────────────────────
  function makeGauge(el, color) {
    const ctx = el.getContext("2d");
    ctx.clearRect(0, 0, el.width, el.height);
    const palette = currentPalette();
    return new Chart(ctx, {
      type: "doughnut",
      data: { labels: ["v","r"], datasets: [{ data: [0,100], backgroundColor: [color, palette.gaugeRemainder], borderWidth: 0 }] },
      options: { responsive: true, maintainAspectRatio: false, cutout: "72%", plugins: { legend: { display: false }, tooltip: { enabled: false } } }
    });
  }
  function updateGauge(c, v) {
    if (!c) return;
    const n = Math.max(0, Math.min(100, Number(v) || 0));
    c.data.datasets[0].data = [n, 100 - n];
    c.update("none");
  }
  function ensureGauges() {
    const ce = document.getElementById("cpuGauge");
    const me = document.getElementById("memoryGauge");
    const de = document.getElementById("diskGauge");
    if (!ce || !me || !de) return false;
    if (!cpuGauge    || cpuGaugeCanvas  !== ce) { if (cpuGauge)    cpuGauge.destroy();    cpuGauge    = makeGauge(ce, "rgba(255,99,132,1)");  cpuGaugeCanvas  = ce; }
    if (!memoryGauge || memGaugeCanvas  !== me) { if (memoryGauge) memoryGauge.destroy(); memoryGauge = makeGauge(me, "rgba(54,162,235,1)");  memGaugeCanvas  = me; }
    if (!diskGauge   || diskGaugeCanvas !== de) { if (diskGauge)   diskGauge.destroy();   diskGauge   = makeGauge(de, "rgba(40,167,69,1)");   diskGaugeCanvas = de; }
    return true;
  }

  // ── 슬라이딩 윈도우 렌더 ─────────────────────────────────────
  function renderSlice(chart, full, viewStart, winSize, isNet) {
    const total = full.labels.length;
    if (total === 0) return;
    const size  = winSize > 0 ? Math.min(winSize, total) : total;
    const start = Math.max(0, Math.min(viewStart, total - size));
    const end   = start + size;
    chart.data.labels = full.labels.slice(start, end);
    if (isNet) {
      chart.data.datasets[0].data = full.tx.slice(start, end);
      chart.data.datasets[1].data = full.rx.slice(start, end);
    } else {
      chart.data.datasets[0].data = full.cpu.slice(start, end);
      chart.data.datasets[1].data = full.mem.slice(start, end);
    }
    chart.update("none");
    return start;
  }

  // ── overlay 드래그 pan ────────────────────────────────────────
  // Blazor가 canvas 이벤트를 가로채므로 canvas 위에 올린 순수 div에서 처리
  function attachOverlayPan(overlayId, getChart, getFull, getView, setView, getWin, isNet) {
    const overlay = document.getElementById(overlayId);
    if (!overlay) return;

    let startX   = null;
    let startView = null;
    let dragging  = false;

    function clearHover() {
      const chart = getChart();
      if (!chart) return;
      chart.setActiveElements([]);
      if (chart.tooltip?.setActiveElements) {
        chart.tooltip.setActiveElements([], { x: 0, y: 0 });
      }
      chart.update("none");
    }

    function showHover(e) {
      const chart = getChart();
      if (!chart || !chart.chartArea || !chart.scales?.x) return;

      const full = getFull();
      const total = full.labels.length;
      if (total === 0 || chart.data.labels.length === 0) {
        clearHover();
        return;
      }

      const rect = chart.canvas.getBoundingClientRect();
      const x = e.clientX - rect.left;
      const y = e.clientY - rect.top;
      const area = chart.chartArea;

      if (x < area.left || x > area.right || y < area.top || y > area.bottom) {
        clearHover();
        return;
      }

      const visibleCount = chart.data.labels.length;
      let index = 0;
      let nearestDistance = Number.POSITIVE_INFINITY;
      for (let i = 0; i < visibleCount; i += 1) {
        const pointX = chart.scales.x.getPixelForValue(i);
        const distance = Math.abs(pointX - x);
        if (distance < nearestDistance) {
          nearestDistance = distance;
          index = i;
        }
      }
      const active = chart.data.datasets
        .map((dataset, datasetIndex) => ({ dataset, datasetIndex }))
        .filter(item => chart.isDatasetVisible(item.datasetIndex) && item.dataset.data.length > index)
        .map(item => ({ datasetIndex: item.datasetIndex, index }));

      if (active.length === 0) {
        clearHover();
        return;
      }

      chart.setActiveElements(active);
      if (chart.tooltip?.setActiveElements) {
        chart.tooltip.setActiveElements(active, { x, y });
      }
      chart.update("none");
    }

    overlay.addEventListener("mousemove", function (e) {
      if (!dragging) showHover(e);
    });

    overlay.addEventListener("mouseleave", clearHover);

    overlay.addEventListener("mousedown", function (e) {
      if (e.button !== 0) return;
      startX    = e.clientX;
      startView = getView();
      dragging  = false;
      overlay.style.cursor = "grabbing";

      function onMove(e2) {
        const dx = e2.clientX - startX;
        if (!dragging && Math.abs(dx) < 4) return;
        dragging = true;

        const chart = getChart();
        if (!chart) return;
        const full  = getFull();
        const total = full.labels.length;
        const win   = getWin() > 0 ? getWin() : total;
        if (total === 0 || win <= 0) return;

        const area      = chart.chartArea;
        const plotW     = (area && area.right > area.left) ? (area.right - area.left) : overlay.clientWidth;
        const pxPerIdx  = plotW / win;
        const delta     = Math.round(dx / pxPerIdx);
        const newStart  = Math.max(0, Math.min(total - win, startView - delta));
        setView(newStart);
        renderSlice(chart, full, newStart, win, isNet);
      }

      function onUp(e2) {
        overlay.style.cursor = "grab";
        dragging = false;
        document.removeEventListener("mousemove", onMove);
        document.removeEventListener("mouseup",   onUp);
        if (e2) showHover(e2);
      }

      document.addEventListener("mousemove", onMove);
      document.addEventListener("mouseup",   onUp);
    });

    // 스크롤 zoom (윈도우 크기 조절)
    overlay.addEventListener("wheel", function (e) {
      e.preventDefault();
      const chart = getChart();
      if (!chart) return;
      const full  = getFull();
      const total = full.labels.length;
      if (total === 0) return;

      const curWin   = getWin() > 0 ? getWin() : total;
      const factor   = e.deltaY > 0 ? 1.15 : 0.87;
      const newWin   = Math.max(5, Math.min(total, Math.round(curWin * factor)));
      const curView  = getView();
      // zoom 중심을 현재 뷰 중앙으로
      const center   = curView + Math.floor(curWin / 2);
      const newStart = Math.max(0, Math.min(total - newWin, center - Math.floor(newWin / 2)));

      if (isNet) { netWin = newWin; netView = newStart; }
      else       { resWin = newWin; resView = newStart; }

      renderSlice(chart, full, newStart, newWin, isNet);
    }, { passive: false });
  }

  // ── 라인 차트 생성 ────────────────────────────────────────────
  function makeLineChart(canvas, datasets, yOpts, tooltipCbs) {
    const palette = currentPalette();
    return new Chart(canvas.getContext("2d"), {
      type: "line",
      data: { labels: [], datasets },
      plugins: [hoverLinePlugin],
      options: {
        responsive: true, animation: false,
        interaction: { mode: "index", intersect: false },
        hover: { mode: "index", intersect: false },
        plugins: {
          legend: { labels: { color: palette.text } },
          zoom: { pan: { enabled: false }, zoom: { drag: { enabled: false }, wheel: { enabled: false } } },
          tooltip: {
            titleColor: palette.tooltipText, bodyColor: palette.tooltipText,
            backgroundColor: palette.tooltipBg,
            borderColor: palette.tooltipBorder, borderWidth: 1,
            displayColors: true,
            padding: 10,
            caretPadding: 8,
            ...(tooltipCbs ? { callbacks: tooltipCbs } : {})
          }
        },
        scales: {
          x: { ticks: { color: palette.subtleText, maxTicksLimit: 8 }, grid: { color: palette.grid } },
          y: yOpts
        }
      }
    });
  }

  function refreshChartTheme(chart) {
    if (!chart) return;
    const palette = currentPalette();
    if (chart.options?.plugins?.legend?.labels) {
      chart.options.plugins.legend.labels.color = palette.text;
    }
    if (chart.options?.plugins?.tooltip) {
      chart.options.plugins.tooltip.titleColor = palette.tooltipText;
      chart.options.plugins.tooltip.bodyColor = palette.tooltipText;
      chart.options.plugins.tooltip.backgroundColor = palette.tooltipBg;
      chart.options.plugins.tooltip.borderColor = palette.tooltipBorder;
    }
    const xTicks = chart.options?.scales?.x?.ticks;
    const xGrid = chart.options?.scales?.x?.grid;
    if (xTicks) xTicks.color = palette.subtleText;
    if (xGrid) xGrid.color = palette.grid;
    const yTicks = chart.options?.scales?.y?.ticks;
    const yGrid = chart.options?.scales?.y?.grid;
    const yTitle = chart.options?.scales?.y?.title;
    if (yTicks) yTicks.color = palette.subtleText;
    if (yGrid) yGrid.color = palette.grid;
    if (yTitle) yTitle.color = palette.subtleText;
    chart.update("none");
  }

  function initNetworkChart(canvas) {
    if (networkChart) { networkChart.destroy(); networkChart = null; }
    netView = 0; netWin = 0;
    const palette = currentPalette();
    networkChart = makeLineChart(canvas,
      [
        { label: "Sent (TX)",     data: [], borderColor: "rgb(247,117,138)", fill: false, tension: 0.25, pointRadius: 0, pointHoverRadius: 4, pointHitRadius: 12 },
        { label: "Received (RX)", data: [], borderColor: "rgb(92,176,255)",  fill: false, tension: 0.25, pointRadius: 0, pointHoverRadius: 4, pointHitRadius: 12 }
      ],
      {
        beginAtZero: true,
        ticks: { color: palette.subtleText, callback: v => formatBitsPerSecond((Number(v)||0)*1000000) },
        grid: { color: palette.grid },
        title: { display: true, text: "bit/sec", color: palette.subtleText }
      },
      { label: ctx => `${ctx.dataset?.label||""}: ${formatBitsPerSecond((Number(ctx.parsed?.y)||0)*1000000)}` }
    );
    networkCanvas = canvas;
  }

  function initResourceChart(canvas) {
    if (resourceChart) { resourceChart.destroy(); resourceChart = null; }
    resView = 0; resWin = 0;
    const palette = currentPalette();
    resourceChart = makeLineChart(canvas,
      [
        { label: "CPU (%)",    data: [], borderColor: "rgba(255,121,145,1)", backgroundColor: "rgba(255,121,145,0.18)", fill: true, tension: 0.3, pointRadius: 0, pointHoverRadius: 4, pointHitRadius: 12 },
        { label: "Memory (%)", data: [], borderColor: "rgba(97,185,255,1)",  backgroundColor: "rgba(97,185,255,0.18)",  fill: true, tension: 0.3, pointRadius: 0, pointHoverRadius: 4, pointHitRadius: 12 }
      ],
      { beginAtZero: true, max: 100, ticks: { color: palette.subtleText }, grid: { color: palette.grid } },
      null
    );
    resourceCanvas = canvas;
  }

  // ── public API ───────────────────────────────────────────────
  return {
    init: function () {
      const zp = window.ChartZoom || window.chartjsPluginZoom;
      if (zp && typeof Chart?.register === "function") { try { Chart.register(zp); } catch {} }

      const nc = document.getElementById("networkChart");
      const rc = document.getElementById("resourceChart");
      if (!nc || !rc) return false;

      if (!networkChart  || networkCanvas  !== nc) {
        initNetworkChart(nc);
        attachOverlayPan("networkChartOverlay",
          () => networkChart, () => netFull,
          () => netView, v => { netView = v; },
          () => netWin, true);
      }
      if (!resourceChart || resourceCanvas !== rc) {
        initResourceChart(rc);
        attachOverlayPan("resourceChartOverlay",
          () => resourceChart, () => resFull,
          () => resView, v => { resView = v; },
          () => resWin, false);
      }
      ensureGauges();
      return true;
    },

    updateGauges: function (p) {
      if (!p) return;
      updateGauge(cpuGauge, p.cpu); updateGauge(memoryGauge, p.memory); updateGauge(diskGauge, p.disk);
    },

    updateNetwork: function (p) {
      if (!p || !networkChart) return;
      netFull.labels = p.labels || []; netFull.tx = p.tx || []; netFull.rx = p.rx || [];
      netWin = netFull.labels.length; netView = 0;
      renderSlice(networkChart, netFull, 0, netWin, true);
    },

    updateResource: function (p) {
      if (!p || !resourceChart) return;
      resFull.labels = p.labels || []; resFull.cpu = p.cpuSeries || []; resFull.mem = p.memorySeries || [];
      resWin = resFull.labels.length; resView = 0;
      renderSlice(resourceChart, resFull, 0, resWin, false);
    },

    update: function (p) {
      if (!p) return;
      updateGauge(cpuGauge, p.cpu); updateGauge(memoryGauge, p.memory); updateGauge(diskGauge, p.disk);
      if (networkChart) {
        netFull.labels = p.networkLabels || p.labels || []; netFull.tx = p.tx || []; netFull.rx = p.rx || [];
        netWin = netFull.labels.length; netView = 0;
        renderSlice(networkChart, netFull, 0, netWin, true);
      }
      if (resourceChart) {
        resFull.labels = p.resourceLabels || p.labels || []; resFull.cpu = p.cpuSeries || []; resFull.mem = p.memorySeries || [];
        resWin = resFull.labels.length; resView = 0;
        renderSlice(resourceChart, resFull, 0, resWin, false);
      }
    },

    resetZoom: function (target) {
      if (!target || target === "network") {
        netView = 0; netWin = netFull.labels.length;
        if (networkChart) renderSlice(networkChart, netFull, 0, netWin, true);
      }
      if (!target || target === "resource") {
        resView = 0; resWin = resFull.labels.length;
        if (resourceChart) renderSlice(resourceChart, resFull, 0, resWin, false);
      }
    },

    refreshTheme: function () {
      const palette = currentPalette();
      if (cpuGauge) {
        cpuGauge.data.datasets[0].backgroundColor[1] = palette.gaugeRemainder;
        cpuGauge.update("none");
      }
      if (memoryGauge) {
        memoryGauge.data.datasets[0].backgroundColor[1] = palette.gaugeRemainder;
        memoryGauge.update("none");
      }
      if (diskGauge) {
        diskGauge.data.datasets[0].backgroundColor[1] = palette.gaugeRemainder;
        diskGauge.update("none");
      }
      refreshChartTheme(networkChart);
      refreshChartTheme(resourceChart);
    }
  };
})();

window.addEventListener("monitor-theme-changed", function () {
  if (window.monitorCharts && typeof window.monitorCharts.refreshTheme === "function") {
    window.monitorCharts.refreshTheme();
  }
});

function formatBitsPerSecond(bps) {
  const abs = Math.abs(bps);
  if (abs >= 1e9) return `${(bps/1e9).toFixed(2)} Gbps`;
  if (abs >= 1e6) return `${(bps/1e6).toFixed(2)} Mbps`;
  if (abs >= 1e3) return `${(bps/1e3).toFixed(0)} kbps`;
  return `${bps.toFixed(0)} bps`;
}
